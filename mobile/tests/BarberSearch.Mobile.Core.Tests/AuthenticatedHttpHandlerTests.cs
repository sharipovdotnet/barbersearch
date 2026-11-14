using System.Net;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Auth;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки авторизационного обработчика: заголовок, один refresh на все <c>401</c> и один повтор.
/// </summary>
/// <remarks>
/// Цепочка настоящая: <see cref="HttpClient"/> → <see cref="AuthenticatedHttpHandler"/> →
/// подменённый последний обработчик. Так проверяется именно поведение обёртки, а не мок.
/// </remarks>
public sealed class AuthenticatedHttpHandlerTests
{
    [Fact]
    public async Task SendAsync_WithSession_SetsBearerHeader()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var terminal = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(session, new FakeAuthApi(), terminal);

        using var response = await client.GetAsync("api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sent = Assert.Single(terminal.Requests);
        Assert.Equal("Bearer", sent.Scheme);
        Assert.Equal("access-1", sent.Token);
    }

    [Fact]
    public async Task SendAsync_WithoutSession_DoesNotSetHeader()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var terminal = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(session, new FakeAuthApi(), terminal);

        using var response = await client.GetAsync("api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Assert.Single(terminal.Requests).Token);
    }

    [Fact]
    public async Task SendAsync_Unauthorized_RefreshesAndRepeatsOnce()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var authApi = new FakeAuthApi();
        var terminal = new ScriptedHandler(request => IsToken(request, "access-1")
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(session, authApi, terminal);

        using var response = await client.GetAsync("api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, terminal.Requests.Count);
        Assert.Equal("access-2", terminal.Requests[1].Token);

        Assert.Equal(1, authApi.RefreshCalls);
        Assert.Equal("refresh-1", authApi.LastRefreshToken);

        Assert.Equal("access-2", session.AccessToken);
        Assert.Equal("refresh-2", store.Tokens!.RefreshToken);
    }

    [Fact]
    public async Task SendAsync_SecondUnauthorized_DoesNotRefreshAgain()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var authApi = new FakeAuthApi();
        var terminal = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = CreateClient(session, authApi, terminal);

        using var response = await client.GetAsync("api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, terminal.Requests.Count);
        Assert.Equal(1, authApi.RefreshCalls);
    }

    [Fact]
    public async Task SendAsync_RefusedRefresh_ClearsSessionAndReturnsUnauthorized()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);
        var sessionEnded = 0;
        session.SessionEnded += (_, _) => sessionEnded++;

        var authApi = new FakeAuthApi
        {
            Refresh = _ => Task.FromException<TokenResponse>(Unauthorized("auth.refresh.reused")),
        };

        var terminal = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = CreateClient(session, authApi, terminal);

        using var response = await client.GetAsync("api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(terminal.Requests);
        Assert.False(session.IsAuthenticated);
        Assert.Null(store.Tokens);
        Assert.Equal(1, sessionEnded);
    }

    [Fact]
    public async Task SendAsync_TemporaryRefreshFailure_KeepsSession()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);

        var authApi = new FakeAuthApi
        {
            Refresh = _ => Task.FromException<TokenResponse>(
                new BarberSearchApiException(
                    HttpStatusCode.TooManyRequests,
                    "Too many requests.",
                    [],
                    new Dictionary<string, string[]>())),
        };

        var terminal = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = CreateClient(session, authApi, terminal);

        using var response = await client.GetAsync("api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(session.IsAuthenticated);
        Assert.NotNull(store.Tokens);
    }

    [Fact]
    public async Task SendAsync_TwoParallelUnauthorized_MakeSingleRefresh()
    {
        var store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
        using var session = new TokenSession(store);

        var authApi = new FakeAuthApi
        {
            Refresh = async _ =>
            {
                // Окно, в котором второй запрос успевает получить свой 401 и встать в очередь.
                await Task.Delay(50);

                return Tokens("access-2", "refresh-2");
            },
        };

        var terminal = new ScriptedHandler(request => IsToken(request, "access-1")
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(session, authApi, terminal);

        var responses = await Task.WhenAll(client.GetAsync("api/auth/me"), client.GetAsync("api/auth/me"));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        foreach (var response in responses)
        {
            response.Dispose();
        }

        // Количество запросов зависит от гонки (второй мог уйти уже с новой парой),
        // а обмен обязан быть ровно один: второй предъявил бы тот же refresh.
        Assert.Equal(1, authApi.RefreshCalls);
        Assert.Equal("access-2", session.AccessToken);
    }

    [Fact]
    public async Task SendAsync_UnauthorizedWithoutSession_DoesNotRefresh()
    {
        var store = new FakeTokenStore();
        using var session = new TokenSession(store);
        var authApi = new FakeAuthApi();
        var terminal = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = CreateClient(session, authApi, terminal);

        using var response = await client.GetAsync("api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, authApi.RefreshCalls);
        Assert.Single(terminal.Requests);
    }

    private static bool IsToken(HttpRequestMessage request, string expected) =>
        string.Equals(request.Headers.Authorization?.Parameter, expected, StringComparison.Ordinal);

    private static HttpClient CreateClient(TokenSession session, IAuthApi authApi, HttpMessageHandler terminal)
    {
        var handler = new AuthenticatedHttpHandler(authApi, session) { InnerHandler = terminal };

        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
    }

    private static TokenResponse Tokens(string accessToken, string refreshToken) =>
        new(accessToken, "Bearer", 900, refreshToken, 1209600);

    private static BarberSearchApiException Unauthorized(string errorCode) =>
        new(
            HttpStatusCode.Unauthorized,
            "Unauthorized.",
            [errorCode],
            new Dictionary<string, string[]>());

    /// <summary>
    /// Последний обработчик цепочки: отвечает по сценарию и запоминает полученные токены.
    /// </summary>
    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<SentRequest> _requests = [];

        public IReadOnlyList<SentRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                // Запрос может быть уже отправлен: запоминаем заголовок до ответа.
                _requests.Add(new SentRequest(
                    request.Headers.Authorization?.Scheme,
                    request.Headers.Authorization?.Parameter));
            }

            return Task.FromResult(respond(request));
        }
    }

    /// <summary>
    /// Снимок отправленного запроса: интересует только токен.
    /// </summary>
    private sealed record SentRequest(string? Scheme, string? Token);

    /// <summary>
    /// Подменённое хранилище пары: состояние в памяти вместо SecureStorage.
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

    /// <summary>
    /// Подменённый клиент авторизации: refresh отвечает сценарием теста.
    /// </summary>
    private sealed class FakeAuthApi : IAuthApi
    {
        public Func<string, Task<TokenResponse>> Refresh { get; set; } =
            _ => Task.FromResult(Tokens("access-2", "refresh-2"));

        public int RefreshCalls { get; private set; }

        public string? LastRefreshToken { get; private set; }

        public Task<TokenResponse> LoginAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The handler must not log in.");

        public Task<TokenResponse> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            LastRefreshToken = refreshToken;

            return Refresh(refreshToken);
        }

        public Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The handler must not log out.");
    }
}
