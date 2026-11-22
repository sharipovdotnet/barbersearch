using System.Net;
using System.Text;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Addresses;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента подсказок адреса: адрес запроса, приоритет провайдера и разбор ответа.
/// </summary>
/// <remarks>
/// Адрес собирается как у веба: <c>query</c>, затем <c>cityProviderId</c> или <c>city</c>,
/// затем <c>limit=8</c>. Провайдерский токен важнее свободного текста, а пустые значения не
/// отправляются вовсе — сервер понимает их как «фильтра нет».
/// </remarks>
public sealed class AddressesApiTests
{
    [Fact]
    public async Task GetAddressSuggestionsAsync_QueryAndCity_BuildsEscapedUrlWithLimit()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AddressesApi(httpClient);

        await api.GetAddressSuggestionsAsync("  Ленина 1  ", "  Москва  ", cityProviderId: null);

        var expected = string.Concat(
            "/api/addresses/suggest?query=",
            Uri.EscapeDataString("Ленина 1"),
            "&city=",
            Uri.EscapeDataString("Москва"),
            "&limit=8");

        Assert.Equal(expected, handler.LastRequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task GetAddressSuggestionsAsync_ProviderId_PreferredOverCity()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AddressesApi(httpClient);

        await api.GetAddressSuggestionsAsync("Ленина", "Москва", "fias-msk");

        var expected = string.Concat(
            "/api/addresses/suggest?query=",
            Uri.EscapeDataString("Ленина"),
            "&cityProviderId=fias-msk",
            "&limit=8");

        Assert.Equal(expected, handler.LastRequestUri!.PathAndQuery);
        Assert.DoesNotContain("city=", handler.LastRequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAddressSuggestionsAsync_EmptyCityAndProvider_OmitsBoth()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AddressesApi(httpClient);

        await api.GetAddressSuggestionsAsync("Ленина", "  ", "  ");

        var expected = string.Concat(
            "/api/addresses/suggest?query=",
            Uri.EscapeDataString("Ленина"),
            "&limit=8");

        Assert.Equal(expected, handler.LastRequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task GetAddressSuggestionsAsync_QueryWithSpecialCharacters_IsEscaped()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AddressesApi(httpClient);

        await api.GetAddressSuggestionsAsync("Ленина, 1/2 & 3", "Москва", null);

        Assert.Contains(
            string.Concat("query=", Uri.EscapeDataString("Ленина, 1/2 & 3")),
            handler.LastRequestUri!.PathAndQuery,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Ленина, 1/2 & 3", handler.LastRequestUri!.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAddressSuggestionsAsync_Answer_DeserialisesAllFields()
    {
        var handler = new StubHandler(HttpStatusCode.OK, SuggestionsJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AddressesApi(httpClient);

        var items = await api.GetAddressSuggestionsAsync("Ленина", "Москва", null);

        var item = Assert.Single(items);

        Assert.Equal("fias-house", item.ProviderId);
        Assert.Equal("г. Москва, ул. Ленина, 1", item.Display);
        Assert.Equal("ул. Ленина, 1", item.Address);
        Assert.Equal("Москва", item.City);
        Assert.Equal("Москва", item.Region);
        Assert.Equal("Россия", item.Country);
        Assert.Equal("101000", item.PostalCode);
        Assert.Equal(55.7558, item.Latitude);
        Assert.Equal(37.6173, item.Longitude);
        Assert.Null(item.TimeZoneId);
    }

    [Fact]
    public async Task GetAddressSuggestionsAsync_NullBody_ThrowsInsteadOfEmptyList()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AddressesApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => api.GetAddressSuggestionsAsync("Ленина", "Москва", null));
    }

    [Fact]
    public async Task GetAddressSuggestionsAsync_NonSuccess_ThrowsParsedProblem()
    {
        const string problemJson = """
            {
              "title": "Unauthorized",
              "status": 401,
              "detail": "Authentication is required.",
              "errorCodes": ["auth.unauthorized"]
            }
            """;

        var handler = new StubHandler(
            HttpStatusCode.Unauthorized,
            problemJson,
            "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AddressesApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.GetAddressSuggestionsAsync("Ленина", "Москва", null));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("auth.unauthorized", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetAddressSuggestionsAsync_BlankQuery_ThrowsWithoutRequest()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new AddressesApi(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => api.GetAddressSuggestionsAsync("   ", "Москва", null));

        Assert.Null(handler.LastRequestUri);
    }

    private static string SuggestionsJson() => """
        [
          {
            "providerId": "fias-house",
            "display": "г. Москва, ул. Ленина, 1",
            "address": "ул. Ленина, 1",
            "city": "Москва",
            "region": "Москва",
            "country": "Россия",
            "postalCode": "101000",
            "latitude": 55.7558,
            "longitude": 37.6173,
            "timeZoneId": null
          }
        ]
        """;

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает адрес запроса.
    /// </summary>
    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string? body = null,
        string contentType = "application/json")
        : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;

            // Тело читается до возврата: после него клиент уже не держит запрос.
            if (request.Content is not null)
            {
                await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = body is null ? null : new StringContent(body, Encoding.UTF8, contentType),
            };
        }
    }
}
