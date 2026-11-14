using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.Auth.Register;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Auth;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента авторизации: тело запросов, разбор пары и отказ сервера.
/// </summary>
/// <remarks>
/// Сеть подменяется <see cref="HttpMessageHandler"/>: тест проходит через настоящий
/// <see cref="HttpClient"/> и настоящие настройки JSON, но не поднимает бэкенд.
/// </remarks>
public sealed class AuthApiTests
{
    private const string TokenJson = """
        {
          "accessToken": "access-1",
          "tokenType": "Bearer",
          "expiresInSeconds": 900,
          "refreshToken": "refresh-1",
          "refreshTokenExpiresInSeconds": 1209600
        }
        """;

    [Fact]
    public async Task LoginAsync_TokenResponse_ParsesTokens()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, TokenJson);
        using var httpClient = CreateClient(handler);
        var api = new AuthApi(httpClient);

        var tokens = await api.LoginAsync("mobile-test@barbersearch.test", "Mobile-test-1!");

        Assert.Equal("access-1", tokens.AccessToken);
        Assert.Equal("Bearer", tokens.TokenType);
        Assert.Equal(900, tokens.ExpiresInSeconds);
        Assert.Equal("refresh-1", tokens.RefreshToken);
        Assert.Equal(1209600, tokens.RefreshTokenExpiresInSeconds);
    }

    [Fact]
    public async Task LoginAsync_Credentials_SendEmailAndPasswordInBody()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, TokenJson);
        using var httpClient = CreateClient(handler);
        var api = new AuthApi(httpClient);

        await api.LoginAsync("mobile-test@barbersearch.test", "Mobile-test-1!");

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/auth/login", handler.LastUri!.AbsolutePath);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(
            "mobile-test@barbersearch.test",
            body.RootElement.GetProperty("email").GetString());
        Assert.Equal(
            "Mobile-test-1!",
            body.RootElement.GetProperty("password").GetString());
    }

    [Fact]
    public async Task RefreshAsync_RefreshToken_IsSentInBody()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, TokenJson);
        using var httpClient = CreateClient(handler);
        var api = new AuthApi(httpClient);

        var tokens = await api.RefreshAsync("refresh-1");

        Assert.Equal("access-1", tokens.AccessToken);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/auth/refresh", handler.LastUri!.AbsolutePath);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("refresh-1", body.RootElement.GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task LogoutAsync_RefreshToken_IsSentInBody()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = CreateClient(handler);
        var api = new AuthApi(httpClient);

        await api.LogoutAsync("refresh-1");

        Assert.Equal("/api/auth/logout", handler.LastUri!.AbsolutePath);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("refresh-1", body.RootElement.GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task LoginAsync_ProblemDetails_TurnsIntoErrorCode()
    {
        const string problemJson = """
            {
              "title": "Unauthorized",
              "status": 401,
              "detail": "Invalid email or password.",
              "errorCodes": ["auth.login.invalidCredentials"]
            }
            """;

        var handler = new RecordingHandler(HttpStatusCode.Unauthorized, problemJson, "application/problem+json");
        using var httpClient = CreateClient(handler);
        var api = new AuthApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.LoginAsync("mobile-test@barbersearch.test", "wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("auth.login.invalidCredentials", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_ReusedToken_TurnsIntoErrorCode()
    {
        const string problemJson = """
            {
              "status": 401,
              "detail": "Refresh token was already used.",
              "errorCodes": ["auth.refresh.reused"]
            }
            """;

        var handler = new RecordingHandler(HttpStatusCode.Unauthorized, problemJson, "application/problem+json");
        using var httpClient = CreateClient(handler);
        var api = new AuthApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.RefreshAsync("refresh-1"));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("auth.refresh.reused", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task RegisterAsync_NewAccount_PostsRequestAndParsesId()
    {
        const string createdJson = """{ "id": "0192f5a4-0000-7000-8000-000000000001" }""";

        var handler = new RecordingHandler(HttpStatusCode.Created, createdJson);
        using var httpClient = CreateClient(handler);
        var api = new AuthApi(httpClient);

        var id = await api.RegisterAsync(new RegisterRequest(
            "mobile-test@barbersearch.test",
            "Mobile-test-1!",
            "Ева",
            "Клиентова",
            "2026-11-11"));

        Assert.Equal(Guid.Parse("0192f5a4-0000-7000-8000-000000000001"), id);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/auth/register", handler.LastUri!.AbsolutePath);

        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("mobile-test@barbersearch.test", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("Mobile-test-1!", body.RootElement.GetProperty("password").GetString());
        Assert.Equal("Ева", body.RootElement.GetProperty("firstName").GetString());
        Assert.Equal("Клиентова", body.RootElement.GetProperty("lastName").GetString());
        Assert.Equal("2026-11-11", body.RootElement.GetProperty("consentVersion").GetString());
    }

    [Fact]
    public async Task RegisterAsync_EmailTaken_TurnsIntoErrorCode()
    {
        const string problemJson = """
            {
              "status": 409,
              "detail": "A user with this email already exists.",
              "errorCodes": ["user.create.emailTaken"]
            }
            """;

        var handler = new RecordingHandler(HttpStatusCode.Conflict, problemJson, "application/problem+json");
        using var httpClient = CreateClient(handler);
        var api = new AuthApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.RegisterAsync(new RegisterRequest(
                "mobile-test@barbersearch.test",
                "Mobile-test-1!",
                "Ева",
                "Клиентова",
                "2026-11-11")));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains("user.create.emailTaken", exception.ErrorCodes, StringComparer.Ordinal);
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://localhost:5243/") };

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает запрос с телом.
    /// </summary>
    private sealed class RecordingHandler(HttpStatusCode statusCode, string body, string contentType = "application/json")
        : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastUri { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            };
        }
    }
}
