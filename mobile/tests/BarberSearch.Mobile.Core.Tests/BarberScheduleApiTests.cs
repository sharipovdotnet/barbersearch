using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileTimeOff;
using BarberSearch.Contracts.BarberSalons.ReplaceWorkingSchedules;
using BarberSearch.Contracts.BarberSalons.SetScheduleException;
using BarberSearch.Domain.Bookings;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки клиента расписания мастера: адреса с датами, <c>force</c> и тела правок.
/// </summary>
/// <remarks>
/// Время недели и исключений — локальное время салона (<see cref="TimeOnly"/>), поэтому
/// проверяется его строковая форма из контракта, а не пересчёт в пояс телефона. <c>force</c>
/// не передаётся в обычном пути: параметр появляется только после подтверждения конфликта.
/// </remarks>
public sealed class BarberScheduleApiTests
{
    private static readonly Guid BarberProfileId = Guid.Parse("9e100000-0000-4000-8000-000000000001");
    private static readonly Guid SalonId = Guid.Parse("9e100000-0000-4000-8000-000000000002");
    private static readonly Guid TimeOffId = Guid.Parse("9e100000-0000-4000-8000-0000000000c1");

    private static readonly DateOnly ExceptionDate = new(2026, 11, 20);

    [Fact]
    public async Task GetAsync_FromAndTo_SendsIsoDatesAndParsesSchedule()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ScheduleJson());
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        var schedule = await api.GetAsync(
            BarberProfileId,
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 30));

        Assert.Equal(
            string.Concat("/api/barber-profiles/", BarberProfileId.ToString("D"), "/schedule"),
            handler.LastRequestUri!.AbsolutePath);

        var query = QueryParameters(handler.LastRequestUri);

        Assert.Equal("2026-11-01", query["from"]);
        Assert.Equal("2026-11-30", query["to"]);

        Assert.Equal(BarberProfileId, schedule.BarberProfileId);
        Assert.Equal(ExceptionDate, Assert.Single(schedule.Salons).Exceptions[0].Date);

        var salon = Assert.Single(schedule.Salons);
        var monday = salon.Weekly[0];

        Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek);
        Assert.False(monday.IsDayOff);
        Assert.Equal(new TimeOnly(10, 0), monday.StartTime);
        Assert.Equal(new TimeOnly(20, 0), monday.EndTime);

        // Выходной приходит с MinValue во времени; смотреть на часы нужно только при IsDayOff = false.
        var exception = Assert.Single(salon.Exceptions);

        Assert.True(exception.IsDayOff);
        Assert.Equal("Отпуск", exception.Reason);

        var timeOff = Assert.Single(schedule.TimeOffs);

        Assert.Equal(TimeOffId, timeOff.Id);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), timeOff.StartAt);
    }

    [Fact]
    public async Task GetAsync_NoWindow_SendsNoQuery()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"barberProfileId":"9e100000-0000-4000-8000-000000000001","salons":[],"timeOffs":[]}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        var schedule = await api.GetAsync(BarberProfileId);

        Assert.Empty(schedule.Salons);
        Assert.Empty(schedule.TimeOffs);
        Assert.Equal(string.Empty, handler.LastRequestUri!.Query);
    }

    [Fact]
    public async Task GetAsync_NullBody_ThrowsInsteadOfReturningEmptySchedule()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetAsync(BarberProfileId));
    }

    [Fact]
    public async Task ReplaceWorkingSchedulesAsync_ForceTrue_SendsForceAndSevenDays()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        await api.ReplaceWorkingSchedulesAsync(
            BarberProfileId,
            SalonId,
            WeekRequest(),
            force: true);

        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/salons/",
                SalonId.ToString("D"),
                "/working-schedules"),
            handler.LastRequestUri!.AbsolutePath);
        Assert.Equal("true", QueryParameters(handler.LastRequestUri)["force"]);

        var parsed = JsonSerializer.Deserialize<ReplaceWorkingSchedulesRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal(7, parsed.Days.Count);
        Assert.Equal(DayOfWeek.Monday, parsed.Days[0].DayOfWeek);
        Assert.False(parsed.Days[0].IsDayOff);
        Assert.Equal(new TimeOnly(10, 0), parsed.Days[0].StartTime);
        Assert.True(parsed.Days[6].IsDayOff);
    }

    [Fact]
    public async Task ReplaceWorkingSchedulesAsync_ForceFalse_OmitsForceQuery()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        await api.ReplaceWorkingSchedulesAsync(BarberProfileId, SalonId, WeekRequest());

        // Обычный путь не меняет адрес: ?force=false сервер трактовал бы как явный отказ,
        // хотя параметра просто не должно быть.
        Assert.Equal(string.Empty, handler.LastRequestUri!.Query);
        Assert.False(QueryParameters(handler.LastRequestUri).ContainsKey("force"));
    }

    [Fact]
    public async Task SetScheduleExceptionAsync_ForceTrue_FormatsDateInPathAndSendsBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        await api.SetScheduleExceptionAsync(
            BarberProfileId,
            SalonId,
            ExceptionDate,
            ExceptionRequest(),
            force: true);

        Assert.Equal(HttpMethod.Put, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/salons/",
                SalonId.ToString("D"),
                "/schedule-exceptions/2026-11-20"),
            handler.LastRequestUri!.AbsolutePath);
        Assert.Equal("true", QueryParameters(handler.LastRequestUri)["force"]);

        var parsed = JsonSerializer.Deserialize<SetScheduleExceptionRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.True(parsed.IsDayOff);
        Assert.Equal(new TimeOnly(0, 0), parsed.StartTime);
        Assert.Equal("Отпуск", parsed.Reason);
    }

    [Fact]
    public async Task SetScheduleExceptionAsync_ForceFalse_OmitsForceQuery()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        await api.SetScheduleExceptionAsync(BarberProfileId, SalonId, ExceptionDate, ExceptionRequest());

        Assert.False(QueryParameters(handler.LastRequestUri!).ContainsKey("force"));
    }

    [Fact]
    public async Task RemoveScheduleExceptionAsync_ValidIds_DeletesDatedPath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        await api.RemoveScheduleExceptionAsync(BarberProfileId, SalonId, ExceptionDate);

        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/salons/",
                SalonId.ToString("D"),
                "/schedule-exceptions/2026-11-20"),
            handler.LastRequestUri!.AbsolutePath);
        Assert.Equal(string.Empty, handler.LastRequestUri.Query);
    }

    [Fact]
    public async Task AddTimeOffAsync_ForceTrue_SendsForceAndBody()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        var request = new AddBarberProfileTimeOffRequest(
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 10, 0, 0, 0, TimeSpan.Zero),
            "Отпуск");

        await api.AddTimeOffAsync(BarberProfileId, request, force: true);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/time-offs"),
            handler.LastRequestUri!.AbsolutePath);
        Assert.Equal("true", QueryParameters(handler.LastRequestUri)["force"]);

        var parsed = JsonSerializer.Deserialize<AddBarberProfileTimeOffRequest>(
            handler.LastBody!,
            ApiJson.Options);

        Assert.NotNull(parsed);
        Assert.Equal(request.StartAt, parsed.StartAt);
        Assert.Equal(request.EndAt, parsed.EndAt);
        Assert.Equal("Отпуск", parsed.Reason);
    }

    [Fact]
    public async Task AddTimeOffAsync_ForceFalse_OmitsForceQuery()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        await api.AddTimeOffAsync(
            BarberProfileId,
            new AddBarberProfileTimeOffRequest(
                new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 10, 0, 0, 0, TimeSpan.Zero),
                null));

        Assert.False(QueryParameters(handler.LastRequestUri!).ContainsKey("force"));
    }

    [Fact]
    public async Task RemoveTimeOffAsync_ValidIds_DeletesPath()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        await api.RemoveTimeOffAsync(BarberProfileId, TimeOffId);

        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Equal(
            string.Concat(
                "/api/barber-profiles/",
                BarberProfileId.ToString("D"),
                "/time-offs/",
                TimeOffId.ToString("D")),
            handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ReplaceWorkingSchedulesAsync_Conflict_ThrowsWithStatusAndCode()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "Active bookings fall outside the new hours.",
              "errorCodes": ["barberSchedule.conflict"],
              "conflicts": []
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.ReplaceWorkingSchedulesAsync(BarberProfileId, SalonId, WeekRequest()));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains("barberSchedule.conflict", exception.ErrorCodes, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ReplaceWorkingSchedulesAsync_ConflictWithVisits_FillsConflictsFromExtension()
    {
        const string problemJson = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "Active bookings fall outside the new hours.",
              "errorCodes": ["barberSchedule.conflict"],
              "conflicts": [
                {
                  "id": "9e100000-0000-4000-8000-0000000000c2",
                  "startAt": "2026-11-20T10:00:00+03:00",
                  "endAt": "2026-11-20T11:00:00+03:00",
                  "timeZoneId": "Europe/Moscow",
                  "customerName": "Иван Петров",
                  "serviceName": "Стрижка",
                  "status": "Confirmed"
                }
              ]
            }
            """;

        var handler = new StubHandler(HttpStatusCode.Conflict, problemJson, "application/problem+json");
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        var exception = await Assert.ThrowsAsync<BarberSearchApiException>(
            () => api.ReplaceWorkingSchedulesAsync(BarberProfileId, SalonId, WeekRequest()));

        var conflict = Assert.Single(exception.Conflicts);

        Assert.Equal(
            new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.FromHours(3)),
            conflict.StartAt);
        Assert.Equal("Europe/Moscow", conflict.TimeZoneId);
        Assert.Equal("Иван Петров", conflict.CustomerName);
        Assert.Equal("Стрижка", conflict.ServiceName);
        Assert.Equal(BookingStatus.Confirmed, conflict.Status);
    }

    [Fact]
    public async Task RemoveTimeOffAsync_EmptyTimeOffId_ThrowsWithoutRequest()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, string.Empty);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5243/") };
        var api = new BarberScheduleApi(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => api.RemoveTimeOffAsync(BarberProfileId, Guid.Empty));

        Assert.Null(handler.LastRequestUri);
    }

    private static ReplaceWorkingSchedulesRequest WeekRequest()
    {
        // Порядок недели в редакторе начинается с понедельника: Enum.GetValues начал бы
        // с воскресенья и проверял бы не тот контракт.
        DayOfWeek[] week =
        [
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday,
        ];

        var days = week
            .Select(day => new ReplaceWorkingScheduleDayRequest(
                day,
                day is DayOfWeek.Saturday or DayOfWeek.Sunday,
                new TimeOnly(10, 0),
                new TimeOnly(20, 0)))
            .ToList();

        return new ReplaceWorkingSchedulesRequest(days);
    }

    private static SetScheduleExceptionRequest ExceptionRequest() => new(
        IsDayOff: true,
        new TimeOnly(0, 0),
        new TimeOnly(0, 0),
        "Отпуск");

    private static string ScheduleJson() => $$"""
        {
          "barberProfileId": "{{BarberProfileId:D}}",
          "salons": [
            {
              "salonId": "{{SalonId:D}}",
              "name": "E2E Studio",
              "city": "Москва",
              "timeZoneId": "Europe/Moscow",
              "isPrimary": true,
              "weekly": [
                { "dayOfWeek": "Monday", "isDayOff": false, "startTime": "10:00:00", "endTime": "20:00:00" }
              ],
              "exceptions": [
                {
                  "date": "2026-11-20",
                  "isDayOff": true,
                  "startTime": "00:00:00",
                  "endTime": "00:00:00",
                  "reason": "Отпуск"
                }
              ]
            }
          ],
          "timeOffs": [
            {
              "id": "{{TimeOffId:D}}",
              "startAt": "2026-12-01T00:00:00+00:00",
              "endAt": "2026-12-10T00:00:00+00:00",
              "reason": null
            }
          ]
        }
        """;

    /// <summary>
    /// Разбирает строку запроса вручную, чтобы сравнить экранированные значения.
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
