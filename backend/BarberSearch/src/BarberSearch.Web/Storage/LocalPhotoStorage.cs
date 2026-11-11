using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Web.Storage;

/// <summary>
/// Локальное файловое хранилище изображений пользователей: фотографий портфолио
/// (<c>{RootPath}/portfolio/{profileId:N}/{mediaId:N}.{ext}</c>) и аватаров
/// (<c>{RootPath}/avatars/{userId:N}/{mediaId:N}.{ext}</c>).
/// </summary>
/// <remarks>
/// <para>
/// Реализация <see cref="IPhotoStorage"/> живёт в Web, потому что это host-адаптер: Application
/// не знает ни про ASP.NET, ни про файловую систему, а отдельный проект решения под хранилище
/// не заводится (см. backend/AGENT.md §4). Один адаптер, один корень и один диск обслуживают
/// два вида файлов: различается только подкаталог, а запись, проверка «путь внутри корня» и
/// MIME-тип общие.
/// </para>
/// <para>
/// Рядом с исходником лежат производные варианты: <c>{mediaId:N}-{sm|md|lg}.webp</c> в том же
/// каталоге. Загрузка пишет их сразу (best effort), а чтение варианта догенерирует файл, если
/// его нет: так работают кадры, загруженные до среза, и кадры, чья генерация на загрузке
/// упала. Если вариант невозможен, отдаётся исходник — сломанной картинки в <c>srcset</c>
/// быть не может.
/// </para>
/// <para>
/// Путь собирается только из разобранного media-URL (<see cref="PortfolioMediaUrl.TryParse"/>
/// или <see cref="UserAvatarMediaUrl.TryParse"/>): листинга каталога, угадывания путей и приёма
/// пути из query здесь нет. Дополнительно проверяется, что итоговый полный путь остался внутри
/// корня: разбор URL уже запрещает <c>..</c>, <c>%</c> и обратный слэш, но проверка не должна
/// зависеть от одного слоя.
/// </para>
/// <para>
/// Файл пишется во временное имя <c>.{mediaId:N}.tmp</c> в том же каталоге и переименовывается
/// атомарно: читатель не увидит недописанный файл. Ошибка ввода-вывода не пробрасывается
/// наружу — она возвращается как <c>Failure</c> или пишется <c>Warning</c>'ом при удалении.
/// </para>
/// </remarks>
public sealed class LocalPhotoStorage : IPhotoStorage
{
    /// <summary>
    /// Подкаталог корня, в котором лежат файлы портфолио.
    /// </summary>
    private const string PORTFOLIO_DIRECTORY = "portfolio";

    /// <summary>
    /// Подкаталог корня, в котором лежат файлы аватаров. Входит в тот же бэкап, что и
    /// <c>portfolio/</c>: файлы не хранятся в PostgreSQL.
    /// </summary>
    private const string AVATAR_DIRECTORY = "avatars";

    /// <summary>
    /// Код ошибки хранилища фотографии: <c>500</c> без пути к диску в тексте.
    /// </summary>
    private const string STORAGE_ERROR_CODE = "barberProfile.uploadPhoto.storage";

    /// <summary>
    /// Код ошибки хранилища аватара: <c>500</c> без пути и имени файла в тексте.
    /// </summary>
    private const string AVATAR_STORAGE_ERROR_CODE = "user.uploadAvatar.storage";

    /// <summary>
    /// Текст ошибки хранилища фотографии: без пути, имени файла и содержимого.
    /// </summary>
    private const string STORAGE_ERROR_MESSAGE =
        "The photo file could not be stored. Try again later.";

    /// <summary>
    /// Текст ошибки хранилища аватара: без пути, имени файла и содержимого.
    /// </summary>
    private const string AVATAR_STORAGE_ERROR_MESSAGE =
        "The avatar file could not be stored. Try again later.";

    /// <summary>
    /// Текст ошибки чтения фотографии: клиент не должен отличать отсутствующий файл от нечитаемого.
    /// </summary>
    private const string NOT_FOUND_MESSAGE = "The portfolio photo file was not found.";

    /// <summary>
    /// Текст ошибки чтения аватара: тот же принцип — причина отказа не раскрывается.
    /// </summary>
    private const string AVATAR_NOT_FOUND_MESSAGE = "The avatar file was not found.";

    private const string JPEG_CONTENT_TYPE = "image/jpeg";
    private const string PNG_CONTENT_TYPE = "image/png";
    private const string WEBP_CONTENT_TYPE = "image/webp";
    private const string UNKNOWN_CONTENT_TYPE = "application/octet-stream";

    private readonly string _rootPath;
    private readonly IPhotoThumbnailer _thumbnailer;
    private readonly ILogger<LocalPhotoStorage> _logger;

    /// <summary>
    /// Создаёт хранилище над каталогом из <see cref="PhotoStorageOptions"/>.
    /// </summary>
    /// <param name="options">Уже разрешённые настройки: каталог создан на старте.</param>
    /// <param name="thumbnailer">Генератор WebP-вариантов: запись при загрузке и лениво.</param>
    /// <param name="logger">Журнал: ошибки удаления и записи без пути к файлу.</param>
    public LocalPhotoStorage(
        PhotoStorageOptions options,
        IPhotoThumbnailer thumbnailer,
        ILogger<LocalPhotoStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(thumbnailer);
        ArgumentNullException.ThrowIfNull(logger);

        _rootPath = options.RootPath;
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
        if (!TryResolveFile(
                url,
                out var fullPath,
                out var isAvatar,
                out var ownerId,
                out var mediaId))
        {
            return NotFoundResult(isAvatar: false);
        }

        if (!IsInsideRoot(fullPath))
        {
            return NotFoundResult(isAvatar);
        }

        // Исходник — обычное чтение: вариант не запрашивали.
        if (variant == PhotoVariant.Original)
        {
            return OpenFile(fullPath, isAvatar, ownerId, mediaId, PhotoVariant.Original);
        }

        var variantPath = ResolveVariantPath(ownerId, mediaId, isAvatar, variant);
        if (!IsInsideRoot(variantPath))
        {
            return NotFoundResult(isAvatar);
        }

        // Вариант уже на диске: отдаём файл, не трогая декодер.
        if (File.Exists(variantPath))
        {
            var stored = OpenFile(variantPath, isAvatar, ownerId, mediaId, variant);
            if (stored.IsSuccess)
            {
                return stored;
            }
        }

        // Ленивая генерация: кадр загружен до среза либо генерация на загрузке упала.
        var generated = await EnsureVariantAsync(
            fullPath,
            variantPath,
            ownerId,
            mediaId,
            isAvatar,
            cancellationToken);
        if (generated)
        {
            var variantContent = OpenFile(variantPath, isAvatar, ownerId, mediaId, variant);
            if (variantContent.IsSuccess)
            {
                return variantContent;
            }
        }

        // Вариант невозможен: исходник уже целевой ширины, инспектор отказал или декодер упал.
        // Отдаём исходник с его Content-Type и его ETag — 404 клиент понял бы как «кадра нет»,
        // а дескриптор в srcset остался бы битым.
        return OpenFile(fullPath, isAvatar, ownerId, mediaId, PhotoVariant.Original);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string url, CancellationToken cancellationToken)
    {
        // Чужие и внешние URL (абсолютные ссылки старых фотографий) не наши: молча выходим.
        if (!TryResolveFile(
                url,
                out var fullPath,
                out var isAvatar,
                out var ownerId,
                out var mediaId))
        {
            return Task.CompletedTask;
        }

        if (!IsInsideRoot(fullPath))
        {
            return Task.CompletedTask;
        }

        try
        {
            // File.Delete идемпотентен: отсутствующий файл — не ошибка.
            File.Delete(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogDeleteFailure(ownerId, isAvatar, exception);
        }

        // Производные удаляются по маске рядом с исходником: имя строится из mediaId,
        // а не приходит строкой, поэтому выйти за корень здесь нечем.
        var directoryPath = Path.GetDirectoryName(fullPath);

        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            return Task.CompletedTask;
        }

        try
        {
            foreach (var variantPath in Directory.EnumerateFiles(
                         directoryPath,
                         PhotoVariantWidths.FilePattern(mediaId)))
            {
                if (IsInsideRoot(variantPath))
                {
                    File.Delete(variantPath);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogDeleteFailure(ownerId, isAvatar, exception);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Открывает файл на чтение и собирает <see cref="PhotoContent"/> с ETag своего варианта.
    /// </summary>
    /// <remarks>
    /// <c>FileShare.Read</c>: параллельная раздача того же файла не блокируется, а удаление
    /// ждёт закрытия потока. ETag считает хранилище — только оно знает, что именно отдало.
    /// </remarks>
    private Result<PhotoContent, Failure> OpenFile(
        string fullPath,
        bool isAvatar,
        Guid ownerId,
        Guid mediaId,
        PhotoVariant variant)
    {
        try
        {
            var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Result.Success<PhotoContent, Failure>(new PhotoContent(
                stream,
                ContentTypeFor(Path.GetExtension(fullPath)),
                stream.Length,
                PhotoVariantWidths.EntityTag(mediaId, variant)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (isAvatar)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to open an avatar file of user {UserId}.",
                    ownerId);
            }
            else
            {
                _logger.LogWarning(
                    exception,
                    "Failed to open a portfolio photo file of barber profile {BarberProfileId}.",
                    ownerId);
            }

            return NotFoundResult(isAvatar);
        }
    }

    /// <summary>
    /// Дочитывает исходник, генерирует доступные варианты и записывает их рядом с исходником.
    /// </summary>
    /// <returns>
    /// <c>true</c>, если файл запрошенного варианта теперь существует. Ошибки ввода-вывода и
    /// отказа декодера дают <c>false</c>: вызывающий отдаст исходник.
    /// </returns>
    private async Task<bool> EnsureVariantAsync(
        string fullPath,
        string variantPath,
        Guid ownerId,
        Guid mediaId,
        bool isAvatar,
        CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> original;

        try
        {
            original = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogReadFailure(ownerId, isAvatar, exception);

            return false;
        }

        var generated = await _thumbnailer.GenerateAsync(
            original,
            Path.GetExtension(fullPath).TrimStart('.'),
            ownerId,
            mediaId,
            isAvatar,
            cancellationToken);

        if (generated.IsFailure)
        {
            return false;
        }

        await WriteVariantsAsync(ownerId, mediaId, isAvatar, generated.Value, cancellationToken);

        // Запрошенного варианта могло не оказаться: исходник уже целевой ширины.
        return File.Exists(variantPath);
    }

    /// <summary>
    /// Записывает производные файлы атомарно: временный <c>.tmp</c> и <c>File.Move</c>.
    /// </summary>
    /// <remarks>
    /// Ошибка записи варианта не отменяет уже записанные и не роняет чтение: кадр живёт с тем,
    /// что успело лечь на диск, а вариант догенерируется при следующем запросе.
    /// </remarks>
    private async Task WriteVariantsAsync(
        Guid ownerId,
        Guid mediaId,
        bool isAvatar,
        IReadOnlyList<PhotoThumbnail> thumbnails,
        CancellationToken cancellationToken)
    {
        foreach (var thumbnail in thumbnails)
        {
            var variantPath = ResolveVariantPath(ownerId, mediaId, isAvatar, thumbnail.Variant);

            if (!IsInsideRoot(variantPath))
            {
                continue;
            }

            var directoryPath = Path.GetDirectoryName(variantPath);
            if (string.IsNullOrEmpty(directoryPath))
            {
                continue;
            }

            var temporaryPath = Path.Combine(
                directoryPath,
                $".{mediaId:N}-{PhotoVariantWidths.Suffix(thumbnail.Variant)}.tmp");

            try
            {
                Directory.CreateDirectory(directoryPath);

                await using (var stream = new FileStream(
                                 temporaryPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 4096,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await stream.WriteAsync(thumbnail.Content, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, variantPath, overwrite: true);
            }
            catch (OperationCanceledException)
            {
                DeleteTemporaryFile(temporaryPath, ownerId, isAvatar);

                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                DeleteTemporaryFile(temporaryPath, ownerId, isAvatar);

                LogWriteFailure(ownerId, isAvatar, exception);
            }
        }
    }

    /// <summary>
    /// Генерирует и записывает варианты после успешной записи исходника. Никогда не бросает.
    /// </summary>
    /// <remarks>
    /// Загрузка уже состоялась, исходник лежит на диске: ошибка генерации или обрыв запроса
    /// не должны превращать успешную загрузку в <c>500</c>. Вариант догенерируется при первом
    /// чтении <c>?size=</c>.
    /// </remarks>
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

            await WriteVariantsAsync(ownerId, mediaId, isAvatar, generated.Value, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Запрос отменён уже после фиксации исходника: возвращать ошибку бессмысленно,
            // а вариант догенерируется на первом чтении.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogWriteFailure(ownerId, isAvatar, exception);
        }
    }

    /// <summary>
    /// Путь производного файла: тот же каталог, что у исходника.
    /// </summary>
    private string ResolveVariantPath(
        Guid ownerId,
        Guid mediaId,
        bool isAvatar,
        PhotoVariant variant)
        => Path.Combine(
            _rootPath,
            isAvatar ? AVATAR_DIRECTORY : PORTFOLIO_DIRECTORY,
            $"{ownerId:N}",
            PhotoVariantWidths.FileName(mediaId, variant));

    /// <summary>
    /// Журнал отказа чтения файла: только идентификатор владельца, без пути и имени файла.
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
    /// Журнал отказа записи: только идентификатор владельца, без пути и имени файла.
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
    /// Журнал отказа удаления: только идентификатор владельца, без пути и имени файла.
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
    /// Общая запись файла для фотографии и аватара: атомарный <c>.tmp</c> + <c>File.Move</c>.
    /// </summary>
    /// <remarks>
    /// Расширение приходит из <c>ImageFileInspector</c>, а идентификаторы — из обработчика.
    /// Некорректное значение здесь означает ошибку кода, а не ввода: файл не пишем,
    /// но и не бросаем исключение в путь запроса.
    /// </remarks>
    private async Task<Result<StoredPhoto, Failure>> SaveFileAsync(
        Guid ownerId,
        Guid mediaId,
        string extension,
        ReadOnlyMemory<byte> content,
        bool isAvatar,
        CancellationToken cancellationToken)
    {
        if (ownerId == Guid.Empty
            || mediaId == Guid.Empty
            || !IsAllowedExtension(extension))
        {
            return StorageFailure(isAvatar);
        }

        var finalPath = ResolveFilePath(ownerId, mediaId, extension, isAvatar);
        if (!IsInsideRoot(finalPath))
        {
            return StorageFailure(isAvatar);
        }

        var directoryPath = Path.GetDirectoryName(finalPath)!;
        var temporaryPath = Path.Combine(directoryPath, $".{mediaId:N}.tmp");

        try
        {
            Directory.CreateDirectory(directoryPath);

            // Временное имя в том же каталоге: File.Move атомарен в пределах тома,
            // а читатель никогда не увидит частично записанный файл.
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, finalPath);

            // Исходник на диске — единственное обязательное условие успешной загрузки.
            // Варианты пишутся best effort: сбой генерации оставляет кадр с исходником,
            // а вариант догенерируется на первом чтении.
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
        catch (OperationCanceledException)
        {
            // Отмена — тоже повод убрать временный файл, но исключение пробрасывается:
            // глотать отмену в пути запроса нельзя.
            DeleteTemporaryFile(temporaryPath, ownerId, isAvatar);

            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DeleteTemporaryFile(temporaryPath, ownerId, isAvatar);

            // Путь и имя файла в лог не попадают: они содержат идентификаторы, которых
            // не должно быть в диагностике, а ответ клиенту вообще не несёт пути.
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

            return StorageFailure(isAvatar);
        }
    }

    /// <summary>
    /// Разбирает media-URL любого из двух видов и собирает полный путь к файлу.
    /// </summary>
    /// <remarks>
    /// Сначала portfolio, затем avatars: префиксы различаются, поэтому порядок — единственное,
    /// что нужно, чтобы один адаптер обслуживал два вида файлов.
    /// </remarks>
    /// <param name="url">Относительный media-URL.</param>
    /// <param name="fullPath">Полный путь к файлу при успехе.</param>
    /// <param name="isAvatar"><c>true</c>, если URL принадлежит аватару.</param>
    /// <param name="ownerId">Профиль барбера либо пользователь — владелец файла.</param>
    /// <param name="mediaId">Идентификатор медиафайла: из него собираются имена вариантов.</param>
    private bool TryResolveFile(
        string url,
        out string fullPath,
        out bool isAvatar,
        out Guid ownerId,
        out Guid mediaId)
    {
        fullPath = string.Empty;
        isAvatar = false;
        ownerId = Guid.Empty;
        mediaId = Guid.Empty;

        if (PortfolioMediaUrl.TryParse(url, out var profileId, out var portfolioMediaId, out var extension))
        {
            ownerId = profileId;
            mediaId = portfolioMediaId;
            fullPath = ResolveFilePath(profileId, portfolioMediaId, extension, isAvatar: false);

            return true;
        }

        if (UserAvatarMediaUrl.TryParse(url, out var userId, out var avatarMediaId, out var avatarExtension))
        {
            isAvatar = true;
            ownerId = userId;
            mediaId = avatarMediaId;
            fullPath = ResolveFilePath(userId, avatarMediaId, avatarExtension, isAvatar: true);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Собирает полный путь к файлу:
    /// <c>{RootPath}/portfolio/{profileId:N}/{mediaId:N}.{ext}</c> либо
    /// <c>{RootPath}/avatars/{userId:N}/{mediaId:N}.{ext}</c>.
    /// </summary>
    private string ResolveFilePath(Guid ownerId, Guid mediaId, string extension, bool isAvatar)
        => Path.Combine(
            _rootPath,
            isAvatar ? AVATAR_DIRECTORY : PORTFOLIO_DIRECTORY,
            $"{ownerId:N}",
            $"{mediaId:N}.{extension}");

    /// <summary>
    /// Проверяет, что полный путь остаётся внутри корня хранилища.
    /// </summary>
    /// <remarks>
    /// <see cref="Path.GetRelativePath"/> даёт <c>..</c> или абсолютный путь, если цель вышла
    /// за пределы корня; «точка» (<c>.</c>) означает сам корень и файлом быть не может.
    /// </remarks>
    private bool IsInsideRoot(string fullPath)
    {
        var relativePath = Path.GetRelativePath(_rootPath, fullPath);

        return !string.IsNullOrEmpty(relativePath)
            && !Path.IsPathRooted(relativePath)
            && relativePath != "."
            && relativePath != ".."
            && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relativePath.StartsWith("../", StringComparison.Ordinal);
    }

    /// <summary>
    /// Удаляет временный файл после неудачной записи; ошибка удаления только пишется в журнал.
    /// </summary>
    private void DeleteTemporaryFile(string temporaryPath, Guid ownerId, bool isAvatar)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (isAvatar)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to remove a temporary avatar file of user {UserId}.",
                    ownerId);
            }
            else
            {
                _logger.LogWarning(
                    exception,
                    "Failed to remove a temporary photo file of barber profile {BarberProfileId}.",
                    ownerId);
            }
        }
    }

    /// <summary>
    /// Разрешённые расширения: тип определяется по сигнатуре, расширение — производное от неё.
    /// </summary>
    private static bool IsAllowedExtension(string extension)
        => extension is "jpg" or "png" or "webp";

    /// <summary>
    /// MIME-тип для раздачи: строго по расширению из media-URL, а не по содержимому файла.
    /// </summary>
    private static string ContentTypeFor(string extensionWithDot) => extensionWithDot switch
    {
        ".jpg" => JPEG_CONTENT_TYPE,
        ".png" => PNG_CONTENT_TYPE,
        ".webp" => WEBP_CONTENT_TYPE,
        _ => UNKNOWN_CONTENT_TYPE
    };

    private static Failure StorageFailure(bool isAvatar)
        => isAvatar
            ? new Failure(Error.Failure(AVATAR_STORAGE_ERROR_CODE, AVATAR_STORAGE_ERROR_MESSAGE))
            : new Failure(Error.Failure(STORAGE_ERROR_CODE, STORAGE_ERROR_MESSAGE));

    /// <summary>
    /// Единый <c>404</c> чтения: отсутствующий, нечитаемый и чужой файл не различаются.
    /// </summary>
    private static Result<PhotoContent, Failure> NotFoundResult(bool isAvatar)
        => isAvatar
            ? Result.Failure<PhotoContent, Failure>(
                Error.NotFound("avatarMedia.notFound", AVATAR_NOT_FOUND_MESSAGE))
            : Result.Failure<PhotoContent, Failure>(
                Error.NotFound("portfolioMedia.notFound", NOT_FOUND_MESSAGE));
}
