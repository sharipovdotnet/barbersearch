using System.Net;
using System.Text;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента данных формы ручной записи мастера: поиск клиентов, options и слоты.
/// </summary>
/// <remarks>
/// Слоты читаются из <c>api/barber/slots</c>, а не из публичной карточки: у кабинета нет
/// идентификатора профиля в запросе, и профиль может быть ещё не подтверждён. Тест фиксирует
/// и этот адрес, и то, что пустая строка поиска не отсекается клиентом — правило объявлено
/// на сервере.
/// </remarks>
public sealed class BarberClientsApiTests
{
    private static readonly Guid BarberProfileId = Guid.Parse("9e100000-0000-4000-8000-000000000001");
    private static readonly Guid SalonId = Guid.Parse("9e100000-0000-4000-8000-000000000002");
    private static readonly Guid HairServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000003");

    [Fact]
    public async Task SearchClientsAsync_Search_SendsTrimmedEscapedQueryAndParsesList()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ClientsJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberClientsApi(httpClient);

        var clients = await api.SearchClientsAsync("  +7 900 000-00-00  ");

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/api/barber/clients", handler.LastRequestUri!.AbsolutePath);

        // Пробелы по краям срезаны, а сам запрос экранирован: «+» иначе стал бы пробелом
        // при разборе строки запроса.
        Assert.Contains(
            string.Concat("search=", Uri.EscapeDataString("+7 900 000-00-00")),
            handler.LastRequestUri.Query,
            StringComparison.Ordinal);

        var client = Assert.Single(clients);

        Assert.Equal("Ева", client.FirstName);
        Assert.Equal("Клиентова", client.LastName);
        Assert.Equal("eva@example.com", client.Email);
        Assert.Null(client.PhoneNumber);
    }

    [Fact]
    public async Task SearchClientsAsync_TooShort_ServerRejectionBecomesException()
    {
        const string problemJson = """
            {
              "title": "Bad Request",
              "status": 400,
              "detail": "At least three characters are required.",
              "errorCodes": ["barberClient.search.tooShort"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.BadRequest, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberClientsApi(httpClient);

        // Клиент не повторяет минимальную длину: отказ приходит доменным кодом сервера.
        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.SearchClientsAsync("ab"));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Contains("barberClient.search.tooShort", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetBookingOptionsAsync_Options_DeserialisesServicesAndSalons()
    {
        var handler = new StubHandler(HttpStatusCode.OK, OptionsJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberClientsApi(httpClient);

        var options = await api.GetBookingOptionsAsync();

        Assert.Equal("/api/barber/booking-options", handler.LastRequestUri!.AbsolutePath);
        Assert.Equal(string.Empty, handler.LastRequestUri.Query);

        var service = Assert.Single(options.Services);

        Assert.Equal(2400m, service.Price);
        Assert.Equal(45, service.DurationMinutes);
        Assert.True(service.IsActive);

        var salon = Assert.Single(options.Salons);

        Assert.Equal(SalonId, salon.SalonId);
        Assert.Equal(PlaceKind.Salon, salon.Kind);
        Assert.True(salon.IsPrimary);
    }

    [Fact]
    public async Task GetSlotsAsync_DateAndOptionalIds_SendsQueryAndParsesSlots()
    {
        var handler = new StubHandler(HttpStatusCode.OK, SlotsJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberClientsApi(httpClient);

        var slots = await api.GetSlotsAsync(
            new DateOnly(2026, 11, 15),
            HairServiceId,
            SalonId);

        Assert.Equal("/api/barber/slots", handler.LastRequestUri!.AbsolutePath);

        var query = QueryParameters(handler.LastRequestUri);

        Assert.Equal("2026-11-15", query["date"]);
        Assert.Equal(HairServiceId.ToString("D"), query["hairServiceId"]);
        Assert.Equal(SalonId.ToString("D"), query["salonId"]);

        Assert.Equal(new DateOnly(2026, 11, 15), slots.Date);
        Assert.Equal(45, slots.DurationMinutes);
        Assert.Equal("Europe/Moscow", slots.TimeZoneId);
        Assert.Single(slots.Slots);
    }

    [Fact]
    public async Task GetSlotsAsync_NullOptionalIds_OmitsThem()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            $$"""
            {
              "barberProfileId": "{{BarberProfileId:D}}",
              "salonId": "{{SalonId:D}}",
              "barberServiceId": null,
              "hairServiceId": null,
              "date": "2026-11-16",
              "timeZoneId": "Europe/Moscow",
              "dayStart": "2026-11-15T21:00:00+00:00",
              "dayEnd": "2026-11-16T21:00:00+00:00",
              "durationMinutes": 30,
              "isWorkingDay": false,
              "slots": []
            }
            """);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberClientsApi(httpClient);

        var slots = await api.GetSlotsAsync(new DateOnly(2026, 11, 16), null, null);

        var query = QueryParameters(handler.LastRequestUri!);

        Assert.Equal("2026-11-16", query["date"]);
        Assert.False(query.ContainsKey("hairServiceId"));
        Assert.False(query.ContainsKey("salonId"));
        Assert.False(slots.IsWorkingDay);
        Assert.Empty(slots.Slots);
    }

    private static string ClientsJson() => """
        [
          {
            "id": "9e100000-0000-4000-8000-0000000000b2",
            "firstName": "Ева",
            "lastName": "Клиентова",
            "email": "eva@example.com",
            "phoneNumber": null
          }
        ]
        """;

    private static string OptionsJson() => $$"""
        {
          "services": [
            {
              "barberServiceId": "9e100000-0000-4000-8000-000000000004",
              "hairServiceId": "{{HairServiceId:D}}",
              "name": "Стрижка",
              "category": "Haircut",
              "defaultDurationMinutes": 40,
              "price": 2400,
              "durationMinutes": 45,
              "isActive": true
            }
          ],
          "salons": [
            {
              "id": "9e100000-0000-4000-8000-000000000010",
              "salonId": "{{SalonId:D}}",
              "name": "E2E Studio",
              "address": "Ленина, 1",
              "city": "Москва",
              "country": "Россия",
              "timeZoneId": "Europe/Moscow",
              "kind": "Salon",
              "isPrimary": true,
              "workingSchedules": []
            }
          ]
        }
        """;

    private static string SlotsJson() => $$"""
        {
          "barberProfileId": "{{BarberProfileId:D}}",
          "salonId": "{{SalonId:D}}",
          "barberServiceId": null,
          "hairServiceId": "{{HairServiceId:D}}",
          "date": "2026-11-15",
          "timeZoneId": "Europe/Moscow",
          "dayStart": "2026-11-14T21:00:00+00:00",
          "dayEnd": "2026-11-15T21:00:00+00:00",
          "durationMinutes": 45,
          "isWorkingDay": true,
          "slots": [
            { "startAt": "2026-11-15T15:30:00+00:00", "endAt": "2026-11-15T16:15:00+00:00" }
          ]
        }
        """;

    /// <summary>
    /// Разбирает строку запроса вручную: <see cref="Uri"/> раскрывает не все символы.
    /// </summary>
    private static Dictionary<string, string> QueryParameters(Uri uri)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var name = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];

            parameters[Uri.UnescapeDataString(name)] = Uri.UnescapeDataString(value);
        }

        return parameters;
    }

    /// <summary>
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод и адрес.
    /// </summary>
    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string? body = null,
        string contentType = "application/json")
        : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = body is null ? null : new StringContent(body, Encoding.UTF8, contentType),
            });
        }
    }
}
