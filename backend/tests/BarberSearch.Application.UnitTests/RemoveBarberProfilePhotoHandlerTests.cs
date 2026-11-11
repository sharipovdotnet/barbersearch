using BarberSearch.Application.BarberProfiles.RemoveBarberProfilePhoto;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Удаление фотографии: файл удаляется после успешного сохранения, а сбой удаления
/// не превращает <c>204</c> в <c>500</c>.
/// </summary>
public class RemoveBarberProfilePhotoHandlerTests
{
    [Fact]
    public async Task Remove_AfterSuccessfulSave_DeletesFile()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        profile.AddPhoto("https://cdn/1.jpg");
        var photo = profile.PortfolioPhotos.Single();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, ownerId).Handle(
            new RemoveBarberProfilePhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(profile.PortfolioPhotos);
        Assert.Equal(photo.Url, Assert.Single(storage.DeletedUrls));
        Assert.Equal(1, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Remove_WhenFileDeleteFails_StillSucceeds()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        profile.AddPhoto("https://cdn/1.jpg");
        var photo = profile.PortfolioPhotos.Single();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var storage = new FakePhotoStorage
        {
            DeleteException = new IOException("The file is locked.")
        };

        var result = await CreateHandler(repository, storage, ownerId).Handle(
            new RemoveBarberProfilePhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(profile.PortfolioPhotos);
        Assert.Single(storage.DeletedUrls);
    }

    [Fact]
    public async Task Remove_WhenRepositoryFails_DoesNotDeleteFile()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        profile.AddPhoto("https://cdn/1.jpg");
        var photo = profile.PortfolioPhotos.Single();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        repository.UpdateBehavior = _ => new Failure(Error.Failure(
            "internal.database",
            "The database rejected the change."));
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, ownerId).Handle(
            new RemoveBarberProfilePhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(storage.DeletedUrls);
    }

    [Fact]
    public async Task Remove_WhenPhotoMissing_ReturnsNotFound()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        profile.AddPhoto("https://cdn/1.jpg");
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var storage = new FakePhotoStorage();

        var result = await CreateHandler(repository, storage, ownerId).Handle(
            new RemoveBarberProfilePhotoCommand(profile.Id, Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("barberProfile.removePhoto.notFound", result.Error.Errors[0].Code);
        Assert.Empty(storage.DeletedUrls);
    }

    private static RemoveBarberProfilePhotoHandler CreateHandler(
        FakeBarberProfileRepository repository,
        FakePhotoStorage storage,
        Guid userId)
        => new(
            repository,
            storage,
            new FakeCurrentUser(userId),
            NullLogger<RemoveBarberProfilePhotoHandler>.Instance);

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
