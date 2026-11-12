using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.BarberProfiles.BarberSchedule;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberSalons.ReplaceWorkingSchedules;
using BarberSearch.Contracts.BarberSalons.SetScheduleException;
using BarberSearch.Contracts.WorkingSchedules.SetWorkingScheduleWorkingHours;
using BarberSearch.Domain.Bookings;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Конфликты расписания на реальной PostgreSQL: <c>409</c> со списком визитов, <c>force</c>,
/// горизонт 60 дней, явная дата за горизонтом и честный поиск по дате.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ScheduleConflictApiTests
{
    /// <summary>
    /// «Сейчас» и локальная дата салона. Часы не подменяются: JWT-валидатор живёт на системных
    /// часах, и зафиксированное «прошлое» сделало бы выданный токен просроченным. Даты
    /// считаются от текущего момента в поясе салона (Europe/Berlin).
    /// </summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly DateOnly Today = DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(Now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).DateTime);

    private static readonly DateOnly BookingDate = Today.AddDays(1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _database;

    public ScheduleConflictApiTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Shorter_hours_with_a_booking_return_409_and_change_nothing()
    {
        await using var app = await StartAppAsync();
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Conflict Barber");
        var customer = await app.CreateUserAsync(CustomerEmail());
        await app.AuthenticateAsync(barber.User.Email);
        await ReplaceWeekAsync(app, barber, new TimeOnly(10, 0), new TimeOnly(18, 0));

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(BookingDate, 10, 0),
            BookingStatus.Confirmed);

        var scheduleId = await GetScheduleIdAsync(app, barber, BookingDate.DayOfWeek);

        using var response = await app.Client.PutAsJsonAsync(
            HoursUrl(barber, scheduleId),
            new SetWorkingScheduleWorkingHoursRequest(new TimeOnly(12, 0), new TimeOnly(18, 0)),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        // Требование этапа: проверяется именно сырое тело, а не только десериализованный DTO.
        Assert.Contains("\"errorCodes\":[\"barberSchedule.conflict\"]", body, StringComparison.Ordinal);
        Assert.Contains("\"conflicts\":[", body, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(409, root.GetProperty("status").GetInt32());
        Assert.Equal(
            "The request conflicts with the current state of the resource.",
            root.GetProperty("title").GetString());
        Assert.Equal(
            "1 booking falls outside the new working hours.",
            root.GetProperty("detail").GetString());

        var conflict = Assert.Single(root.GetProperty("conflicts").EnumerateArray());
        Assert.Equal(booking.Id, conflict.GetProperty("id").GetGuid());
        Assert.Equal("Europe/Berlin", conflict.GetProperty("timeZoneId").GetString());
        Assert.Equal("Integration Test", conflict.GetProperty("customerName").GetString());
        Assert.Equal($"Haircut Conflict Barber", conflict.GetProperty("serviceName").GetString());
        Assert.Equal("Confirmed", conflict.GetProperty("status").GetString());
        Assert.True(conflict.GetProperty("startAt").GetDateTimeOffset() < conflict.GetProperty("endAt").GetDateTimeOffset());

        Assert.Equal(BookingStatus.Confirmed, await ReadBookingStatusAsync(app, booking.Id));
        var schedule = await GetScheduleAsync(app, barber.Profile.Id);
        var bookedDay = schedule.Salons.Single().Weekly.Single(day => day.DayOfWeek == BookingDate.DayOfWeek);
        Assert.Equal(new TimeOnly(10, 0), bookedDay.StartTime);
        Assert.Equal(new TimeOnly(18, 0), bookedDay.EndTime);
    }

    [Fact(Timeout = 180_000)]
    public async Task Force_saves_the_hours_and_slots_no_longer_show_the_booking()
    {
        await using var app = await StartAppAsync();
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Force Barber");
        var customer = await app.CreateUserAsync(CustomerEmail());
        await app.AuthenticateAsync(barber.User.Email);
        await ReplaceWeekAsync(app, barber, new TimeOnly(10, 0), new TimeOnly(18, 0));

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(BookingDate, 10, 0),
            BookingStatus.Confirmed);

        var scheduleId = await GetScheduleIdAsync(app, barber, BookingDate.DayOfWeek);

        using var response = await app.Client.PutAsJsonAsync(
            $"{HoursUrl(barber, scheduleId)}?force=true",
            new SetWorkingScheduleWorkingHoursRequest(new TimeOnly(12, 0), new TimeOnly(18, 0)),
            JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(BookingStatus.Confirmed, await ReadBookingStatusAsync(app, booking.Id));

        var schedule = await GetScheduleAsync(app, barber.Profile.Id);
        var bookedDay = schedule.Salons.Single().Weekly.Single(day => day.DayOfWeek == BookingDate.DayOfWeek);
        Assert.Equal(new TimeOnly(12, 0), bookedDay.StartTime);

        var slots = await GetSlotsAsync(app, barber.Profile.Id, barber.Salon.Id, BookingDate);
        Assert.True(slots.IsWorkingDay);
        Assert.DoesNotContain(slots.Slots, slot => slot.StartAt == Local(BookingDate, 10, 0));
        Assert.Contains(slots.Slots, slot => slot.StartAt == Local(BookingDate, 12, 0));
    }

    [Fact(Timeout = 180_000)]
    public async Task Invalid_force_value_is_treated_as_false()
    {
        await using var app = await StartAppAsync();
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Invalid Force Barber");
        var customer = await app.CreateUserAsync(CustomerEmail());
        await app.AuthenticateAsync(barber.User.Email);
        await ReplaceWeekAsync(app, barber, new TimeOnly(10, 0), new TimeOnly(18, 0));

        await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(BookingDate, 10, 0),
            BookingStatus.Confirmed);

        var scheduleId = await GetScheduleIdAsync(app, barber, BookingDate.DayOfWeek);

        using var response = await app.Client.PutAsJsonAsync(
            $"{HoursUrl(barber, scheduleId)}?force=maybe",
            new SetWorkingScheduleWorkingHoursRequest(new TimeOnly(12, 0), new TimeOnly(18, 0)),
            JsonOptions);

        // Невалидное значение биндер трактует как false: это конфликт, а не 400.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Day_off_on_a_booked_date_returns_409_and_force_saves_it()
    {
        await using var app = await StartAppAsync();
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Day Off Barber");
        var customer = await app.CreateUserAsync(CustomerEmail());
        await app.AuthenticateAsync(barber.User.Email);
        await ReplaceWeekAsync(app, barber, new TimeOnly(10, 0), new TimeOnly(18, 0));

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(BookingDate, 10, 0),
            BookingStatus.Confirmed);

        var url = ExceptionUrl(barber, BookingDate);

        using var blocked = await app.Client.PutAsJsonAsync(
            url,
            new SetScheduleExceptionRequest(true, TimeOnly.MinValue, TimeOnly.MinValue, "Выходной"),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains(booking.Id.ToString(), await blocked.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var forced = await app.Client.PutAsJsonAsync(
            $"{url}?force=true",
            new SetScheduleExceptionRequest(true, TimeOnly.MinValue, TimeOnly.MinValue, "Выходной"),
            JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, forced.StatusCode);

        var slots = await GetSlotsAsync(app, barber.Profile.Id, barber.Salon.Id, BookingDate);
        Assert.False(slots.IsWorkingDay);
        Assert.Empty(slots.Slots);
    }

    [Fact(Timeout = 180_000)]
    public async Task Time_off_with_a_booking_returns_409_and_force_saves_it()
    {
        await using var app = await StartAppAsync();
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Time Off Barber");
        var customer = await app.CreateUserAsync(CustomerEmail());
        await app.AuthenticateAsync(barber.User.Email);
        await ReplaceWeekAsync(app, barber, new TimeOnly(10, 0), new TimeOnly(18, 0));

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(BookingDate, 10, 0),
            BookingStatus.Confirmed);

        var startAt = Local(BookingDate, 8, 0);
        var endAt = Local(BookingDate, 12, 0);

        using var blocked = await app.Client.PostAsJsonAsync(
            TimeOffUrl(barber),
            new { startAt, endAt, reason = "Отпуск" },
            JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var body = await blocked.Content.ReadAsStringAsync();
        Assert.Contains("inside the time off", body, StringComparison.Ordinal);
        Assert.Contains(booking.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await CountTimeOffsAsync(app, barber.Profile.Id));

        using var forced = await app.Client.PostAsJsonAsync(
            $"{TimeOffUrl(barber)}?force=true",
            new { startAt, endAt, reason = "Отпуск" },
            JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, forced.StatusCode);
        Assert.Equal(1, await CountTimeOffsAsync(app, barber.Profile.Id));

        // Интервал без броней сохраняется без force.
        using var plain = await app.Client.PostAsJsonAsync(
            TimeOffUrl(barber),
            new
            {
                startAt = Local(BookingDate.AddDays(7), 8, 0),
                endAt = Local(BookingDate.AddDays(7), 12, 0),
                reason = "Отпуск"
            },
            JsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, plain.StatusCode);
        Assert.Equal(2, await CountTimeOffsAsync(app, barber.Profile.Id));
    }

    [Fact(Timeout = 180_000)]
    public async Task Deletes_are_never_checked_for_conflicts()
    {
        await using var app = await StartAppAsync();
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Delete Barber");
        var customer = await app.CreateUserAsync(CustomerEmail());
        await app.AuthenticateAsync(barber.User.Email);
        await ReplaceWeekAsync(app, barber, new TimeOnly(10, 0), new TimeOnly(18, 0));

        await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(BookingDate, 10, 0),
            BookingStatus.Confirmed);

        // Исключение и отпуск создаются с force: на дате и в интервале есть визит.
        var exceptionUrl = ExceptionUrl(barber, BookingDate);
        using (var exception = await app.Client.PutAsJsonAsync(
                   $"{exceptionUrl}?force=true",
                   new SetScheduleExceptionRequest(true, TimeOnly.MinValue, TimeOnly.MinValue, null),
                   JsonOptions))
        {
            Assert.Equal(HttpStatusCode.NoContent, exception.StatusCode);
        }

        using (var deletedException = await app.Client.DeleteAsync(exceptionUrl))
        {
            Assert.Equal(HttpStatusCode.NoContent, deletedException.StatusCode);
        }

        var startAt = Local(BookingDate, 9, 0);
        var endAt = Local(BookingDate, 12, 0);

        using (var timeOff = await app.Client.PostAsJsonAsync(
                   $"{TimeOffUrl(barber)}?force=true",
                   new { startAt, endAt, reason = (string?)null },
                   JsonOptions))
        {
            Assert.Equal(HttpStatusCode.NoContent, timeOff.StatusCode);
        }

        var timeOffId = await GetSingleTimeOffIdAsync(app, barber.Profile.Id);

        using (var deletedTimeOff = await app.Client.DeleteAsync($"{TimeOffUrl(barber)}/{timeOffId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deletedTimeOff.StatusCode);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Weekly_day_off_blocks_but_unchanged_days_do_not()
    {
        await using var app = await StartAppAsync();
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Week Barber");
        var customer = await app.CreateUserAsync(CustomerEmail());
        await app.AuthenticateAsync(barber.User.Email);
        await ReplaceWeekAsync(app, barber, new TimeOnly(10, 0), new TimeOnly(18, 0));

        var wednesdayBooking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(BookingDate, 10, 0),
            BookingStatus.Confirmed);

        // «Унаследованный» визит вне часов на неизменённом дне: он не должен блокировать
        // повторную отправку той же недели.
        await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(BookingDate.AddDays(-1), 8, 0),
            BookingStatus.Confirmed);

        using (var sameWeek = await app.Client.PutAsJsonAsync(
                   WeekUrl(barber),
                   new ReplaceWorkingSchedulesRequest(Week(new TimeOnly(10, 0), new TimeOnly(18, 0))),
                   JsonOptions))
        {
            Assert.Equal(HttpStatusCode.NoContent, sameWeek.StatusCode);
        }

        using var dayOff = await app.Client.PutAsJsonAsync(
            WeekUrl(barber),
            new ReplaceWorkingSchedulesRequest(
                Week(new TimeOnly(10, 0), new TimeOnly(18, 0), dayOff: BookingDate.DayOfWeek)),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, dayOff.StatusCode);

        using var document = JsonDocument.Parse(await dayOff.Content.ReadAsStringAsync());
        var conflict = Assert.Single(document.RootElement.GetProperty("conflicts").EnumerateArray());
        Assert.Equal(wednesdayBooking.Id, conflict.GetProperty("id").GetGuid());
    }

    [Fact(Timeout = 180_000)]
    public async Task Weekly_change_ignores_day_61_but_an_explicit_date_is_checked_beyond_the_horizon()
    {
        await using var app = await StartAppAsync();
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Horizon Barber");
        var customer = await app.CreateUserAsync(CustomerEmail());
        await app.AuthenticateAsync(barber.User.Email);
        await ReplaceWeekAsync(app, barber, new TimeOnly(10, 0), new TimeOnly(18, 0));

        var day61 = Today.AddDays(61);
        await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(day61, 10, 0),
            BookingStatus.Confirmed);

        using (var weekly = await app.Client.PutAsJsonAsync(
                   WeekUrl(barber),
                   new ReplaceWorkingSchedulesRequest(Week(new TimeOnly(12, 0), new TimeOnly(18, 0))),
                   JsonOptions))
        {
            // Бронь на 61-й день за горизонтом 60: недельная правка проходит.
            Assert.Equal(HttpStatusCode.NoContent, weekly.StatusCode);
        }

        var day75 = Today.AddDays(75);
        var bookingId = (await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(day75, 13, 0),
            BookingStatus.Confirmed)).Id;

        using var exception = await app.Client.PutAsJsonAsync(
            ExceptionUrl(barber, day75),
            new SetScheduleExceptionRequest(true, TimeOnly.MinValue, TimeOnly.MinValue, "Отпуск"),
            JsonOptions);

        // Явная дата проверяется на всю глубину, даже за горизонтом.
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);

        using var document = JsonDocument.Parse(await exception.Content.ReadAsStringAsync());
        var conflict = Assert.Single(document.RootElement.GetProperty("conflicts").EnumerateArray());
        Assert.Equal(bookingId, conflict.GetProperty("id").GetGuid());
    }

    [Fact(Timeout = 180_000)]
    public async Task Guest_without_last_name_gets_a_non_empty_customer_name()
    {
        await using var app = await StartAppAsync();
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Guest Barber");
        await app.AuthenticateAsync(barber.User.Email);
        await ReplaceWeekAsync(app, barber, new TimeOnly(10, 0), new TimeOnly(18, 0));

        await data.CreateGuestBookingAsync(
            barber,
            Local(BookingDate, 10, 0),
            guestFirstName: "Иван");

        var scheduleId = await GetScheduleIdAsync(app, barber, BookingDate.DayOfWeek);

        using var response = await app.Client.PutAsJsonAsync(
            HoursUrl(barber, scheduleId),
            new SetWorkingScheduleWorkingHoursRequest(new TimeOnly(12, 0), new TimeOnly(18, 0)),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var conflict = Assert.Single(document.RootElement.GetProperty("conflicts").EnumerateArray());
        Assert.Equal("Иван", conflict.GetProperty("customerName").GetString());
    }

    [Fact(Timeout = 240_000)]
    public async Task Search_by_date_honours_exceptions_and_the_primary_salon_time_off()
    {
        await using var app = await StartAppAsync();
        var data = new TestData(app);

        var dayOff = await data.CreateBarberProfileAsync("Day Off Search Barber");
        await data.AttachSalonAsync(dayOff.Id, "Berlin");
        await SeedWeekAsync(app, dayOff.Id);
        await SetExceptionAsync(app, dayOff.Id, BookingDate, isDayOff: true, TimeOnly.MinValue, TimeOnly.MinValue);

        var fullDayOff = await data.CreateBarberProfileAsync("Full Time Off Search Barber");
        await data.AttachSalonAsync(fullDayOff.Id, "Berlin");
        await SeedWeekAsync(app, fullDayOff.Id);
        await AddTimeOffAsync(
            app,
            fullDayOff.Id,
            Local(BookingDate, 0, 0),
            Local(BookingDate.AddDays(1), 0, 0));

        var partialTimeOff = await data.CreateBarberProfileAsync("Partial Time Off Search Barber");
        await data.AttachSalonAsync(partialTimeOff.Id, "Berlin");
        await SeedWeekAsync(app, partialTimeOff.Id);
        await AddTimeOffAsync(
            app,
            partialTimeOff.Id,
            Local(BookingDate, 10, 0),
            Local(BookingDate, 12, 0));

        var customHours = await data.CreateBarberProfileAsync("Custom Hours Search Barber");
        await data.AttachSalonAsync(customHours.Id, "Berlin");
        await SeedWeekAsync(app, customHours.Id);
        await SetExceptionAsync(app, customHours.Id, BookingDate, isDayOff: false, new TimeOnly(14, 0), new TimeOnly(16, 0));

        var plain = await data.CreateBarberProfileAsync("Plain Search Barber");
        await data.AttachSalonAsync(plain.Id, "Berlin");
        await SeedWeekAsync(app, plain.Id);

        var found = await SearchIdsAsync(app.Client, $"api/barbers?date={BookingDate:yyyy-MM-dd}");

        Assert.Equal(1, await CountExceptionsAsync(app, dayOff.Id));
        Assert.DoesNotContain(dayOff.Id, found);
        Assert.DoesNotContain(fullDayOff.Id, found);
        Assert.Contains(partialTimeOff.Id, found);
        Assert.Contains(customHours.Id, found);
        Assert.Contains(plain.Id, found);
    }

    private async Task<TestApp> StartAppAsync()
        => await TestApp.StartAsync(_database);

    private static string CustomerEmail() => $"customer-{Guid.NewGuid():N}@barbersearch.test";

    private static string WeekUrl(SeededBarber barber)
        => $"api/barber-profiles/{barber.Profile.Id}/salons/{barber.Salon.Id}/working-schedules";

    private static string HoursUrl(SeededBarber barber, Guid scheduleId)
        => $"{WeekUrl(barber)}/{scheduleId}/hours";

    private static string ExceptionUrl(SeededBarber barber, DateOnly date)
        => $"api/barber-profiles/{barber.Profile.Id}/salons/{barber.Salon.Id}/schedule-exceptions/{date:yyyy-MM-dd}";

    private static string TimeOffUrl(SeededBarber barber)
        => $"api/barber-profiles/{barber.Profile.Id}/time-offs";

    private static async Task ReplaceWeekAsync(
        TestApp app,
        SeededBarber barber,
        TimeOnly start,
        TimeOnly end)
    {
        using var response = await app.Client.PutAsJsonAsync(
            WeekUrl(barber),
            new ReplaceWorkingSchedulesRequest(Week(start, end)),
            JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static IReadOnlyList<ReplaceWorkingScheduleDayRequest> Week(
        TimeOnly start,
        TimeOnly end,
        DayOfWeek? dayOff = null)
        =>
        [
            Day(DayOfWeek.Monday, start, end, dayOff),
            Day(DayOfWeek.Tuesday, start, end, dayOff),
            Day(DayOfWeek.Wednesday, start, end, dayOff),
            Day(DayOfWeek.Thursday, start, end, dayOff),
            Day(DayOfWeek.Friday, start, end, dayOff),
            Day(DayOfWeek.Saturday, start, end, dayOff),
            Day(DayOfWeek.Sunday, start, end, dayOff)
        ];

    private static ReplaceWorkingScheduleDayRequest Day(
        DayOfWeek day,
        TimeOnly start,
        TimeOnly end,
        DayOfWeek? dayOff)
        => day == dayOff
            ? new ReplaceWorkingScheduleDayRequest(day, true, TimeOnly.MinValue, TimeOnly.MinValue)
            : new ReplaceWorkingScheduleDayRequest(day, false, start, end);

    /// <summary>
    /// Местное время салона (Europe/Berlin) как момент UTC.
    /// </summary>
    private static DateTimeOffset Local(DateOnly date, int hour, int minute = 0)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var unspecified = date.ToDateTime(new TimeOnly(hour, minute));

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone));
    }

    private static async Task<Guid> GetScheduleIdAsync(
        TestApp app,
        SeededBarber barber,
        DayOfWeek dayOfWeek)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.WorkingSchedules
            .AsNoTracking()
            .Where(schedule => schedule.BarberSalonId == barber.Profile.Salons.Single().Id
                               && schedule.DayOfWeek == dayOfWeek)
            .Select(schedule => schedule.Id)
            .SingleAsync();
    }

    private static async Task<BarberScheduleResponse> GetScheduleAsync(
        TestApp app,
        Guid barberProfileId)
    {
        using var response = await app.Client.GetAsync($"api/barber-profiles/{barberProfileId}/schedule");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BarberScheduleResponse>(JsonOptions);
        Assert.NotNull(body);

        return body;
    }

    private static async Task<BarberSlotsResponse> GetSlotsAsync(
        TestApp app,
        Guid barberProfileId,
        Guid salonId,
        DateOnly date)
    {
        using var response = await app.Client.GetAsync(
            $"api/barbers/{barberProfileId}/slots?date={date:yyyy-MM-dd}&salonId={salonId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BarberSlotsResponse>(JsonOptions);
        Assert.NotNull(body);

        return body;
    }

    private static async Task<BookingStatus> ReadBookingStatusAsync(TestApp app, Guid bookingId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Bookings
            .AsNoTracking()
            .Where(booking => booking.Id == bookingId)
            .Select(booking => booking.Status)
            .SingleAsync();
    }

    private static async Task<int> CountExceptionsAsync(TestApp app, Guid barberProfileId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await (from exception in db.ScheduleOverrides.AsNoTracking()
                      join link in db.BarberSalons.AsNoTracking()
                          on exception.BarberSalonId equals link.Id
                      where link.BarberProfileId == barberProfileId
                      select exception).CountAsync();
    }

    private static async Task<int> CountTimeOffsAsync(TestApp app, Guid barberProfileId)    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.TimeOffs.CountAsync(timeOff => timeOff.BarberProfileId == barberProfileId);
    }

    private static async Task<Guid> GetSingleTimeOffIdAsync(TestApp app, Guid barberProfileId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.TimeOffs
            .AsNoTracking()
            .Where(timeOff => timeOff.BarberProfileId == barberProfileId)
            .Select(timeOff => timeOff.Id)
            .SingleAsync();
    }

    /// <summary>
    /// Семь рабочих дней 10:00–18:00 прямо в БД: профили поиска не аутентифицируются.
    /// </summary>
    private static async Task SeedWeekAsync(TestApp app, Guid barberProfileId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var link = await db.BarberSalons
            .Include(barberSalon => barberSalon.WorkingSchedules)
            .FirstAsync(barberSalon => barberSalon.BarberProfileId == barberProfileId);

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            Require(link.AddWorkingSchedule(day, new TimeOnly(10, 0), new TimeOnly(18, 0)));
        }

        await db.SaveChangesAsync();
    }

    private static async Task SetExceptionAsync(
        TestApp app,
        Guid barberProfileId,
        DateOnly date,
        bool isDayOff,
        TimeOnly start,
        TimeOnly end)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var link = await db.BarberSalons
            .Include(barberSalon => barberSalon.ScheduleExceptions)
            .FirstAsync(barberSalon => barberSalon.BarberProfileId == barberProfileId);

        Require(link.SetScheduleException(date, isDayOff, start, end, null));
        await db.SaveChangesAsync();
    }

    private static async Task AddTimeOffAsync(
        TestApp app,
        Guid barberProfileId,
        DateTimeOffset startAt,
        DateTimeOffset endAt)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profile = await db.BarberProfiles
            .Include(barberProfile => barberProfile.TimeOffs)
            .FirstAsync(barberProfile => barberProfile.Id == barberProfileId);

        Require(profile.AddTimeOff(startAt, endAt, null));
        await db.SaveChangesAsync();
    }

    private static async Task<List<Guid>> SearchIdsAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        return [.. document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())];
    }

    private static T Require<T>(Result<T, Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }

        return result.Value;
    }

    private static void Require(UnitResult<Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }
    }
}
