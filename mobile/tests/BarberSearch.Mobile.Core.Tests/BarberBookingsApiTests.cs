using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.Bookings.AddBookingBarberNote;
using BarberSearch.Contracts.Bookings.CancelBooking;
using BarberSearch.Contracts.Bookings.ChangeBookingPrice;
using BarberSearch.Contracts.Bookings.CreateBarberBooking;
using BarberSearch.Contracts.Bookings.RescheduleBooking;
using BarberSearch.Domain.Bookings;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента записей кабинета мастера: маршруты, тела действий и разбор ответов.
/// </summary>
/// <remarks>
/// Сеть подменяется <see cref="HttpMessageHandler"/>: тест проходит через настоящий
/// <see cref="HttpClient"/> и настоящие настройки JSON. Заголовки <c>X-CSRF-TOKEN</c> и
/// <c>X-Client-Surface</c> проверяются на отсутствие: первый не нужен Bearer-клиенту, второй
/// пометил бы действие мастера как действие клиента и отменил бы push.
/// </remarks>
public sealed class BarberBookingsApiTests
{
    private static readonly Guid BookingId = Guid.Parse("9e100000-0000-4000-8000-0000000000b1");
    private static readonly Guid CreatedId = Guid.Parse("9e100000-0000-4000-8000-0000000000a1");
    private static readonly Guid BarberProfileId = Guid.Parse("9e100000-0000-4000-8000-000000000001");
    private static readonly Guid BarberServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000004");
    private static readonly Guid SalonId = Guid.Parse("9e100000-0000-4000-8000-000000000002");
    private static readonly Guid CustomerId = Guid.Parse("9e100000-0000-4000-8000-0000000000b2");

    private static readonly DateTimeOffset StartAt = new(2026, 11, 15, 18, 30, 0, TimeSpan.FromHours(3));
    private static readonly DateTimeOffset EndAt = new(2026, 11, 15, 19, 15, 0, TimeSpan.FromHours(3));

    [Fact]
    public async Task GetAsync_UpcomingScope_SendsScopePageSizeAndParsesPage()
    {
        var handler = new StubHandler(HttpStatusCode.OK, PageJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        var page = await api.GetAsync("upcoming");

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/api/barber/bookings", handler.LastRequestUri!.AbsolutePath);

        var query = QueryParameters(handler.LastRequestUri);

        Assert.Equal("upcoming", query["scope"]);
        Assert.Equal("20", query["pageSize"]);
        Assert.False(query.ContainsKey("cursor"));

        var booking = Assert.Single(page.Items);

        Assert.Equal(BookingId, booking.Id);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(StartAt, booking.StartAt);
        Assert.Equal(StartAt.Offset, booking.StartAt.Offset);
        Assert.Equal(2400m, booking.Price);
        Assert.Equal("Ева Клиентова", booking.Customer.DisplayName);
        Assert.Equal("+7 900 000-00-00", booking.Customer.PhoneNumber);
        Assert.Equal("Стрижка", booking.Service.Name);
        Assert.Equal("E2E Studio", booking.Salon.Name);
        Assert.Equal("Europe/Moscow", booking.Salon.TimeZoneId);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task GetAsync_NullScopeCursorAndPageSize_OmitsEveryParameter()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"items":[],"nextCursor":null}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        var page = await api.GetAsync(pageSize: null);

        Assert.Empty(page.Items);
        Assert.Equal("/api/barber/bookings", handler.LastRequestUri!.AbsolutePath);

        // Отсутствие параметров — это первая страница и серверный скоуп по умолчанию;
        // пустые scope=/cursor= были бы невалидными значениями, а не «не задано».
        Assert.Equal(string.Empty, handler.LastRequestUri.Query);
    }

    [Fact]
    public async Task GetAsync_PastScopeWithCursor_EscapesCursorAndKeepsItOpaque()
    {
        const string cursor = "abc+/=";
        var handler = new StubHandler(
            HttpStatusCode.OK,
            $$"""{"items":[],"nextCursor":"{{cursor}}"}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        var page = await api.GetAsync("past", cursor, pageSize: 10);

        var rawQuery = handler.LastRequestUri!.Query;
        var query = QueryParameters(handler.LastRequestUri);

        Assert.Equal("past", query["scope"]);
        Assert.Equal("10", query["pageSize"]);
        Assert.Contains("cursor=abc%2B%2F%3D", rawQuery, StringComparison.Ordinal);
        Assert.Equal(cursor, query["cursor"]);
        Assert.Equal(cursor, page.NextCursor);
    }

    [Fact]
    public async Task GetAsync_Forbidden_ThrowsWithStatusCodeAndErrorCode()
    {
        const string problemJson = """
            {
              "title": "Forbidden",
              "status": 403,
              "detail": "The barber role is required.",
              "errorCodes": ["auth.forbidden"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Forbidden, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(() => api.GetAsync("upcoming"));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("auth.forbidden", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetAsync_NullJsonBody_ThrowsInsteadOfReturningEmptyPage()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetAsync());
    }

    [Fact]
    public async Task CreateAsync_ClientBooking_SendsContractBodyAndParsesCreatedId()
    {
        var handler = new StubHandler(
            HttpStatusCode.Created,
            $$"""{"id":"{{CreatedId:D}}"}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        var id = await api.CreateAsync(new CreateBarberBookingRequest(
            CustomerId,
            GuestFirstName: null,
            GuestLastName: null,
            GuestPhone: null,
            BarberServiceId,
            SalonId,
            StartAt,
            EndAt,
            "0123456789abcdef0123456789abcdef",
            "Покороче сзади"));

        Assert.Equal(CreatedId, id);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/barber/bookings", handler.LastRequestUri!.AbsolutePath);

        var parsed = JsonSerializer.Deserialize<CreateBarberBookingRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal(CustomerId, parsed.CustomerId);
        Assert.Null(parsed.GuestFirstName);
        Assert.Equal(BarberServiceId, parsed.BarberServiceId);
        Assert.Equal(SalonId, parsed.SalonId);
        Assert.Equal(StartAt, parsed.StartAt);
        Assert.Equal(StartAt.Offset, parsed.StartAt.Offset);
        Assert.Equal("0123456789abcdef0123456789abcdef", parsed.IdempotencyKey);
        Assert.Equal("Покороче сзади", parsed.CustomerNote);

        // Профиль мастера в теле не передаётся: сервер берёт его из сессии.
        Assert.DoesNotContain("barberProfileId", handler.LastBody, StringComparison.Ordinal);
        Assert.False(handler.LastHeaders.ContainsKey("X-Client-Surface"));
        Assert.False(handler.LastHeaders.ContainsKey("X-CSRF-TOKEN"));
    }

    [Fact]
    public async Task CreateAsync_EmptyCreatedId_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.Created, "{}");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateAsync(Request()));
    }

    [Fact]
    public async Task ConfirmAsync_ValidId_PostsWithoutBodyAndDoesNotReadNoContent()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        await api.ConfirmAsync(BookingId);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/bookings/", BookingId.ToString("D"), "/confirmation"),
            handler.LastRequestUri!.AbsolutePath);
        Assert.True(string.IsNullOrEmpty(handler.LastBody));

        // Bearer-клиенту CSRF не нужен, а пометка поверхности отменила бы push о действии.
        Assert.False(handler.LastHeaders.ContainsKey("X-CSRF-TOKEN"));
        Assert.False(handler.LastHeaders.ContainsKey("X-Client-Surface"));
    }

    [Fact]
    public async Task CompleteAsync_ValidId_PostsToCompletion()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        await api.CompleteAsync(BookingId);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/bookings/", BookingId.ToString("D"), "/completion"),
            handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CancelAsync_Reason_SendsReasonInContractBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        await api.CancelAsync(BookingId, "  Мастер заболел  ");

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/bookings/", BookingId.ToString("D"), "/cancellation"),
            handler.LastRequestUri!.AbsolutePath);

        // Причина уходит как есть: обрезку по краям и проверку делает сервер.
        var parsed = JsonSerializer.Deserialize<CancelBookingRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal("  Мастер заболел  ", parsed.Reason);
    }

    [Fact]
    public async Task CancelAsync_NullReason_StillSendsBodyWithNullReason()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        await api.CancelAsync(BookingId, reason: null);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;

        // Тело отправляется всегда: отказ должен прийти кодом booking.cancel.reasonRequired,
        // а не зависеть от того, как сервер разберёт пустой запрос.
        Assert.True(root.TryGetProperty("reason", out var reason));
        Assert.Equal(JsonValueKind.Null, reason.ValueKind);
    }

    [Fact]
    public async Task CancelAsync_Conflict_ThrowsWithStatusAndErrorCode()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "The booking can no longer be cancelled.",
              "errorCodes": ["booking.cancel.invalidStatus"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.CancelAsync(BookingId, "Мастер заболел"));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains("booking.cancel.invalidStatus", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task MarkNoShowAsync_ValidId_PostsToNoShow()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        await api.MarkNoShowAsync(BookingId);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/bookings/", BookingId.ToString("D"), "/no-show"),
            handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SaveNoteAsync_Note_SendsContractBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        await api.SaveNoteAsync(BookingId, "Покороче сзади");

        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/bookings/", BookingId.ToString("D"), "/barber-note"),
            handler.LastRequestUri!.AbsolutePath);

        var parsed = JsonSerializer.Deserialize<AddBookingBarberNoteRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal("Покороче сзади", parsed.Note);
    }

    [Fact]
    public async Task ChangePriceAsync_Price_SendsContractBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        await api.ChangePriceAsync(BookingId, 1500.50m);

        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/bookings/", BookingId.ToString("D"), "/price"),
            handler.LastRequestUri!.AbsolutePath);

        var parsed = JsonSerializer.Deserialize<ChangeBookingPriceRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal(1500.50m, parsed.Price);
    }

    [Fact]
    public async Task GetRescheduleSlotsAsync_Date_FormatsYyyyMmDdAndParsesSlots()
    {
        var handler = new StubHandler(HttpStatusCode.OK, SlotsJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        var slots = await api.GetRescheduleSlotsAsync(BookingId, new DateOnly(2026, 11, 15));

        Assert.Equal(
            string.Concat("/api/bookings/", BookingId.ToString("D"), "/reschedule-slots"),
            handler.LastRequestUri!.AbsolutePath);

        // Дата — календарная строка салона в инвариантной культуре, без пересчёта в UTC.
        Assert.Equal("2026-11-15", QueryParameters(handler.LastRequestUri)["date"]);
        Assert.Equal(new DateOnly(2026, 11, 15), slots.Date);
        Assert.Equal(45, slots.DurationMinutes);
        Assert.Equal("Europe/Moscow", slots.TimeZoneId);

        var slot = Assert.Single(slots.Slots);

        Assert.Equal(new DateTimeOffset(2026, 11, 15, 15, 30, 0, TimeSpan.Zero), slot.StartAt);
    }

    [Fact]
    public async Task RescheduleAsync_StartAt_SendsSameInstant()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        var startAt = new DateTimeOffset(2026, 11, 15, 18, 30, 0, TimeSpan.FromHours(3));

        await api.RescheduleAsync(BookingId, startAt);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/bookings/", BookingId.ToString("D"), "/reschedule"),
            handler.LastRequestUri!.AbsolutePath);

        var parsed = JsonSerializer.Deserialize<RescheduleBookingRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);

        // Перенос отправляет ровно тот инстант, что пришёл из сетки, вместе со смещением.
        Assert.Equal(startAt, parsed.StartAt);
        Assert.Equal(startAt.Offset, parsed.StartAt.Offset);
        Assert.Equal(startAt.UtcDateTime, parsed.StartAt.UtcDateTime);
    }

    [Fact]
    public async Task RescheduleAsync_Conflict_ThrowsWithStatus()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "The slot is occupied.",
              "errorCodes": ["booking.reschedule.slotTaken"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberBookingsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.RescheduleAsync(BookingId, StartAt));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains("booking.reschedule.slotTaken", exception.ErrorCodes, StringComparer.Ordinal);
    }

    private static CreateBarberBookingRequest Request() => new(
        CustomerId,
        GuestFirstName: null,
        GuestLastName: null,
        GuestPhone: null,
        BarberServiceId,
        SalonId,
        StartAt,
        EndAt,
        "0123456789abcdef0123456789abcdef",
        CustomerNote: null);

    private static string PageJson() => $$"""
        {
          "items": [
            {
              "id": "{{BookingId:D}}",
              "status": "Confirmed",
              "startAt": "2026-11-15T18:30:00+03:00",
              "endAt": "2026-11-15T19:15:00+03:00",
              "price": 2400,
              "customerNote": null,
              "barberNote": null,
              "cancellationReason": null,
              "customer": {
                "displayName": "Ева Клиентова",
                "phoneNumber": "+7 900 000-00-00",
                "avatarUrl": null
              },
              "service": { "name": "Стрижка" },
              "salon": {
                "name": "E2E Studio",
                "address": "Ленина, 1",
                "city": "Москва",
                "timeZoneId": "Europe/Moscow"
              }
            }
          ],
          "nextCursor": null
        }
        """;

    private static string SlotsJson() => $$"""
        {
          "barberProfileId": "{{BarberProfileId:D}}",
          "salonId": "{{SalonId:D}}",
          "barberServiceId": null,
          "hairServiceId": null,
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
    /// Разбирает строку запроса вручную: <see cref="Uri"/> раскрывает только те символы,
    /// которые считает безопасными, и для курсора этого недостаточно.
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
    /// Подменённый обработчик: отдаёт заданный ответ и запоминает метод, адрес, заголовки и тело.
    /// </summary>
    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string? body = null,
        string contentType = "application/json")
        : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public Dictionary<string, string> LastHeaders { get; private set; } =
            new(StringComparer.Ordinal);

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            LastHeaders = request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.Ordinal);
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
