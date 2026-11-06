using BarberSearch.Application.Salons.UpdateSalonContactInfo;
using BarberSearch.Application.Salons.UpdateSalonLocation;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Право правки адреса и контактов салона: владелец — свой салон, администратор — любой,
/// остальные — <c>403 auth.forbidden</c>, аноним — <c>401 auth.unauthorized</c>.
/// </summary>
/// <remarks>
/// У салона, созданного администратором, <see cref="Salon.OwnerUserId"/> = <c>null</c>.
/// Отсутствие владельца нельзя подменять <see cref="Guid.Empty"/>: клиент с пустым
/// идентификатором не должен получить доступ (R-29). Параметр теории <c>updateLocation</c>
/// выбирает обработчик: <c>true</c> — адрес, <c>false</c> — контакты.
/// </remarks>
public class SalonUpdateAuthorizationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Owner_can_update_own_salon(bool updateLocation)
    {
        var ownerId = Guid.CreateVersion7();
        var salon = CreatePendingSalon(ownerId);
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);

        var result = await UpdateAsync(
            updateLocation,
            salons,
            new FakeCurrentUser(ownerId));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, salons.UpdateCalls);
        AssertUpdated(salon, updateLocation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Other_non_admin_gets_forbidden_and_nothing_is_saved(bool updateLocation)
    {
        var salon = CreatePendingSalon(Guid.CreateVersion7());
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);
        var originalAddress = salon.Address;

        var result = await UpdateAsync(
            updateLocation,
            salons,
            new FakeCurrentUser(Guid.CreateVersion7()));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
        Assert.Equal(0, salons.UpdateCalls);
        Assert.Equal(originalAddress, salon.Address);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Admin_can_update_salon_without_owner(bool updateLocation)
    {
        var salon = CreateAdminSalon();
        Assert.Null(salon.OwnerUserId);
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);

        var result = await UpdateAsync(
            updateLocation,
            salons,
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, salons.UpdateCalls);
        AssertUpdated(salon, updateLocation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_user_id_cannot_update_salon_without_owner(bool updateLocation)
    {
        var salon = CreateAdminSalon();
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);

        var result = await UpdateAsync(
            updateLocation,
            salons,
            new FakeCurrentUser(Guid.Empty));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
        Assert.Equal(0, salons.UpdateCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Anonymous_gets_unauthorized_and_nothing_is_saved(bool updateLocation)
    {
        var salon = CreatePendingSalon(Guid.CreateVersion7());
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);

        var result = await UpdateAsync(
            updateLocation,
            salons,
            FakeCurrentUser.Anonymous());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
        Assert.Equal(0, salons.UpdateCalls);
    }

    private static Task<UnitResult<Failure>> UpdateAsync(
        bool updateLocation,
        FakeSalonRepository salons,
        FakeCurrentUser currentUser)
    {
        if (updateLocation)
        {
            return new UpdateSalonLocationHandler(
                    salons,
                    currentUser,
                    NullLogger<UpdateSalonLocationHandler>.Instance)
                .Handle(NewLocationCommand(salons.Salons.Single().Id), CancellationToken.None);
        }

        return new UpdateSalonContactInfoHandler(
                salons,
                currentUser,
                NullLogger<UpdateSalonContactInfoHandler>.Instance)
            .Handle(NewContactCommand(salons.Salons.Single().Id), CancellationToken.None);
    }

    private static void AssertUpdated(Salon salon, bool updateLocation)
    {
        if (updateLocation)
        {
            Assert.Equal("Neue Strasse 1", salon.Address);
            Assert.Equal("Potsdam", salon.City);
            Assert.Equal("Germany", salon.Country);
            Assert.Equal("Europe/Berlin", salon.TimeZoneId);
            Assert.Equal(52.3906, salon.Latitude);
            Assert.Equal(13.0645, salon.Longitude);
            Assert.Null(salon.PostalCode);

            return;
        }

        Assert.Equal("+4930123456", salon.PhoneNumber);
        Assert.Equal("https://salon.example/", salon.WebsiteUrl);
    }

    private static UpdateSalonLocationCommand NewLocationCommand(Guid salonId)
        => new(
            salonId,
            "Neue Strasse 1",
            "Potsdam",
            "Germany",
            "Europe/Berlin",
            52.3906,
            13.0645,
            null);

    private static UpdateSalonContactInfoCommand NewContactCommand(Guid salonId)
        => new(salonId, "+4930123456", "https://salon.example/");

    private static Salon CreateAdminSalon()
        => Salon.Create(
            "Admin Salon",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173).Value;

    private static Salon CreatePendingSalon(Guid ownerUserId)
        => Salon.SubmitByOwner(
            ownerUserId,
            "Owner Salon",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173).Value;
}
