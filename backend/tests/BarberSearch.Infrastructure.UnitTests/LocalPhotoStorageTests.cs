using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Domain.Users;
using BarberSearch.Web.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Локальное файловое хранилище фотографий портфолио: запись, чтение, удаление и защита пути.
/// </summary>
/// <remarks>
/// Хранилище не проверяет содержимое файла — это делает <c>ImageFileInspector</c> до записи,
/// — поэтому тесту достаточно байтов заданной длины. Корень каждый раз временный: в дереве
/// репозитория <c>App_Data</c> не появляется.
/// </remarks>
public class LocalPhotoStorageTests : IDisposable
{
    private readonly string _rootPath =
        Path.Combine(Path.GetTempPath(), $"bs-photos-{Guid.NewGuid():N}");

    private readonly Guid _profileId = Guid.CreateVersion7();
    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly LocalPhotoStorage _storage;

    /// <summary>
    /// Готовит временный корень и хранилище над ним.
    /// </summary>
    public LocalPhotoStorageTests()
    {
        Directory.CreateDirectory(_rootPath);

        _storage = new LocalPhotoStorage(
            new PhotoStorageOptions { RootPath = _rootPath },
            NullLogger<LocalPhotoStorage>.Instance);
    }

    [Fact]
    public async Task SaveAsync_WritesFileUnderRootAndReturnsRelativeMediaUrl()
    {
        var mediaId = Guid.CreateVersion7();
        var content = CreateContent();

        var saved = await _storage.SaveAsync(
            _profileId,
            mediaId,
            "png",
            content,
            CancellationToken.None);

        Assert.True(saved.IsSuccess);
        Assert.Equal(
            PortfolioMediaUrl.Build(_profileId, mediaId, "png"),
            saved.Value.Url);

        var filePath = PhotoPath(mediaId, "png");
        Assert.True(File.Exists(filePath));
        Assert.Equal(content, await File.ReadAllBytesAsync(filePath));

        // Временный файл после атомарного переименования не остаётся.
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(filePath)!, "*.tmp"));
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsStreamContentTypeAndLength()
    {
        var mediaId = Guid.CreateVersion7();
        var content = CreateContent();

        var saved = await _storage.SaveAsync(
            _profileId,
            mediaId,
            "png",
            content,
            CancellationToken.None);

        var opened = await _storage.OpenReadAsync(saved.Value.Url, CancellationToken.None);

        Assert.True(opened.IsSuccess);
        Assert.Equal("image/png", opened.Value.ContentType);
        Assert.Equal(content.Length, opened.Value.Length);

        await using var stream = opened.Value.Stream;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);

        Assert.Equal(content, copy.ToArray());
    }

    [Theory]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    public async Task OpenReadAsync_ContentTypeIsDerivedFromTheExtension(
        string extension,
        string expectedContentType)
    {
        var mediaId = Guid.CreateVersion7();

        var saved = await _storage.SaveAsync(
            _profileId,
            mediaId,
            extension,
            CreateContent(),
            CancellationToken.None);

        var opened = await _storage.OpenReadAsync(saved.Value.Url, CancellationToken.None);

        Assert.True(opened.IsSuccess);
        Assert.Equal(expectedContentType, opened.Value.ContentType);

        await opened.Value.Stream.DisposeAsync();
    }

    [Fact]
    public async Task DeleteAsync_RemovesFileAndIsIdempotent()
    {
        var mediaId = Guid.CreateVersion7();

        var saved = await _storage.SaveAsync(
            _profileId,
            mediaId,
            "png",
            CreateContent(),
            CancellationToken.None);

        var filePath = PhotoPath(mediaId, "png");
        Assert.True(File.Exists(filePath));

        await _storage.DeleteAsync(saved.Value.Url, CancellationToken.None);
        Assert.False(File.Exists(filePath));

        // Повторное удаление отсутствующего файла — не ошибка.
        await _storage.DeleteAsync(saved.Value.Url, CancellationToken.None);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task DeleteAsync_IgnoresForeignAndExternalUrls()
    {
        await _storage.DeleteAsync("https://cdn.example/photo.png", CancellationToken.None);
        await _storage.DeleteAsync("/other/prefix/photo.png", CancellationToken.None);
        await _storage.DeleteAsync(string.Empty, CancellationToken.None);
    }

    [Fact]
    public async Task OpenReadAsync_MissingFile_ReturnsNotFound()
    {
        var url = PortfolioMediaUrl.Build(_profileId, Guid.CreateVersion7(), "png");

        var opened = await _storage.OpenReadAsync(url, CancellationToken.None);

        Assert.True(opened.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, opened.Error.Errors[0].Type);
    }

    [Fact]
    public async Task OpenReadAsync_NonMediaUrl_ReturnsNotFound()
    {
        var opened = await _storage.OpenReadAsync(
            "https://cdn.example/photo.png",
            CancellationToken.None);

        Assert.True(opened.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, opened.Error.Errors[0].Type);
    }

    [Theory]
    [InlineData("../../secret.png")]
    [InlineData("..%2F..%2Fsecret.png")]
    [InlineData("/api/media/portfolio/{0}/..%2F{Guid}.png")]
    [InlineData("C:\\Windows\\System32\\secret.png")]
    [InlineData("/api/media/portfolio/{0}/{Guid}.png?query=1")]
    [InlineData("/api/media/portfolio/{0}/{Guid}.PNG")]
    public async Task OpenReadAsync_RejectsTraversalForeignAndMalformedUrls(string template)
    {
        var url = template
            .Replace("{0}", _profileId.ToString("D"), StringComparison.Ordinal)
            .Replace("{Guid}", Guid.CreateVersion7().ToString("N"), StringComparison.Ordinal);

        var opened = await _storage.OpenReadAsync(url, CancellationToken.None);

        Assert.True(opened.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, opened.Error.Errors[0].Type);
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("bmp")]
    [InlineData("PNG")]
    [InlineData("")]
    public async Task SaveAsync_RejectsUnknownExtension(string extension)
    {
        var mediaId = Guid.CreateVersion7();

        var saved = await _storage.SaveAsync(
            _profileId,
            mediaId,
            extension,
            CreateContent(),
            CancellationToken.None);

        Assert.True(saved.IsFailure);
        Assert.Equal("barberProfile.uploadPhoto.storage", saved.Error.Errors[0].Code);

        var portfolioPath = Path.Combine(_rootPath, "portfolio", $"{_profileId:N}");
        Assert.False(Directory.Exists(portfolioPath));
    }

    [Fact]
    public async Task SaveAsync_RejectsEmptyIdentifiers()
    {
        var content = CreateContent();

        var emptyMedia = await _storage.SaveAsync(
            _profileId,
            Guid.Empty,
            "png",
            content,
            CancellationToken.None);

        var emptyProfile = await _storage.SaveAsync(
            Guid.Empty,
            Guid.CreateVersion7(),
            "png",
            content,
            CancellationToken.None);

        Assert.True(emptyMedia.IsFailure);
        Assert.True(emptyProfile.IsFailure);
        Assert.Equal("barberProfile.uploadPhoto.storage", emptyMedia.Error.Errors[0].Code);
        Assert.Equal("barberProfile.uploadPhoto.storage", emptyProfile.Error.Errors[0].Code);
    }

    [Fact]
    public async Task SaveAvatarAsync_WritesFileUnderAvatarsAndReturnsRelativeUrl()
    {
        var mediaId = Guid.CreateVersion7();
        var content = CreateContent();

        var saved = await _storage.SaveAvatarAsync(
            _userId,
            mediaId,
            "webp",
            content,
            CancellationToken.None);

        Assert.True(saved.IsSuccess);
        Assert.Equal(UserAvatarMediaUrl.Build(_userId, mediaId, "webp"), saved.Value.Url);

        var filePath = AvatarPath(mediaId, "webp");
        Assert.True(File.Exists(filePath));
        Assert.Equal(content, await File.ReadAllBytesAsync(filePath));

        // Временный файл после атомарного переименования не остаётся.
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(filePath)!, "*.tmp"));
    }

    [Theory]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    public async Task OpenReadAsync_AvatarContentTypeIsDerivedFromTheExtension(
        string extension,
        string expectedContentType)
    {
        var mediaId = Guid.CreateVersion7();

        var saved = await _storage.SaveAvatarAsync(
            _userId,
            mediaId,
            extension,
            CreateContent(),
            CancellationToken.None);

        var opened = await _storage.OpenReadAsync(saved.Value.Url, CancellationToken.None);

        Assert.True(opened.IsSuccess);
        Assert.Equal(expectedContentType, opened.Value.ContentType);
        Assert.Equal(CreateContent().Length, opened.Value.Length);

        await opened.Value.Stream.DisposeAsync();
    }

    [Fact]
    public async Task DeleteAsync_RemovesAvatarFileAndIsIdempotent()
    {
        var mediaId = Guid.CreateVersion7();

        var saved = await _storage.SaveAvatarAsync(
            _userId,
            mediaId,
            "png",
            CreateContent(),
            CancellationToken.None);

        var filePath = AvatarPath(mediaId, "png");
        Assert.True(File.Exists(filePath));

        await _storage.DeleteAsync(saved.Value.Url, CancellationToken.None);
        Assert.False(File.Exists(filePath));

        await _storage.DeleteAsync(saved.Value.Url, CancellationToken.None);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task DeleteAsync_ForeignAvatarUrl_DoesNotDeleteTheOwnFile()
    {
        var mediaId = Guid.CreateVersion7();

        await _storage.SaveAvatarAsync(
            _userId,
            mediaId,
            "png",
            CreateContent(),
            CancellationToken.None);

        var foreignUrl = UserAvatarMediaUrl.Build(
            Guid.CreateVersion7(),
            mediaId,
            "png");

        await _storage.DeleteAsync(foreignUrl, CancellationToken.None);

        Assert.True(File.Exists(AvatarPath(mediaId, "png")));
    }

    [Fact]
    public async Task OpenReadAsync_MissingAvatar_ReturnsAvatarMediaNotFound()
    {
        var url = UserAvatarMediaUrl.Build(_userId, Guid.CreateVersion7(), "png");

        var opened = await _storage.OpenReadAsync(url, CancellationToken.None);

        Assert.True(opened.IsFailure);

        var error = Assert.Single(opened.Error.Errors);

        Assert.Equal(ErrorType.NOT_FOUND, error.Type);
        Assert.Equal("avatarMedia.notFound", error.Code);
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("bmp")]
    [InlineData("PNG")]
    [InlineData("")]
    public async Task SaveAvatarAsync_RejectsUnknownExtension(string extension)
    {
        var saved = await _storage.SaveAvatarAsync(
            _userId,
            Guid.CreateVersion7(),
            extension,
            CreateContent(),
            CancellationToken.None);

        Assert.True(saved.IsFailure);
        Assert.Equal("user.uploadAvatar.storage", saved.Error.Errors[0].Code);
        Assert.False(Directory.Exists(Path.Combine(_rootPath, "avatars", $"{_userId:N}")));
    }

    [Fact]
    public async Task SaveAvatarAsync_RejectsEmptyIdentifiers()
    {
        var content = CreateContent();

        var emptyMedia = await _storage.SaveAvatarAsync(
            _userId,
            Guid.Empty,
            "png",
            content,
            CancellationToken.None);

        var emptyUser = await _storage.SaveAvatarAsync(
            Guid.Empty,
            Guid.CreateVersion7(),
            "png",
            content,
            CancellationToken.None);

        Assert.True(emptyMedia.IsFailure);
        Assert.True(emptyUser.IsFailure);
        Assert.Equal("user.uploadAvatar.storage", emptyMedia.Error.Errors[0].Code);
        Assert.Equal("user.uploadAvatar.storage", emptyUser.Error.Errors[0].Code);
    }

    [Theory]
    [InlineData("/api/media/avatars/{0}/../../../secret.png")]
    [InlineData("/api/media/avatars/{0}/{Guid}.PNG")]
    [InlineData("/api/media/avatars/{0}/{Guid}.png?query=1")]
    public async Task OpenReadAsync_RejectsTraversalAndMalformedAvatarUrls(string template)
    {
        var url = template.Replace("{0}", _userId.ToString("D"), StringComparison.Ordinal)
            .Replace("{Guid}", Guid.CreateVersion7().ToString("N"), StringComparison.Ordinal);

        var opened = await _storage.OpenReadAsync(url, CancellationToken.None);

        Assert.True(opened.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, opened.Error.Errors[0].Type);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Временный каталог — не предмет проверки; ОС уберёт его сама.
        }

        GC.SuppressFinalize(this);
    }

    private string PhotoPath(Guid mediaId, string extension)
        => Path.Combine(_rootPath, "portfolio", $"{_profileId:N}", $"{mediaId:N}.{extension}");

    private string AvatarPath(Guid mediaId, string extension)
        => Path.Combine(_rootPath, "avatars", $"{_userId:N}", $"{mediaId:N}.{extension}");

    private static byte[] CreateContent()
    {
        var content = new byte[2048];

        for (var index = 0; index < content.Length; index++)
        {
            content[index] = (byte)(index % 251);
        }

        return content;
    }
}
