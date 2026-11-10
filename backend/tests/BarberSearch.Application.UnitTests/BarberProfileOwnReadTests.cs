using BarberSearch.Application.BarberProfiles.GetOwnBarberProfile;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.BarberProfiles.Enums;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Собственная карточка барбера: чтение до верификации, 403 для чужого и 404 для неизвестного.
/// </summary>
public class BarberProfileOwnReadTests
{
    [Fact]
    public async Task GetOwn_UnverifiedProfile_IsReturnedToItsOwner()
    {
        // Публичная карточка неподтверждённый профиль прячет; кабинет обязан его показать,
        // иначе барбер не увидит, что заполнил, и не поймёт, чего не хватает.
        var ownerId = Guid.CreateVersion7();
        var profileId = Guid.CreateVersion7();
        var repository = new FakeBarberProfileRepository();
        repository.AddCard(CreateCard(profileId, ownerId) with
        {
            IsVerified = false,
            IsActive = false,
            DisplayName = "Unverified Barber"
        });
        var handler = new GetOwnBarberProfileHandler(
            repository,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new GetOwnBarberProfileQuery(profileId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(profileId, result.Value.Id);
        Assert.Equal("Unverified Barber", result.Value.DisplayName);
        Assert.False(result.Value.IsVerified);
        Assert.False(result.Value.IsActive);
    }

    [Fact]
    public async Task GetOwn_Stranger_ReturnsForbidden()
    {
        var profileId = Guid.CreateVersion7();
        var repository = new FakeBarberProfileRepository();
        repository.AddCard(CreateCard(profileId, Guid.CreateVersion7()));
        var handler = new GetOwnBarberProfileHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(
            new GetOwnBarberProfileQuery(profileId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetOwn_Admin_CanReadAnyProfile()
    {
        var profileId = Guid.CreateVersion7();
        var repository = new FakeBarberProfileRepository();
        repository.AddCard(CreateCard(profileId, Guid.CreateVersion7()));
        var handler = new GetOwnBarberProfileHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

        var result = await handler.Handle(
            new GetOwnBarberProfileQuery(profileId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(profileId, result.Value.Id);
    }

    [Fact]
    public async Task GetOwn_MissingProfile_ReturnsNotFound()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new GetOwnBarberProfileHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(
            new GetOwnBarberProfileQuery(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("barberProfile.notFound", result.Error.Errors[0].Code);
    }

    private static BarberProfileResponse CreateCard(Guid id, Guid userId)
        => new(
            id,
            userId,
            "Barber",
            Grade.MiddleBarber,
            "Bio",
            5,
            [],
            [],
            null,
            0,
            [],
            [],
            [],
            [],
            "Wait 10 minutes",
            true,
            true,
            [],
            [],
            [],
            []);
}
