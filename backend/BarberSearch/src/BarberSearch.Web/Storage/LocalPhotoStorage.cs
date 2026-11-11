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
    private readonly ILogger<LocalPhotoStorage> _logger;

    /// <summary>
    /// Создаёт хранилище над каталогом из <see cref="PhotoStorageOptions"/>.
    /// </summary>
    /// <param name="options">Уже разрешённые настройки: каталог создан на старте.</param>
    /// <param name="logger">Журнал: ошибки удаления и записи без пути к файлу.</param>
    public LocalPhotoStorage(PhotoStorageOptions options, ILogger<LocalPhotoStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _rootPath = options.RootPath;
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
    public Task<Result<PhotoContent, Failure>> OpenReadAsync(
        string url,
        CancellationToken cancellationToken)
    {
        if (!TryResolveFile(url, out var fullPath, out var isAvatar, out var ownerId))
        {
            return Task.FromResult(NotFoundResult(isAvatar: false));
        }

        if (!IsInsideRoot(fullPath))
        {
            return Task.FromResult(NotFoundResult(isAvatar));
        }

        try
        {
            // FileShare.Read: параллельная раздача того же файла не блокируется,
            // а удаление ждёт закрытия потока.
            var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Task.FromResult(Result.Success<PhotoContent, Failure>(
                new PhotoContent(stream, ContentTypeFor(Path.GetExtension(fullPath)), stream.Length)));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
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

            return Task.FromResult(NotFoundResult(isAvatar));
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string url, CancellationToken cancellationToken)
    {
        // Чужие и внешние URL (абсолютные ссылки старых фотографий) не наши: молча выходим.
        if (!TryResolveFile(url, out var fullPath, out var isAvatar, out var ownerId))
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

        return Task.CompletedTask;
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
    private bool TryResolveFile(
        string url,
        out string fullPath,
        out bool isAvatar,
        out Guid ownerId)
    {
        fullPath = string.Empty;
        isAvatar = false;
        ownerId = Guid.Empty;

        if (PortfolioMediaUrl.TryParse(url, out var profileId, out var mediaId, out var extension))
        {
            ownerId = profileId;
            fullPath = ResolveFilePath(profileId, mediaId, extension, isAvatar: false);

            return true;
        }

        if (UserAvatarMediaUrl.TryParse(url, out var userId, out var avatarMediaId, out var avatarExtension))
        {
            isAvatar = true;
            ownerId = userId;
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
