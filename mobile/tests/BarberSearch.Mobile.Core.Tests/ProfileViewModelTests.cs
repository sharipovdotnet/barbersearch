using System.Net;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Contracts.Auth.Register;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки вкладки «Профиль»: вход, хаб аккаунта, тихое обновление и выход.
/// </summary>
/// <remarks>
/// <para>
/// Тексты сверяются дословно с вебом (<c>frontend/src/lib/sign-in-errors.ts</c>): расхождение
/// здесь — не опечатка, а разная копия одного продукта в двух клиентах.
/// </para>
/// <para>
/// Хаб читает <c>GET api/users/me</c>, а не <c>/auth/me</c>: второй остаётся проверкой сессии
/// и источником <c>userId</c> для «Записей». Поэтому фальшивый клиент считает оба чтения
/// раздельно, а тесты «без сессии» доказывают отсутствие сети обоими счётчиками.
/// </para>
/// </remarks>
public sealed class ProfileViewModelTests
{
    private const string UserEmail = "mobile-test@barbersearch.test";
    private const string UserPassword = "Mobile-test-1!";
    private const string SessionExpiredText = "Сессия истекла — войдите снова.";

    [Fact]
    public async Task LoginAsync_ValidCredentials_SavesTokensAndShowsAccount()
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
    public async Task LoginAsync_ValidCredentials_ShowsNameRoleAndInitials()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var viewModel = await CreateSignedOutViewModelAsync(session);
        viewModel.Email = UserEmail;
        viewModel.Password = UserPassword;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal("Ева Клиентова", viewModel.DisplayName);
        Assert.Equal("ЕК", viewModel.Initials);
        Assert.Equal("Клиент", viewModel.RoleText);
        Assert.Equal("Адрес подтверждён.", viewModel.EmailStatusText);
        Assert.False(viewModel.ShowEmailWarning);
    }

    [Fact]
    public async Task LoginAsync_JustLoggedInWithoutProfile_KeepsEmailAndSections()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var accountApi = new FakeAccountApi
        {
            Profile = () => Task.FromException<MyProfileResponse>(
                ApiError(HttpStatusCode.InternalServerError, "internal.database")),
        };
        var viewModel = await CreateSignedOutViewModelAsync(session, accountApi: accountApi);
        viewModel.Email = UserEmail;
        viewModel.Password = UserPassword;

        await viewModel.LoginCommand.ExecuteAsync(null);

        // Вход состоялся, и терять его из-за показа имени нельзя: email из формы виден,
        // разделы доступны, а причина ошибки осталась в памяти.
        Assert.True(viewModel.IsAuthenticated);
        Assert.True(viewModel.IsProfileVisible);
        Assert.False(viewModel.ShowProfileError);
        Assert.Equal(UserEmail, viewModel.ProfileEmail);
        Assert.NotNull(viewModel.ProfileError);
        Assert.Empty(viewModel.DisplayName);
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
    public async Task LogoutAsync_LoadedProfile_ClearsPreviousUserData()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var viewModel = await CreateSignedOutViewModelAsync(session);

        await viewModel.InitializeAsync();
        await viewModel.LogoutCommand.ExecuteAsync(null);

        // Данные прошлого пользователя не остаются ни в одном привязанном свойстве.
        Assert.Empty(viewModel.DisplayName);
        Assert.Empty(viewModel.Initials);
        Assert.Empty(viewModel.RoleText);
        Assert.Empty(viewModel.EmailStatusText);
        Assert.Null(viewModel.ProfileEmail);
        Assert.False(viewModel.IsEmailVerified);
        Assert.False(viewModel.IsProfileVisible);
    }

    [Fact]
    public async Task InitializeAsync_WithoutTokens_MakesNoRequests()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var accountApi = new FakeAccountApi();
        var viewModel = CreateViewModel(session, accountApi: accountApi);

        await viewModel.InitializeAsync();

        Assert.Equal(0, accountApi.MeCalls);
        Assert.Equal(0, accountApi.ProfileCalls);
        Assert.False(viewModel.IsAuthenticated);
        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.IsFormVisible);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_StoredTokens_ShowsAccountWithoutPassword()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var accountApi = new FakeAccountApi();
        var viewModel = CreateViewModel(session, accountApi: accountApi);

        await viewModel.InitializeAsync();

        Assert.Equal(1, accountApi.ProfileCalls);
        Assert.True(viewModel.IsAuthenticated);
        Assert.True(viewModel.IsProfileVisible);
        Assert.False(viewModel.IsLoading);
        Assert.Equal(UserEmail, viewModel.ProfileEmail);
        Assert.Equal("Ева Клиентова", viewModel.DisplayName);
        Assert.Empty(viewModel.Password);
    }

    [Fact]
    public async Task InitializeAsync_ProfileUnauthorized_ClearsSessionAndShowsForm()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var accountApi = new FakeAccountApi
        {
            Profile = () => Task.FromException<MyProfileResponse>(
                ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized")),
        };
        var viewModel = CreateViewModel(session, accountApi: accountApi);

        await viewModel.InitializeAsync();

        Assert.False(session.IsAuthenticated);
        Assert.Null(store.Tokens);
        Assert.False(viewModel.IsAuthenticated);
        Assert.True(viewModel.IsFormVisible);
        Assert.Equal(SessionExpiredText, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_NetworkFailure_KeepsSessionAndShowsRetry()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var accountApi = new FakeAccountApi
        {
            Profile = () => Task.FromException<MyProfileResponse>(new HttpRequestException("refused")),
        };
        var viewModel = CreateViewModel(session, accountApi: accountApi);

        await viewModel.InitializeAsync();

        // Ошибка сети из аккаунта не выкидывает: сессия жива, показать нужно «Повторить».
        Assert.True(session.IsAuthenticated);
        Assert.True(viewModel.IsAuthenticated);
        Assert.True(viewModel.ShowProfileError);
        Assert.False(viewModel.IsFormVisible);
        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            viewModel.ProfileError);
    }

    [Fact]
    public async Task InitializeAsync_RateLimited_ShowsRateLimitText()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var accountApi = new FakeAccountApi
        {
            Profile = () => Task.FromException<MyProfileResponse>(
                ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit")),
        };
        var viewModel = CreateViewModel(session, accountApi: accountApi);

        await viewModel.InitializeAsync();

        Assert.Equal(
            "Слишком много запросов. Подождите минуту и повторите.",
            viewModel.ProfileError);
        Assert.True(viewModel.IsAuthenticated);
    }

    [Fact]
    public async Task InitializeAsync_SecondAppearance_RefreshesProfileSilently()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var accountApi = new FakeAccountApi();
        var viewModel = CreateViewModel(session, accountApi: accountApi);

        await viewModel.InitializeAsync();
        accountApi.Profile = () => Task.FromResult(ProfileOf(firstName: "Анна"));
        await viewModel.InitializeAsync();

        Assert.Equal(2, accountApi.ProfileCalls);
        Assert.Equal("Анна Клиентова", viewModel.DisplayName);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.IsProfileLoading);
        Assert.False(viewModel.ShowProfileLoading);
    }

    [Fact]
    public async Task InitializeAsync_SilentRefreshFailure_KeepsShownData()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var accountApi = new FakeAccountApi();
        var viewModel = CreateViewModel(session, accountApi: accountApi);

        await viewModel.InitializeAsync();
        accountApi.Profile = () => Task.FromException<MyProfileResponse>(new HttpRequestException("refused"));
        await viewModel.InitializeAsync();

        // Тихое обновление не стирает карточку: человек видит прежние данные и причину.
        Assert.Equal("Ева Клиентова", viewModel.DisplayName);
        Assert.True(viewModel.IsProfileVisible);
        Assert.NotNull(viewModel.ProfileError);
        Assert.True(viewModel.HasProfileError);
    }

    [Fact]
    public async Task RetryProfileAsync_AfterFailure_LoadsProfile()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var accountApi = new FakeAccountApi
        {
            Profile = () => Task.FromException<MyProfileResponse>(new HttpRequestException("refused")),
        };
        var viewModel = CreateViewModel(session, accountApi: accountApi);

        await viewModel.InitializeAsync();
        accountApi.Profile = () => Task.FromResult(ProfileOf());
        await viewModel.RetryProfileCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ProfileError);
        Assert.False(viewModel.ShowProfileError);
        Assert.Equal("Ева Клиентова", viewModel.DisplayName);
    }

    [Fact]
    public async Task InitializeAsync_UnverifiedEmail_ShowsWarning()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var accountApi = new FakeAccountApi
        {
            Profile = () => Task.FromResult(ProfileOf(isEmailVerified: false)),
        };
        var viewModel = CreateViewModel(session, accountApi: accountApi);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.ShowEmailWarning);
        Assert.False(viewModel.IsEmailVerified);
        Assert.Equal("Адрес не подтверждён.", viewModel.EmailStatusText);
    }

    [Fact]
    public void EditProfileCommand_RaisesNavigationEvent()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var viewModel = CreateViewModel(session);
        var raised = 0;
        viewModel.EditProfileRequested += (_, _) => raised++;

        viewModel.EditProfileCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void ReviewsCommand_RaisesNavigationEvent()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var viewModel = CreateViewModel(session);
        var raised = 0;
        viewModel.ReviewsRequested += (_, _) => raised++;

        viewModel.ReviewsCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void SecurityCommand_RaisesNavigationEvent()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var viewModel = CreateViewModel(session);
        var raised = 0;
        viewModel.SecurityRequested += (_, _) => raised++;

        viewModel.SecurityCommand.Execute(null);

        Assert.Equal(1, raised);
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

    private static MyProfileResponse ProfileOf(
        Guid? id = null,
        string firstName = "Ева",
        string lastName = "Клиентова",
        string? phoneNumber = "+70000000003",
        bool isEmailVerified = true) =>
        new(
            id ?? Guid.CreateVersion7(),
            UserEmail,
            firstName,
            lastName,
            phoneNumber,
            "https://cdn.example.test/avatar.jpg",
            UserRole.Customer,
            null,
            isEmailVerified,
            false,
            new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 1, 12, 30, 0, TimeSpan.Zero),
            false,
            PhotoModerationStatus.Approved,
            null);

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

        public Task<Guid> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Вкладка «Профиль» не регистрирует.");

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
    /// Подменённый клиент аккаунта: считает оба чтения раздельно, чтобы «старт без токенов»
    /// доказывался отсутствием сети, а не состоянием экрана.
    /// </summary>
    private sealed class FakeAccountApi : IAccountApi
    {
        public int MeCalls { get; private set; }

        public int ProfileCalls { get; private set; }

        public Func<Task<MyProfileResponse>> Profile { get; set; } = () => Task.FromResult(ProfileOf());

        public Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default)
        {
            MeCalls++;

            return Task.FromResult(new MeResponse(
                Guid.CreateVersion7(),
                UserEmail,
                "Customer",
                null,
                IsEmailVerified: true));
        }

        public Task<MyProfileResponse> GetMyProfileAsync(CancellationToken cancellationToken = default)
        {
            ProfileCalls++;

            return Profile();
        }

        public Task UpdateProfileAsync(
            Guid userId,
            string firstName,
            string lastName,
            string? phoneNumber,
            string? avatarUrl,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Хаб профиля не сохраняет личные данные.");

        public Task ChangePasswordAsync(
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Хаб профиля не меняет пароль.");

        public Task RequestEmailChangeAsync(
            string newEmail,
            string currentPassword,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Хаб профиля не меняет email.");

        public Task RequestEmailVerificationAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Хаб профиля не отправляет письмо подтверждения.");
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
