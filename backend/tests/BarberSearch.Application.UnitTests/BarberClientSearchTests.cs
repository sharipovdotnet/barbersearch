using BarberSearch.Application.BarberClients;
using BarberSearch.Application.BarberProfiles.GetBarberBookingOptions;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.BarberProfiles.GetBarberBookingOptions;
using BarberSearch.Domain.Users;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Поиск пользователя и данные формы ручной записи: границы строки, роль, активность и
/// передача коллекций из репозитория.
/// </summary>
public class BarberClientSearchTests
{
    [Fact]
    public async Task Search_TooShort_ReturnsValidation()
    {
        var handler = new SearchBarberClientsHandler(new FakeUserRepository());

        var result = await handler.Handle(
            new SearchBarberClientsQuery("  ab  "),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("barberClient.search.tooShort", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Search_TooLong_ReturnsValidation()
    {
        var handler = new SearchBarberClientsHandler(new FakeUserRepository());

        var result = await handler.Handle(
            new SearchBarberClientsQuery(new string('a', 101)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("barberClient.search.tooLong", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Search_ReturnsOnlyActiveUsersOfAnyRoleMatchingEmailOrPhone()
    {
        var users = new FakeUserRepository();
        var customer = CreateCustomer("client@example.com", "+7 999 000-00-00");
        var inactive = CreateCustomer("inactive@example.com", null);
        Assert.True(inactive.Deactivate().IsSuccess);
        var admin = User.Create(
            "admin@example.com",
            "hash",
            "Admin",
            "User",
            UserRole.Admin).Value;
        var barber = User.Create(
            "barber@example.com",
            "hash",
            "Boris",
            "Barber",
            UserRole.Barber).Value;
        users.Add(customer);
        users.Add(inactive);
        users.Add(admin);
        users.Add(barber);
        var handler = new SearchBarberClientsHandler(users);

        var byEmail = await handler.Handle(
            new SearchBarberClientsQuery(" example "),
            CancellationToken.None);
        var byPhone = await handler.Handle(
            new SearchBarberClientsQuery("000-00-00"),
            CancellationToken.None);

        Assert.True(byEmail.IsSuccess);
        var ids = byEmail.Value.Select(user => user.Id).ToHashSet();
        Assert.Equal(3, ids.Count);
        Assert.Contains(customer.Id, ids);
        Assert.Contains(admin.Id, ids);
        Assert.Contains(barber.Id, ids);
        Assert.DoesNotContain(inactive.Id, ids);

        Assert.True(byPhone.IsSuccess);
        Assert.Equal(customer.Id, Assert.Single(byPhone.Value).Id);
    }

    [Fact]
    public async Task GetOptions_EmptyProfileId_ReturnsValidation()
    {
        var handler = new GetBarberBookingOptionsHandler(
            new FakeBarberProfileRepository());

        var result = await handler.Handle(
            new GetBarberBookingOptionsQuery(Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("barberProfile.id.invalid", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetOptions_ReturnsRepositoryCollections()
    {
        var profiles = new FakeBarberProfileRepository
        {
            BookingOptions = new BarberBookingOptionsResponse([], []),
        };
        var handler = new GetBarberBookingOptionsHandler(profiles);
        var profileId = Guid.CreateVersion7();

        var result = await handler.Handle(
            new GetBarberBookingOptionsQuery(profileId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Services);
        Assert.Empty(result.Value.Salons);
    }

    private static User CreateCustomer(string email, string? phoneNumber)
    {
        var user = User.Create(email, "hash", "Ivan", "Petrov").Value;

        Assert.True(user.UpdateProfile(
            "Ivan",
            "Petrov",
            phoneNumber,
            null).IsSuccess);

        return user;
    }
}
