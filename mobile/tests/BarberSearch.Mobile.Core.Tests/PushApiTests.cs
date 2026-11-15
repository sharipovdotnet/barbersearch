using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Domain.Push;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Push;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента push-устройств: адрес, тело и разбор ошибок.
/// </summary>
/// <remarks>
/// Оба запроса — <c>POST</c>: снятие с регистрации идёт телом, а не <c>DELETE</c> с токеном
/// в пути, потому что токен — секрет устройства и не должен попадать в URL и журналы прокси.
/// Платформа проверяется строкой <c>"Android"</c>: перечисления уходят именами, как у
/// остальных клиентов через <see cref="ApiJson.Options"/>.
/// </remarks>
public sealed class PushApiTests
{
    private const string DeviceToken = "fcm-device-token-0123456789";

    [Fact]
    public async Task RegisterDeviceAsync_ValidToken_PostsAndroidPlatformAsString()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new PushApi(httpClient);

        await api.RegisterDeviceAsync(DeviceToken, PushPlatform.Android);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/push/devices", handler.LastRequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;

        Assert.Equal(DeviceToken, root.GetProperty("token").GetString());

        // Перечисление — именем, а не числом: целые значения ApiJson запрещает.
        Assert.Equal("Android", root.GetProperty("platform").GetString());
        Assert.Contains("\"platform\":\"Android\"", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnregisterDeviceAsync_ValidToken_PostsTokenInBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new PushApi(httpClient);

        await api.UnregisterDeviceAsync(DeviceToken);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/push/devices/unregister", handler.LastRequestUri!.AbsolutePath);

        // Токен в теле, а не в пути: иначе он попал бы в журналы прокси и историю URL.
        Assert.Equal(DeviceToken, JsonDocument.Parse(handler.LastBody!).RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public async Task RegisterDeviceAsync_NoContent_DoesNotReadBody()
    {
        // 204 без тела — норма успеха: клиент не должен пытаться разобрать JSON.
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new PushApi(httpClient);

        await api.RegisterDeviceAsync(DeviceToken, PushPlatform.Android);
    }

    [Fact]
    public async Task RegisterDeviceAsync_ProblemDetails_ThrowsWithCodes()
    {
        const string problemJson = """
            {
              "title": "Unauthorized",
              "status": 401,
              "detail": "The access token is missing or expired.",
              "errorCodes": ["auth.unauthorized"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Unauthorized, problemJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new PushApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.RegisterDeviceAsync(DeviceToken, PushPlatform.Android));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("auth.unauthorized", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task UnregisterDeviceAsync_ServerError_ThrowsWithStatus()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new PushApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.UnregisterDeviceAsync(DeviceToken));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
    }

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод, адрес и тело.
    /// </summary>
    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
