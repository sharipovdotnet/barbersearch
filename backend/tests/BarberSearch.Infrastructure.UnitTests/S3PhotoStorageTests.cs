using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.UnitTests.Fakes;
using BarberSearch.Web.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using SkiaSharp;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// S3-хранилище фотографий: ключи, байты, варианты, ленивую генерацию и удаление — без сети.
/// </summary>
/// <remarks>
/// SDK не участвует: адаптер работает через <see cref="IPhotoObjectStore"/>, поэтому фейк
/// в памяти проверяет именно раскладку ключей и контракт ответов. Живой MinIO — отдельные
/// тесты по env-флагу.
/// </remarks>
public class S3PhotoStorageTests
{
    private readonly FakePhotoObjectStore _store = new();
    private readonly Guid _profileId = Guid.CreateVersion7();
    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly S3PhotoStorage _storage;

    /// <summary>
    /// Готовит адаптер над фейковым хранилищем и настоящим генератором вариантов.
    /// </summary>
    public S3PhotoStorageTests()
    {
        _storage = new S3PhotoStorage(
            _store,
            new PhotoThumbnailer(NullLogger<PhotoThumbnailer>.Instance),
            NullLogger<S3PhotoStorage>.Instance);
    }

    [Fact]
    public async Task SaveAsync_WritesOriginalUnderPortfolioKeyAndReturnsRelativeUrl()
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
        Assert.Equal(PortfolioMediaUrl.Build(_profileId, mediaId, "png"), saved.Value.Url);

        var key = PortfolioKey(mediaId, "png");
        Assert.True(_store.Contains(key));
        Assert.Equal(content, _store.Content(key));
        Assert.Equal("image/png", _store.ContentType(key));
    }

    [Fact]
    public async Task SaveAsync_WritesEveryGeneratedVariantAsWebp()
    {
        var mediaId = Guid.CreateVersion7();

        var saved = await _storage.SaveAsync(
            _profileId,
            mediaId,
            "jpg",
            CreateJpeg(1600, 1200),
            CancellationToken.None);

        Assert.True(saved.IsSuccess);

        foreach (var suffix in new[] { "sm", "md", "lg" })
        {
            var key = PortfolioVariantKey(mediaId, suffix);
            Assert.True(_store.Contains(key));
            Assert.Equal("image/webp", _store.ContentType(key));
            Assert.NotEmpty(_store.Content(key));
        }
    }

    [Fact]
    public async Task SaveAsync_WhenVariantWriteFails_KeepsOriginalAndSucceeds()
    {
        var mediaId = Guid.CreateVersion7();

        _store.FailPutsWhere(key => key.EndsWith("-sm.webp", StringComparison.Ordinal));

        var saved = await _storage.SaveAsync(
            _profileId,
            mediaId,
            "jpg",
            CreateJpeg(1600, 1200),
            CancellationToken.None);

        Assert.True(saved.IsSuccess);
        Assert.True(_store.Contains(PortfolioKey(mediaId, "jpg")));

        // Остальные варианты записались: сбой одного не отменяет загрузку.
        Assert.True(_store.Contains(PortfolioVariantKey(mediaId, "md")));
        Assert.True(_store.Contains(PortfolioVariantKey(mediaId, "lg")));
    }

    [Fact]
    public async Task SaveAsync_WhenOriginalWriteFails_ReturnsStorageFailureWithoutKey()
    {
        var mediaId = Guid.CreateVersion7();
        _store.FailPutsWhere(_ => true);

        var saved = await _storage.SaveAsync(
            _profileId,
            mediaId,
            "png",
            CreateContent(),
            CancellationToken.None);

        Assert.True(saved.IsFailure);

        var error = saved.Error.Errors[0];
        Assert.Equal("barberProfile.uploadPhoto.storage", error.Code);
        Assert.DoesNotContain(mediaId.ToString("N"), error.Message + error.Code);
        Assert.DoesNotContain("portfolio", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bucket", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAvatarAsync_WritesUnderAvatarsKeyAndUsesAvatarFailureCode()
    {
        var mediaId = Guid.CreateVersion7();

        var saved = await _storage.SaveAvatarAsync(
            _userId,
            mediaId,
            "png",
            CreateContent(),
            CancellationToken.None);

        Assert.True(saved.IsSuccess);
        Assert.Equal(UserAvatarMediaUrl.Build(_userId, mediaId, "png"), saved.Value.Url);
        Assert.True(_store.Contains(AvatarKey(mediaId, "png")));

        var failedMediaId = Guid.CreateVersion7();
        _store.FailPutsWhere(_ => true);

        var failed = await _storage.SaveAvatarAsync(
            _userId,
            failedMediaId,
            "png",
            CreateContent(),
            CancellationToken.None);

        Assert.True(failed.IsFailure);
        Assert.Equal("user.uploadAvatar.storage", failed.Error.Errors[0].Code);
    }

    [Fact]
    public async Task OpenReadAsync_Original_ReturnsBytesContentTypeLengthAndEntityTag()
    {
        var mediaId = Guid.CreateVersion7();
        var content = CreateContent();

        await _storage.SaveAsync(_profileId, mediaId, "jpg", content, CancellationToken.None);

        var opened = await _storage.OpenReadAsync(
            PortfolioMediaUrl.Build(_profileId, mediaId, "jpg"),
            PhotoVariant.Original,
            CancellationToken.None);

        Assert.True(opened.IsSuccess);
        Assert.Equal("image/jpeg", opened.Value.ContentType);
        Assert.Equal(content.Length, opened.Value.Length);
        Assert.Equal(
            PhotoVariantWidths.EntityTag(mediaId, PhotoVariant.Original),
            opened.Value.EntityTag);

        await using var stream = opened.Value.Stream;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);

        Assert.Equal(content, copy.ToArray());
    }

    [Fact]
    public async Task OpenReadAsync_ReadyVariant_ReadsOnlyTheVariant()
    {
        var mediaId = Guid.CreateVersion7();

        await _storage.SaveAsync(
            _profileId,
            mediaId,
            "jpg",
            CreateJpeg(1600, 1200),
            CancellationToken.None);

        _store.ResetCalls();

        var opened = await _storage.OpenReadAsync(
            PortfolioMediaUrl.Build(_profileId, mediaId, "jpg"),
            PhotoVariant.Medium,
            CancellationToken.None);

        Assert.True(opened.IsSuccess);
        Assert.Equal("image/webp", opened.Value.ContentType);
        Assert.Equal(
            PhotoVariantWidths.EntityTag(mediaId, PhotoVariant.Medium),
            opened.Value.EntityTag);
        Assert.Equal(PortfolioVariantKey(mediaId, "md"), Assert.Single(_store.GetKeys));

        await opened.Value.Stream.DisposeAsync();
    }

    [Fact]
    public async Task OpenReadAsync_MissingVariant_GeneratesAndWritesIt()
    {
        var mediaId = Guid.CreateVersion7();
        var original = CreateJpeg(1600, 1200);

        await _storage.SaveAsync(_profileId, mediaId, "jpg", original, CancellationToken.None);

        // Имитация кадра, загруженного до среза: производных объектов нет.
        foreach (var suffix in new[] { "sm", "md", "lg" })
        {
            _store.Remove(PortfolioVariantKey(mediaId, suffix));
        }

        _store.ResetCalls();

        var opened = await _storage.OpenReadAsync(
            PortfolioMediaUrl.Build(_profileId, mediaId, "jpg"),
            PhotoVariant.Medium,
            CancellationToken.None);

        Assert.True(opened.IsSuccess);
        Assert.Equal("image/webp", opened.Value.ContentType);
        Assert.Equal(
            PhotoVariantWidths.EntityTag(mediaId, PhotoVariant.Medium),
            opened.Value.EntityTag);

        // Запрошенный вариант и остальные доступные догенерированы, исходник не тронут.
        Assert.True(_store.Contains(PortfolioVariantKey(mediaId, "md")));
        Assert.Equal(original, _store.Content(PortfolioKey(mediaId, "jpg")));

        await opened.Value.Stream.DisposeAsync();
    }

    [Fact]
    public async Task OpenReadAsync_WhenOriginalIsNarrowerThanTheVariant_ReturnsOriginal()
    {
        var mediaId = Guid.CreateVersion7();

        await _storage.SaveAsync(
            _profileId,
            mediaId,
            "jpg",
            CreateJpeg(400, 300),
            CancellationToken.None);

        var opened = await _storage.OpenReadAsync(
            PortfolioMediaUrl.Build(_profileId, mediaId, "jpg"),
            PhotoVariant.Large,
            CancellationToken.None);

        Assert.True(opened.IsSuccess);
        Assert.Equal("image/jpeg", opened.Value.ContentType);
        Assert.Equal(
            PhotoVariantWidths.EntityTag(mediaId, PhotoVariant.Original),
            opened.Value.EntityTag);

        Assert.False(_store.Contains(PortfolioVariantKey(mediaId, "lg")));

        await opened.Value.Stream.DisposeAsync();
    }

    [Theory]
    [InlineData("https://cdn.example/photo.png")]
    [InlineData("/api/media/portfolio/not-a-guid/photo.png")]
    [InlineData("")]
    public async Task OpenReadAsync_ForeignUrl_ReturnsPortfolioNotFoundWithoutStorageCalls(
        string url)
    {
        var opened = await _storage.OpenReadAsync(url, PhotoVariant.Original, CancellationToken.None);

        Assert.True(opened.IsFailure);
        Assert.Equal("portfolioMedia.notFound", opened.Error.Errors[0].Code);
        Assert.Empty(_store.GetKeys);
    }

    [Fact]
    public async Task OpenReadAsync_MissingAvatar_ReturnsAvatarNotFound()
    {
        var url = UserAvatarMediaUrl.Build(_userId, Guid.CreateVersion7(), "png");

        var opened = await _storage.OpenReadAsync(url, PhotoVariant.Small, CancellationToken.None);

        Assert.True(opened.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, opened.Error.Errors[0].Type);
        Assert.Equal("avatarMedia.notFound", opened.Error.Errors[0].Code);
    }

    [Fact]
    public async Task DeleteAsync_DeletesOriginalAndThreeVariantsAndIsIdempotent()
    {
        var mediaId = Guid.CreateVersion7();
        var url = PortfolioMediaUrl.Build(_profileId, mediaId, "jpg");

        await _storage.SaveAsync(
            _profileId,
            mediaId,
            "jpg",
            CreateJpeg(1600, 1200),
            CancellationToken.None);

        _store.ResetCalls();

        await _storage.DeleteAsync(url, CancellationToken.None);

        Assert.Equal(4, _store.DeleteKeys.Count);
        Assert.Equal(0, _store.Count);

        // Повторное удаление — успех: отсутствие объекта не ошибка.
        await _storage.DeleteAsync(url, CancellationToken.None);
        Assert.Equal(8, _store.DeleteKeys.Count);

        // Чужой URL не трогает хранилище.
        await _storage.DeleteAsync("https://cdn.example/photo.png", CancellationToken.None);
        Assert.Equal(8, _store.DeleteKeys.Count);
    }

    [Fact]
    public async Task DeleteAsync_WhenStoreThrows_DoesNotThrow()
    {
        var mediaId = Guid.CreateVersion7();
        _store.FailDeletes = true;

        await _storage.DeleteAsync(
            PortfolioMediaUrl.Build(_profileId, mediaId, "jpg"),
            CancellationToken.None);
    }

    private string PortfolioKey(Guid mediaId, string extension)
        => $"portfolio/{_profileId:N}/{mediaId:N}.{extension}";

    private string PortfolioVariantKey(Guid mediaId, string suffix)
        => $"portfolio/{_profileId:N}/{mediaId:N}-{suffix}.webp";

    private string AvatarKey(Guid mediaId, string extension)
        => $"avatars/{_userId:N}/{mediaId:N}.{extension}";

    /// <summary>
    /// Настоящий JPEG заданных размеров: производные варианты создаются только из декодируемого
    /// файла, а инспектор требует не меньше 300 px по каждой стороне и не меньше 1 КиБ.
    /// </summary>
    private static byte[] CreateJpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);

            using var paint = new SKPaint { Color = SKColors.OrangeRed, IsAntialias = true };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);

        return data.ToArray();
    }

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
