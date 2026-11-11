using BarberSearch.Application.PortfolioPhotos;
using CSharpFunctionalExtensions;
using Shared;
using SkiaSharp;

namespace BarberSearch.Web.Storage;

/// <summary>
/// Генератор WebP-вариантов на SkiaSharp: один проход по трём известным ширинам.
/// </summary>
/// <remarks>
/// <para>
/// SkiaSharp — первая нативная зависимость backend и единственное место, где живёт декодер:
/// Application не знает ни про библиотеку, ни про пиксели. Пакет
/// <c>SkiaSharp.NativeAssets.Linux.NoDependencies</c> обязателен, иначе на <c>ubuntu-latest</c>
/// и в контейнере API не найдётся <c>libSkiaSharp</c>.
/// </para>
/// <para>
/// Декодирование идёт <b>сразу в целевой масштаб</b> (<see cref="SKCodec.GetScaledDimensions"/>),
/// а не в полный кадр: у 24-мегапиксельного исходника полноразмерный битмап — это ~96 МиБ,
/// и три варианта подряд съели бы память процесса. Глобальный <see cref="SemaphoreSlim"/>
/// на два одновременных декодирования ограничивает пик памяти сверху: семафор статический,
/// потому что генератор зарегистрирован синглтоном и обслуживает все запросы сразу.
/// </para>
/// <para>
/// Кодирование всегда <c>webp</c> качества 80 без кропа: пропорции исходника сохраняются,
/// апскейла нет (вариант шире исходника не создаётся). Перекодирование заодно вычищает
/// EXIF/GPS, даже если файл пришёл не из браузера; исходник при этом не трогается.
/// </para>
/// <para>
/// Ошибки не пробрасываются наружу: хранилище отдаёт исходник. В журнал попадают только
/// идентификаторы — без URL, пути, имени файла и содержимого.
/// </para>
/// </remarks>
internal sealed class PhotoThumbnailer : IPhotoThumbnailer
{
    /// <summary>
    /// Качество WebP: то же значение, что в зафиксированном решении (backend/AGENT.md §17).
    /// </summary>
    private const int WEBP_QUALITY = 80;

    /// <summary>
    /// Одновременных декодирований — не больше двух: память кадра не бесконечна.
    /// </summary>
    private const int MAX_CONCURRENT_GENERATIONS = 2;

    /// <summary>
    /// Производные варианты в порядке возрастания ширины: маленький вариант нужен чаще.
    /// </summary>
    private static readonly PhotoVariant[] DERIVATIVE_VARIANTS =
    [
        PhotoVariant.Small,
        PhotoVariant.Medium,
        PhotoVariant.Large
    ];

    private static readonly SemaphoreSlim GATE = new(MAX_CONCURRENT_GENERATIONS);

    private readonly ILogger<PhotoThumbnailer> _logger;

    /// <summary>
    /// Создаёт генератор вариантов.
    /// </summary>
    /// <param name="logger">Журнал: только идентификаторы и причина отказа декодера.</param>
    public PhotoThumbnailer(ILogger<PhotoThumbnailer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PhotoThumbnail>, Failure>> GenerateAsync(
        ReadOnlyMemory<byte> original,
        string extension,
        Guid ownerId,
        Guid mediaId,
        bool isAvatar,
        CancellationToken cancellationToken)
    {
        // Инспектор — тот же, что у загрузки: стороны и лимиты берутся из заголовка файла,
        // а не из доверия к расширению или к клиенту.
        var inspection = ImageFileInspector.Inspect(original.Span);
        if (inspection.IsFailure)
        {
            LogFailure(ownerId, mediaId, isAvatar, extension, null);

            return GenerationFailure();
        }

        var info = inspection.Value;
        var targets = new List<PhotoVariant>(DERIVATIVE_VARIANTS.Length);

        foreach (var variant in DERIVATIVE_VARIANTS)
        {
            // Апскейла нет: вариант шире исходника (или равный ему) не создаётся, иначе
            // в srcset появился бы дескриптор, за которым стоит растянутая картинка.
            if (PhotoVariantWidths.Width(variant, isAvatar) < info.Width)
            {
                targets.Add(variant);
            }
        }

        if (targets.Count == 0)
        {
            return Array.Empty<PhotoThumbnail>();
        }

        await GATE.WaitAsync(cancellationToken);

        try
        {
            return Render(original, info, targets, ownerId, mediaId, isAvatar, extension);
        }
        finally
        {
            GATE.Release();
        }
    }

    /// <summary>
    /// Декодирует и кодирует нужные варианты под семафором.
    /// </summary>
    private Result<IReadOnlyList<PhotoThumbnail>, Failure> Render(
        ReadOnlyMemory<byte> original,
        ImageFileInfo info,
        List<PhotoVariant> targets,
        Guid ownerId,
        Guid mediaId,
        bool isAvatar,
        string extension)
    {
        try
        {
            using var data = SKData.CreateCopy(original.Span);
            using var codec = SKCodec.Create(data);

            if (codec is null)
            {
                LogFailure(ownerId, mediaId, isAvatar, extension, null);

                return GenerationFailure();
            }

            var thumbnails = new List<PhotoThumbnail>(targets.Count);

            foreach (var variant in targets)
            {
                var width = PhotoVariantWidths.Width(variant, isAvatar);
                var height = Math.Max(1, (int)Math.Round(info.Height * ((double)width / info.Width)));
                var content = EncodeVariant(codec, width, height);

                if (content is null)
                {
                    LogFailure(ownerId, mediaId, isAvatar, extension, null);

                    return GenerationFailure();
                }

                thumbnails.Add(new PhotoThumbnail(variant, content, width, height));
            }

            return thumbnails;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Повреждённый файл может уронить декодер любым исключением: наружу отдаём
            // Failure, чтобы хранилище вернуло исходник, а не 500.
            LogFailure(ownerId, mediaId, isAvatar, extension, exception);

            return GenerationFailure();
        }
    }

    /// <summary>
    /// Декодирует кадр в масштабе целевой ширины и кодирует точный размер в WebP.
    /// </summary>
    /// <remarks>
    /// Прозрачность PNG заливается белым: миниатюра — это JPEG-подобная картинка в WebP
    /// без альфа-канала, иначе прозрачные места показывали бы чёрный фон.
    /// </remarks>
    private static byte[]? EncodeVariant(SKCodec codec, int width, int height)
    {
        var desiredScale = (float)width / codec.Info.Width;
        var scaledSize = codec.GetScaledDimensions(desiredScale);
        var scaledInfo = new SKImageInfo(
            Math.Max(1, scaledSize.Width),
            Math.Max(1, scaledSize.Height),
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        using var decoded = SKBitmap.Decode(codec, scaledInfo);
        if (decoded is null)
        {
            return null;
        }

        using var target = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Opaque));

        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(
                decoded,
                new SKRect(0, 0, width, height),
                new SKSamplingOptions(SKCubicResampler.Mitchell),
                paint: null);
        }

        using var image = SKImage.FromBitmap(target);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, WEBP_QUALITY);

        return encoded?.ToArray();
    }

    /// <summary>
    /// Пишет <c>Warning</c> без URL, пути, имени файла и содержимого.
    /// </summary>
    private void LogFailure(
        Guid ownerId,
        Guid mediaId,
        bool isAvatar,
        string extension,
        Exception? exception)
    {
        if (isAvatar)
        {
            _logger.LogWarning(
                exception,
                "Failed to generate variants of avatar {MediaId} of user {UserId} ({Extension}).",
                mediaId,
                ownerId,
                extension);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Failed to generate variants of portfolio photo {MediaId} of barber profile "
                + "{BarberProfileId} ({Extension}).",
                mediaId,
                ownerId,
                extension);
        }
    }

    /// <summary>
    /// Внутренний отказ генерации: клиент его не увидит, хранилище отдаст исходник.
    /// </summary>
    private static Result<IReadOnlyList<PhotoThumbnail>, Failure> GenerationFailure()
        => Result.Failure<IReadOnlyList<PhotoThumbnail>, Failure>(
            Error.Failure(GENERATION_ERROR_CODE, GENERATION_ERROR_MESSAGE));

    private const string GENERATION_ERROR_CODE = "photoThumbnail.generation";

    private const string GENERATION_ERROR_MESSAGE =
        "Image variants could not be generated; the original file is served instead.";
}
