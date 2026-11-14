using System.Net;
using System.Text;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента текущего пользователя: разбор <c>GET api/auth/me</c> и отказ сервера.
/// </summary>
public sealed class AccountApiTests
{
    private const string MeJson = """
        {
          "id": "0199d3b4-1f2c-7cc5-9c1a-2b3c4d5e6f70",
          "email": "mobile-test@barbersearch.test",
          "role": "Customer",
          "barberProfileId": null,
          "isEmailVerified": true
        }
        """;

    [Fact]
    public async Task GetMeAsync_CurrentUser_ParsesResponse()
    {
        var handler = new StubHandler(HttpStatusCode.OK, MeJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        var me = await api.GetMeAsync();

        Assert.Equal("mobile-test@barbersearch.test", me.Email);
        Assert.Equal("Customer", me.Role);
        Assert.Null(me.BarberProfileId);
        Assert.True(me.IsEmailVerified);
        Assert.Equal("/api/auth/me", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetMeAsync_Unauthorized_TurnsIntoException()
    {
        const string problemJson = """
            {
              "status": 401,
              "errorCodes": ["auth.unauthorized"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Unauthorized, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AccountApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(() => api.GetMeAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("auth.unauthorized", exception.ErrorCodes, StringComparer.Ordinal);
    }

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает адрес запроса.
    /// </summary>
    private sealed class StubHandler(HttpStatusCode statusCode, string body, string contentType = "application/json")
        : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            });
        }
    }
}
