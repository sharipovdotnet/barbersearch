using BarberSearch.Domain;
using BarberSearch.Domain.PortfolioPhotos;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

public class PortfolioMediaUrlTests
{
    private static readonly Guid ProfileId = Guid.CreateVersion7();
    private static readonly Guid MediaId = Guid.CreateVersion7();

    [Theory]
    [InlineData("jpg")]
    [InlineData("png")]
    [InlineData("webp")]
    public void BuildAndTryParse_RoundTrip(string extension)
    {
        var url = PortfolioMediaUrl.Build(ProfileId, MediaId, extension);

        var parsed = PortfolioMediaUrl.TryParse(
            url,
            ProfileId,
            out var mediaId,
            out var parsedExtension);

        Assert.True(parsed);
        Assert.Equal(MediaId, mediaId);
        Assert.Equal(extension, parsedExtension);
        Assert.StartsWith(
            Constants.PORTFOLIO_MEDIA_PATH_PREFIX + "/",
            url,
            StringComparison.Ordinal);

        // Перегрузка для хранилища восстанавливает профиль из самой строки.
        var parsedWithoutProfile = PortfolioMediaUrl.TryParse(
            url,
            out var parsedProfileId,
            out var mediaIdFromUrl,
            out var extensionFromUrl);

        Assert.True(parsedWithoutProfile);
        Assert.Equal(ProfileId, parsedProfileId);
        Assert.Equal(MediaId, mediaIdFromUrl);
        Assert.Equal(extension, extensionFromUrl);
    }

    [Fact]
    public void Build_UsesCanonicalLowercaseSegments()
    {
        var url = PortfolioMediaUrl.Build(ProfileId, MediaId, "jpg");

        Assert.Contains($"/{ProfileId:D}/", url, StringComparison.Ordinal);
        Assert.Contains($"/{MediaId:N}.jpg", url, StringComparison.Ordinal);
        Assert.DoesNotContain($"{MediaId:D}", url, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_RejectsUrlOfAnotherProfile()
    {
        var url = PortfolioMediaUrl.Build(Guid.CreateVersion7(), MediaId, "jpg");

        var parsed = PortfolioMediaUrl.TryParse(
            url,
            ProfileId,
            out var mediaId,
            out var extension);

        Assert.False(parsed);
        Assert.Equal(Guid.Empty, mediaId);
        Assert.Equal(string.Empty, extension);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/api/media/portfolio")]
    [InlineData("/api/media/portfolio/")]
    [InlineData("/api/media/portfolio/x/y.jpg")]
    public void TryParse_RejectsMalformedUrls(string url)
    {
        Assert.False(PortfolioMediaUrl.TryParse(
            url,
            ProfileId,
            out _,
            out _));
    }

    [Fact]
    public void TryParse_RejectsEmptyProfileId()
    {
        var url = PortfolioMediaUrl.Build(Guid.CreateVersion7(), MediaId, "jpg");

        Assert.False(PortfolioMediaUrl.TryParse(
            url,
            Guid.Empty,
            out _,
            out _));
    }
}
