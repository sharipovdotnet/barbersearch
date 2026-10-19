using BarberSearch.Application.BarberProfiles.GetBarberProfile;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.BarberProfiles.Enums;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Карточка барбера: успешное чтение, 404 и пустой идентификатор.
/// </summary>
public class BarberProfileReadTests
{
    [Fact]
    public async Task Get_ExistingProfile_ReturnsCard()
    {
        var profileId = Guid.CreateVersion7();
        var repository = new FakeBarberProfileRepository();
        var card = CreateCard(profileId);
        repository.AddCard(card);
        var handler = new GetBarberProfileHandler(repository);

        var result = await handler.Handle(
            new GetBarberProfileQuery(profileId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(profileId, result.Value.Id);
        Assert.Equal("Barber", result.Value.DisplayName);
    }

    [Fact]
    public async Task Get_MissingProfile_ReturnsNotFound()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new GetBarberProfileHandler(repository);

        var result = await handler.Handle(
            new GetBarberProfileQuery(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Get_EmptyIdentifier_ReturnsValidation()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new GetBarberProfileHandler(repository);

        var result = await handler.Handle(
            new GetBarberProfileQuery(Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
    }

    private static BarberProfileResponse CreateCard(Guid id)
        => new(
            id,
            Guid.CreateVersion7(),
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
            []);
}
