using System.Text;
using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Web.Storage;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using SkiaSharp;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Генератор WebP-вариантов: ширины, пропорции, отсутствие метаданных и отказы.
/// </summary>
/// <remarks>
/// Проверяется настоящий конвейер SkiaSharp без файловой системы: на вход идут байты
/// изображения, на выходе — байты WebP. Так тест ловит и ошибку масштаба, и потерю
/// прозрачности, и падение нативной библиотеки (на Linux CI — тех же ассетов).
/// </remarks>
public class PhotoThumbnailerTests
{
    private readonly PhotoThumbnailer _thumbnailer =
        new(NullLogger<PhotoThumbnailer>.Instance);

    private readonly Guid _ownerId = Guid.CreateVersion7();
    private readonly Guid _mediaId = Guid.CreateVersion7();

    [Fact]
    public async Task GenerateAsync_Jpeg_ProducesAllWidthsAsWebp()
    {
        var source = CreateJpeg(1600, 1200);

        var generated = await GenerateAsync(source, isAvatar: false);

        Assert.True(generated.IsSuccess);
        Assert.Equal(3, generated.Value.Count);

        var small = Assert.Single(generated.Value, item => item.Variant == PhotoVariant.Small);
        Assert.Equal(PhotoVariantWidths.PORTFOLIO_SMALL, small.Width);
        Assert.Equal(192, small.Height);
        AssertWebp(small.Content.Span);

        var medium = Assert.Single(generated.Value, item => item.Variant == PhotoVariant.Medium);
        Assert.Equal(PhotoVariantWidths.PORTFOLIO_MEDIUM, medium.Width);
        Assert.Equal(384, medium.Height);

        var large = Assert.Single(generated.Value, item => item.Variant == PhotoVariant.Large);
        Assert.Equal(PhotoVariantWidths.PORTFOLIO_LARGE, large.Width);
        AssertWebp(large.Content.Span);

        // Производные меньше исходника и не содержат EXIF: кодирование идёт из пикселей.
        Assert.True(small.Content.Length < source.Length);
        AssertNoExif(small.Content.Span);
    }

    [Fact]
    public async Task GenerateAsync_PortraitSource_KeepsProportions()
    {
        var generated = await GenerateAsync(CreateJpeg(800, 1600), isAvatar: false);

        Assert.True(generated.IsSuccess);

        var small = Assert.Single(generated.Value, item => item.Variant == PhotoVariant.Small);
        Assert.Equal(256, small.Width);
        Assert.Equal(512, small.Height);
    }

    [Fact]
    public async Task GenerateAsync_SourceNarrowerThanLarge_ProducesOnlyFeasibleWidths()
    {
        var generated = await GenerateAsync(CreateJpeg(400, 300), isAvatar: false);

        Assert.True(generated.IsSuccess);

        var thumbnail = Assert.Single(generated.Value);
        Assert.Equal(PhotoVariant.Small, thumbnail.Variant);
        Assert.Equal(256, thumbnail.Width);
        Assert.Equal(192, thumbnail.Height);
    }

    [Fact]
    public async Task GenerateAsync_MinimalAcceptedSource_ProducesOnlySmallVariant()
    {
        var generated = await GenerateAsync(CreateJpeg(300, 300), isAvatar: false);

        Assert.True(generated.IsSuccess);

        // Инспектор требует не меньше 300 px, а самый маленький вариант — 256 px, поэтому
        // у любого принятого файла есть хотя бы он. Апскейла по-прежнему нет.
        var thumbnail = Assert.Single(generated.Value);
        Assert.Equal(PhotoVariant.Small, thumbnail.Variant);
        Assert.Equal(256, thumbnail.Width);
        Assert.Equal(256, thumbnail.Height);
    }

    [Fact]
    public async Task GenerateAsync_SourceBelowInspectorMinimum_ReturnsFailure()
    {
        // 200 px не проходит инспектор загрузки: это отказ, а не «вариантов не будет».
        // Наружу генератор отдаёт один внутренний код — клиент его не видит, хранилище
        // просто отдаёт исходник.
        var generated = await GenerateAsync(CreateJpeg(200, 200), isAvatar: false);

        Assert.True(generated.IsFailure);
        Assert.Equal("photoThumbnail.generation", generated.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GenerateAsync_Avatar_UseAvatarWidths()
    {
        var generated = await GenerateAsync(CreateJpeg(512, 512), isAvatar: true);

        Assert.True(generated.IsSuccess);
        Assert.Equal(3, generated.Value.Count);
        Assert.Equal(96, Assert.Single(generated.Value, item => item.Variant == PhotoVariant.Small).Width);
        Assert.Equal(192, Assert.Single(generated.Value, item => item.Variant == PhotoVariant.Medium).Width);
        Assert.Equal(384, Assert.Single(generated.Value, item => item.Variant == PhotoVariant.Large).Width);
    }

    [Fact]
    public async Task GenerateAsync_PngWithTransparency_UsesWhiteBackgroundWithoutAlpha()
    {
        var source = CreateTransparentPng(800, 800);

        var generated = await GenerateAsync(source, isAvatar: false);

        Assert.True(generated.IsSuccess);

        var small = Assert.Single(generated.Value, item => item.Variant == PhotoVariant.Small);

        using var decoded = SKBitmap.Decode(small.Content.Span.ToArray());

        Assert.NotNull(decoded);

        // Угол исходника был прозрачным: в варианте он обязан стать белым, а не чёрным.
        var corner = decoded.GetPixel(0, 0);

        Assert.Equal(255, corner.Alpha);
        Assert.True(corner.Red > 240 && corner.Green > 240 && corner.Blue > 240);
    }

    [Fact]
    public async Task GenerateAsync_BrokenFile_ReturnsFailureInsteadOfThrowing()
    {
        var generated = await GenerateAsync(CreateUndecodablePng(), isAvatar: false);

        Assert.True(generated.IsFailure);
        Assert.Equal("photoThumbnail.generation", generated.Error.Errors[0].Code);
    }

    /// <summary>
    /// Прогон генератора с фиксированным владельцем и медиаидентификатором.
    /// </summary>
    private Task<Result<IReadOnlyList<PhotoThumbnail>, Failure>> GenerateAsync(
        byte[] source,
        bool isAvatar)
        => _thumbnailer.GenerateAsync(
            source,
            "png",
            _ownerId,
            _mediaId,
            isAvatar,
            CancellationToken.None);

    /// <summary>
    /// Проверяет контейнер WebP: <c>RIFF</c>, затем <c>WEBP</c> — как у файла с диска.
    /// </summary>
    private static void AssertWebp(ReadOnlySpan<byte> content)
    {
        Assert.True(content.Length > 12);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(content[..4].ToArray()));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(content.Slice(8, 4).ToArray()));
    }

    /// <summary>
    /// Метаданные EXIF в производном файле отсутствуют: байты сняты с пикселей, а не с контейнера.
    /// </summary>
    private static void AssertNoExif(ReadOnlySpan<byte> content)
        => Assert.DoesNotContain(
            "Exif",
            Encoding.Latin1.GetString(content.ToArray()),
            StringComparison.Ordinal);

    /// <summary>
    /// JPEG заданных размеров: сплошной фон и фигура, чтобы кодировщику было что сжимать.
    /// </summary>
    private static byte[] CreateJpeg(int width, int height)
    {
        using var bitmap = CreateBitmap(width, height);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);

        return data.ToArray();
    }

    /// <summary>
    /// PNG с прозрачным фоном и непрозрачным квадратом в центре.
    /// </summary>
    private static byte[] CreateTransparentPng(int width, int height)
    {
        using var bitmap = CreateBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    /// <summary>
    /// PNG с корректными сигнатурой и <c>IHDR</c>, но с мусором вместо пикселей: инспектор
    /// такой файл принимает, а декодер обязан отказать.
    /// </summary>
    private static byte[] CreateUndecodablePng()
    {
        var content = new byte[2048];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        signature.CopyTo(content);
        "IHDR"u8.CopyTo(content.AsSpan(12));
        content[16] = 0x00;
        content[17] = 0x00;
        content[18] = 0x03;
        content[19] = 0x20;
        content[20] = 0x00;
        content[21] = 0x00;
        content[22] = 0x02;
        content[23] = 0x58;

        return content;
    }

    /// <summary>
    /// Битмап с прозрачным фоном и оранжевым кругом: общий источник для JPEG и PNG.
    /// </summary>
    private static SKBitmap CreateBitmap(int width, int height)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint { Color = SKColors.OrangeRed, IsAntialias = true };

        canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);

        return bitmap;
    }
}
