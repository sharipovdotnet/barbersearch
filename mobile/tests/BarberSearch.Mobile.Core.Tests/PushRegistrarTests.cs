using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Domain.Push;
using BarberSearch.Mobile.Core.Api.Push;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Push;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки регистратора push: когда телефон регистрируется, когда нет и что остаётся
/// в настройках после сбоя.
/// </summary>
/// <remarks>
/// Все фальшивые зависимости написаны руками: пакетов для моков в проекте нет. Проверяется
/// наблюдаемое поведение — запросы к API, метка в настройках и состояние токена, — а не
/// внутренние вызовы: регистратор обязан не бросать и не мешать входу и выходу.
/// </remarks>
public sealed class PushRegistrarTests
{
    private const string DeviceToken = "fcm-token-1";
    private const string RefreshedToken = "fcm-token-2";

    private static readonly PushOptions Configured = new(true, "project", "app-id", "api-key", "sender");

    [Fact]
    public async Task EnsureRegisteredAsync_SignedInAndAvailable_RegistersAndroidToken()
    {
        using var harness = Create();

        var result = await harness.Registrar.EnsureRegisteredAsync();

        Assert.Equal(PushTokenStatus.Available, result.Status);

        var registration = Assert.Single(harness.Api.Registrations);

        Assert.Equal(DeviceToken, registration.Token);
        Assert.Equal(PushPlatform.Android, registration.Platform);
        Assert.Equal(DeviceToken, harness.State.RegisteredToken);

        // Разрешение спрашивается при регистрации, а не при каждом обращении к токену.
        Assert.True(harness.Tokens.LastRequestPermission);
    }

    [Fact]
    public async Task EnsureRegisteredAsync_NotConfigured_DoesNothing()
    {
        using var harness = Create(options: PushOptions.Disabled);

        var result = await harness.Registrar.EnsureRegisteredAsync();

        Assert.Equal(PushTokenStatus.Disabled, result.Status);
        Assert.Empty(harness.Api.Registrations);
        Assert.Equal(0, harness.Tokens.Calls);
        Assert.Equal(0, harness.Permission.Requests);
        Assert.Null(harness.State.RegisteredToken);
    }

    [Fact]
    public async Task EnsureRegisteredAsync_UserDisabled_DoesNothing()
    {
        using var harness = Create();
        harness.State.SetEnabled(false);

        var result = await harness.Registrar.EnsureRegisteredAsync();

        Assert.Equal(PushTokenStatus.Disabled, result.Status);
        Assert.Empty(harness.Api.Registrations);
        Assert.Equal(0, harness.Tokens.Calls);
    }

    [Fact]
    public async Task EnsureRegisteredAsync_NotSignedIn_DoesNothing()
    {
        using var harness = Create(signedIn: false);

        var result = await harness.Registrar.EnsureRegisteredAsync();

        Assert.Equal(PushTokenStatus.Disabled, result.Status);
        Assert.Empty(harness.Api.Registrations);
        Assert.Equal(0, harness.Tokens.Calls);
    }

    [Fact]
    public async Task EnsureRegisteredAsync_PermissionDenied_DoesNotRegister()
    {
        using var harness = Create(token: PushTokenResult.Denied);

        var result = await harness.Registrar.EnsureRegisteredAsync();

        Assert.Equal(PushTokenStatus.Denied, result.Status);
        Assert.Empty(harness.Api.Registrations);
        Assert.Null(harness.State.RegisteredToken);
    }

    [Fact]
    public async Task EnsureRegisteredAsync_DeviceUnavailable_DoesNotRegister()
    {
        using var harness = Create(token: PushTokenResult.Unavailable);

        var result = await harness.Registrar.EnsureRegisteredAsync();

        Assert.Equal(PushTokenStatus.Unavailable, result.Status);
        Assert.Empty(harness.Api.Registrations);
    }

    [Fact]
    public async Task EnsureRegisteredAsync_TwiceSameSessionAndToken_RegistersOnce()
    {
        using var harness = Create();

        await harness.Registrar.EnsureRegisteredAsync();
        await harness.Registrar.EnsureRegisteredAsync();

        Assert.Single(harness.Api.Registrations);
    }

    [Fact]
    public async Task EnsureRegisteredAsync_ApiFailure_ReturnsTransientAndClearsMarker()
    {
        using var harness = Create();
        harness.Api.RegisterFailure = _ => new HttpRequestException("Connection refused");

        var result = await harness.Registrar.EnsureRegisteredAsync();

        Assert.Equal(PushTokenStatus.TransientFailure, result.Status);
        Assert.Empty(harness.Api.Registrations);

        // Метка не должна пережить сбой регистрации: иначе следующий запуск не повторил бы её.
        Assert.Null(harness.State.RegisteredToken);
    }

    [Fact]
    public async Task UnregisterAsync_RegisteredToken_PostsTokenAndClearsMarker()
    {
        using var harness = Create();
        await harness.Registrar.EnsureRegisteredAsync();

        await harness.Registrar.UnregisterAsync();

        Assert.Equal(DeviceToken, Assert.Single(harness.Api.Unregistrations));
        Assert.Null(harness.State.RegisteredToken);
    }

    [Fact]
    public async Task UnregisterAsync_ApiFailure_KeepsMarkerForRetry()
    {
        using var harness = Create();
        await harness.Registrar.EnsureRegisteredAsync();
        harness.Api.UnregisterFailure = new HttpRequestException("Connection refused");

        await harness.Registrar.UnregisterAsync();

        // Снятие не прошло: метка остаётся единственным, что известно о токене, и следующий
        // Ensure при выключенном переключателе повторит запрос.
        Assert.Equal(DeviceToken, harness.State.RegisteredToken);
        Assert.Empty(harness.Api.Unregistrations);
    }

    [Fact]
    public async Task EnsureRegisteredAsync_ToggleOffAfterFailedUnregister_RetriesIt()
    {
        using var harness = Create();
        await harness.Registrar.EnsureRegisteredAsync();
        harness.Api.UnregisterFailure = new HttpRequestException("Connection refused");

        await harness.Registrar.SetEnabledAsync(false);

        Assert.Equal(DeviceToken, harness.State.RegisteredToken);

        harness.Api.UnregisterFailure = null;

        var result = await harness.Registrar.EnsureRegisteredAsync();

        Assert.Equal(PushTokenStatus.Disabled, result.Status);
        Assert.Null(harness.State.RegisteredToken);
        Assert.Equal(DeviceToken, Assert.Single(harness.Api.Unregistrations));
    }

    [Fact]
    public async Task EnsureRegisteredAsync_SessionEndedDuringTokenFetch_DoesNotRegister()
    {
        using var harness = Create();

        harness.Tokens.OnGetToken = async () =>
        {
            await harness.Session.ClearAsync();

            return PushTokenResult.Available(DeviceToken);
        };

        var result = await harness.Registrar.EnsureRegisteredAsync();

        // Сессия кончилась, пока ждали токен: регистрация за вышедшим пользователем не нужна.
        Assert.Equal(PushTokenStatus.Disabled, result.Status);
        Assert.Empty(harness.Api.Registrations);
        Assert.Null(harness.State.RegisteredToken);
    }

    [Fact]
    public async Task EnsureRegisteredAsync_SecondCallWaitingOnGateAfterSignOut_DoesNotRegister()
    {
        using var harness = Create();
        harness.Api.RegisterGate = new TaskCompletionSource();

        // Первый вызов держит семафор внутри HTTP-регистрации.
        var first = harness.Registrar.EnsureRegisteredAsync();

        // Второй проходит проверку сессии до выхода и встаёт в очередь на семафор.
        var second = harness.Registrar.EnsureRegisteredAsync();

        // Пока регистрация в полёте, сессия завершается: поколение растёт.
        await harness.Session.ClearAsync();

        harness.Api.RegisterGate.SetResult();

        await first;
        await second;

        // Первый запрос уже нельзя отменить, но второй — запоздавший — не должен отправиться.
        Assert.Single(harness.Api.Registrations);
    }

    [Fact]
    public async Task UnregisterAsync_NoMarker_DoesNotCallApi()
    {
        using var harness = Create();

        await harness.Registrar.UnregisterAsync();

        Assert.Empty(harness.Api.Unregistrations);
    }

    [Fact]
    public async Task HandleTokenChangedAsync_NewToken_RegistersIt()
    {
        using var harness = Create();
        await harness.Registrar.EnsureRegisteredAsync();

        await harness.Registrar.HandleTokenChangedAsync(RefreshedToken);

        Assert.Equal(2, harness.Api.Registrations.Count);
        Assert.Equal(RefreshedToken, harness.Api.Registrations[1].Token);
        Assert.Equal(RefreshedToken, harness.State.RegisteredToken);
    }

    [Fact]
    public async Task HandleTokenChangedAsync_PermissionDenied_DoesNotRegister()
    {
        using var harness = Create(granted: false);

        await harness.Registrar.HandleTokenChangedAsync(RefreshedToken);

        Assert.Empty(harness.Api.Registrations);
    }

    [Fact]
    public async Task HandleTokenChangedAsync_NotSignedIn_DoesNotRegister()
    {
        using var harness = Create(signedIn: false);

        await harness.Registrar.HandleTokenChangedAsync(RefreshedToken);

        Assert.Empty(harness.Api.Registrations);
    }

    [Fact]
    public async Task TokenRefreshed_RegisteredSession_RegistersNewToken()
    {
        using var harness = Create();
        await harness.Registrar.EnsureRegisteredAsync();

        // Служба FCM сообщает новый токен событием, а не прямым вызовом регистратора.
        harness.Tokens.RaiseTokenRefreshed(RefreshedToken);

        Assert.Equal(2, harness.Api.Registrations.Count);
        Assert.Equal(RefreshedToken, harness.Api.Registrations[1].Token);
    }

    [Fact]
    public async Task SetEnabledAsync_False_UnregistersAndStoresChoice()
    {
        using var harness = Create();
        await harness.Registrar.EnsureRegisteredAsync();

        var result = await harness.Registrar.SetEnabledAsync(false);

        Assert.Equal(PushTokenStatus.Disabled, result.Status);
        Assert.False(harness.State.IsEnabled);
        Assert.Null(harness.State.RegisteredToken);
        Assert.Equal(DeviceToken, Assert.Single(harness.Api.Unregistrations));
    }

    [Fact]
    public async Task SetEnabledAsync_True_RegistersDevice()
    {
        using var harness = Create();
        harness.State.SetEnabled(false);

        var result = await harness.Registrar.SetEnabledAsync(true);

        Assert.Equal(PushTokenStatus.Available, result.Status);
        Assert.True(harness.State.IsEnabled);
        Assert.Single(harness.Api.Registrations);
    }

    [Fact]
    public async Task SessionStarted_RegistersDevice()
    {
        using var harness = Create(signedIn: false);

        await harness.Session.ApplyAsync(Tokens("access-2", "refresh-2"));

        // Вход — новая сессия: сервер должен привязать токен к вошедшему пользователю.
        Assert.Single(harness.Api.Registrations);
        Assert.Equal(DeviceToken, harness.Api.Registrations[0].Token);
    }

    [Fact]
    public async Task SessionEnded_KeepsMarkerWithoutUnregister()
    {
        using var harness = Create();
        await harness.Registrar.EnsureRegisteredAsync();

        // Сессия истекла: POST unregister без Bearer уже не пройдёт, но метку не теряем —
        // после следующего входа снятие можно повторить (переключатель выключен) или
        // перезаписать её регистрацией того же токена.
        await harness.Session.ClearAsync();

        Assert.Equal(DeviceToken, harness.State.RegisteredToken);
        Assert.Empty(harness.Api.Unregistrations);
    }

    [Fact]
    public void OpenSettings_DelegatesToPermission()
    {
        using var harness = Create();

        harness.Registrar.OpenSettings();

        Assert.Equal(1, harness.Permission.OpenSettingsCalls);
    }

    private static Harness Create(
        PushOptions? options = null,
        bool signedIn = true,
        bool granted = true,
        PushTokenResult? token = null)
    {
        var tokenStore = new FakeTokenStore();

        if (signedIn)
        {
            tokenStore.Tokens = new StoredTokens("access-1", "refresh-1");
        }

        var session = new TokenSession(tokenStore);
        var api = new FakePushApi();
        var tokens = new FakePushTokenProvider(token ?? PushTokenResult.Available(DeviceToken));
        var permission = new FakePushPermission { Granted = granted };
        var state = new FakePushStateStore();
        var registrar = new PushRegistrar(
            api,
            tokens,
            permission,
            state,
            session,
            options ?? Configured);

        return new Harness(registrar, session, api, tokens, permission, state);
    }

    private static TokenResponse Tokens(string accessToken, string refreshToken) =>
        new(accessToken, "Bearer", 900, refreshToken, 1209600);

    /// <summary>
    /// Собранный регистратор вместе с фальшивыми зависимостями и освобождением ресурсов.
    /// </summary>
    private sealed class Harness(
        PushRegistrar registrar,
        TokenSession session,
        FakePushApi api,
        FakePushTokenProvider tokens,
        FakePushPermission permission,
        FakePushStateStore state) : IDisposable
    {
        public PushRegistrar Registrar { get; } = registrar;

        public TokenSession Session { get; } = session;

        public FakePushApi Api { get; } = api;

        public FakePushTokenProvider Tokens { get; } = tokens;

        public FakePushPermission Permission { get; } = permission;

        public FakePushStateStore State { get; } = state;

        public void Dispose()
        {
            Registrar.Dispose();
            Session.Dispose();
        }
    }

    /// <summary>
    /// Подменённый API: считает запросы и умеет отказывать по требованию теста.
    /// </summary>
    private sealed class FakePushApi : IPushApi
    {
        public List<(string Token, PushPlatform Platform)> Registrations { get; } = [];

        public List<string> Unregistrations { get; } = [];

        public Func<string, Exception?>? RegisterFailure { get; set; }

        public Exception? UnregisterFailure { get; set; }

        /// <summary>
        /// Задерживает ответ регистрации: тесту нужен второй вызов, ждущий семафор.
        /// </summary>
        public TaskCompletionSource? RegisterGate { get; set; }

        public async Task RegisterDeviceAsync(
            string token,
            PushPlatform platform,
            CancellationToken cancellationToken = default)
        {
            var failure = RegisterFailure?.Invoke(token);

            if (failure is not null)
            {
                throw failure;
            }

            if (RegisterGate is not null)
            {
                await RegisterGate.Task;
            }

            Registrations.Add((token, platform));
        }

        public Task UnregisterDeviceAsync(
            string token,
            CancellationToken cancellationToken = default)
        {
            if (UnregisterFailure is not null)
            {
                return Task.FromException(UnregisterFailure);
            }

            Unregistrations.Add(token);

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Подменённый источник токена: отдаёт заданный результат и умеет поднять событие FCM.
    /// </summary>
    private sealed class FakePushTokenProvider(PushTokenResult result) : IPushTokenProvider
    {
        public PushTokenResult Result { get; set; } = result;

        /// <summary>
        /// Подмена ответа на лету: тесту смены сессии нужно выполнить действие до возврата.
        /// </summary>
        public Func<Task<PushTokenResult>>? OnGetToken { get; set; }

        public int Calls { get; private set; }

        public bool? LastRequestPermission { get; private set; }

        public event EventHandler<string>? TokenRefreshed;

        public async Task<PushTokenResult> GetTokenAsync(
            bool requestPermission,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequestPermission = requestPermission;

            return OnGetToken is null
                ? Result
                : await OnGetToken();
        }

        public void RaiseTokenRefreshed(string token) => TokenRefreshed?.Invoke(this, token);
    }

    /// <summary>
    /// Подменённое разрешение: считает запросы, открытие настроек и выдачу.
    /// </summary>
    private sealed class FakePushPermission : IPushPermission
    {
        public bool Granted { get; init; } = true;

        public int Requests { get; private set; }

        public int OpenSettingsCalls { get; private set; }

        public Task<bool> IsGrantedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Granted);

        public Task<bool> RequestAsync(CancellationToken cancellationToken = default)
        {
            Requests++;

            return Task.FromResult(Granted);
        }

        public void OpenSettings() => OpenSettingsCalls++;
    }

    /// <summary>
    /// Подменённое локальное состояние push.
    /// </summary>
    private sealed class FakePushStateStore : IPushStateStore
    {
        public bool IsEnabled { get; private set; } = true;

        public string? RegisteredToken { get; private set; }

        public void SetEnabled(bool enabled) => IsEnabled = enabled;

        public void SetRegisteredToken(string? token) => RegisteredToken = token;
    }

    /// <summary>
    /// Подменённое хранилище пары токенов.
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
