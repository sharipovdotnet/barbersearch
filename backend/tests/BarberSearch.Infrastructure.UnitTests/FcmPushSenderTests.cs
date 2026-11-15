using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BarberSearch.Application.Push;
using BarberSearch.Contracts.Common;
using BarberSearch.Infrastructure.Push;
using BarberSearch.Infrastructure.UnitTests.Fakes;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет контракт FCM HTTP v1: OAuth-токен, тело сообщения, кэш токена и разбор ошибок.
/// </summary>
/// <remarks>
/// Настоящий Google не вызывается: <see cref="IHttpClientFactory"/> подменён, поэтому тест
/// не зависит от сети и не может отправить сообщение. Ключ сервисного аккаунта генерируется
/// в тесте, а не лежит в репозитории.
/// </remarks>
public class FcmPushSenderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid BookingId = Guid.CreateVersion7();

    [Fact]
    public async Task Send_WhenConfigured_RequestsATokenAndPostsTheStandardMessageBody()
    {
        using var harness = new Harness(ConfiguredOptions());

        var result = await harness.Sender.SendAsync(CreateMessage(), "device-token");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, harness.Handler.Requests.Count);

        var tokenRequest = harness.Handler.Requests[0];
        Assert.Equal("oauth2.googleapis.com", tokenRequest.Uri.Host);

        var form = ParseForm(tokenRequest.Body);
        Assert.Equal(
            "urn:ietf:params:oauth:grant-type:jwt-bearer",
            form["grant_type"]);
        Assert.True(form.ContainsKey("assertion"));

        var assertion = form["assertion"].Split('.');
        Assert.Equal(3, assertion.Length);

        using var payload = JsonDocument.Parse(DecodeBase64Url(assertion[1]));
        Assert.Equal(
            "test@example.iam.gserviceaccount.com",
            payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal(FcmAccessTokenProvider.SCOPE, payload.RootElement.GetProperty("scope").GetString());
        Assert.Equal(
            ServiceAccountKey.DEFAULT_TOKEN_URI,
            payload.RootElement.GetProperty("aud").GetString());
        Assert.True(payload.RootElement.GetProperty("exp").GetInt64()
                    > payload.RootElement.GetProperty("iat").GetInt64());

        var sendRequest = harness.Handler.Requests[1];
        Assert.Equal(
            "https://fcm.googleapis.com/v1/projects/demo-project/messages:send",
            sendRequest.Uri.ToString());
        Assert.Equal("Bearer access-token-1", sendRequest.Authorization);

        using var body = JsonDocument.Parse(sendRequest.Body);
        var message = body.RootElement.GetProperty("message");

        Assert.Equal("device-token", message.GetProperty("token").GetString());
        Assert.Equal(
            "Через час: салон «Барбершоп»",
            message.GetProperty("notification").GetProperty("title").GetString());
        Assert.Equal(
            "Мастер Иван · Стрижка · сегодня в 17:30",
            message.GetProperty("notification").GetProperty("body").GetString());

        var data = message.GetProperty("data");
        Assert.Equal(PushMessage.TYPE, data.GetProperty("type").GetString());
        Assert.Equal(BookingId.ToString("D"), data.GetProperty("bookingId").GetString());

        var android = message.GetProperty("android");
        Assert.Equal("high", android.GetProperty("priority").GetString());
        Assert.Equal(
            "reminders",
            android.GetProperty("notification").GetProperty("channel_id").GetString());
    }

    [Fact]
    public async Task Send_Twice_ReusesTheCachedAccessToken()
    {
        using var harness = new Harness(ConfiguredOptions());

        await harness.Sender.SendAsync(CreateMessage(), "device-1");
        await harness.Sender.SendAsync(CreateMessage(), "device-2");

        var tokenRequests = harness.Handler.Requests
            .Count(request => request.Uri.Host == "oauth2.googleapis.com");

        Assert.Equal(1, tokenRequests);
        Assert.Equal(3, harness.Handler.Requests.Count);
    }

    [Fact]
    public async Task Send_WhenFcmReportsUnregistered_ReportsTheTokenAsUnregistered()
    {
        using var harness = new Harness(
            ConfiguredOptions(),
            request => IsOAuth(request)
                ? TokenResponse()
                : Json(
                    HttpStatusCode.NotFound,
                    """{"error":{"status":"NOT_FOUND","details":[{"errorCode":"UNREGISTERED"}]}}"""));

        var result = await harness.Sender.SendAsync(CreateMessage(), "device-token");

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.Push.TokenUnregistered, result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Send_WhenFcmReturns404ForAnotherReason_DoesNotBlameTheToken()
    {
        using var harness = new Harness(
            ConfiguredOptions(),
            request => IsOAuth(request)
                ? TokenResponse()
                : Json(
                    HttpStatusCode.NotFound,
                    """{"error":{"status":"NOT_FOUND","message":"Requested entity was not found."}}"""));

        var result = await harness.Sender.SendAsync(CreateMessage(), "device-token");

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.Push.Unavailable, result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Send_WhenFcmReturns400WithTheDocumentedTokenMessage_ReportsTheTokenAsUnregistered()
    {
        using var harness = new Harness(
            ConfiguredOptions(),
            request => IsOAuth(request)
                ? TokenResponse()
                : Json(
                    HttpStatusCode.BadRequest,
                    """{"error":{"status":"INVALID_ARGUMENT","message":"The registration token is not a valid FCM registration token"}}"""));

        var result = await harness.Sender.SendAsync(CreateMessage(), "device-token");

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.Push.TokenUnregistered, result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Send_WhenFcmReturns400ForAMalformedPayload_RetriesAsTemporary()
    {
        using var harness = new Harness(
            ConfiguredOptions(),
            request => IsOAuth(request)
                ? TokenResponse()
                : Json(
                    HttpStatusCode.BadRequest,
                    """{"error":{"status":"INVALID_ARGUMENT","message":"Invalid JSON payload received."}}"""));

        var result = await harness.Sender.SendAsync(CreateMessage(), "device-token");

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.Push.Send, result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Send_WhenFcmReturns400WithoutInvalidArgument_RetriesAsTemporary()
    {
        using var harness = new Harness(
            ConfiguredOptions(),
            request => IsOAuth(request)
                ? TokenResponse()
                : Json(HttpStatusCode.BadRequest, """{"error":{"status":"INVALID_PAYLOAD"}}"""));

        var result = await harness.Sender.SendAsync(CreateMessage(), "device-token");

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.Push.Send, result.Error.Errors[0].Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Send_WhenFcmReturnsTransientError_ReturnsTemporaryFailure(HttpStatusCode status)
    {
        using var harness = new Harness(
            ConfiguredOptions(),
            request => IsOAuth(request) ? TokenResponse() : Json(status, """{"error":{}}"""));

        var result = await harness.Sender.SendAsync(CreateMessage(), "device-token");

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.Push.Send, result.Error.Errors[0].Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Send_WhenCredentialsAreRejected_ReturnsUnavailable(HttpStatusCode status)
    {
        using var harness = new Harness(
            ConfiguredOptions(),
            request => IsOAuth(request) ? TokenResponse() : Json(status, """{"error":{}}"""));

        var result = await harness.Sender.SendAsync(CreateMessage(), "device-token");

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.Push.Unavailable, result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Send_WhenTheAccessTokenIsRejected_RefreshesItAndRetriesOnce()
    {
        var tokenCalls = 0;
        var sendCalls = 0;

        using var harness = new Harness(
            ConfiguredOptions(),
            request =>
            {
                if (IsOAuth(request))
                {
                    tokenCalls++;

                    return TokenResponse($"access-token-{tokenCalls}");
                }

                sendCalls++;

                return sendCalls == 1
                    ? Json(HttpStatusCode.Unauthorized, """{"error":{"status":"UNAUTHENTICATED"}}""")
                    : Json(HttpStatusCode.OK, """{"name":"ok"}""");
            });

        var result = await harness.Sender.SendAsync(CreateMessage(), "device-token");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, tokenCalls);
        Assert.Equal(2, sendCalls);
    }

    [Fact]
    public async Task Send_WhenTheKeyIsNotAValidPem_ReturnsUnavailableWithoutCallingHttp()
    {
        var options = new PushOptions
        {
            ProjectId = "demo-project",
            ServiceAccountJson = JsonSerializer.Serialize(new
            {
                type = "service_account",
                client_email = "test@example.iam.gserviceaccount.com",
                private_key = "-----BEGIN PRIVATE KEY-----\nnot base64 at all\n-----END PRIVATE KEY-----",
                token_uri = ServiceAccountKey.DEFAULT_TOKEN_URI
            })
        };

        Assert.True(options.IsConfigured);

        using var harness = new Harness(options);

        var first = await harness.Sender.SendAsync(CreateMessage(), "device-token");
        var second = await harness.Sender.SendAsync(CreateMessage(), "device-token");

        Assert.True(first.IsFailure);
        Assert.True(second.IsFailure);
        Assert.Equal(ApiErrorCodes.Push.Unavailable, first.Error.Errors[0].Code);
        Assert.Empty(harness.Handler.Requests);

        // Ошибка конфигурации логируется один раз, а не на каждую строку каждого тика.
        Assert.Single(harness.ProviderLogger.Messages);
        Assert.DoesNotContain(
            "not base64",
            harness.ProviderLogger.Messages[0],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_WithoutConfiguration_DoesNotCallHttp()
    {
        using var harness = new Harness(new PushOptions
        {
            ProjectId = "demo-project",
            ServiceAccountJson = "{ broken key }"
        });

        Assert.False(harness.Sender.IsConfigured);

        var result = await harness.Sender.SendAsync(CreateMessage(), "device-token");

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.Push.Unavailable, result.Error.Errors[0].Code);
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task Send_DoesNotLogTokensOrTheServiceAccount()
    {
        using var harness = new Harness(
            ConfiguredOptions(),
            request => IsOAuth(request)
                ? TokenResponse()
                : Json(HttpStatusCode.ServiceUnavailable, """{"error":{}}"""));

        await harness.Sender.SendAsync(CreateMessage(), "secret-device-token");

        Assert.All(
            harness.Logger.Messages,
            message =>
            {
                Assert.DoesNotContain("secret-device-token", message, StringComparison.Ordinal);
                Assert.DoesNotContain("access-token-1", message, StringComparison.Ordinal);
                Assert.DoesNotContain("PRIVATE KEY", message, StringComparison.Ordinal);
                Assert.DoesNotContain("test@example", message, StringComparison.Ordinal);
            });
    }

    private static PushMessage CreateMessage()
        => new(
            "Через час: салон «Барбершоп»",
            "Мастер Иван · Стрижка · сегодня в 17:30",
            BookingId);

    private static PushOptions ConfiguredOptions()
        => new()
        {
            ProjectId = "demo-project",
            ServiceAccountJson = CreateServiceAccountJson()
        };

    private static string CreateServiceAccountJson()
    {
        using var rsa = RSA.Create(2048);

        return JsonSerializer.Serialize(new
        {
            type = "service_account",
            project_id = "demo-project",
            client_email = "test@example.iam.gserviceaccount.com",
            private_key = rsa.ExportPkcs8PrivateKeyPem(),
            token_uri = ServiceAccountKey.DEFAULT_TOKEN_URI
        });
    }

    private static bool IsOAuth(HttpRequestMessage request)
        => request.RequestUri?.Host == "oauth2.googleapis.com";

    private static HttpResponseMessage TokenResponse(string accessToken = "access-token-1")
        => Json(
            HttpStatusCode.OK,
            $$"""{"access_token":"{{accessToken}}","expires_in":3600,"token_type":"Bearer"}""");

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static Dictionary<string, string> ParseForm(string body)
        => body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty);

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - (padded.Length % 4)) % 4);

        return Convert.FromBase64String(padded);
    }

    private sealed class Harness : IDisposable
    {
        public Harness(
            PushOptions options,
            Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
        {
            Handler = new RecordingHandler(request => respond is null
                ? IsOAuth(request) ? TokenResponse() : Json(HttpStatusCode.OK, """{"name":"ok"}""")
                : respond(request));
            Factory = new StubHttpClientFactory(new HttpClient(Handler));
            Logger = new RecordingLogger<FcmPushSender>();
            ProviderLogger = new RecordingLogger<FcmAccessTokenProvider>();
            Provider = new FcmAccessTokenProvider(
                Factory,
                options,
                new FakeTimeProvider(Now),
                ProviderLogger);
            Sender = new FcmPushSender(Factory, options, Provider, Logger);
        }

        public RecordingHandler Handler { get; }

        public StubHttpClientFactory Factory { get; }

        public RecordingLogger<FcmPushSender> Logger { get; }

        public RecordingLogger<FcmAccessTokenProvider> ProviderLogger { get; }

        public FcmAccessTokenProvider Provider { get; }

        public FcmPushSender Sender { get; }

        public void Dispose() => Provider.Dispose();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            => _respond = respond;

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                body));

            return _respond(request);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed record RecordedRequest(Uri Uri, string? Authorization, string Body);
}
