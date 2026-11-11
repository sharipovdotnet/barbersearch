using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Domain.PortfolioPhotos;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Web.Storage;

/// <summary>
/// Локальное файловое хранилище фотографий портфолио: <c>{RootPath}/portfolio/{profileId:N}/{mediaId:N}.{ext}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Реализация <see cref="IPhotoStorage"/> живёт в Web, потому что это host-адаптер: Application
/// не знает ни про ASP.NET, ни про файловую систему, а отдельный проект решения под хранилище
/// не заводится (см. backend/AGENT.md §4).
/// </para>
/// <para>
/// Путь собирается только из разобранного media-URL (<see cref="PortfolioMediaUrl.TryParse"/>):
/// листинга каталога, угадывания путей и приёма пути из query здесь нет. Дополнительно
/// проверяется, что итоговый полный путь остался внутри корня: разбор URL уже запрещает
/// <c>..</c>, <c>%</c> и обратный слэш, но проверка не должна зависеть от одного слоя.
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
    /// Код ошибки хранилища: <c>500</c> без пути к диску в тексте.
    /// </summary>
    private const string STORAGE_ERROR_CODE = "barberProfile.uploadPhoto.storage";

    /// <summary>
    /// Текст ошибки хранилища: без пути, имени файла и содержимого.
    /// </summary>
    private const string STORAGE_ERROR_MESSAGE =
        "The photo file could not be stored. Try again later.";

    /// <summary>
    /// Текст ошибки чтения: клиент не должен отличать отсутствующий файл от нечитаемого.
    /// </summary>
    private const string NOT_FOUND_MESSAGE = "The portfolio photo file was not found.";

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
    public async Task<Result<StoredPhoto, Failure>> SaveAsync(
        Guid barberProfileId,
        Guid mediaId,
        string extension,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        // Расширение приходит из ImageFileInspector, а идентификаторы — из обработчика.
        // Некорректное значение здесь означает ошибку кода, а не ввода: файл не пишем,
        // но и не бросаем исключение в путь запроса.
        if (barberProfileId == Guid.Empty
            || mediaId == Guid.Empty
            || !IsAllowedExtension(extension))
        {
            return StorageFailure();
        }

        var finalPath = ResolveFilePath(barberProfileId, mediaId, extension);
        if (!IsInsideRoot(finalPath))
        {
            return StorageFailure();
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

            return new StoredPhoto(PortfolioMediaUrl.Build(barberProfileId, mediaId, extension));
        }
        catch (OperationCanceledException)
        {
            // Отмена — тоже повод убрать временный файл, но исключение пробрасывается:
            // глотать отмену в пути запроса нельзя.
            DeleteTemporaryFile(temporaryPath, barberProfileId);

            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DeleteTemporaryFile(temporaryPath, barberProfileId);

            // Путь и имя файла в лог не попадают: они содержат идентификаторы, которых
            // не должно быть в диагностике, а ответ клиенту вообще не несёт пути.
            _logger.LogWarning(
                exception,
                "Failed to save a portfolio photo file of barber profile {BarberProfileId}.",
                barberProfileId);

            return StorageFailure();
        }
    }

    /// <inheritdoc />
    public Task<Result<PhotoContent, Failure>> OpenReadAsync(
        string url,
        CancellationToken cancellationToken)
    {
        if (!PortfolioMediaUrl.TryParse(url, out var profileId, out var mediaId, out var extension))
        {
            return Task.FromResult(NotFoundResult());
        }

        var fullPath = ResolveFilePath(profileId, mediaId, extension);
        if (!IsInsideRoot(fullPath))
        {
            return Task.FromResult(NotFoundResult());
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
                new PhotoContent(stream, ContentTypeFor(extension), stream.Length)));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "Failed to open a portfolio photo file of barber profile {BarberProfileId}.",
                profileId);

            return Task.FromResult(NotFoundResult());
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string url, CancellationToken cancellationToken)
    {
        // Чужие и внешние URL (абсолютные ссылки старых фотографий) не наши: молча выходим.
        if (!PortfolioMediaUrl.TryParse(url, out var profileId, out var mediaId, out var extension))
        {
            return Task.CompletedTask;
        }

        var fullPath = ResolveFilePath(profileId, mediaId, extension);
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
            _logger.LogWarning(
                exception,
                "Failed to delete a portfolio photo file of barber profile {BarberProfileId}.",
                profileId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Собирает полный путь к файлу: <c>{RootPath}/portfolio/{profileId:N}/{mediaId:N}.{ext}</c>.
    /// </summary>
    private string ResolveFilePath(Guid barberProfileId, Guid mediaId, string extension)
        => Path.Combine(
            _rootPath,
            PORTFOLIO_DIRECTORY,
            $"{barberProfileId:N}",
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
    private void DeleteTemporaryFile(string temporaryPath, Guid barberProfileId)
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
            _logger.LogWarning(
                exception,
                "Failed to remove a temporary photo file of barber profile {BarberProfileId}.",
                barberProfileId);
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
    private static string ContentTypeFor(string extension) => extension switch
    {
        "jpg" => JPEG_CONTENT_TYPE,
        "png" => PNG_CONTENT_TYPE,
        "webp" => WEBP_CONTENT_TYPE,
        _ => UNKNOWN_CONTENT_TYPE
    };

    private static Failure StorageFailure()
        => new(Error.Failure(STORAGE_ERROR_CODE, STORAGE_ERROR_MESSAGE));

    /// <summary>
    /// Единый <c>404</c> чтения: отсутствующий, нечитаемый и чужой файл не различаются.
    /// </summary>
    private static Result<PhotoContent, Failure> NotFoundResult()
        => Result.Failure<PhotoContent, Failure>(
            Error.NotFound("portfolioMedia.notFound", NOT_FOUND_MESSAGE));
}
