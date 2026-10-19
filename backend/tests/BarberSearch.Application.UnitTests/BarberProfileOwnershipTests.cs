using BarberSearch.Application.BarberProfiles.ActivateBarberProfile;
using BarberSearch.Application.BarberProfiles.AddBarberProfilePhoto;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

public class BarberProfileOwnershipTests
{
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

    private static ActivateBarberProfileHandler CreateActivateHandler(
        FakeBarberProfileRepository repository,
        FakeCurrentUser currentUser)
        => new(repository, NullLogger<ActivateBarberProfileHandler>.Instance, currentUser);

    [Fact]
    public async Task Activate_ByProfileOwner_Succeeds()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);

        var result = await CreateActivateHandler(repository, new FakeCurrentUser(ownerId)).Handle(
            new ActivateBarberProfileCommand(profile.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Activate_ByStranger_ReturnsForbidden()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);

        var result = await CreateActivateHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7())).Handle(
            new ActivateBarberProfileCommand(profile.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Activate_ByAdmin_Succeeds()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);

        var result = await CreateActivateHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true)).Handle(
            new ActivateBarberProfileCommand(profile.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Activate_WhenProfileMissing_ReturnsNotFound()
    {
        var repository = new FakeBarberProfileRepository();

        var result = await CreateActivateHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true)).Handle(
            new ActivateBarberProfileCommand(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task AddPhoto_ByStranger_ReturnsForbidden()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var handler = new AddBarberProfilePhotoHandler(
            repository,
            NullLogger<AddBarberProfilePhotoHandler>.Instance,
            new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(
            new AddBarberProfilePhotoCommand(profile.Id, "https://example.com/photo.jpg", null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Empty(profile.PortfolioPhotos);
    }

    [Fact]
    public async Task AddPhoto_ByProfileOwner_AddsPhoto()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var handler = new AddBarberProfilePhotoHandler(
            repository,
            NullLogger<AddBarberProfilePhotoHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new AddBarberProfilePhotoCommand(profile.Id, "https://example.com/photo.jpg", null, "Fade"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(profile.PortfolioPhotos);
    }
}
