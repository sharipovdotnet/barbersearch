using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Domain;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Проверка загружаемого файла по сигнатуре и заголовку: без декодирования и без I/O.
/// </summary>
public class ImageFileInspectorTests
{
    [Fact]
    public void Png_ReturnsFormatContentTypeAndDimensions()
    {
        var result = ImageFileInspector.Inspect(TestImages.Png(800, 600));

        Assert.True(result.IsSuccess);

        var info = result.Value;

        Assert.Equal(PhotoFormat.Png, info.Format);
        Assert.Equal("image/png", info.ContentType);
        Assert.Equal("png", info.Extension);
        Assert.Equal(800, info.Width);
        Assert.Equal(600, info.Height);
    }

    [Fact]
    public void Jpeg_ReturnsFormatContentTypeAndDimensions()
    {
        var result = ImageFileInspector.Inspect(TestImages.Jpeg(1200, 900));

        Assert.True(result.IsSuccess);

        var info = result.Value;

        Assert.Equal(PhotoFormat.Jpeg, info.Format);
        Assert.Equal("image/jpeg", info.ContentType);
        Assert.Equal("jpg", info.Extension);
        Assert.Equal(1200, info.Width);
        Assert.Equal(900, info.Height);
    }

    [Fact]
    public void WebP_Lossless_ReturnsDimensions()
    {
        var result = ImageFileInspector.Inspect(TestImages.WebPLossless(1024, 768));

        Assert.True(result.IsSuccess);
        Assert.Equal(PhotoFormat.WebP, result.Value.Format);
        Assert.Equal("image/webp", result.Value.ContentType);
        Assert.Equal("webp", result.Value.Extension);
        Assert.Equal(1024, result.Value.Width);
        Assert.Equal(768, result.Value.Height);
    }

    [Fact]
    public void WebP_Lossy_ReturnsDimensions()
    {
        var result = ImageFileInspector.Inspect(TestImages.WebPLossy(640, 480));

        Assert.True(result.IsSuccess);
        Assert.Equal(640, result.Value.Width);
        Assert.Equal(480, result.Value.Height);
    }

    [Fact]
    public void WebP_Extended_ReturnsCanvasDimensions()
    {
        var result = ImageFileInspector.Inspect(TestImages.WebPExtended(1920, 1080));

        Assert.True(result.IsSuccess);
        Assert.Equal(1920, result.Value.Width);
        Assert.Equal(1080, result.Value.Height);
    }

    [Fact]
    public void SmallerThanOneKilobyte_ReturnsEmpty()
    {
        AssertFailure(
            ImageFileInspector.Inspect(TestImages.SmallerThanMinimalFile()),
            "barberProfile.uploadPhoto.empty");
    }

    [Fact]
    public void LargerThanFiveMegabytes_ReturnsTooLarge()
    {
        AssertFailure(
            ImageFileInspector.Inspect(TestImages.LargerThanLimit()),
            "barberProfile.uploadPhoto.tooLarge");
    }

    [Fact]
    public void BadSignature_ReturnsUnsupportedType()
    {
        AssertFailure(
            ImageFileInspector.Inspect(TestImages.WithBadSignature()),
            "barberProfile.uploadPhoto.unsupportedType");
    }

    [Fact]
    public void Svg_ReturnsUnsupportedType()
    {
        AssertFailure(
            ImageFileInspector.Inspect(TestImages.Svg()),
            "barberProfile.uploadPhoto.unsupportedType");
    }

    [Fact]
    public void Gif_ReturnsUnsupportedType()
    {
        AssertFailure(
            ImageFileInspector.Inspect(TestImages.Gif()),
            "barberProfile.uploadPhoto.unsupportedType");
    }

    [Fact]
    public void TruncatedJpeg_ReturnsInvalidImage()
    {
        AssertFailure(
            ImageFileInspector.Inspect(TestImages.TruncatedJpeg()),
            "barberProfile.uploadPhoto.invalidImage");
    }

    [Fact]
    public void PngWithoutIhdr_ReturnsInvalidImage()
    {
        var content = TestImages.Png(800, 600);
        content[12] = 0x00;

        AssertFailure(
            ImageFileInspector.Inspect(content),
            "barberProfile.uploadPhoto.invalidImage");
    }

    [Theory]
    [InlineData(299, 600)]
    [InlineData(600, 299)]
    [InlineData(6001, 600)]
    [InlineData(600, 6001)]
    [InlineData(6000, 5000)]
    public void DimensionsOutsideAllowedRange_ReturnDimensionsError(int width, int height)
    {
        AssertFailure(
            ImageFileInspector.Inspect(TestImages.Png(width, height)),
            "barberProfile.uploadPhoto.dimensions");
    }

    [Fact]
    public void DimensionsAtMinimum_AreAccepted()
    {
        var result = ImageFileInspector.Inspect(TestImages.Png(300, 300));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void DimensionsAtExactlyTwentyFourMegapixels_AreAccepted()
    {
        var result = ImageFileInspector.Inspect(TestImages.Png(6000, 4000));

        Assert.True(result.IsSuccess);
    }

    private static void AssertFailure(
        CSharpFunctionalExtensions.Result<ImageFileInfo, Shared.Failure> result,
        string expectedCode)
    {
        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Error.Errors);

        Assert.Equal(expectedCode, error.Code);
        Assert.Equal("file", error.InvalidField);
    }
}
