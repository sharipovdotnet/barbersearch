using BarberSearch.Domain;
using BarberSearch.Domain.Users;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

public class UserAvatarMediaUrlTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid MediaId = Guid.CreateVersion7();

    [Theory]
    [InlineData("jpg")]
    [InlineData("png")]
    [InlineData("webp")]
    public void BuildAndTryParse_RoundTrip(string extension)
    {
        var url = UserAvatarMediaUrl.Build(UserId, MediaId, extension);

        var parsed = UserAvatarMediaUrl.TryParse(
            url,
            UserId,
            out var mediaId,
            out var parsedExtension);

        Assert.True(parsed);
        Assert.Equal(MediaId, mediaId);
        Assert.Equal(extension, parsedExtension);
        Assert.StartsWith(
            Constants.AVATAR_MEDIA_PATH_PREFIX + "/",
            url,
            StringComparison.Ordinal);

        // Перегрузка для хранилища восстанавливает пользователя из самой строки.
        var parsedWithoutUser = UserAvatarMediaUrl.TryParse(
            url,
            out var parsedUserId,
            out var mediaIdFromUrl,
            out var extensionFromUrl);

        Assert.True(parsedWithoutUser);
        Assert.Equal(UserId, parsedUserId);
        Assert.Equal(MediaId, mediaIdFromUrl);
        Assert.Equal(extension, extensionFromUrl);
    }

    [Fact]
    public void Build_UsesCanonicalLowercaseSegments()
    {
        var url = UserAvatarMediaUrl.Build(UserId, MediaId, "jpg");

        Assert.Contains($"/{UserId:D}/", url, StringComparison.Ordinal);
        Assert.Contains($"/{MediaId:N}.jpg", url, StringComparison.Ordinal);
        Assert.DoesNotContain($"{MediaId:D}", url, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_RejectsUrlOfAnotherUser()
    {
        var url = UserAvatarMediaUrl.Build(Guid.CreateVersion7(), MediaId, "jpg");

        var parsed = UserAvatarMediaUrl.TryParse(
            url,
            UserId,
            out var mediaId,
            out var extension);

        Assert.False(parsed);
        Assert.Equal(Guid.Empty, mediaId);
        Assert.Equal(string.Empty, extension);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/api/media/avatars")]
    [InlineData("/api/media/avatars/")]
    [InlineData("/api/media/avatars/x/y.jpg")]
    [InlineData("/api/media/portfolio/{0}/{1}.jpg")]
    public void TryParse_RejectsMalformedUrls(string template)
    {
        var url = template
            .Replace("{0}", UserId.ToString("D"), StringComparison.Ordinal)
            .Replace("{1}", MediaId.ToString("N"), StringComparison.Ordinal);

        Assert.False(UserAvatarMediaUrl.TryParse(url, UserId, out _, out _));
        Assert.False(UserAvatarMediaUrl.TryParse(url, out _, out _, out _));
    }

    [Fact]
    public void TryParse_RejectsEmptyUserId()
    {
        var url = UserAvatarMediaUrl.Build(Guid.CreateVersion7(), MediaId, "jpg");

        Assert.False(UserAvatarMediaUrl.TryParse(url, Guid.Empty, out _, out _));
    }

    [Fact]
    public void TryParse_RejectsMediaIdInUpperCase()
    {
        var fileName = MediaId.ToString("N").ToUpperInvariant();
        var url = $"{Constants.AVATAR_MEDIA_PATH_PREFIX}/{UserId:D}/{fileName}.jpg";

        Assert.False(UserAvatarMediaUrl.TryParse(url, UserId, out _, out _));
    }

    [Fact]
    public void TryParse_RejectsUserIdInUpperCase()
    {
        var upperUserId = UserId.ToString("D").ToUpperInvariant();
        var url = $"{Constants.AVATAR_MEDIA_PATH_PREFIX}/{upperUserId}/{MediaId:N}.jpg";

        Assert.False(UserAvatarMediaUrl.TryParse(url, UserId, out _, out _));
    }

    [Theory]
    [InlineData("%2e%2e")]
    [InlineData("..")]
    [InlineData("?query=1")]
    [InlineData("#fragment")]
    [InlineData("\\..\\")]
    public void TryParse_RejectsUnsafeCharacters(string injection)
    {
        var url =
            $"{Constants.AVATAR_MEDIA_PATH_PREFIX}/{UserId:D}/{injection}{MediaId:N}.jpg";

        Assert.False(UserAvatarMediaUrl.TryParse(url, UserId, out _, out _));
    }

    [Fact]
    public void TryParse_RejectsExtraSegment()
    {
        var url = $"{Constants.AVATAR_MEDIA_PATH_PREFIX}/{UserId:D}/{MediaId:N}.jpg/extra";

        Assert.False(UserAvatarMediaUrl.TryParse(url, UserId, out _, out _));
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("bmp")]
    [InlineData("JPG")]
    [InlineData("")]
    public void TryParse_RejectsUnsupportedExtension(string extension)
    {
        var suffix = string.IsNullOrEmpty(extension) ? string.Empty : $".{extension}";
        var url = $"{Constants.AVATAR_MEDIA_PATH_PREFIX}/{UserId:D}/{MediaId:N}{suffix}";

        Assert.False(UserAvatarMediaUrl.TryParse(url, UserId, out _, out _));
    }

    [Fact]
    public void TryParse_RejectsEmptyGuidAndNonHexFileName()
    {
        var emptyGuid = $"{Constants.AVATAR_MEDIA_PATH_PREFIX}/{UserId:D}/{new string('0', 32)}.jpg";
        var nonHex = $"{Constants.AVATAR_MEDIA_PATH_PREFIX}/{UserId:D}/{new string('z', 32)}.jpg";

        Assert.False(UserAvatarMediaUrl.TryParse(emptyGuid, UserId, out _, out _));
        Assert.False(UserAvatarMediaUrl.TryParse(nonHex, UserId, out _, out _));
    }

    [Fact]
    public void TryParse_WithExpectedUser_ProducesEmptyValuesOnFailure()
    {
        Assert.False(UserAvatarMediaUrl.TryParse(
            "https://cdn.example/avatar.jpg",
            UserId,
            out var mediaId,
            out var extension));

        Assert.Equal(Guid.Empty, mediaId);
        Assert.Equal(string.Empty, extension);
    }
}
