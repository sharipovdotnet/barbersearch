using BarberSearch.Application.BarberProfiles.CreateBarberProfile;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.BarberProfiles.CreateBarberProfile;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Создание профиля барбера сразу привязывает его к текущему пользователю (R-26):
/// профиль и <c>User.BarberProfileId</c> пишутся в одной транзакции, повторное создание
/// даёт <c>409</c>, а любой <c>Failure</c> откатывает обе записи.
/// </summary>
public class CreateBarberProfileTests
{
    [Fact]
    public async Task Create_LinksProfileToCurrentUserAndCommits()
    {
        var user = CreateBarber();
        var profiles = new FakeBarberProfileRepository();
        var users = new FakeUserRepository();
        users.Add(user);
        var unitOfWork = new FakeUnitOfWork(profiles, users);
        var handler = CreateHandler(profiles, users, unitOfWork, new FakeCurrentUser(user.Id));

        var result = await handler.Handle(
            new CreateBarberProfileCommand(CreateRequest()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(profiles.Find(result.Value));

        var stored = Assert.Single(users.Users);
        Assert.Equal(result.Value, stored.BarberProfileId);
        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.Equal(0, unitOfWork.RollbackCount);
        Assert.False(unitOfWork.IsInTransaction);
    }

    [Fact]
    public async Task Create_WhenUserAlreadyHasProfile_ReturnsConflictAndWritesNothing()
    {
        var existingProfileId = Guid.CreateVersion7();
        var user = CreateBarber();
        user.LinkBarberProfile(existingProfileId);
        var profiles = new FakeBarberProfileRepository();
        var users = new FakeUserRepository();
        users.Add(user);
        var unitOfWork = new FakeUnitOfWork(profiles, users);
        var handler = CreateHandler(profiles, users, unitOfWork, new FakeCurrentUser(user.Id));

        var result = await handler.Handle(
            new CreateBarberProfileCommand(CreateRequest()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.BarberProfile.CreateAlreadyExists, result.Error.Errors[0].Code);
        Assert.Empty(profiles.Profiles);
        Assert.Equal(0, users.UpdateCallCount);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.Equal(1, unitOfWork.RollbackCount);

        // Пользователь не «перепривязан» к новому профилю: прежняя привязка осталась.
        var stored = Assert.Single(users.Users);
        Assert.Equal(existingProfileId, stored.BarberProfileId);
    }

    [Fact]
    public async Task Create_WhenAddLosesRaceOnUniqueIndex_MapsToAlreadyExistsAndRollsBack()
    {
        var user = CreateBarber();
        var profiles = new FakeBarberProfileRepository();
        profiles.AddBehavior = _ => new Failure(Error.Conflict(
            ApiErrorCodes.BarberProfile.AddUniqueViolation,
            "A record with the same unique values already exists (ux_barber_profiles_user_id)."));
        var users = new FakeUserRepository();
        users.Add(user);
        var unitOfWork = new FakeUnitOfWork(profiles, users);
        var handler = CreateHandler(profiles, users, unitOfWork, new FakeCurrentUser(user.Id));

        var result = await handler.Handle(
            new CreateBarberProfileCommand(CreateRequest()),
            CancellationToken.None);

        // Гонка двух create: проигравший видит тот же 409, что и предполётная проверка,
        // а не инфраструктурный barberProfile.add.uniqueViolation.
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.BarberProfile.CreateAlreadyExists, result.Error.Errors[0].Code);
        Assert.Empty(profiles.Profiles);
        Assert.Equal(0, users.UpdateCallCount);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
        Assert.Null(Assert.Single(users.Users).BarberProfileId);
    }

    [Fact]
    public async Task Create_WhenUserUpdateFails_RollsBackProfile()
    {
        var user = CreateBarber();
        var profiles = new FakeBarberProfileRepository();
        var users = new FakeUserRepository();
        users.Add(user);
        users.UpdateBehavior = _ => new Failure(
            Error.Failure("user.update.failed", "User update failed."));
        var unitOfWork = new FakeUnitOfWork(profiles, users);
        var handler = CreateHandler(profiles, users, unitOfWork, new FakeCurrentUser(user.Id));

        var result = await handler.Handle(
            new CreateBarberProfileCommand(CreateRequest()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("user.update.failed", result.Error.Errors[0].Code);
        Assert.Empty(profiles.Profiles);
        Assert.Equal(1, users.UpdateCallCount);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.Equal(1, unitOfWork.RollbackCount);

        var stored = Assert.Single(users.Users);
        Assert.Null(stored.BarberProfileId);
    }

    [Fact]
    public async Task Create_WithInvalidDisplayName_DoesNotTouchRepositories()
    {
        var user = CreateBarber();
        var profiles = new FakeBarberProfileRepository();
        var users = new FakeUserRepository();
        users.Add(user);
        var unitOfWork = new FakeUnitOfWork(profiles, users);
        var handler = CreateHandler(profiles, users, unitOfWork, new FakeCurrentUser(user.Id));

        var result = await handler.Handle(
            new CreateBarberProfileCommand(CreateRequest(displayName: "   ")),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("barberProfile.displayName", result.Error.Errors[0].Code);
        Assert.Empty(profiles.Profiles);
        Assert.Equal(0, users.GetByIdCallCount);
        Assert.Equal(0, users.UpdateCallCount);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.Equal(0, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Create_Anonymous_ReturnsUnauthorizedAndWritesNothing()
    {
        var user = CreateBarber();
        var profiles = new FakeBarberProfileRepository();
        var users = new FakeUserRepository();
        users.Add(user);
        var unitOfWork = new FakeUnitOfWork(profiles, users);
        var handler = CreateHandler(
            profiles,
            users,
            unitOfWork,
            FakeCurrentUser.Anonymous());

        var result = await handler.Handle(
            new CreateBarberProfileCommand(CreateRequest()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
        Assert.Empty(profiles.Profiles);
        Assert.Equal(0, users.GetByIdCallCount);
        Assert.Equal(0, users.UpdateCallCount);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.Equal(0, unitOfWork.RollbackCount);
    }

    private static CreateBarberProfileHandler CreateHandler(
        FakeBarberProfileRepository profiles,
        FakeUserRepository users,
        FakeUnitOfWork unitOfWork,
        FakeCurrentUser currentUser)
        => new(
            profiles,
            users,
            unitOfWork,
            currentUser,
            NullLogger<CreateBarberProfileHandler>.Instance);

    private static BarberProfileCreateRequest CreateRequest(
        string displayName = "Ivan the Barber")
        => new(
            displayName,
            5,
            "Bio",
            [Language.Ru, Language.En],
            ["Cert"],
            ["Achievement"],
            [PaymentMethod.Cash],
            ["Chatty"],
            ["Coffee"],
            "Wait 10 minutes max");

    private static User CreateBarber()
        => User.Create(
            "barber@example.com",
            "hash",
            "Ivan",
            "Petrov",
            UserRole.Barber).Value;
}
