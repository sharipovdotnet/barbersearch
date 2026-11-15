using BarberSearch.Application.Push;
using BarberSearch.Application.Push.RegisterPushDevice;
using BarberSearch.Application.Push.UnregisterPushDevice;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Push.RegisterPushDevice;
using BarberSearch.Contracts.Push.UnregisterPushDevice;
using BarberSearch.Domain.Push;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Регистрация и снятие устройства: upsert по токену, передача владения и предел устройств.
/// </summary>
/// <remarks>
/// Оба действия идемпотентны и работают от текущего пользователя из сессии, поэтому
/// идентификатор пользователя в команде не участвует. Предел устройств проверяется на
/// фейке, который повторяет ту же транзакционную логику, что и PostgreSQL-репозиторий.
/// </remarks>
public class PushRegistrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public async Task Register_WithNewToken_StoresItForTheCurrentUser()
    {
        var context = CreateRegistrationContext();

        var result = await context.Handler.Handle(
            new RegisterPushDeviceCommand(new RegisterPushDeviceRequest("device-1", PushPlatform.Android)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var token = Assert.Single(context.Tokens.Items);
        Assert.Equal(UserId, token.UserId);
        Assert.Equal("device-1", token.Token);
        Assert.Equal(PushPlatform.Android, token.Platform);
        Assert.Equal(Now, token.LastSeenAt);
    }

    [Fact]
    public async Task Register_WithSameTokenAgain_KeepsOneRowAndMovesSeenAt()
    {
        var context = CreateRegistrationContext();

        await context.Handler.Handle(
            new RegisterPushDeviceCommand(new RegisterPushDeviceRequest("device-1", PushPlatform.Android)),
            CancellationToken.None);

        context.Clock.UtcNow = Now.AddHours(1);

        await context.Handler.Handle(
            new RegisterPushDeviceCommand(new RegisterPushDeviceRequest("device-1", PushPlatform.Android)),
            CancellationToken.None);

        var token = Assert.Single(context.Tokens.Items);
        Assert.Equal(Now.AddHours(1), token.LastSeenAt);
    }

    [Fact]
    public async Task Register_WithTokenOfAnotherUser_TransfersOwnership()
    {
        var context = CreateRegistrationContext();
        var otherUser = Guid.CreateVersion7();
        context.Tokens.Seed(CreateToken(otherUser, "shared-device", Now.AddDays(-1)));

        var result = await context.Handler.Handle(
            new RegisterPushDeviceCommand(
                new RegisterPushDeviceRequest("shared-device", PushPlatform.Android)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var token = Assert.Single(context.Tokens.Items);
        Assert.Equal(UserId, token.UserId);
        Assert.Equal(Now, token.LastSeenAt);
    }

    [Fact]
    public async Task Register_WithMoreThanFiveDevices_DropsTheLeastRecentlySeen()
    {
        var context = CreateRegistrationContext();

        for (var index = 1; index <= 5; index++)
        {
            context.Tokens.Seed(CreateToken(UserId, $"device-{index}", Now.AddDays(-index)));
        }

        var result = await context.Handler.Handle(
            new RegisterPushDeviceCommand(new RegisterPushDeviceRequest("device-new", PushPlatform.Android)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, context.Tokens.Items.Count);
        Assert.DoesNotContain(context.Tokens.Items, token => token.Token == "device-5");
        Assert.Contains(context.Tokens.Items, token => token.Token == "device-new");
    }

    [Fact]
    public async Task Register_WithEarlierSeenAt_DoesNotMoveLastSeenAtBackwards()
    {
        var context = CreateRegistrationContext();
        var future = Now.AddHours(2);
        context.Tokens.Seed(CreateToken(UserId, "device-1", future));

        var result = await context.Handler.Handle(
            new RegisterPushDeviceCommand(
                new RegisterPushDeviceRequest("device-1", PushPlatform.Android)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var token = Assert.Single(context.Tokens.Items);
        Assert.Equal(future, token.LastSeenAt);
    }

    [Fact]
    public async Task Register_WithoutAuthentication_ReturnsUnauthorized()
    {
        var context = CreateRegistrationContext(FakeCurrentUser.Anonymous());

        var result = await context.Handler.Handle(
            new RegisterPushDeviceCommand(new RegisterPushDeviceRequest("device-1", PushPlatform.Android)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
        Assert.Empty(context.Tokens.Items);
    }

    [Fact]
    public async Task Register_WithEmptyToken_ReturnsDomainValidationError()
    {
        var context = CreateRegistrationContext();

        var result = await context.Handler.Handle(
            new RegisterPushDeviceCommand(new RegisterPushDeviceRequest("   ", PushPlatform.Android)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("pushDeviceToken.token.invalid", result.Error.Errors[0].Code);
        Assert.Empty(context.Tokens.Items);
    }

    [Fact]
    public async Task Unregister_WithOwnToken_DeletesOnlyThatToken()
    {
        var context = CreateUnregistrationContext();
        context.Tokens.Seed(CreateToken(UserId, "device-1", Now));
        context.Tokens.Seed(CreateToken(UserId, "device-2", Now));

        var result = await context.Handler.Handle(
            new UnregisterPushDeviceCommand(new UnregisterPushDeviceRequest("device-1")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var remaining = Assert.Single(context.Tokens.Items);
        Assert.Equal("device-2", remaining.Token);
    }

    [Fact]
    public async Task Unregister_WithForeignOrUnknownToken_SucceedsWithoutDeleting()
    {
        var context = CreateUnregistrationContext();
        var otherUser = Guid.CreateVersion7();
        context.Tokens.Seed(CreateToken(otherUser, "foreign-device", Now));

        var foreign = await context.Handler.Handle(
            new UnregisterPushDeviceCommand(new UnregisterPushDeviceRequest("foreign-device")),
            CancellationToken.None);
        var unknown = await context.Handler.Handle(
            new UnregisterPushDeviceCommand(new UnregisterPushDeviceRequest("unknown-device")),
            CancellationToken.None);

        Assert.True(foreign.IsSuccess);
        Assert.True(unknown.IsSuccess);
        Assert.Single(context.Tokens.Items);
    }

    [Fact]
    public async Task Unregister_WithEmptyToken_SucceedsWithoutCallingTheRepository()
    {
        var context = CreateUnregistrationContext();

        var result = await context.Handler.Handle(
            new UnregisterPushDeviceCommand(new UnregisterPushDeviceRequest(string.Empty)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, context.Tokens.DeleteCalls);
    }

    private static RegistrationContext CreateRegistrationContext(FakeCurrentUser? currentUser = null)
    {
        var tokens = new FakePushDeviceTokenRepository();
        var clock = new FakeTimeProvider(Now);

        var handler = new RegisterPushDeviceHandler(
            tokens,
            new PushOptions { MaxTokensPerUser = 5 },
            currentUser ?? new FakeCurrentUser(UserId),
            clock);

        return new RegistrationContext(handler, tokens, clock);
    }

    private static UnregistrationContext CreateUnregistrationContext()
    {
        var tokens = new FakePushDeviceTokenRepository();

        var handler = new UnregisterPushDeviceHandler(
            tokens,
            new FakeCurrentUser(UserId));

        return new UnregistrationContext(handler, tokens);
    }

    private static PushDeviceToken CreateToken(Guid userId, string token, DateTimeOffset seenAt)
        => PushDeviceToken.Register(userId, token, PushPlatform.Android, seenAt).Value;

    private sealed record RegistrationContext(
        RegisterPushDeviceHandler Handler,
        FakePushDeviceTokenRepository Tokens,
        FakeTimeProvider Clock);

    private sealed record UnregistrationContext(
        UnregisterPushDeviceHandler Handler,
        FakePushDeviceTokenRepository Tokens);
}
