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
using BarberSearch.Mobile.Core.Push;
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
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";

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
        Assert.False(viewModel.ShowLoading);
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
    public async Task InitializeAsync_FirstProfileLoad_ShowsOneSpinner()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var accountApi = new FakeAccountApi();
        var viewModel = CreateViewModel(session, accountApi: accountApi);
        var spinnerWhileReading = false;

        accountApi.Profile = () =>
        {
            // Пока идёт первое чтение профиля, страница показывает ровно один индикатор:
            // им же покрыта загрузка профиля, поэтому второго спиннера на экране нет.
            spinnerWhileReading = viewModel.ShowLoading && viewModel.ShowProfileLoading;

            return Task.FromResult(ProfileOf());
        };

        await viewModel.InitializeAsync();

        Assert.True(spinnerWhileReading);
        Assert.False(viewModel.ShowLoading);
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
        Assert.False(viewModel.ShowLoading);
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

        // Повтор после ошибки — это первая загрузка без данных: индикатор нужен и тогда,
        // когда проверка сессии уже прошла (IsLoading снят), поэтому сторожа — ShowLoading.
        var spinnerWhileRetrying = false;
        accountApi.Profile = () =>
        {
            spinnerWhileRetrying = viewModel.ShowLoading;

            return Task.FromResult(ProfileOf());
        };

        await viewModel.RetryProfileCommand.ExecuteAsync(null);

        Assert.True(spinnerWhileRetrying);
        Assert.False(viewModel.ShowLoading);
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

    [Fact]
    public void ForgotPasswordCommand_RaisesNavigationEvent()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var viewModel = CreateViewModel(session);
        var raised = 0;
        viewModel.ForgotPasswordRequested += (_, _) => raised++;

        viewModel.ForgotPasswordCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task InitializeAsync_PushTransientFailure_ShowsNoDeviceHint()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var push = new FakePushRegistrar
        {
            IsConfigured = true,
            EnsureResult = PushTokenResult.TransientFailure,
        };
        var viewModel = CreateViewModel(session, push: push);

        await viewModel.InitializeAsync();

        // Обрыв связи или 5xx — не свойство устройства: подсказка про настройки телефона
        // была бы неправдой, а следующая попытка может успеть.
        Assert.True(viewModel.NotificationsEnabled);
        Assert.Empty(viewModel.NotificationsStatusText);
        Assert.False(viewModel.HasNotificationsStatus);
        Assert.False(viewModel.ShowNotificationSettings);
    }

    [Fact]
    public async Task LogoutAsync_UnregistersBeforeLogoutRequest()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var order = new List<string>(2);
        var authApi = new FakeAuthApi
        {
            Logout = _ =>
            {
                order.Add("logout");

                return Task.CompletedTask;
            },
        };
        var push = new FakePushRegistrar
        {
            UnregisterCallback = () =>
            {
                order.Add("unregister");

                // Снятие идёт с Bearer: к этому моменту пара ещё должна быть жива.
                Assert.True(session.IsAuthenticated);
            },
        };
        var viewModel = CreateViewModel(session, authApi: authApi, push: push);

        await viewModel.InitializeAsync();
        await viewModel.LogoutCommand.ExecuteAsync(null);

        Assert.Equal(2, order.Count);
        Assert.Equal("unregister", order[0]);
        Assert.Equal("logout", order[1]);
        Assert.Equal(1, push.UnregisterCalls);
    }

    [Fact]
    public async Task InitializeAsync_PushNotConfigured_HidesNotificationsRow()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var push = new FakePushRegistrar();
        var viewModel = CreateViewModel(session, push: push);

        await viewModel.InitializeAsync();

        // Без push.json строка настроек не показывается и разрешение не запрашивается.
        Assert.False(viewModel.IsPushConfigured);
        Assert.Equal(0, push.EnsureCalls);
        Assert.Empty(viewModel.NotificationsStatusText);
        Assert.False(viewModel.HasNotificationsStatus);
    }

    [Fact]
    public async Task InitializeAsync_PushConfigured_ShowsToggleOnAndRegisters()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var push = new FakePushRegistrar { IsConfigured = true };
        var viewModel = CreateViewModel(session, push: push);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsPushConfigured);
        Assert.True(viewModel.NotificationsEnabled);
        Assert.Equal(1, push.EnsureCalls);
        Assert.Empty(viewModel.NotificationsStatusText);
        Assert.False(viewModel.ShowNotificationSettings);
    }

    [Fact]
    public async Task InitializeAsync_PushPermissionDenied_ShowsSettingsHint()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var push = new FakePushRegistrar
        {
            IsConfigured = true,
            EnsureResult = PushTokenResult.Denied,
        };
        var viewModel = CreateViewModel(session, push: push);

        await viewModel.InitializeAsync();

        Assert.Equal(
            "Уведомления выключены в настройках телефона. Разрешите их, чтобы получать напоминания.",
            viewModel.NotificationsStatusText);
        Assert.True(viewModel.HasNotificationsStatus);

        // Отказ лечится только в настройках телефона, поэтому ссылка показана.
        Assert.True(viewModel.ShowNotificationSettings);
    }

    [Fact]
    public async Task InitializeAsync_PushUnavailable_ShowsDeviceHint()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var push = new FakePushRegistrar
        {
            IsConfigured = true,
            EnsureResult = PushTokenResult.Unavailable,
        };
        var viewModel = CreateViewModel(session, push: push);

        await viewModel.InitializeAsync();

        Assert.Equal("Уведомления недоступны на этом устройстве", viewModel.NotificationsStatusText);
        Assert.False(viewModel.ShowNotificationSettings);
    }

    [Fact]
    public async Task ToggleNotificationsCommand_Off_UnregistersAndStoresChoice()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var push = new FakePushRegistrar { IsConfigured = true };
        var viewModel = CreateViewModel(session, push: push);

        await viewModel.InitializeAsync();
        await viewModel.ToggleNotificationsCommand.ExecuteAsync(false);

        Assert.False(push.IsUserEnabled);
        Assert.False(viewModel.NotificationsEnabled);
        Assert.Equal(false, push.LastSetEnabled);
        Assert.Empty(viewModel.NotificationsStatusText);
    }

    [Fact]
    public async Task ToggleNotificationsCommand_OnDenied_KeepsChoiceAndShowsHint()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var push = new FakePushRegistrar
        {
            IsConfigured = true,
            SetEnabledResult = PushTokenResult.Denied,
        };
        var viewModel = CreateViewModel(session, push: push);

        await viewModel.InitializeAsync();
        await viewModel.ToggleNotificationsCommand.ExecuteAsync(true);

        // Выбор человека остаётся включённым, а подсказка объясняет, что разрешить —
        // в настройках телефона, а не в приложении.
        Assert.True(viewModel.NotificationsEnabled);
        Assert.Equal(true, push.LastSetEnabled);
        Assert.Equal(
            "Уведомления выключены в настройках телефона. Разрешите их, чтобы получать напоминания.",
            viewModel.NotificationsStatusText);
        Assert.True(viewModel.ShowNotificationSettings);
    }

    [Fact]
    public async Task StartAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        using var harness = new Harness(FastRetry());
        var calls = 0;

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls <= 3
                ? Task.FromException<MyProfileResponse>(new HttpRequestException("refused"))
                : Task.FromResult(ProfileOf());
        };

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowProfileError);

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(3, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal("Ева Клиентова", harness.ViewModel.DisplayName);
        Assert.Null(harness.ViewModel.ProfileError);
        Assert.False(harness.ViewModel.ShowProfileError);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task StartAutoRetry_TerminalFailure_DoesNotStart(HttpStatusCode statusCode)
    {
        using var harness = new Harness(FastRetry());

        harness.AccountApi.Profile = () => Task.FromException<MyProfileResponse>(
            ApiError(statusCode, "user.notFound"));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal(1, harness.AccountApi.ProfileCalls);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task StopAutoRetry_WhileWaiting_StopsFurtherAttempts()
    {
        using var harness = new Harness(StopAfterFirstAttemptRetry());

        harness.AccountApi.Profile = () => Task.FromException<MyProfileResponse>(
            new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.True(harness.ViewModel.IsAutoRetrying);

        harness.ViewModel.StopAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal(2, harness.AccountApi.ProfileCalls);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task RetryProfileCommand_WhileAutoRetrying_LoadsImmediatelyAndSuccessDoesNotRestart()
    {
        using var harness = new Harness(HangingRetry());
        var calls = 0;

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<MyProfileResponse>(new HttpRequestException("refused"))
                : Task.FromResult(ProfileOf());
        };

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.True(harness.ViewModel.IsAutoRetrying);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);

        // «Повторить» — это «повторить сейчас»: цикл останавливается, чтение идёт без паузы.
        await harness.ViewModel.RetryProfileCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.AccountApi.ProfileCalls);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
        Assert.True(harness.ViewModel.IsProfileVisible);
        Assert.Null(harness.ViewModel.ProfileError);
    }

    [Fact]
    public async Task RetryProfileCommand_WhileAutoRetrying_LoadFailsRetryable_RestartsAndKeepsRetryingUntilSuccess()
    {
        var restartGate = new TaskCompletionSource();
        var silentAttempt = new TaskCompletionSource<MyProfileResponse>();

        using var harness = new Harness(StopThenGateRetry(restartGate.Task));
        var calls = 0;

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls switch
            {
                1 or 2 => Task.FromException<MyProfileResponse>(new HttpRequestException("refused")),
                3 => silentAttempt.Task,
                _ => Task.FromResult(ProfileOf()),
            };
        };

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsAutoRetrying);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);

        // Кнопка при работающем цикле читает сразу: к возврату команды ручной запрос уже ушёл,
        // а перезапущенный цикл ждёт паузы — второго запроса без неё нет.
        await viewModel.RetryProfileCommand.ExecuteAsync(null);

        Assert.Equal(2, calls);
        Assert.Equal(2, harness.AccountApi.ProfileCalls);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.True(viewModel.IsAutoRetrying);

        // Повторяемый сбой кнопки возвращает цикл к жизни: первая же тихая попытка падает
        // снова, и цикл не сдаётся, а повторяет до успеха.
        restartGate.SetResult();
        silentAttempt.SetException(new HttpRequestException("refused"));

        await viewModel.AutoRetryCompletion;

        Assert.Equal(2, viewModel.AutoRetry.Attempts);
        Assert.Equal(4, calls);
        Assert.Equal(4, harness.AccountApi.ProfileCalls);
        Assert.Equal("Ева Клиентова", viewModel.DisplayName);
        Assert.Null(viewModel.ProfileError);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_FailedSilentAttempt_KeepsProfile()
    {
        using var harness = new Harness(StopAfterFirstAttemptRetry());
        var calls = 0;
        var retry = new TaskCompletionSource<MyProfileResponse>();

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<MyProfileResponse>(new HttpRequestException("refused"))
                : retry.Task;
        };

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowProfileError);
        Assert.NotNull(viewModel.ProfileError);

        viewModel.StartAutoRetry();

        // Попытка висит: причина, строка «Пробуем снова…» и «Повторить» остаются на месте,
        // пока ответа нет. Иначе блок с кнопкой исчезал бы на каждой паузе.
        Assert.Equal(1, viewModel.AutoRetry.Attempts);
        Assert.True(viewModel.ShowProfileError);
        Assert.NotNull(viewModel.ProfileError);
        Assert.True(viewModel.IsAutoRetrying);
        Assert.False(viewModel.ShowLoading);

        retry.SetException(new HttpRequestException("refused"));

        Assert.True(viewModel.ShowProfileError);
        Assert.Equal(NetworkText, viewModel.ProfileError);

        viewModel.StopAutoRetry();
        await viewModel.AutoRetryCompletion;

        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task StopAutoRetry_DuringSilentAttempt_KeepsProfile()
    {
        using var harness = new Harness(StopAfterFirstAttemptRetry());
        var calls = 0;
        var retry = new TaskCompletionSource<MyProfileResponse>();

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<MyProfileResponse>(new HttpRequestException("refused"))
                : retry.Task;
        };

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();

        // Отменённая попытка не имеет права унести с собой причину: иначе экран остался бы
        // пустым и нажать было бы нечего.
        viewModel.StopAutoRetry();
        retry.SetCanceled();
        await viewModel.AutoRetryCompletion;

        Assert.True(viewModel.ShowProfileError);
        Assert.Equal(NetworkText, viewModel.ProfileError);
        Assert.False(viewModel.IsAutoRetrying);
        Assert.False(viewModel.ShowLoading);
    }

    [Fact]
    public async Task AutoRetry_Timeout_ShowsNetworkText()
    {
        using var harness = new Harness(FastRetry());
        var calls = 0;

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<MyProfileResponse>(new TaskCanceledException("timeout"))
                : Task.FromResult(ProfileOf());
        };

        await harness.ViewModel.InitializeAsync();

        // Таймаут HttpClient приходит отменой, но токен жив: для человека это «нет связи».
        Assert.Equal(NetworkText, harness.ViewModel.ProfileError);

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal("Ева Клиентова", harness.ViewModel.DisplayName);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_TerminalAfterRetryable_DoesNotRestart()
    {
        using var harness = new Harness(FastRetry());
        var calls = 0;

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<MyProfileResponse>(new HttpRequestException("refused"))
                : Task.FromException<MyProfileResponse>(
                    ApiError(HttpStatusCode.NotFound, "user.notFound"));
        };

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);

        // Вид сбоя запомнен для неуспеха, а не для «повторяемого»: иначе старый транспорт
        // пережил бы 404 и новое появление вкладки снова запустило бы цикл на отказе.
        harness.ViewModel.StartAutoRetry();

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal(2, harness.AccountApi.ProfileCalls);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task IsAutoRetrying_TogglesWithLoopState()
    {
        using var harness = new Harness(HangingRetry());

        harness.AccountApi.Profile = () => Task.FromException<MyProfileResponse>(
            new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();
        Assert.True(harness.ViewModel.IsAutoRetrying);

        harness.ViewModel.StopAutoRetry();
        Assert.False(harness.ViewModel.IsAutoRetrying);

        await harness.ViewModel.AutoRetryCompletion;
    }

    [Fact]
    public async Task LogoutCommand_WhileAutoRetrying_StopsLoopAndShowsSignInForm()
    {
        using var harness = new Harness(HangingRetry());

        harness.AccountApi.Profile = () => Task.FromException<MyProfileResponse>(
            new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.True(harness.ViewModel.IsAutoRetrying);

        // Выход — это и есть «повторять больше нечего»: цикл не должен сходить в сеть без токена.
        await harness.ViewModel.LogoutCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.IsAutoRetrying);
        Assert.False(harness.ViewModel.IsAuthenticated);
        Assert.True(harness.ViewModel.IsFormVisible);
        Assert.False(harness.Session.IsAuthenticated);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);

        await harness.ViewModel.AutoRetryCompletion;
    }

    [Fact]
    public async Task StopAutoRetry_BeforePushNavigation_StopsRequestsUntilPageShown()
    {
        using var harness = new Harness(StopAfterFirstAttemptRetry());
        var calls = 0;

        harness.AccountApi.Profile = () =>
        {
            calls++;

            return calls <= 2
                ? Task.FromException<MyProfileResponse>(new HttpRequestException("refused"))
                : Task.FromResult(ProfileOf());
        };

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);

        // Страница останавливает цикл перед push-навигацией: Shell может не вызвать
        // OnDisappearing, и повтор иначе ходил бы в сеть под открытым дочерним экраном.
        harness.ViewModel.StopAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.False(harness.ViewModel.IsAutoRetrying);
        Assert.Equal(2, calls);
        Assert.True(harness.ViewModel.ShowProfileError);

        // Возврат на вкладку снова поднимает цикл: ошибка всё ещё повторяемая.
        harness.ViewModel.StartAutoRetry();

        Assert.True(harness.ViewModel.IsAutoRetrying);

        harness.ViewModel.StopAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;
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
        FakeAccountApi? accountApi = null,
        IPushRegistrar? push = null,
        AutoRetryLoop? autoRetry = null) =>
        new(
            authApi ?? new FakeAuthApi(),
            accountApi ?? new FakeAccountApi(),
            session,
            push ?? new FakePushRegistrar(),
            autoRetry);

    /// <summary>
    /// Цикл с мгновенной паузой: секунды расписания в тесте не тратятся.
    /// </summary>
    private static AutoRetryLoop FastRetry() =>
        new(
            static (_, _) => Task.CompletedTask,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    /// <summary>
    /// Цикл, у которого первая пауза проходит сразу, а вторая висит до остановки: так видно
    /// состояние между попытками, не ожидая реального времени.
    /// </summary>
    private static AutoRetryLoop StopAfterFirstAttemptRetry()
    {
        var waits = 0;

        return new AutoRetryLoop(
            (_, cancellationToken) => Interlocked.Increment(ref waits) == 1
                ? Task.CompletedTask
                : WaitForCancellationAsync(cancellationToken),
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);
    }

    /// <summary>
    /// Цикл, у которого первая пауза висит до остановки, а следующая ждёт внешнего сигнала:
    /// так видно и работу цикла до нажатия кнопки, и его перезапуск после неё, без таймеров.
    /// </summary>
    private static AutoRetryLoop StopThenGateRetry(Task restartGate)
    {
        var waits = 0;

        return new AutoRetryLoop(
            (_, cancellationToken) =>
            {
                var wait = Interlocked.Increment(ref waits);

                return wait == 1
                    ? WaitForCancellationAsync(cancellationToken)
                    : wait == 2
                        ? restartGate
                        : Task.CompletedTask;
            },
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);
    }

    /// <summary>
    /// Цикл, который висит на первой же паузе: проверяется остановка до всякой попытки.
    /// </summary>
    private static AutoRetryLoop HangingRetry() =>
        new(
            (_, cancellationToken) => WaitForCancellationAsync(cancellationToken),
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    /// <summary>
    /// Ожидание, которое заканчивается только отменой: Stop проверяется без таймеров.
    /// </summary>
    private static Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));

        return pending.Task;
    }

    /// <summary>
    /// Собранный хаб: сессия с парой, подменённые клиенты и цикл автоповтора без реальных пауз.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        public Harness(AutoRetryLoop autoRetry)
        {
            Store.Tokens = new StoredTokens("access-1", "refresh-1");
            Session = new TokenSession(Store);
            ViewModel = new ProfileViewModel(AuthApi, AccountApi, Session, Push, autoRetry);
        }

        public FakeTokenStore Store { get; } = new();

        public TokenSession Session { get; }

        public FakeAuthApi AuthApi { get; } = new();

        public FakeAccountApi AccountApi { get; } = new();

        public FakePushRegistrar Push { get; } = new();

        public ProfileViewModel ViewModel { get; }

        public void Dispose() => Session.Dispose();
    }

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

        public Task RequestPasswordResetAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Вкладка «Профиль» не просит ссылку сброса.");

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
    /// Подменённый регистратор push: считает вызовы и отдаёт заданное состояние.
    /// </summary>
    private sealed class FakePushRegistrar : IPushRegistrar
    {
        public bool IsConfigured { get; init; }

        public bool IsUserEnabled { get; private set; } = true;

        public PushTokenResult EnsureResult { get; init; } = PushTokenResult.Available("fcm-token");

        public PushTokenResult SetEnabledResult { get; init; } = PushTokenResult.Available("fcm-token");

        public int EnsureCalls { get; private set; }

        public int UnregisterCalls { get; private set; }

        public bool? LastSetEnabled { get; private set; }

        /// <summary>
        /// Действие в момент снятия: тесту нужен порядок относительно запроса выхода.
        /// </summary>
        public Action? UnregisterCallback { get; init; }

        public Task<PushTokenResult> EnsureRegisteredAsync(
            CancellationToken cancellationToken = default)
        {
            EnsureCalls++;

            return Task.FromResult(EnsureResult);
        }

        public Task UnregisterAsync(CancellationToken cancellationToken = default)
        {
            UnregisterCalls++;
            UnregisterCallback?.Invoke();

            return Task.CompletedTask;
        }

        public Task HandleTokenChangedAsync(
            string token,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PushTokenResult> SetEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            LastSetEnabled = enabled;
            IsUserEnabled = enabled;

            return Task.FromResult(enabled ? SetEnabledResult : PushTokenResult.Disabled);
        }

        public void OpenSettings()
        {
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
