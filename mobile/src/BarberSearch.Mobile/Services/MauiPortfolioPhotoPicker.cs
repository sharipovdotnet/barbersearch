using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Media;
using Microsoft.Maui.Graphics.Platform;

namespace BarberSearch.Mobile.Services;

/// <summary>
/// Файл портфолио из галереи телефона: выбор, проверка типа и подготовка к загрузке.
/// </summary>
/// <remarks>
/// <para>
/// Подготовка повторяет веб (<c>frontend/src/lib/image-prep.ts</c>): исходник больше 15 МБ
/// отбрасывается до декодирования, длинная сторона уменьшается до 1600 px, а результат
/// перекодируется в JPEG. Сжатие идёт циклом по качеству: первый проход 0,9, дальше шаг
/// вниз, пока файл не уложится в 5 МБ. Маленькие кадры не растягиваются: их сторона
/// остаётся как есть.
/// </para>
/// <para>
/// Декодирование и кодирование делает <c>PlatformImage</c> из состава Microsoft.Maui.Graphics,
/// поэтому платформенный код (<c>BitmapFactory</c>/<c>UIImage</c>) не нужен, а новых
/// пакетов приложение не получает. Сбой декодирования — <see cref="PortfolioImageError.InvalidImage"/>:
/// текст для человека собирает ViewModel, а не этот сервис.
/// </para>
/// </remarks>
public sealed class MauiPortfolioPhotoPicker : IPortfolioPhotoPicker
{
    /// <summary>
    /// Типы, которые принимает веб: сервер тоже не берёт SVG и GIF.
    /// </summary>
    private static readonly string[] SupportedContentTypes =
        ["image/jpeg", "image/jpg", "image/png", "image/webp"];

    /// <summary>
    /// Запасные расширения: часть платформ отдаёт <c>application/octet-stream</c>.
    /// </summary>
    private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>
    /// Качества JPEG по порядку: от лучшего к худшему, пока файл не уложится в лимит.
    /// </summary>
    private static readonly float[] JpegQualities = [0.9f, 0.8f, 0.7f, 0.6f, 0.5f];

    /// <summary>
    /// Размер порции при чтении файла: исходник читается потоком, а не целиком.
    /// </summary>
    private const int ReadChunkBytes = 81920;

    /// <summary>
    /// Фильтр выбора файла: JPEG, PNG и WebP — те же типы, что и в вебе.
    /// </summary>
    /// <remarks>
    /// iOS адресует типы идентификаторами UTI, Android — MIME-типами. Других целевых
    /// платформ у приложения нет, поэтому словарь ограничен двумя записями.
    /// </remarks>
    private static readonly FilePickerFileType ImageFileTypes = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = ["image/jpeg", "image/png", "image/webp"],
            [DevicePlatform.iOS] = ["public.jpeg", "public.png", "org.webmproject.webp"],
        });

    /// <inheritdoc />
    public async Task<PortfolioImage?> PickAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FileResult? file;

        try
        {
            file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = Loc.T("Выбрать фото"),
                FileTypes = ImageFileTypes,
            });
        }
        catch (OperationCanceledException)
        {
            // Выбор отменили жестом или кнопкой «назад»: это не ошибка файла.
            return null;
        }

        if (file is null)
        {
            return null;
        }

        if (!IsSupportedImage(file))
        {
            throw new PortfolioImageException(PortfolioImageError.UnsupportedType);
        }

        var sourceBytes = await ReadSourceAsync(file, cancellationToken);
        var jpeg = Prepare(sourceBytes, cancellationToken);

        return new PortfolioImage(
            jpeg,
            string.Concat(Path.GetFileNameWithoutExtension(file.FileName), ".jpg"),
            "image/jpeg");
    }

    /// <summary>
    /// Читает исходник в память, не давая файлу больше лимита занять её целиком.
    /// </summary>
    /// <param name="file">Выбранный файл.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Содержимое файла.</returns>
    /// <exception cref="PortfolioImageException">Файл больше 15 МБ или не читается.</exception>
    private static async Task<byte[]> ReadSourceAsync(
        FileResult file,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = await file.OpenReadAsync();

            if (source.CanSeek && source.Length > PortfolioPhotoLimits.MaxSourceFileBytes)
            {
                throw new PortfolioImageException(PortfolioImageError.SourceTooLarge);
            }

            using var buffer = new MemoryStream();
            var chunk = new byte[ReadChunkBytes];

            while (true)
            {
                var read = await source.ReadAsync(
                    chunk.AsMemory(0, chunk.Length),
                    cancellationToken);

                if (read == 0)
                {
                    break;
                }

                if (buffer.Length + read > PortfolioPhotoLimits.MaxSourceFileBytes)
                {
                    throw new PortfolioImageException(PortfolioImageError.SourceTooLarge);
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (PortfolioImageException)
        {
            // Лимит уже назван точно: оборачивать его в «не прочитали» нельзя.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Экран закрыли: отмена отличается от ошибки файла.
            throw;
        }
        catch (Exception exception)
        {
            throw new PortfolioImageException(PortfolioImageError.InvalidImage, exception);
        }
    }

    /// <summary>
    /// Уменьшает кадр и перекодирует его в JPEG в пределах 5 МБ.
    /// </summary>
    /// <param name="sourceBytes">Содержимое выбранного файла.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Содержимое готового JPEG.</returns>
    /// <exception cref="PortfolioImageException">
    /// Кадр не декодируется или не укладывается в лимит ни на одном качестве.
    /// </exception>
    private static byte[] Prepare(byte[] sourceBytes, CancellationToken cancellationToken)
    {
        // Тип выводится из FromStream: платформенная реализация может вернуть и
        // PlatformImage, и интерфейс IImage — дальше нужны только его члены.
        object? decoded;

        try
        {
            using var source = new MemoryStream(sourceBytes, writable: false);

            decoded = PlatformImage.FromStream(source);
        }
        catch (Exception exception)
        {
            throw new PortfolioImageException(PortfolioImageError.InvalidImage, exception);
        }

        if (decoded is not Microsoft.Maui.Graphics.IImage image)
        {
            Release(decoded);

            throw new PortfolioImageException(PortfolioImageError.InvalidImage);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var longestSide = Math.Max(image.Width, image.Height);

            if (longestSide <= 0)
            {
                throw new PortfolioImageException(PortfolioImageError.InvalidImage);
            }

            // Маленький кадр остаётся собой: увеличение только испортило бы его.
            var prepared = longestSide > PortfolioPhotoLimits.MaxSide
                ? image.Downsize(PortfolioPhotoLimits.MaxSide)
                : image;

            try
            {
                foreach (var quality in JpegQualities)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var bytes = prepared.AsBytes(ImageFormat.Jpeg, quality);

                    if (bytes.Length <= PortfolioPhotoLimits.MaxFileBytes)
                    {
                        return bytes;
                    }
                }
            }
            finally
            {
                if (!ReferenceEquals(prepared, image))
                {
                    // Уменьшенная копия — отдельный объект; исходник освобождается ниже.
                    Release(prepared);
                }
            }

            throw new PortfolioImageException(PortfolioImageError.TooLarge);
        }
        finally
        {
            Release(image);
        }
    }

    /// <summary>
    /// Освобождает платформенный кадр, если реализация это умеет.
    /// </summary>
    /// <remarks>
    /// Через <see cref="object"/>: интерфейс <c>IImage</c> не объявляет <see cref="IDisposable"/>,
    /// а платформенные реализации его реализуют и держат нативные битмапы.
    /// </remarks>
    /// <param name="image">Кадр или <c>null</c>.</param>
    private static void Release(object? image) => (image as IDisposable)?.Dispose();

    /// <summary>
    /// Проверяет, что выбранный файл — растровое изображение поддерживаемого типа.
    /// </summary>
    /// <param name="file">Выбранный файл.</param>
    /// <returns><c>true</c>, если файл можно готовить к загрузке.</returns>
    private static bool IsSupportedImage(FileResult file) =>
        IsSupportedContentType(file.ContentType) || IsSupportedExtension(file.FileName);

    /// <summary>
    /// Проверяет MIME-тип без параметров: <c>image/jpeg; charset=binary</c> — тот же JPEG.
    /// </summary>
    /// <param name="contentType">Тип из файла.</param>
    /// <returns><c>true</c>, если тип поддерживается.</returns>
    private static bool IsSupportedContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var value = contentType.Split(';')[0].Trim();

        return SupportedContentTypes.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Проверяет расширение файла: платформа не всегда знает MIME-тип.
    /// </summary>
    /// <param name="fileName">Имя файла.</param>
    /// <returns><c>true</c>, если расширение поддерживается.</returns>
    private static bool IsSupportedExtension(string? fileName) =>
        Path.GetExtension(fileName ?? string.Empty) is { Length: > 0 } extension
        && SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
}
