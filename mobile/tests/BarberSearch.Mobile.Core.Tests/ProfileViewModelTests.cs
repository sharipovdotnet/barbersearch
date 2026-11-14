using System.Net;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Профиль»: вход, тексты отказов, выход и состояние сессии на старте.
/// </summary>
/// <remarks>
/// Тексты сверяются дословно с вебом (<c>frontend/src/lib/sign-in-errors.ts</c>): расхождение
/// здесь — не опечатка, а разная копия одного продукта в двух клиентах.
/// </remarks>
public sealed class ProfileViewModelTests
{
    private const string UserEmail = "mobile-test@barbersearch.test";
    private const string UserPassword = "Mobile-test-1!";

    [Fact]
    public async Task LoginAsync_ValidCredentials_SavesTokensAndShowsProfile()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var viewModel = await CreateSignedOutViewModelAsync(session);
        viewModel.Email = UserEmail;
        viewModel.Password = UserPassword;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsAuthenticated);
        Assert.True(viewModel.IsProfileVisible);
        Assert.False(viewModel.IsFormVisible);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.IsSigningIn);
        Assert.Equal(UserEmail, viewModel.ProfileEmail);
        Assert.Null(viewModel.ErrorMessage);

        Assert.True(session.IsAuthenticated);
        Assert.Equal("access-1", session.AccessToken);
        Assert.Equal("refresh-1", store.Tokens!.RefreshToken);
    }

    [Fact]
    public async Task LoginAsync_AfterSuccess_DoesNotKeepPassword()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var authApi = new FakeAuthApi();
        var viewModel = await CreateSignedOutViewModelAsync(session, authApi);
        viewModel.Email = UserEmail;
        viewModel.Password = UserPassword;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(UserPassword, authApi.LastPassword);
        Assert.Empty(viewModel.Password);
    }

    [Fact]
    public async Task LoginAsync_InvalidCredentials_ShowsWordingFromWeb()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var authApi = new FakeAuthApi
        {
            Login = () => Task.FromException<TokenResponse>(
                ApiError(HttpStatusCode.Unauthorized, "auth.login.invalidCredentials")),
        };
        var viewModel = await CreateSignedOutViewModelAsync(session, authApi);
        viewModel.Email = UserEmail;
        viewModel.Password = "wrong";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal("Неверный email или пароль.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsAuthenticated);
        Assert.True(viewModel.IsFormVisible);
        Assert.False(session.IsAuthenticated);
        Assert.Null(store.Tokens);
    }

    [Fact]
    public async Task LoginAsync_RateLimited_ShowsRateLimitWording()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var authApi = new FakeAuthApi
        {
            Login = () => Task.FromException<TokenResponse>(
                ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit")),
        };
        var viewModel = await CreateSignedOutViewModelAsync(session, authApi);
        viewModel.Email = UserEmail;
        viewModel.Password = UserPassword;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal("Слишком много попыток входа. Подождите минуту и повторите.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsAuthenticated);
        Assert.False(viewModel.IsSigningIn);
    }

    [Fact]
    public async Task LoginAsync_NetworkFailure_ShowsNetworkWording()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var authApi = new FakeAuthApi
        {
            Login = () => Task.FromException<TokenResponse>(new HttpRequestException("Connection refused")),
        };
        var viewModel = await CreateSignedOutViewModelAsync(session, authApi);
        viewModel.Email = UserEmail;
        viewModel.Password = UserPassword;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            viewModel.ErrorMessage);
        Assert.False(viewModel.IsAuthenticated);
    }

    [Fact]
    public async Task LoginAsync_Timeout_ShowsNetworkWording()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var authApi = new FakeAuthApi
        {
            // HttpClient сообщает об истечении таймаута отменой, а не HttpRequestException.
            Login = () => Task.FromException<TokenResponse>(new TaskCanceledException("Timed out")),
        };
        var viewModel = await CreateSignedOutViewModelAsync(session, authApi);
        viewModel.Email = UserEmail;
        viewModel.Password = UserPassword;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            viewModel.ErrorMessage);
        Assert.False(viewModel.IsAuthenticated);
        Assert.False(viewModel.IsSigningIn);
    }

    [Fact]
    public async Task LogoutAsync_ServerFailure_ClearsSession()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var authApi = new FakeAuthApi
        {
            Logout = _ => Task.FromException(new HttpRequestException("Connection refused")),
        };
        var viewModel = await CreateSignedOutViewModelAsync(session, authApi);

        await viewModel.InitializeAsync();
        await viewModel.LogoutCommand.ExecuteAsync(null);

        Assert.Equal("refresh-1", authApi.LastLogoutRefreshToken);
        Assert.False(session.IsAuthenticated);
        Assert.Null(store.Tokens);
        Assert.False(viewModel.IsAuthenticated);
        Assert.True(viewModel.IsFormVisible);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Empty(viewModel.Password);
        Assert.Empty(viewModel.Email);
    }

    [Fact]
    public async Task InitializeAsync_WithoutTokens_MakesNoRequests()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var accountApi = new FakeAccountApi();
        var viewModel = CreateViewModel(session, accountApi: accountApi);

        await viewModel.InitializeAsync();

        Assert.Equal(0, accountApi.Calls);
        Assert.False(viewModel.IsAuthenticated);
        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.IsFormVisible);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_StoredTokens_ShowsProfileWithoutPassword()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var accountApi = new FakeAccountApi();
        var viewModel = CreateViewModel(session, accountApi: accountApi);

        await viewModel.InitializeAsync();

        Assert.Equal(1, accountApi.Calls);
        Assert.True(viewModel.IsAuthenticated);
        Assert.True(viewModel.IsProfileVisible);
        Assert.False(viewModel.IsLoading);
        Assert.Equal(UserEmail, viewModel.ProfileEmail);
        Assert.Empty(viewModel.Password);
    }

    /// <summary>
    /// Создаёт уже открытый экран без сессии — то, что видит пользователь перед вводом пароля.
    /// </summary>
    /// <remarks>
    /// Вход в реальном приложении возможен только после <c>OnAppearing</c>: до этого экран
    /// в состоянии «проверяем сессию», и форма недоступна. Тесты повторяют этот порядок.
    /// </remarks>
    private static async Task<ProfileViewModel> CreateSignedOutViewModelAsync(
        TokenSession session,
        FakeAuthApi? authApi = null,
        FakeAccountApi? accountApi = null)
    {
        var viewModel = CreateViewModel(session, authApi, accountApi);

        await viewModel.InitializeAsync();

        return viewModel;
    }

    private static ProfileViewModel CreateViewModel(
        TokenSession session,
        FakeAuthApi? authApi = null,
        FakeAccountApi? accountApi = null) =>
        new(authApi ?? new FakeAuthApi(), accountApi ?? new FakeAccountApi(), session);

    private static TokenResponse Tokens(string accessToken, string refreshToken) =>
        new(accessToken, "Bearer", 900, refreshToken, 1209600);

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, string errorCode) =>
        new(
            statusCode,
            "Request failed.",
            [errorCode],
            new Dictionary<string, string[]>());

    /// <summary>
    /// Подменённый клиент авторизации.
    /// </summary>
    private sealed class FakeAuthApi : IAuthApi
    {
        public Func<Task<TokenResponse>> Login { get; set; } = () => Task.FromResult(Tokens("access-1", "refresh-1"));

        public Func<string?, Task> Logout { get; set; } = _ => Task.CompletedTask;

        public string? LastPassword { get; private set; }

        public string? LastLogoutRefreshToken { get; private set; }

        public Task<TokenResponse> LoginAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default)
        {
            LastPassword = password;

            return Login();
        }

        public Task<TokenResponse> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Tokens("access-2", "refresh-2"));

        public Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
        {
            LastLogoutRefreshToken = refreshToken;

            return Logout(refreshToken);
        }
    }

    /// <summary>
    /// Подменённый клиент текущего пользователя: считает вызовы, чтобы «старт без токенов»
    /// доказывался отсутствием сети, а не состоянием экрана.
    /// </summary>
    private sealed class FakeAccountApi : IAccountApi
    {
        public int Calls { get; private set; }

        public Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default)
        {
            Calls++;

            return Task.FromResult(new MeResponse(
                Guid.CreateVersion7(),
                UserEmail,
                "Customer",
                null,
                IsEmailVerified: true));
        }
    }

    /// <summary>
    /// Подменённое хранилище пары.
    /// </summary>
    private sealed class FakeTokenStore : ITokenStore
    {
        public StoredTokens? Tokens { get; set; }

        public Task<StoredTokens?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Tokens);

        public Task SaveAsync(StoredTokens tokens, CancellationToken cancellationToken = default)
        {
            Tokens = tokens;

            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Tokens = null;

            return Task.CompletedTask;
        }
    }
}
