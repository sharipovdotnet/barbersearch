using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Mobile.Core.Api;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки HTTP-клиента: разбор успешной страницы и ошибки сервера.
/// </summary>
/// <remarks>
/// Сеть подменяется <see cref="HttpMessageHandler"/>: тест не поднимает бэкенд, но проходит
/// через настоящий <see cref="HttpClient"/> и настоящие настройки JSON.
/// </remarks>
public sealed class BarberSearchApiTests
{
    private const string SearchPageJson = """
        {
          "items": [
            {
              "id": "0199d3b4-1f2c-7cc5-9c1a-2b3c4d5e6f70",
              "displayName": "Алиса",
              "grade": "SeniorBarber",
              "yearsOfExperience": 7,
              "languages": ["Ru"],
              "rating": 4.5,
              "reviewsCount": 12,
              "isVerified": true,
              "primarySalonId": "0199d3b4-1f2c-7cc5-9c1a-2b3c4d5e6f71",
              "primarySalonName": "Барбершоп на Ленина",
              "city": "Тюмень",
              "primaryPlaceKind": "Salon",
              "additionalServices": ["борода"],
              "avatarUrl": null
            }
          ],
          "nextCursor": null
        }
        """;

    [Fact]
    public async Task SearchBarbersAsync_SendsFiltersAndParsesPage()
    {
        var handler = new StubHandler(HttpStatusCode.OK, SearchPageJson, "application/json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberSearchApi(httpClient);

        var page = await api.SearchBarbersAsync("Тюмень", pageSize: 10);

        var barber = Assert.Single(page.Items);
        Assert.Equal("Алиса", barber.DisplayName);
        Assert.Equal(4.5m, barber.Rating);
        Assert.Null(page.NextCursor);

        var query = Uri.UnescapeDataString(handler.LastRequestUri!.Query);
        Assert.Contains("city=Тюмень", query, StringComparison.Ordinal);
        Assert.Contains("pageSize=10", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchBarbersAsync_TurnsProblemDetailsIntoException()
    {
        const string problemJson = """
            {
              "title": "One or more validation errors occurred.",
              "status": 400,
              "detail": "Город слишком длинный.",
              "errorCodes": ["barberSearch.city.tooLong"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.BadRequest, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberSearchApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.SearchBarbersAsync("Тюмень"));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("Город слишком длинный.", exception.Message);
        Assert.Contains("barberSearch.city.tooLong", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task SearchBarbersAsync_KeepsStatusCode_WhenBodyIsNotProblemDetails()
    {
        var handler = new StubHandler(HttpStatusCode.BadGateway, "<html>Bad gateway</html>", "text/html");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberSearchApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.SearchBarbersAsync(null));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Empty(exception.ErrorCodes);
    }

    [Fact]
    public async Task SearchBarbersAsync_OmitsEmptyFilters()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"items":[],"nextCursor":null}""", "application/json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberSearchApi(httpClient);

        var page = await api.SearchBarbersAsync("   ");

        Assert.Empty(page.Items);
        Assert.Equal("/api/barbers", handler.LastRequestUri!.AbsolutePath);
        Assert.Equal(string.Empty, handler.LastRequestUri.Query);
    }

    /// <summary>
    /// Подменённый обработчик: отдаёт заранее заданный ответ и запоминает адрес запроса.
    /// </summary>
    private sealed class StubHandler(HttpStatusCode statusCode, string body, string contentType) : HttpMessageHandler
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
