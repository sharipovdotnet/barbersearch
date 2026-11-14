using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Bookings.RescheduleBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Bookings;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента создания брони: тело запроса, разбор ответа и ошибок.
/// </summary>
/// <remarks>
/// Время проверяется разбором отправленного JSON, а не сравнением строк: клиент обязан
/// отправить тот же инстант, что вернул <c>/slots</c>, и не пересчитывать его в пояс телефона.
/// </remarks>
public sealed class BookingsApiTests
{
    private static readonly Guid BarberProfileId = Guid.Parse("9e100000-0000-4000-8000-000000000001");
    private static readonly Guid BarberServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000004");
    private static readonly Guid SalonId = Guid.Parse("9e100000-0000-4000-8000-000000000002");
    private static readonly Guid CreatedId = Guid.Parse("9e100000-0000-4000-8000-00000000000a");
    private static readonly Guid BookingId = Guid.Parse("9e100000-0000-4000-8000-0000000000b1");
    private static readonly Guid UserId = Guid.Parse("9e100000-0000-4000-8000-0000000000b2");

    private static readonly DateTimeOffset StartAt = new(2026, 11, 15, 18, 30, 0, TimeSpan.FromHours(3));
    private static readonly DateTimeOffset EndAt = new(2026, 11, 15, 19, 15, 0, TimeSpan.FromHours(3));

    [Fact]
    public async Task CreateBookingAsync_AllFields_SendsThemAndParsesCreatedId()
    {
        var handler = new StubHandler(HttpStatusCode.Created, CreatedJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);
        var request = new CreateBookingRequest(
            BarberProfileId,
            BarberServiceId,
            SalonId,
            StartAt,
            EndAt,
            "0123456789abcdef0123456789abcdef",
            CustomerNote: null);

        var id = await api.CreateBookingAsync(request);

        Assert.Equal(CreatedId, id);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/bookings", handler.LastRequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(handler.LastBody!);
        var root = document.RootElement;

        Assert.Equal(BarberProfileId.ToString("D"), root.GetProperty("barberProfileId").GetString());
        Assert.Equal(BarberServiceId.ToString("D"), root.GetProperty("barberServiceId").GetString());
        Assert.Equal(SalonId.ToString("D"), root.GetProperty("salonId").GetString());
        Assert.Equal("0123456789abcdef0123456789abcdef", root.GetProperty("idempotencyKey").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("customerNote").ValueKind);

        // Пустая заметка уходит как null, а не пустой строкой.
        var parsed = JsonSerializer.Deserialize<CreateBookingRequest>(handler.LastBody!, ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal(StartAt, parsed.StartAt);
        Assert.Equal(StartAt.UtcDateTime, parsed.StartAt.UtcDateTime);
        Assert.Equal(StartAt.Offset, parsed.StartAt.Offset);
        Assert.Equal(EndAt, parsed.EndAt);
        Assert.Null(parsed.CustomerNote);
    }

    [Fact]
    public async Task CreateBookingAsync_Note_PassedAsIs()
    {
        var handler = new StubHandler(HttpStatusCode.Created, CreatedJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        await api.CreateBookingAsync(new CreateBookingRequest(
            BarberProfileId,
            BarberServiceId,
            SalonId,
            StartAt,
            EndAt,
            "key",
            "Покороче сзади"));

        var parsed = JsonSerializer.Deserialize<CreateBookingRequest>(handler.LastBody!, ApiJson.Options);

        Assert.Equal("Покороче сзади", parsed!.CustomerNote);
    }

    [Fact]
    public async Task CreateBookingAsync_Conflict_TurnsProblemDetailsIntoException()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "The slot is occupied.",
              "errorCodes": ["booking.add.overlap"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.CreateBookingAsync(Request()));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains(ApiErrorCodes.Booking.AddOverlap, exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task CreateBookingAsync_EmptyCreatedBody_Throws()
    {
        var handler = new StubHandler(HttpStatusCode.Created, "{}");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateBookingAsync(Request()));
    }

    [Fact]
    public async Task GetBookingsAsync_UpcomingScope_SendsScopePageSizeAndUserId()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"items":[],"nextCursor":null}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        var page = await api.GetBookingsAsync(BookingScope.Upcoming, UserId, pageSize: 20);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/api/bookings", handler.LastRequestUri!.AbsolutePath);

        var query = QueryParameters(handler.LastRequestUri);

        Assert.Equal("upcoming", query["scope"]);
        Assert.Equal("20", query["pageSize"]);

        // userId уходит всегда: без него администратор получил бы в «Моих записях» всю систему.
        Assert.Equal(UserId.ToString("D"), query["userId"]);

        // Первая страница: курсора нет вовсе, а не пустая строка в адресе.
        Assert.False(query.ContainsKey("cursor"));
    }

    [Fact]
    public async Task GetBookingsAsync_NextPage_SendsCursorAsIs()
    {
        // Курсор непрозрачен: зарезервированные символы нельзя ни разбирать, ни терять.
        const string cursor = "abc+/=";
        var handler = new StubHandler(HttpStatusCode.OK, $$"""{"items":[],"nextCursor":"{{cursor}}"}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        var page = await api.GetBookingsAsync(BookingScope.Upcoming, UserId, cursor: cursor);

        var rawQuery = handler.LastRequestUri!.Query;
        var query = QueryParameters(handler.LastRequestUri);

        // В адресе курсор экранирован, иначе «+», «/» и «=» сломали бы разбор строки запроса.
        Assert.Contains("cursor=abc%2B%2F%3D", rawQuery, StringComparison.Ordinal);

        // После разбора значение совпадает с исходным: клиент возвращает курсор байт в байт.
        Assert.Equal(cursor, query["cursor"]);

        // Из ответа курсор читается без изменений и уходит в следующую страницу как есть.
        Assert.Equal(cursor, page.NextCursor);
    }

    [Fact]
    public async Task GetBookingsAsync_PastScope_ReturnsPageWithStringStatuses()
    {
        var handler = new StubHandler(HttpStatusCode.OK, PastPageJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        var page = await api.GetBookingsAsync(BookingScope.Past, UserId, pageSize: 20);

        var query = QueryParameters(handler.LastRequestUri!);

        Assert.Equal("past", query["scope"]);

        var booking = Assert.Single(page.Items);

        Assert.Equal(BookingId, booking.Id);

        // userId == null — бронь создана барбером для гостя (walk-in); это не ошибка разбора.
        Assert.Null(booking.UserId);
        Assert.Equal(BarberProfileId, booking.BarberProfileId);
        Assert.Equal(BarberServiceId, booking.BarberServiceId);
        Assert.Equal(SalonId, booking.SalonId);

        // Время — тот же инстант, что прислал сервер, вместе со смещением:
        // пересчёт в пояс устройства потерял бы исходный offset.
        Assert.Equal(StartAt, booking.StartAt);
        Assert.Equal(StartAt.Offset, booking.StartAt.Offset);
        Assert.Equal(StartAt.UtcDateTime, booking.StartAt.UtcDateTime);
        Assert.Equal(EndAt, booking.EndAt);
        Assert.Equal(EndAt.UtcDateTime, booking.EndAt.UtcDateTime);

        Assert.Equal(2400m, booking.Price);

        // Статус приходит строкой; ApiJson.Options разбирает перечисления только так.
        Assert.Equal(BookingStatus.Cancelled, booking.Status);

        // Причина отмены остаётся в истории, даже когда отменил барбер, а не клиент.
        Assert.Equal("Барбер заболел", booking.CancellationReason);
        Assert.True(booking.HasReview);

        // null в nextCursor — страница последняя; клиент не должен догадываться об этом сам.
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task GetBookingsAsync_TooManyRequests_ThrowsWithStatus()
    {
        // 429 — нормальный ответ лимитера, а не сбой: экран различает его по коду состояния.
        const string problemJson = """
            {
              "title": "Too Many Requests",
              "status": 429,
              "detail": "Rate limit exceeded.",
              "errorCodes": ["rateLimit.exceeded"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.TooManyRequests, problemJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.GetBookingsAsync(BookingScope.Upcoming, UserId));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Contains("rateLimit.exceeded", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task CancelBookingAsync_ValidId_PostsWithoutBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        await api.CancelBookingAsync(BookingId);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/bookings/", BookingId.ToString("D"), "/cancellation"),
            handler.LastRequestUri!.AbsolutePath);

        // Клиент причину отмены не пишет: тело не отправляется вовсе, а не пустой объект.
        Assert.True(string.IsNullOrEmpty(handler.LastBody));
    }

    [Fact]
    public async Task CancelBookingAsync_Conflict_ThrowsWithCode()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "The booking can no longer be cancelled.",
              "errorCodes": ["booking.cancel.invalidStatus"]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.CancelBookingAsync(BookingId));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);

        // Код объявлен строкой в Application/Domain, а не в ApiErrorCodes: сверяем литерал,
        // как auth.login.* в срезе 1. Ветвление экрана идёт по нему, а не по detail.
        Assert.Contains("booking.cancel.invalidStatus", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task GetRescheduleSlotsAsync_Date_FormatsYyyyMmDd()
    {
        const string slotsJson = """
            {
              "barberProfileId": "9e100000-0000-4000-8000-000000000001",
              "salonId": "9e100000-0000-4000-8000-000000000002",
              "barberServiceId": "9e100000-0000-4000-8000-000000000004",
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

        var handler = new StubHandler(HttpStatusCode.OK, slotsJson);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        var slots = await api.GetRescheduleSlotsAsync(BookingId, new DateOnly(2026, 11, 15));

        Assert.Equal(
            string.Concat("/api/bookings/", BookingId.ToString("D"), "/reschedule-slots"),
            handler.LastRequestUri!.AbsolutePath);

        // Дата — календарная строка салона: только yyyy-MM-dd в инвариантной культуре,
        // без ToUniversalTime() и без подстановки культуры устройства.
        Assert.Equal("2026-11-15", QueryParameters(handler.LastRequestUri)["date"]);

        Assert.Equal(new DateOnly(2026, 11, 15), slots.Date);
        Assert.Equal("Europe/Moscow", slots.TimeZoneId);
        Assert.Equal(45, slots.DurationMinutes);
        Assert.True(slots.IsWorkingDay);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 21, 0, 0, TimeSpan.Zero), slots.DayStart);

        var slot = Assert.Single(slots.Slots);

        Assert.Equal(new DateTimeOffset(2026, 11, 15, 15, 30, 0, TimeSpan.Zero), slot.StartAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 16, 15, 0, TimeSpan.Zero), slot.EndAt);
    }

    [Fact]
    public async Task RescheduleBookingAsync_SlotStartAt_SendsSameInstant()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BookingsApi(httpClient);

        var slot = new BarberSlotResponse(
            new DateTimeOffset(2026, 11, 15, 18, 30, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 11, 15, 19, 15, 0, TimeSpan.FromHours(3)));

        await api.RescheduleBookingAsync(BookingId, slot.StartAt);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat("/api/bookings/", BookingId.ToString("D"), "/reschedule"),
            handler.LastRequestUri!.AbsolutePath);

        var parsed = JsonSerializer.Deserialize<RescheduleBookingRequest>(handler.LastBody!, ApiJson.Options);

        Assert.NotNull(parsed);

        // Перенос отправляет ровно тот инстант, что пришёл из сетки: сервер сравнивает
        // абсолютные моменты, а не настенное время в поясе телефона.
        Assert.Equal(slot.StartAt, parsed.StartAt);
        Assert.Equal(slot.StartAt.Offset, parsed.StartAt.Offset);
        Assert.Equal(slot.StartAt.UtcDateTime, parsed.StartAt.UtcDateTime);
    }

    private static CreateBookingRequest Request() => new(
        BarberProfileId,
        BarberServiceId,
        SalonId,
        StartAt,
        EndAt,
        "0123456789abcdef0123456789abcdef",
        CustomerNote: null);

    private static string CreatedJson() => $$"""{"id":"{{CreatedId:D}}"}""";

    /// <summary>
    /// Страница истории: отмена с причиной и отзывом, гостевая бронь без <c>userId</c>.
    /// </summary>
    /// <remarks>
    /// Смещение у времени намеренно не нулевое: разбор обязан сохранить и инстант, и offset,
    /// а не перевести значение в UTC молча.
    /// </remarks>
    private static string PastPageJson() => $$"""
        {
          "items": [
            {
              "id": "{{BookingId:D}}",
              "userId": null,
              "barberProfileId": "{{BarberProfileId:D}}",
              "barberServiceId": "{{BarberServiceId:D}}",
              "salonId": "{{SalonId:D}}",
              "startAt": "2026-11-15T18:30:00+03:00",
              "endAt": "2026-11-15T19:15:00+03:00",
              "price": 2400,
              "status": "Cancelled",
              "idempotencyKey": "0123456789abcdef0123456789abcdef",
              "customerNote": null,
              "barberNote": null,
              "cancellationReason": "Барбер заболел",
              "createdAt": "2026-11-01T10:00:00+03:00",
              "hasReview": true
            }
          ],
          "nextCursor": null
        }
        """;

    /// <summary>
    /// Разбирает строку запроса вручную: <see cref="Uri"/> раскрывает только те символы,
    /// которые считает безопасными, и для курсора этого недостаточно.
    /// </summary>
    /// <remarks>
    /// Значения декодируются по одному, а не через <c>Uri.UnescapeDataString</c> от всей
    /// строки сразу: иначе экранированный «&amp;» внутри значения разрезал бы запрос
    /// на лишние пары.
    /// </remarks>
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
