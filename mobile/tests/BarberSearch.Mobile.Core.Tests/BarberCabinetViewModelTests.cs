using System.Net;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки гейта кабинета мастера: сессия, роль, профиль и бейдж непрочитанных.
/// </summary>
/// <remarks>
/// Гейт — единственное место, которое решает, что показать вместо кабинета, поэтому ни один
/// раздел до него не должен ходить в сеть. Подменены <c>/me</c> и счётчик уведомлений; часов
/// гейту не нужно, а цикл автоповтора молчит на окончательных отказах и не стартует в тестах.
/// </remarks>
public sealed class BarberCabinetViewModelTests
{
    [Fact]
    public async Task InitializeAsync_WithoutSession_RequiresSignInAndMakesNoRequests()
    {
        using var harness = new Harness(authenticated: false);

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.IsSignInRequired);
        Assert.False(harness.ViewModel.IsReady);
        Assert.Equal(0, harness.Account.MeCalls);
        Assert.Equal(0, harness.Notifications.UnreadCalls);
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_ClearsSessionAndRequiresSignIn()
    {
        using var harness = new Harness();
        var ended = false;

        harness.Session.SessionEnded += (_, _) => ended = true;
        harness.Account.Me = _ => Task.FromException<MeResponse>(
            BarberCabinetApiError.Create(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.IsSignInRequired);
        Assert.False(harness.ViewModel.IsReady);
        Assert.Null(harness.ViewModel.ErrorMessage);
        Assert.Null(harness.Store.Tokens);
        Assert.False(harness.Session.IsAuthenticated);
        Assert.True(ended);
        Assert.Equal(1, harness.Account.MeCalls);
        Assert.Equal(0, harness.Notifications.UnreadCalls);
    }

    [Fact]
    public async Task InitializeAsync_CustomerRole_ForbidsCabinetAndKeepsName()
    {
        using var harness = new Harness();
        harness.Account.Me = _ => Task.FromResult(
            BarberCabinetFixtures.Me(role: "Customer", email: "client@example.com"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.IsForbidden);
        Assert.False(harness.ViewModel.IsReady);
        Assert.False(harness.ViewModel.IsNoProfile);
        Assert.Equal("client@example.com", harness.ViewModel.DisplayName);
        Assert.Equal(0, harness.Notifications.UnreadCalls);
    }

    [Fact]
    public async Task InitializeAsync_BarberWithoutProfile_OffersOnboarding()
    {
        using var harness = new Harness();
        var me = BarberCabinetFixtures.Me(email: "master@example.com");

        harness.Account.Me = _ => Task.FromResult(me with { BarberProfileId = null });

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.IsNoProfile);
        Assert.False(harness.ViewModel.IsReady);
        Assert.False(harness.ViewModel.IsForbidden);
        Assert.Equal("master@example.com", harness.ViewModel.DisplayName);
        Assert.Null(harness.ViewModel.BarberProfileId);
        Assert.Equal(0, harness.Notifications.UnreadCalls);
    }

    [Fact]
    public async Task InitializeAsync_BarberWithProfile_IsReadyAndShowsUnreadBadge()
    {
        using var harness = new Harness();
        harness.Notifications.UnreadCount = _ => Task.FromResult(150);

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.IsReady);
        Assert.Equal(BarberCabinetFixtures.BarberProfileId, harness.ViewModel.BarberProfileId);
        Assert.Equal("master@example.com", harness.ViewModel.DisplayName);
        Assert.Equal(150, harness.ViewModel.UnreadCount);
        Assert.True(harness.ViewModel.HasUnread);
        Assert.Equal("99+", harness.ViewModel.UnreadBadgeText);

        harness.ViewModel.UnreadCount = 7;

        Assert.Equal("7", harness.ViewModel.UnreadBadgeText);
        Assert.True(harness.ViewModel.HasUnread);

        harness.ViewModel.UnreadCount = 0;

        Assert.Equal(0, harness.ViewModel.UnreadCount);
        Assert.False(harness.ViewModel.HasUnread);
    }

    [Fact]
    public async Task RefreshUnreadAsync_Failure_KeepsCountAndShowsNoError()
    {
        using var harness = new Harness();
        harness.Notifications.UnreadCount = _ => Task.FromResult(5);

        await harness.ViewModel.InitializeAsync();

        harness.Notifications.UnreadCount = _ => Task.FromException<int>(
            new HttpRequestException("offline"));

        await harness.ViewModel.RefreshUnreadAsync();

        Assert.Equal(5, harness.ViewModel.UnreadCount);
        Assert.Null(harness.ViewModel.ErrorMessage);
        Assert.True(harness.ViewModel.IsReady);
        Assert.Equal(2, harness.Notifications.UnreadCalls);
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_ReloadsMeAndStaysReady()
    {
        using var harness = new Harness();

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.IsReady);
        Assert.Equal(BarberCabinetFixtures.BarberProfileId, harness.ViewModel.BarberProfileId);
        Assert.Equal(2, harness.Account.MeCalls);
        Assert.Equal(2, harness.Notifications.UnreadCalls);
        Assert.False(harness.ViewModel.IsSignInRequired);
        Assert.False(harness.ViewModel.IsForbidden);
        Assert.False(harness.ViewModel.IsNoProfile);
    }

    /// <summary>
    /// Собранный гейт: хранилище пары, клиенты и ViewModel.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        public Harness(bool authenticated = true)
        {
            if (authenticated)
            {
                Store.Tokens = new StoredTokens("access-1", "refresh-1");
            }

            Session = new TokenSession(Store);
            ViewModel = new BarberCabinetViewModel(Account, Notifications, Session);
        }

        public FakeAccountApi Account { get; } = new();

        public FakeBarberNotificationsApi Notifications { get; } = new();

        public FakeTokenStore Store { get; } = new();

        public TokenSession Session { get; }

        public BarberCabinetViewModel ViewModel { get; }

        public void Dispose() => Session.Dispose();
    }
}
