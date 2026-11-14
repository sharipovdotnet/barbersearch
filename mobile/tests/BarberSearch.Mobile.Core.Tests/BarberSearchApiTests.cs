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
    private static readonly Guid BarberProfileId = Guid.Parse("9e100000-0000-4000-8000-000000000001");
    private static readonly Guid SalonId = Guid.Parse("9e100000-0000-4000-8000-000000000002");
    private static readonly Guid HairServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000003");
    private static readonly Guid BarberServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000004");

    private const string BarberProfileJson = """
        {
          "id": "9e100000-0000-4000-8000-000000000001",
          "userId": "9e100000-0000-4000-8000-000000000005",
          "avatarUrl": null,
          "displayName": "Тимур Барберов",
          "grade": "SeniorBarber",
          "bio": "Стригу с 2015 года.",
          "yearsOfExperience": 11,
          "languages": ["Ru"],
          "certificatesAndTraining": [],
          "rating": 4.8,
          "reviewsCount": 12,
          "achievements": [],
          "paymentMethods": ["Cash"],
          "communicationFormat": [],
          "goodies": [],
          "latePolicy": "До 10 минут.",
          "isActive": true,
          "isVerified": true,
          "services": [
            {
              "barberServiceId": "9e100000-0000-4000-8000-000000000004",
              "hairServiceId": "9e100000-0000-4000-8000-000000000003",
              "name": "E2E Стрижка",
              "category": "Haircut",
              "defaultDurationMinutes": 40,
              "price": 2400,
              "durationMinutes": 45,
              "isActive": true
            },
            {
              "barberServiceId": "9e100000-0000-4000-8000-000000000006",
              "hairServiceId": "9e100000-0000-4000-8000-000000000007",
              "name": "Скрытая услуга",
              "category": "Beard",
              "defaultDurationMinutes": 30,
              "price": 1000,
              "durationMinutes": 30,
              "isActive": false
            }
          ],
          "salons": [
            {
              "id": "9e100000-0000-4000-8000-000000000008",
              "salonId": "9e100000-0000-4000-8000-000000000002",
              "name": "E2E Studio",
              "address": "Ленина, 1",
              "city": "Москва",
              "country": "Россия",
              "timeZoneId": "Europe/Moscow",
              "kind": "Salon",
              "isPrimary": true,
              "workingSchedules": []
            }
          ],
          "specializations": [],
          "additionalServices": [],
          "photos": []
        }
        """;

    private const string SlotsJson = """
        {
          "barberProfileId": "9e100000-0000-4000-8000-000000000001",
          "salonId": "9e100000-0000-4000-8000-000000000002",
          "barberServiceId": "9e100000-0000-4000-8000-000000000004",
          "hairServiceId": "9e100000-0000-4000-8000-000000000003",
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

    [Fact]
    public async Task GetBarberProfileAsync_ParsesNestedServicesAndSalons()
    {
        var handler = new StubHandler(HttpStatusCode.OK, BarberProfileJson, "application/json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberSearchApi(httpClient);

        var profile = await api.GetBarberProfileAsync(BarberProfileId);

        Assert.Equal("Тимур Барберов", profile.DisplayName);
        Assert.Equal(2, profile.Services.Count);

        var active = profile.Services[0];
        Assert.Equal(BarberServiceId, active.BarberServiceId);
        Assert.Equal(HairServiceId, active.HairServiceId);
        Assert.Equal(2400m, active.Price);
        Assert.Equal(45, active.DurationMinutes);
        Assert.True(active.IsActive);

        // Выключенная услуга приходит в карточке вместе с активными: фильтрует экран.
        Assert.False(profile.Services[1].IsActive);

        var salon = Assert.Single(profile.Salons);
        Assert.Equal(SalonId, salon.SalonId);
        Assert.Equal("Europe/Moscow", salon.TimeZoneId);
        Assert.True(salon.IsPrimary);

        Assert.Equal(string.Concat("/api/barbers/", BarberProfileId.ToString("D")), handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetBarberProfileAsync_NotFound_TurnsProblemDetailsIntoException()
    {
        const string problemJson = """
            {
              "title": "Not Found",
              "status": 404,
              "detail": "Barber profile was not found.",
              "errorCodes": ["barberProfile.notFound"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.NotFound, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberSearchApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.GetBarberProfileAsync(BarberProfileId));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Contains("barberProfile.notFound", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetSlotsAsync_SendsSalonDateAndFilters()
    {
        var handler = new StubHandler(HttpStatusCode.OK, SlotsJson, "application/json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberSearchApi(httpClient);

        var slots = await api.GetSlotsAsync(
            BarberProfileId,
            new DateOnly(2026, 11, 15),
            HairServiceId,
            SalonId);

        var query = Uri.UnescapeDataString(handler.LastRequestUri!.Query);

        Assert.Equal(
            string.Concat("/api/barbers/", BarberProfileId.ToString("D"), "/slots"),
            handler.LastRequestUri.AbsolutePath);
        Assert.Contains("date=2026-11-15", query, StringComparison.Ordinal);
        Assert.Contains(string.Concat("hairServiceId=", HairServiceId.ToString("D")), query, StringComparison.Ordinal);
        Assert.Contains(string.Concat("salonId=", SalonId.ToString("D")), query, StringComparison.Ordinal);

        Assert.Equal(45, slots.DurationMinutes);
        Assert.Equal("Europe/Moscow", slots.TimeZoneId);
        Assert.Equal(BarberServiceId, slots.BarberServiceId);
        Assert.Equal(new DateOnly(2026, 11, 15), slots.Date);
        Assert.Single(slots.Slots);
    }

    [Fact]
    public async Task GetSlotsAsync_EmptySlots_ParsesAsEmptyList()
    {
        const string emptyJson = """
            {
              "barberProfileId": "9e100000-0000-4000-8000-000000000001",
              "salonId": "9e100000-0000-4000-8000-000000000002",
              "barberServiceId": null,
              "hairServiceId": null,
              "date": "2026-11-16",
              "timeZoneId": "Europe/Moscow",
              "dayStart": "2026-11-15T21:00:00+00:00",
              "dayEnd": "2026-11-16T21:00:00+00:00",
              "durationMinutes": 30,
              "isWorkingDay": true,
              "slots": []
            }
            """;

        var handler = new StubHandler(HttpStatusCode.OK, emptyJson, "application/json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberSearchApi(httpClient);

        var slots = await api.GetSlotsAsync(BarberProfileId, new DateOnly(2026, 11, 16), null, null);

        Assert.Empty(slots.Slots);
        Assert.Null(slots.BarberServiceId);
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
