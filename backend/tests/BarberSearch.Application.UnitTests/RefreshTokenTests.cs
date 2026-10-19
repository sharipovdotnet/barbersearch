using BarberSearch.Application.RefreshTokens;
using BarberSearch.Application.RefreshTokens.IssueRefreshToken;
using BarberSearch.Application.RefreshTokens.RefreshSession;
using BarberSearch.Application.RefreshTokens.RevokeRefreshToken;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Application.Users;
using BarberSearch.Application.Users.ChangePassword;
using BarberSearch.Application.Users.ChangeUserRole;
using BarberSearch.Application.Users.DeactivateUser;
using BarberSearch.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Ротация refresh-токенов, детект повторного использования, истечение и отзыв.
/// </summary>
public class RefreshTokenTests
{
    private const int LifetimeDays = 14;

    [Fact]
    public async Task Issue_StoresOnlySha256Hash()
    {
        var context = new Context();

        var issued = await context.IssueAsync();

        var stored = Assert.Single(context.Tokens.Tokens);
        Assert.NotEqual(issued.Token, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.Equal(RefreshTokenGenerator.ComputeHash(issued.Token), stored.TokenHash);
    }

    [Fact]
    public async Task Issue_WithLifetimeOutsideAllowedRange_Fails()
    {
        var context = new Context();

        var result = await context.Issuer.Handle(
            new IssueRefreshTokenCommand(context.User.Id, 6),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Empty(context.Tokens.Tokens);
    }

    [Fact]
    public async Task Refresh_WithActiveToken_RotatesAndLinksReplacement()
    {
        var context = new Context();
        var first = await context.IssueAsync();

        var result = await context.Refresher.Handle(
            new RefreshSessionCommand(first.Token, LifetimeDays),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var old = context.Tokens.FindByRawToken(first.Token);
        var replacement = context.Tokens.FindByRawToken(result.Value.RefreshToken);

        Assert.NotNull(old);
        Assert.NotNull(replacement);
        Assert.True(old!.IsRevoked);
        Assert.Equal(replacement!.Id, old.ReplacedBy);
        Assert.True(replacement.IsActive(context.Clock.UtcNow));
        Assert.Equal(context.User.Id, result.Value.User.Id);
        Assert.NotEqual(first.Token, result.Value.RefreshToken);
    }

    [Fact]
    public async Task Refresh_WithReplacedToken_RevokesWholeChain()
    {
        var context = new Context();
        var first = await context.IssueAsync();
        var second = await context.RefreshAsync(first.Token);
        var third = await context.RefreshAsync(second);

        var reuse = await context.Refresher.Handle(
            new RefreshSessionCommand(first.Token, LifetimeDays),
            CancellationToken.None);

        Assert.True(reuse.IsFailure);
        Assert.Equal("auth.refresh.reused", reuse.Error.Errors[0].Code);
        Assert.True(context.Tokens.FindByRawToken(second)!.IsRevoked);
        Assert.True(context.Tokens.FindByRawToken(third)!.IsRevoked);
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_ReturnsUnauthorized()
    {
        var context = new Context();
        var first = await context.IssueAsync();
        context.Clock.UtcNow = context.Clock.UtcNow.AddDays(LifetimeDays + 1);

        var result = await context.Refresher.Handle(
            new RefreshSessionCommand(first.Token, LifetimeDays),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.refresh.expired", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Refresh_WithRevokedToken_ReturnsUnauthorized()
    {
        var context = new Context();
        var first = await context.IssueAsync();

        var revoked = await context.Revoker.Handle(
            new RevokeRefreshTokenCommand(first.Token),
            CancellationToken.None);
        var result = await context.Refresher.Handle(
            new RefreshSessionCommand(first.Token, LifetimeDays),
            CancellationToken.None);

        Assert.True(revoked.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("auth.refresh.revoked", result.Error.Errors[0].Code);

        var stored = context.Tokens.FindByRawToken(first.Token);
        Assert.True(stored!.IsRevoked);
        Assert.Null(stored.ReplacedBy);
    }

    [Fact]
    public async Task Refresh_WithUnknownToken_ReturnsUnauthorized()
    {
        var context = new Context();

        var result = await context.Refresher.Handle(
            new RefreshSessionCommand("not-a-real-token", LifetimeDays),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.refresh.invalid", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Refresh_ForDeactivatedUser_RevokesAllTokens()
    {
        var context = new Context();
        var first = await context.IssueAsync();
        var second = await context.IssueAsync();
        context.User.Deactivate();

        var result = await context.Refresher.Handle(
            new RefreshSessionCommand(first.Token, LifetimeDays),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.refresh.inactive", result.Error.Errors[0].Code);
        Assert.True(context.Tokens.FindByRawToken(first.Token)!.IsRevoked);
        Assert.True(context.Tokens.FindByRawToken(second.Token)!.IsRevoked);
    }

    [Fact]
    public async Task ChangeRole_RevokesAllRefreshTokens()
    {
        var context = new Context();
        await context.IssueAsync();
        await context.IssueAsync();

        var result = await context.RoleChanger.Handle(
            new ChangeUserRoleCommand(context.User.Id, UserRole.Admin),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserRole.Admin, context.User.Role);
        Assert.All(context.Tokens.Tokens, token => Assert.True(token.IsRevoked));
    }

    [Fact]
    public async Task Deactivate_RevokesAllRefreshTokens()
    {
        var context = new Context();
        await context.IssueAsync();
        await context.IssueAsync();

        var result = await context.Deactivator.Handle(
            new DeactivateUserCommand(context.User.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(context.User.IsActive);
        Assert.All(context.Tokens.Tokens, token => Assert.True(token.IsRevoked));
    }

    [Fact]
    public async Task ChangePassword_RevokesAllRefreshTokens()
    {
        var context = new Context();
        await context.IssueAsync();
        await context.IssueAsync();

        var result = await context.PasswordChanger.Handle(
            new ChangePasswordCommand(context.User.Id, "password", "new-password"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            PasswordVerification.Success,
            context.Passwords.Verify(context.User.PasswordHash, "new-password"));
        Assert.All(context.Tokens.Tokens, token => Assert.True(token.IsRevoked));
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_DoesNotRevokeTokens()
    {
        var context = new Context();
        var issued = await context.IssueAsync();

        var result = await context.PasswordChanger.Handle(
            new ChangePasswordCommand(context.User.Id, "wrong", "new-password"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.False(context.Tokens.FindByRawToken(issued.Token)!.IsRevoked);
    }

    private sealed class Context
    {
        public Context()
        {
            User = User.Create(
                $"user-{Guid.NewGuid():N}@example.com",
                "hash:password",
                "First",
                "Last").Value;

            Users.Add(User);
        }

        public FakeTimeProvider Clock { get; } =
            new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

        public FakeUserRepository Users { get; } = new();

        public FakeRefreshTokenRepository Tokens { get; } = new();

        public FakePasswordService Passwords { get; } = new();

        public User User { get; }

        public IssueRefreshTokenHandler Issuer => new(
            Tokens,
            Clock,
            NullLogger<IssueRefreshTokenHandler>.Instance);

        public RefreshSessionHandler Refresher => new(
            Tokens,
            Users,
            Clock,
            NullLogger<RefreshSessionHandler>.Instance);

        public RevokeRefreshTokenHandler Revoker => new(
            Tokens,
            Clock,
            NullLogger<RevokeRefreshTokenHandler>.Instance);

        public ChangeUserRoleHandler RoleChanger => new(
            Users,
            Tokens,
            Clock,
            NullLogger<ChangeUserRoleHandler>.Instance);

        public DeactivateUserHandler Deactivator => new(
            Users,
            Tokens,
            Clock,
            NullLogger<DeactivateUserHandler>.Instance);

        public ChangePasswordHandler PasswordChanger => new(
            Users,
            Passwords,
            Tokens,
            Clock,
            NullLogger<ChangePasswordHandler>.Instance);

        public async Task<IssuedRefreshToken> IssueAsync()
        {
            var result = await Issuer.Handle(
                new IssueRefreshTokenCommand(User.Id, LifetimeDays),
                CancellationToken.None);

            Assert.True(result.IsSuccess);

            return result.Value;
        }

        public async Task<string> RefreshAsync(string rawToken)
        {
            var result = await Refresher.Handle(
                new RefreshSessionCommand(rawToken, LifetimeDays),
                CancellationToken.None);

            Assert.True(result.IsSuccess);

            return result.Value.RefreshToken;
        }
    }
}
