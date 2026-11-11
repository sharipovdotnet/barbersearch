using SkiaSharp;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Настоящие JPEG-кадры для сценариев раздачи вариантов: SkiaSharp их декодирует,
/// поэтому загрузка и ленивое чтение действительно создают WebP-файлы.
/// </summary>
/// <remarks>
/// <para>
/// «Минимальный PNG с настоящим <c>IHDR</c>» инспектор принимает по заголовку, но декодер
/// его не понимает: вариант не создаётся, и <c>?size=</c> законно отдаёт исходник. Там, где
/// тест ожидает <c>image/webp</c>, файл обязан быть настоящей картинкой.
/// </para>
/// <para>
/// Стороны не меньше 300 px: это нижняя граница <c>ImageFileInspector</c>, через которую
/// проходит каждая загрузка. Полосы и круг делают файл заметно больше 1 КиБ даже на
/// минимальном размере, чтобы инспектор не принял кадр за мусор.
/// </para>
/// </remarks>
internal static class MediaTestImage
{
    /// <summary>
    /// Кодирует настоящий JPEG заданных размеров.
    /// </summary>
    /// <param name="width">Ширина в пикселях, не меньше 300.</param>
    /// <param name="height">Высота в пикселях, не меньше 300.</param>
    /// <returns>Байты JPEG, пригодные для загрузки и декодирования.</returns>
    public static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.Clear(SKColors.White);

            for (var column = 0; column < width; column += 16)
            {
                paint.Color = new SKColor((byte)(column * 255 / width), 96, 160);
                canvas.DrawRect(column, 0, 16, height, paint);
            }

            paint.Color = SKColors.OrangeRed;
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);

        return data.ToArray();
    }

    /// <summary>
    /// Читает стороны изображения декодером: у вариантов 96–512 px они меньше нижней
    /// границы <c>ImageFileInspector</c> (300 px), поэтому инспектор для этой проверки
    /// не подходит.
    /// </summary>
    /// <param name="content">Байты изображения из ответа API.</param>
    /// <returns>Ширина и высота изображения.</returns>
    /// <exception cref="InvalidOperationException">Ответ не является декодируемым изображением.</exception>
    public static (int Width, int Height) Dimensions(byte[] content)
    {
        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);

        if (codec is null)
        {
            throw new InvalidOperationException("The response is not a decodable image.");
        }

        return (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>
    /// Ожидаемые стороны производного варианта: ширина целевая, высота — пропорция
    /// исходника с округлением, как в генераторе.
    /// </summary>
    /// <param name="sourceWidth">Ширина исходника.</param>
    /// <param name="sourceHeight">Высота исходника.</param>
    /// <param name="targetWidth">Целевая ширина варианта.</param>
    /// <returns>Ширина и высота варианта.</returns>
    public static (int Width, int Height) VariantDimensions(
        int sourceWidth,
        int sourceHeight,
        int targetWidth)
    {
        var height = Math.Max(
            1,
            (int)Math.Round(sourceHeight * ((double)targetWidth / sourceWidth)));

        return (targetWidth, height);
    }
}
