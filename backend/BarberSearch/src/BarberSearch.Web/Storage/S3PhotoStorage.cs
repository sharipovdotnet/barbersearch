using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Web.Storage;

/// <summary>
/// Хранилище изображений в S3-совместимом бакете: фотографии портфолио и аватары.
/// </summary>
/// <remarks>
/// <para>
/// Поведение — зеркало <see cref="LocalPhotoStorage"/>, только вместо файловых путей ключи
/// <see cref="PhotoMediaKeys"/>: исходник пишется одним <c>PUT</c>, производные варианты
/// генерирует тот же <see cref="IPhotoThumbnailer"/> и пишет тот же адаптер. Атомарность даёт
/// сам object storage, поэтому временных ключей и переименований нет.
/// </para>
/// <para>
/// Загрузка успешна, когда записан исходник. Варианты пишутся best effort: сбой ставит
/// <c>Warning</c> и не превращает загрузку в <c>500</c>, а вариант догенерируется на первом
/// чтении <c>?size=</c> — так же, как у локального провайдера.
/// </para>
/// <para>
/// Чтение разбирает оба префикса (<c>portfolio</c>, затем <c>avatars</c>) и собирает ключ
/// только из идентификаторов; строка запроса в ключ не попадает. Поток отдаётся как
/// <see cref="MemoryStream"/> с известной длиной: <c>FileStreamResult</c> с <c>Range</c> требует
/// seekable-поток. ETag остаётся производным от <c>mediaId</c> и фактически отданного варианта,
/// а не берётся из метаданных S3: контракт <c>304</c> закреплён тестами.
/// </para>
/// <para>
/// Ошибки хранилища не несут ключа, бакета, эндпоинта и секретов: публичные коды прежние
/// (<c>barberProfile.uploadPhoto.storage</c>, <c>user.uploadAvatar.storage</c>,
/// <c>portfolioMedia.notFound</c>, <c>avatarMedia.notFound</c>).
/// </para>
/// </remarks>
internal sealed class S3PhotoStorage : IPhotoStorage
{
    /// <summary>
    /// Код ошибки хранилища фотографии: <c>500</c> без ключа и бакета в тексте.
    /// </summary>
    private const string STORAGE_ERROR_CODE = "barberProfile.uploadPhoto.storage";

    /// <summary>
    /// Код ошибки хранилища аватара: <c>500</c> без ключа и имени файла в тексте.
    /// </summary>
    private const string AVATAR_STORAGE_ERROR_CODE = "user.uploadAvatar.storage";

    /// <summary>
    /// Текст ошибки хранилища фотографии: без ключа, бакета и содержимого.
    /// </summary>
    private const string STORAGE_ERROR_MESSAGE =
        "The photo file could not be stored. Try again later.";

    /// <summary>
    /// Текст ошибки хранилища аватара: без ключа, бакета и содержимого.
    /// </summary>
    private const string AVATAR_STORAGE_ERROR_MESSAGE =
        "The avatar file could not be stored. Try again later.";

    /// <summary>
    /// Текст ошибки чтения фотографии: отсутствие и недоступность не различаются.
    /// </summary>
    private const string NOT_FOUND_MESSAGE = "The portfolio photo file was not found.";

    /// <summary>
    /// Текст ошибки чтения аватара: та же логика — причина отказа не раскрывается.
    /// </summary>
    private const string AVATAR_NOT_FOUND_MESSAGE = "The avatar file was not found.";

    /// <summary>
    /// Производные варианты в порядке возрастания ширины: пишутся все, что вернул генератор.
    /// </summary>
    private static readonly PhotoVariant[] DERIVATIVE_VARIANTS =
    [
        PhotoVariant.Small,
        PhotoVariant.Medium,
        PhotoVariant.Large
    ];

    private readonly IPhotoObjectStore _objects;
    private readonly IPhotoThumbnailer _thumbnailer;
    private readonly ILogger<S3PhotoStorage> _logger;

    /// <summary>
    /// Создаёт хранилище над бакетом из <see cref="S3StorageOptions"/>.
    /// </summary>
    /// <param name="objects">Шов к object storage: ключи и байты, без S3-специфики.</param>
    /// <param name="thumbnailer">Генератор WebP-вариантов: запись при загрузке и лениво.</param>
    /// <param name="logger">Журнал: ошибки без ключа, бакета и эндпоинта.</param>
    public S3PhotoStorage(
        IPhotoObjectStore objects,
        IPhotoThumbnailer thumbnailer,
        ILogger<S3PhotoStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(thumbnailer);
        ArgumentNullException.ThrowIfNull(logger);

        _objects = objects;
        _thumbnailer = thumbnailer;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<Result<StoredPhoto, Failure>> SaveAsync(
        Guid barberProfileId,
        Guid mediaId,
        string extension,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
        => SaveFileAsync(
            barberProfileId,
            mediaId,
            extension,
            content,
            isAvatar: false,
            cancellationToken);

    /// <inheritdoc />
    public Task<Result<StoredPhoto, Failure>> SaveAvatarAsync(
        Guid userId,
        Guid mediaId,
        string extension,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
        => SaveFileAsync(userId, mediaId, extension, content, isAvatar: true, cancellationToken);

    /// <inheritdoc />
    public async Task<Result<PhotoContent, Failure>> OpenReadAsync(
        string url,
        PhotoVariant variant,
        CancellationToken cancellationToken)
    {
        if (!TryResolveKey(url, out var isAvatar, out var ownerId, out var mediaId, out var extension))
        {
            return NotFoundResult(isAvatar: false);
        }

        var originalKey = PhotoMediaKeys.Original(isAvatar, ownerId, mediaId, extension);

        if (variant == PhotoVariant.Original)
        {
            var original = await TryGetAsync(originalKey, ownerId, isAvatar, cancellationToken);

            return original is null
                ? NotFoundResult(isAvatar)
                : Content(original.Content, PhotoContentTypes.For(extension), mediaId, PhotoVariant.Original);
        }

        var variantKey = PhotoMediaKeys.Variant(isAvatar, ownerId, mediaId, variant);

        // Вариант уже в бакете: отдаём его, не трогая декодер.
        var existing = await TryGetAsync(variantKey, ownerId, isAvatar, cancellationToken);
        if (existing is not null)
        {
            return Content(existing.Content, PhotoContentTypes.WEBP, mediaId, variant);
        }

        var source = await TryGetAsync(originalKey, ownerId, isAvatar, cancellationToken);
        if (source is null)
        {
            return NotFoundResult(isAvatar);
        }

        // Ленивая генерация: кадр загружен до среза либо генерация на загрузке упала.
        var generated = await _thumbnailer.GenerateAsync(
            source.Content,
            extension,
            ownerId,
            mediaId,
            isAvatar,
            cancellationToken);

        if (generated.IsSuccess)
        {
            await WriteVariantsAsync(
                ownerId,
                mediaId,
                isAvatar,
                generated.Value,
                cancellationToken);

            // Запрошенного варианта могло не оказаться: исходник уже целевой ширины.
            var generatedVariant = await TryGetAsync(
                variantKey,
                ownerId,
                isAvatar,
                cancellationToken);

            if (generatedVariant is not null)
            {
                return Content(generatedVariant.Content, PhotoContentTypes.WEBP, mediaId, variant);
            }
        }

        // Вариант невозможен: отдаём исходник с его MIME и его ETag — битой ссылки в srcset
        // быть не может.
        return Content(
            source.Content,
            PhotoContentTypes.For(extension),
            mediaId,
            PhotoVariant.Original);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string url, CancellationToken cancellationToken)
    {
        // Чужие и внешние URL не наши: молча выходим, как локальный адаптер.
        if (!TryResolveKey(url, out var isAvatar, out var ownerId, out var mediaId, out var extension))
        {
            return;
        }

        // Исходник и три известных варианта: листинг бакета не нужен, а вариант не может
        // прийти из query — его имя строится из mediaId.
        var keys = new List<string>(DERIVATIVE_VARIANTS.Length + 1)
        {
            PhotoMediaKeys.Original(isAvatar, ownerId, mediaId, extension)
        };

        foreach (var variant in DERIVATIVE_VARIANTS)
        {
            keys.Add(PhotoMediaKeys.Variant(isAvatar, ownerId, mediaId, variant));
        }

        foreach (var key in keys)
        {
            try
            {
                await _objects.DeleteAsync(key, cancellationToken);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // Строка в БД уже удалена: файл-сирота не ломает UX, а повторное удаление
                // идемпотентно. В журнал — только владелец.
                LogDeleteFailure(ownerId, isAvatar, exception);
            }
        }
    }

    /// <summary>
    /// Общая запись исходника для фотографии и аватара: один <c>PUT</c> и best-effort варианты.
    /// </summary>
    private async Task<Result<StoredPhoto, Failure>> SaveFileAsync(
        Guid ownerId,
        Guid mediaId,
        string extension,
        ReadOnlyMemory<byte> content,
        bool isAvatar,
        CancellationToken cancellationToken)
    {
        // Расширение приходит из инспектора, идентификаторы — из обработчика: некорректное
        // значение здесь означает ошибку кода, а не ввода.
        if (ownerId == Guid.Empty
            || mediaId == Guid.Empty
            || !IsAllowedExtension(extension))
        {
            return StorageFailure(isAvatar);
        }

        var key = PhotoMediaKeys.Original(isAvatar, ownerId, mediaId, extension);

        try
        {
            await _objects.PutAsync(
                key,
                content,
                PhotoContentTypes.For(extension),
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Ключ и бакет в текст не попадают: они содержат идентификаторы, которых не должно
            // быть в диагностике, а ответ клиенту вообще не несёт ключа.
            LogSaveFailure(ownerId, isAvatar, exception);

            return StorageFailure(isAvatar);
        }

        // Исходник записан — единственное обязательное условие успешной загрузки.
        await GenerateVariantsBestEffortAsync(
            ownerId,
            mediaId,
            isAvatar,
            content,
            extension,
            cancellationToken);

        return isAvatar
            ? new StoredPhoto(UserAvatarMediaUrl.Build(ownerId, mediaId, extension))
            : new StoredPhoto(PortfolioMediaUrl.Build(ownerId, mediaId, extension));
    }

    /// <summary>
    /// Генерирует и записывает варианты после успешной записи исходника. Никогда не бросает.
    /// </summary>
    private async Task GenerateVariantsBestEffortAsync(
        Guid ownerId,
        Guid mediaId,
        bool isAvatar,
        ReadOnlyMemory<byte> content,
        string extension,
        CancellationToken cancellationToken)
    {
        try
        {
            var generated = await _thumbnailer.GenerateAsync(
                content,
                extension,
                ownerId,
                mediaId,
                isAvatar,
                cancellationToken);

            if (generated.IsFailure)
            {
                return;
            }

            await WriteVariantsAsync(
                ownerId,
                mediaId,
                isAvatar,
                generated.Value,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Запрос отменён уже после фиксации исходника: возвращать ошибку бессмысленно,
            // вариант догенерируется на первом чтении.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogWriteFailure(ownerId, isAvatar, exception);
        }
    }

    /// <summary>
    /// Записывает производные варианты; ошибка одного варианта не отменяет остальные.
    /// </summary>
    private async Task WriteVariantsAsync(
        Guid ownerId,
        Guid mediaId,
        bool isAvatar,
        IReadOnlyList<PhotoThumbnail> thumbnails,
        CancellationToken cancellationToken)
    {
        foreach (var thumbnail in thumbnails)
        {
            var key = PhotoMediaKeys.Variant(isAvatar, ownerId, mediaId, thumbnail.Variant);

            try
            {
                await _objects.PutAsync(
                    key,
                    thumbnail.Content,
                    PhotoContentTypes.WEBP,
                    cancellationToken);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                LogWriteFailure(ownerId, isAvatar, exception);
            }
        }
    }

    /// <summary>
    /// Читает объект; ошибка чтения превращается в <c>null</c>, то есть в тот же <c>404</c>,
    /// что и отсутствие файла.
    /// </summary>
    private async Task<PhotoObject?> TryGetAsync(
        string key,
        Guid ownerId,
        bool isAvatar,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _objects.GetAsync(key, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogReadFailure(ownerId, isAvatar, exception);

            return null;
        }
    }

    /// <summary>
    /// Собирает <see cref="PhotoContent"/> из байтов с ETag фактически отданного представления.
    /// </summary>
    private static Result<PhotoContent, Failure> Content(
        ReadOnlyMemory<byte> bytes,
        string contentType,
        Guid mediaId,
        PhotoVariant variant)
        => Result.Success<PhotoContent, Failure>(new PhotoContent(
            new MemoryStream(bytes.ToArray(), writable: false),
            contentType,
            bytes.Length,
            PhotoVariantWidths.EntityTag(mediaId, variant)));

    /// <summary>
    /// Разбирает media-URL любого из двух видов: сначала portfolio, затем avatars.
    /// </summary>
    private static bool TryResolveKey(
        string url,
        out bool isAvatar,
        out Guid ownerId,
        out Guid mediaId,
        out string extension)
    {
        isAvatar = false;
        ownerId = Guid.Empty;
        mediaId = Guid.Empty;
        extension = string.Empty;

        if (PortfolioMediaUrl.TryParse(url, out var profileId, out var portfolioMediaId, out var portfolioExtension))
        {
            ownerId = profileId;
            mediaId = portfolioMediaId;
            extension = portfolioExtension;

            return true;
        }

        if (UserAvatarMediaUrl.TryParse(url, out var userId, out var avatarMediaId, out var avatarExtension))
        {
            isAvatar = true;
            ownerId = userId;
            mediaId = avatarMediaId;
            extension = avatarExtension;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Журнал отказа чтения: только идентификатор владельца, без ключа и бакета.
    /// </summary>
    private void LogReadFailure(Guid ownerId, bool isAvatar, Exception exception)
    {
        if (isAvatar)
        {
            _logger.LogWarning(
                exception,
                "Failed to read an avatar file of user {UserId}.",
                ownerId);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Failed to read a portfolio photo file of barber profile {BarberProfileId}.",
                ownerId);
        }
    }

    /// <summary>
    /// Журнал отказа записи: только идентификатор владельца, без ключа и бакета.
    /// </summary>
    private void LogWriteFailure(Guid ownerId, bool isAvatar, Exception exception)
    {
        if (isAvatar)
        {
            _logger.LogWarning(
                exception,
                "Failed to write an avatar variant of user {UserId}.",
                ownerId);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Failed to write a portfolio photo variant of barber profile "
                + "{BarberProfileId}.",
                ownerId);
        }
    }

    /// <summary>
    /// Журнал отказа удаления: только идентификатор владельца, без ключа и бакета.
    /// </summary>
    private void LogDeleteFailure(Guid ownerId, bool isAvatar, Exception exception)
    {
        if (isAvatar)
        {
            _logger.LogWarning(
                exception,
                "Failed to delete an avatar file of user {UserId}.",
                ownerId);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Failed to delete a portfolio photo file of barber profile {BarberProfileId}.",
                ownerId);
        }
    }

    /// <summary>
    /// Журнал отказа записи исходника: только идентификатор владельца.
    /// </summary>
    private void LogSaveFailure(Guid ownerId, bool isAvatar, Exception exception)
    {
        if (isAvatar)
        {
            _logger.LogWarning(
                exception,
                "Failed to save an avatar file of user {UserId}.",
                ownerId);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Failed to save a portfolio photo file of barber profile {BarberProfileId}.",
                ownerId);
        }
    }

    /// <summary>
    /// Разрешённые расширения: тип определяется по сигнатуре, расширение — производное от неё.
    /// </summary>
    private static bool IsAllowedExtension(string extension)
        => extension is "jpg" or "png" or "webp";

    private static Failure StorageFailure(bool isAvatar)
        => isAvatar
            ? new Failure(Error.Failure(AVATAR_STORAGE_ERROR_CODE, AVATAR_STORAGE_ERROR_MESSAGE))
            : new Failure(Error.Failure(STORAGE_ERROR_CODE, STORAGE_ERROR_MESSAGE));

    /// <summary>
    /// Единый <c>404</c> чтения: отсутствующий и недоступный объект не различаются.
    /// </summary>
    private static Result<PhotoContent, Failure> NotFoundResult(bool isAvatar)
        => isAvatar
            ? Result.Failure<PhotoContent, Failure>(
                Error.NotFound("avatarMedia.notFound", AVATAR_NOT_FOUND_MESSAGE))
            : Result.Failure<PhotoContent, Failure>(
                Error.NotFound("portfolioMedia.notFound", NOT_FOUND_MESSAGE));
}
