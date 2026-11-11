using BarberSearch.Application.PortfolioPhotos.UploadPortfolioPhoto;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain;
using BarberSearch.Domain.BarberProfiles;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Загрузка фотографии: права, проверка файла, лимит, компенсация файла при ошибке записи.
/// </summary>
public class UploadPortfolioPhotoHandlerTests
{
    [Fact]
    public async Task Upload_ByOwner_SavesFileAndPhotoAndReturnsPhotoId()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var storage = new FakePhotoStorage();
        var content = TestImages.Png(800, 600);

        var result = await CreateHandler(repository, storage, ownerId).Handle(
            new UploadPortfolioPhotoCommand(profile.Id, content, "  Fade  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Equal(1, storage.SaveCallCount);
        Assert.Equal("png", Assert.Single(storage.SavedExtensions));
        Assert.Equal(content, Assert.Single(storage.SavedContents));

        var photo = Assert.Single(profile.PortfolioPhotos);

        Assert.Equal(result.Value, photo.Id);
        Assert.Equal(Assert.Single(storage.SavedUrls), photo.Url);
        Assert.Equal("Fade", photo.Description);
        Assert.Null(photo.ThumbnailUrl);
        Assert.Equal(0, photo.SortOrder);
        Assert.True(photo.IsPublished);
        Assert.False(photo.IsBlocked);
    }

    [Fact]
    public async Task Upload_WhenRepositoryFails_DeletesSavedFile()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        repository.UpdateBehavior = _ => new Failure(Error.Failure(
            "internal.database",
            "The database rejected the change."));
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, ownerId).Handle(
            new UploadPortfolioPhotoCommand(profile.Id, TestImages.Jpeg(800, 600), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("internal.database", result.Error.Errors[0].Code);
        Assert.Single(storage.SavedUrls);
        Assert.Equal(storage.SavedUrls, storage.DeletedUrls);
    }

    [Fact]
    public async Task Upload_WhenStorageFails_DoesNotCreateRow()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var storage = new FakePhotoStorage
        {
            SaveFailure = new Failure(Error.Failure(
                "barberProfile.uploadPhoto.storage",
                "The file could not be stored."))
        };

        var result = await CreateHandler(repository, storage, ownerId).Handle(
            new UploadPortfolioPhotoCommand(profile.Id, TestImages.Png(800, 600), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.uploadPhoto.storage", result.Error.Errors[0].Code);
        Assert.Empty(profile.PortfolioPhotos);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(0, storage.SaveCallCount);
        Assert.Empty(storage.DeletedUrls);
    }

    [Fact]
    public async Task Upload_OfForeignProfile_ReturnsForbiddenWithoutTouchingStorage()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, Guid.CreateVersion7()).Handle(
            new UploadPortfolioPhotoCommand(profile.Id, TestImages.Png(800, 600), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
        Assert.Equal(0, storage.SaveCallCount);
        Assert.Empty(profile.PortfolioPhotos);
    }

    [Fact]
    public async Task Upload_WhenPortfolioIsFull_ReturnsConflictBeforeSavingFile()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);

        for (var index = 0; index < Constants.MAX_PHOTOS_COUNT; index++)
        {
            Assert.True(profile.AddPhoto($"https://cdn/{index}.jpg").IsSuccess);
        }

        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, ownerId).Handle(
            new UploadPortfolioPhotoCommand(profile.Id, TestImages.Png(800, 600), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("barberProfile.addPhoto.limit", result.Error.Errors[0].Code);
        Assert.Equal(0, storage.SaveCallCount);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(Constants.MAX_PHOTOS_COUNT, profile.PortfolioPhotos.Count);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("unsupported")]
    public async Task Upload_WithInvalidFile_ReturnsValidationWithoutTouchingStorage(string kind)
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var storage = new FakePhotoStorage();
        var content = kind == "empty"
            ? TestImages.SmallerThanMinimalFile()
            : TestImages.Gif();

        var result = await CreateHandler(repository, storage, ownerId).Handle(
            new UploadPortfolioPhotoCommand(profile.Id, content, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(
            kind == "empty"
                ? "barberProfile.uploadPhoto.empty"
                : "barberProfile.uploadPhoto.unsupportedType",
            result.Error.Errors[0].Code);
        Assert.Equal(0, storage.SaveCallCount);
        Assert.Empty(profile.PortfolioPhotos);
    }

    private static UploadPortfolioPhotoHandler CreateHandler(
        FakeBarberProfileRepository repository,
        FakePhotoStorage storage,
        Guid userId)
        => new(
            repository,
            storage,
            new FakeCurrentUser(userId),
            NullLogger<UploadPortfolioPhotoHandler>.Instance);

    private static BarberProfile CreateProfile(Guid userId)
        => BarberProfile.Create(
            userId,
            "Master Barber",
            5,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null).Value;
}
