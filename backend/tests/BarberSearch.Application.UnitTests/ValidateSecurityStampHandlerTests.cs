using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Application.Users.ValidateSecurityStamp;
using BarberSearch.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Проверка security stamp (R-11): активность и совпадение отпечатка, без загрузки агрегата.
/// </summary>
/// <remarks>
/// Проверка вынесена в Application, поэтому тесты не требуют тестового проекта Web:
/// Web-адаптер только парсит claims и вызывает этот обработчик.
/// </remarks>
public class ValidateSecurityStampHandlerTests
{
    private const string PasswordHash = "AQAAAAIAAYagAAAAEHash";

    [Fact]
    public async Task Handle_WithActiveUserAndMatchingStamp_ReturnsTrue()
    {
        var user = CreateUser();
        var users = new FakeUserRepository();
        users.Add(user);

        var result = await CreateHandler(users).Handle(
            new ValidateSecurityStampQuery(user.Id, user.SecurityStamp),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task Handle_WithDeactivatedUser_ReturnsFalse()
    {
        var user = CreateUser();
        user.Deactivate();

        var users = new FakeUserRepository();
        users.Add(user);

        var result = await CreateHandler(users).Handle(
            new ValidateSecurityStampQuery(user.Id, user.SecurityStamp),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task Handle_WithStaleStamp_ReturnsFalse()
    {
        var user = CreateUser();
        var staleStamp = user.SecurityStamp;

        // Смена пароля/роли или деактивация ротирует отпечаток; выданный ранее токен
        // предъявляет старое значение и должен быть отклонён.
        user.RotateSecurityStamp();

        var users = new FakeUserRepository();
        users.Add(user);

        var result = await CreateHandler(users).Handle(
            new ValidateSecurityStampQuery(user.Id, staleStamp),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsFalse()
    {
        var users = new FakeUserRepository();

        var result = await CreateHandler(users).Handle(
            new ValidateSecurityStampQuery(Guid.NewGuid(), Guid.NewGuid().ToString("N")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.Equal(1, users.GetSecurityStampCallCount);
    }

    [Fact]
    public async Task Handle_ReadsOnlyNarrowProjection_NotTheAggregate()
    {
        var user = CreateUser();
        var users = new FakeUserRepository();
        users.Add(user);

        var result = await CreateHandler(users).Handle(
            new ValidateSecurityStampQuery(user.Id, user.SecurityStamp),
            CancellationToken.None);

        Assert.True(result.Value);

        // Проверка не должна загружать агрегат с избранным: только узкий SELECT
        // is_active, security_stamp — ровно один раз.
        Assert.Equal(0, users.GetByIdCallCount);
        Assert.Equal(1, users.GetSecurityStampCallCount);
    }

    private static ValidateSecurityStampHandler CreateHandler(FakeUserRepository users)
        => new(users, NullLogger<ValidateSecurityStampHandler>.Instance);

    private static User CreateUser()
        => User.Create("user@example.com", PasswordHash, "Ivan", "Petrov").Value;
}
