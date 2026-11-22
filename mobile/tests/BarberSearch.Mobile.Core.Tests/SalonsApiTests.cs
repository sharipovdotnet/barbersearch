using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Salons;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента создания собственного салона: маршрут, тело и разбор ответа.
/// </summary>
/// <remarks>
/// Путь <c>POST api/salons/own</c> важен: владелец берётся из токена, а не из тела, и другой
/// маршрут создал бы салон от имени администратора. Тело проверяется разбором контракта, а не
/// сравнением строки: camelCase и строковое перечисление — часть настроек <see cref="ApiJson"/>.
/// </remarks>
public sealed class SalonsApiTests
{
    private static readonly Guid CreatedId = Guid.Parse("9e100000-0000-4000-8000-0000000000e1");

    [Fact]
    public async Task CreateOwnAsync_Request_PostsToOwnPathAndParsesCreatedId()
    {
        var handler = new StubHandler(
            HttpStatusCode.Created,
            $$"""{"id":"{{CreatedId:D}}"}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new SalonsApi(httpClient);

        var id = await api.CreateOwnAsync(CreateRequest());

        Assert.Equal(CreatedId, id);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/salons/own", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CreateOwnAsync_Request_SendsWholeContractBody()
    {
        var handler = new StubHandler(
            HttpStatusCode.Created,
            $$"""{"id":"{{CreatedId:D}}"}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new SalonsApi(httpClient);

        await api.CreateOwnAsync(new CreateSalonRequest(
            "Hairo",
            "Ленина, 1",
            "Москва",
            "Россия",
            "Europe/Moscow",
            55.7558,
            37.6173,
            PostalCode: null,
            PhoneNumber: "+7 495 000-00-00",
            WebsiteUrl: "https://example.com",
            Kind: PlaceKind.RentalStudio,
            IsListed: false));

        var parsed = JsonSerializer.Deserialize<CreateSalonRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal("Hairo", parsed.Name);
        Assert.Equal("Ленина, 1", parsed.Address);
        Assert.Equal("Москва", parsed.City);
        Assert.Equal("Россия", parsed.Country);
        Assert.Equal("Europe/Moscow", parsed.TimeZoneId);
        Assert.Equal(55.7558, parsed.Latitude);
        Assert.Equal(37.6173, parsed.Longitude);
        Assert.Null(parsed.PostalCode);
        Assert.Equal("+7 495 000-00-00", parsed.PhoneNumber);
        Assert.Equal("https://example.com", parsed.WebsiteUrl);
        Assert.Equal(PlaceKind.RentalStudio, parsed.Kind);
        Assert.False(parsed.IsListed);

        // Тип места уходит именем перечисления, а не числом: числа в JSON запрещены настройками.
        Assert.Contains("\"kind\":\"RentalStudio\"", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateOwnAsync_NullBody_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.Created, "null");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new SalonsApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateOwnAsync(CreateRequest()));
    }

    [Fact]
    public async Task CreateOwnAsync_CreatedWithoutId_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.Created, "{}");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new SalonsApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateOwnAsync(CreateRequest()));
    }

    [Fact]
    public async Task CreateOwnAsync_EmptyId_Throws()
    {
        var handler = new StubHandler(
            HttpStatusCode.Created,
            """{"id":"00000000-0000-0000-0000-000000000000"}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new SalonsApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateOwnAsync(CreateRequest()));
    }

    [Fact]
    public async Task CreateOwnAsync_ValidationProblem_ParsesFieldCodes()
    {
        const string problemJson = """
            {
              "title": "Bad Request",
              "status": 400,
              "detail": "Validation failed.",
              "errorCodes": ["salon.websiteUrl", "salon.city"]
            }
            """;

        var handler = new StubHandler(
            HttpStatusCode.BadRequest,
            problemJson,
            "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new SalonsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.CreateOwnAsync(CreateRequest()));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Contains("salon.websiteUrl", exception.ErrorCodes, StringComparer.Ordinal);
        Assert.Contains("salon.city", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task CreateOwnAsync_Forbidden_ParsesProfileCode()
    {
        const string problemJson = """
            {
              "title": "Forbidden",
              "status": 403,
              "detail": "The barber profile was not found.",
              "errorCodes": ["barberProfile.notFound"]
            }
            """;

        var handler = new StubHandler(
            HttpStatusCode.Forbidden,
            problemJson,
            "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new SalonsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.CreateOwnAsync(CreateRequest()));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("barberProfile.notFound", exception.ErrorCodes, StringComparer.Ordinal);
    }

    private static CreateSalonRequest CreateRequest() => new(
        "Hairo",
        "Ленина, 1",
        "Москва",
        "Россия",
        "Europe/Moscow",
        55.7558,
        37.6173,
        PostalCode: null,
        PhoneNumber: null,
        WebsiteUrl: null,
        Kind: PlaceKind.Salon,
        IsListed: true);

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод, адрес и тело.
    /// </summary>
    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string? body = null,
        string contentType = "application/json")
        : HttpMessageHandler
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
                Content = body is null ? null : new StringContent(body, Encoding.UTF8, contentType),
            };
        }
    }
}
