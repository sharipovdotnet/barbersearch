using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Bookings.RescheduleBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Перенос визита на реальной PostgreSQL: <c>204</c>, сохранение услуги/цены/статуса,
/// различимые <c>409</c>, аудит, чтение слотов переноса и права.
/// </summary>
/// <remarks>
/// Сценарии повторяют клиентский путь: рабочая неделя барбера сидируется его же доменными
/// методами, брони — <see cref="TestData"/>, а действия идут через HTTP-конвейер, поэтому
/// проверяются и маппинг ошибок, и транзакция с advisory-блокировкой.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class BookingRescheduleApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly TimeZoneInfo SalonZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private readonly PostgresFixture _database;

    public BookingRescheduleApiTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Reschedule_moves_only_the_time_and_swaps_the_occupied_slots()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Reschedule Move Barber");
        await SeedWeekAsync(app, barber.Profile.Id);
        var customer = await app.CreateUserAsync(CustomerEmail());

        var date = TodayInSalon().AddDays(1);
        var originalStart = Local(date, 11);
        var targetStart = Local(date, 12);

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            originalStart,
            BookingStatus.Confirmed);

        await app.AuthenticateAsync(customer.Email);

        using (var response = await RescheduleAsync(app.Client, booking.Id, targetStart))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var page = await GetUpcomingAsync(app.Client);
        var row = Assert.Single(page.Items, item => item.Id == booking.Id);

        Assert.Equal(targetStart, row.StartAt);
        Assert.Equal(targetStart.AddMinutes(30), row.EndAt);

        // Перенос не трогает ни цену, ни статус, ни услугу/салон/барбера.
        Assert.Equal(45m, row.Price);
        Assert.Equal(BookingStatus.Confirmed, row.Status);
        Assert.Equal(barber.Service.Id, row.BarberServiceId);
        Assert.Equal(barber.Salon.Id, row.SalonId);
        Assert.Equal(barber.Profile.Id, row.BarberProfileId);

        var slots = await GetPublicSlotsAsync(app.Client, barber.Profile.Id, barber.Salon.Id, date);
        Assert.Contains(slots.Slots, slot => slot.StartAt == originalStart);
        Assert.DoesNotContain(slots.Slots, slot => slot.StartAt == targetStart);
    }

    [Fact(Timeout = 180_000)]
    public async Task Occupied_target_returns_overlap_and_changes_nothing()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Reschedule Overlap Barber");
        await SeedWeekAsync(app, barber.Profile.Id);
        var customer = await app.CreateUserAsync(CustomerEmail());
        var otherCustomer = await app.CreateUserAsync(CustomerEmail());

        var date = TodayInSalon().AddDays(1);
        var originalStart = Local(date, 11);
        var targetStart = Local(date, 12);

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            originalStart,
            BookingStatus.Confirmed);
        await data.CreateBookingAsync(
            barber,
            otherCustomer.Id,
            targetStart,
            BookingStatus.Confirmed);

        await app.AuthenticateAsync(customer.Email);

        using (var response = await RescheduleAsync(app.Client, booking.Id, targetStart))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains(
                "\"errorCodes\":[\"booking.reschedule.overlap\"]",
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        var unchanged = await ReadBookingAsync(app, booking.Id);
        Assert.Equal(originalStart, unchanged.StartAt);
        Assert.Equal(BookingStatus.Confirmed, unchanged.Status);
    }

    [Fact(Timeout = 180_000)]
    public async Task Outside_hours_day_off_and_time_off_return_slot_conflict()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Reschedule Slot Barber");
        await SeedWeekAsync(app, barber.Profile.Id);
        var customer = await app.CreateUserAsync(CustomerEmail());

        var today = TodayInSalon();
        var bookingDate = today.AddDays(1);
        var dayOffDate = today.AddDays(2);
        var timeOffDate = today.AddDays(3);
        var originalStart = Local(bookingDate, 11);

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            originalStart,
            BookingStatus.Confirmed);

        await SetDayOffAsync(app, barber.Profile.Id, dayOffDate);
        await AddTimeOffAsync(
            app,
            barber.Profile.Id,
            Local(timeOffDate, 13),
            Local(timeOffDate, 14));

        await app.AuthenticateAsync(customer.Email);

        await AssertSlotConflictAsync(app, booking.Id, Local(bookingDate, 9));
        await AssertSlotConflictAsync(app, booking.Id, Local(dayOffDate, 12));
        await AssertSlotConflictAsync(app, booking.Id, Local(timeOffDate, 13));

        var unchanged = await ReadBookingAsync(app, booking.Id);
        Assert.Equal(originalStart, unchanged.StartAt);
        Assert.Equal(BookingStatus.Confirmed, unchanged.Status);
    }

    [Fact(Timeout = 180_000)]
    public async Task Past_and_too_far_targets_are_validation_and_started_visit_is_conflict()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Reschedule Window Barber");
        await SeedWeekAsync(app, barber.Profile.Id);
        var customer = await app.CreateUserAsync(CustomerEmail());

        var date = TodayInSalon().AddDays(1);

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(date, 11),
            BookingStatus.Confirmed);
        var started = await data.CreateBookingAsync(
            barber,
            customer.Id,
            DateTimeOffset.UtcNow.AddDays(-1),
            BookingStatus.Confirmed);

        await app.AuthenticateAsync(customer.Email);

        using (var past = await RescheduleAsync(app.Client, booking.Id, DateTimeOffset.UtcNow.AddHours(-1)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
            Assert.Contains(
                "booking.reschedule.startAt.inPast",
                await past.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        using (var tooFar = await RescheduleAsync(
                   app.Client,
                   booking.Id,
                   DateTimeOffset.UtcNow.AddDays(91)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, tooFar.StatusCode);
            Assert.Contains(
                "booking.reschedule.startAt.tooFar",
                await tooFar.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        using (var alreadyStarted = await RescheduleAsync(app.Client, started.Id, Local(date, 12)))
        {
            Assert.Equal(HttpStatusCode.Conflict, alreadyStarted.StatusCode);
            Assert.Contains(
                "\"errorCodes\":[\"booking.reschedule.status\"]",
                await alreadyStarted.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        var unchanged = await ReadBookingAsync(app, booking.Id);
        Assert.Equal(Local(date, 11), unchanged.StartAt);

        var startedUnchanged = await ReadBookingAsync(app, started.Id);
        Assert.True(startedUnchanged.StartAt < DateTimeOffset.UtcNow);
    }

    [Fact(Timeout = 240_000)]
    public async Task Two_parallel_reschedules_never_return_500_or_create_a_second_row()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Reschedule Race Barber");
        await SeedWeekAsync(app, barber.Profile.Id);
        var customer = await app.CreateUserAsync(CustomerEmail());

        var date = TodayInSalon().AddDays(1);
        var firstTarget = Local(date, 12);
        var secondTarget = Local(date, 13);

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(date, 11),
            BookingStatus.Confirmed);

        await app.AuthenticateAsync(customer.Email);

        var responses = await Task.WhenAll(
            RescheduleAsync(app.Client, booking.Id, firstTarget),
            RescheduleAsync(app.Client, booking.Id, secondTarget));

        try
        {
            // Advisory-блокировка сериализует попытки: вторая читает уже перенесённую бронь,
            // поэтому допустимы и два 204, и 204 + 409. Запрещены 500 и вторая строка.
            Assert.All(
                responses,
                response => Assert.True(
                    response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.Conflict,
                    $"Unexpected status {response.StatusCode}."));
            Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.NoContent);

            foreach (var conflict in responses.Where(response =>
                         response.StatusCode == HttpStatusCode.Conflict))
            {
                Assert.Contains(
                    "booking.",
                    await conflict.Content.ReadAsStringAsync(),
                    StringComparison.Ordinal);
            }

            var moved = await ReadBookingAsync(app, booking.Id);
            Assert.True(moved.StartAt == firstTarget || moved.StartAt == secondTarget);
            Assert.Equal(30, (moved.EndAt - moved.StartAt).TotalMinutes);

            // Ни одной второй строки: ни на старом месте, ни на новом.
            Assert.Equal(
                1,
                await CountBookingsOfDayAsync(app, barber.Profile.Id, date));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Guest_booking_is_moved_by_the_barber_but_not_by_a_customer()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Reschedule Guest Barber");
        await SeedWeekAsync(app, barber.Profile.Id);
        var customer = await app.CreateUserAsync(CustomerEmail());

        var date = TodayInSalon().AddDays(1);
        var targetStart = Local(date, 12);

        var booking = await data.CreateGuestBookingAsync(barber, Local(date, 11));

        await app.AuthenticateAsync(customer.Email);

        using (var forbidden = await RescheduleAsync(app.Client, booking.Id, targetStart))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        await app.AuthenticateAsync(barber.User.Email);

        using (var moved = await RescheduleAsync(app.Client, booking.Id, targetStart))
        {
            Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        }

        var guest = await ReadBookingAsync(app, booking.Id);
        Assert.Null(guest.UserId);
        Assert.Equal(targetStart, guest.StartAt);
    }

    [Fact(Timeout = 180_000)]
    public async Task Administrator_can_move_a_customer_booking()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Reschedule Admin Barber");
        await SeedWeekAsync(app, barber.Profile.Id);
        var customer = await app.CreateUserAsync(CustomerEmail());
        var admin = await app.CreateUserAsync(CustomerEmail(), UserRole.Admin);

        var date = TodayInSalon().AddDays(1);
        var targetStart = Local(date, 12);

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(date, 11),
            BookingStatus.Confirmed);

        await app.AuthenticateAsync(admin.Email);

        using (var response = await RescheduleAsync(app.Client, booking.Id, targetStart))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var moved = await ReadBookingAsync(app, booking.Id);
        Assert.Equal(targetStart, moved.StartAt);
    }

    [Fact(Timeout = 180_000)]
    public async Task Reschedule_writes_exactly_one_audit_row_and_creation_writes_none()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Reschedule Audit Barber");
        await SeedWeekAsync(app, barber.Profile.Id);
        var customer = await app.CreateUserAsync(CustomerEmail());

        var date = TodayInSalon().AddDays(1);
        var originalStart = Local(date, 11);
        var targetStart = Local(date, 12);

        await app.AuthenticateAsync(customer.Email);

        Guid bookingId;

        using (var created = await app.Client.PostAsJsonAsync(
                   "api/bookings",
                   new CreateBookingRequest(
                       barber.Profile.Id,
                       barber.Service.Id,
                       barber.Salon.Id,
                       originalStart,
                       originalStart.AddMinutes(30),
                       $"reschedule-audit-{Guid.NewGuid():N}",
                       null)))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var body = await created.Content.ReadFromJsonAsync<CreatedResourceResponse>();
            Assert.NotNull(body);
            bookingId = body.Id;
        }

        // Создание брони по-прежнему не пишет строку в журнал (AuditsCreation: false).
        Assert.Equal(0, await data.CountAuditRowsAsync("booking"));

        using (var response = await RescheduleAsync(app.Client, bookingId, targetStart))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var rows = await data.ReadAuditRowsAsync();
        var row = Assert.Single(
            rows,
            candidate => candidate.EntityType == "booking" && candidate.EntityId == bookingId);

        Assert.Equal("booking.updated", row.Action);
        Assert.Equal(1, await data.CountAuditRowsAsync("booking"));

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Equal(
            originalStart,
            ParseInstant(changes.GetProperty("startAt").GetProperty("from").GetString()));
        Assert.Equal(
            targetStart,
            ParseInstant(changes.GetProperty("startAt").GetProperty("to").GetString()));
        Assert.Equal(
            originalStart.AddMinutes(30),
            ParseInstant(changes.GetProperty("endAt").GetProperty("from").GetString()));
        Assert.Equal(
            targetStart.AddMinutes(30),
            ParseInstant(changes.GetProperty("endAt").GetProperty("to").GetString()));
    }

    [Fact(Timeout = 180_000)]
    public async Task Reschedule_slots_use_the_booking_duration_and_exclude_only_foreign_bookings()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Reschedule Slots Barber");
        await SeedWeekAsync(app, barber.Profile.Id);
        var customer = await app.CreateUserAsync(CustomerEmail());
        var otherCustomer = await app.CreateUserAsync(CustomerEmail());

        var date = TodayInSalon().AddDays(1);
        var ownStart = Local(date, 11, 30);
        var foreignStart = Local(date, 12, 15);

        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            ownStart,
            BookingStatus.Confirmed,
            durationMinutes: 45);
        await data.CreateBookingAsync(
            barber,
            otherCustomer.Id,
            foreignStart,
            BookingStatus.Confirmed,
            durationMinutes: 45);

        await app.AuthenticateAsync(customer.Email);

        var beforePriceChange = await ReadRescheduleSlotsAsync(app.Client, booking.Id, date);
        Assert.Equal(45, beforePriceChange.DurationMinutes);
        Assert.Equal(barber.Service.Id, beforePriceChange.BarberServiceId);
        Assert.Null(beforePriceChange.HairServiceId);
        Assert.Contains(beforePriceChange.Slots, slot => slot.StartAt == ownStart);
        Assert.DoesNotContain(beforePriceChange.Slots, slot => slot.StartAt == foreignStart);

        // Прайс переписали после записи — сетка переноса обязана остаться прежней (D6).
        await SetBarberServiceDurationAsync(app, barber.Service.Id, 60);

        var afterPriceChange = await ReadRescheduleSlotsAsync(app.Client, booking.Id, date);
        Assert.Equal(45, afterPriceChange.DurationMinutes);
        Assert.Contains(afterPriceChange.Slots, slot => slot.StartAt == ownStart);
        Assert.All(
            afterPriceChange.Slots,
            slot => Assert.Equal(45, (slot.EndAt - slot.StartAt).TotalMinutes));
    }

    [Fact(Timeout = 180_000)]
    public async Task Reschedule_slots_are_forbidden_for_a_foreigner_and_not_found_for_an_unknown_booking()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Reschedule Read Barber");
        await SeedWeekAsync(app, barber.Profile.Id);
        var customer = await app.CreateUserAsync(CustomerEmail());
        var foreigner = await app.CreateUserAsync(CustomerEmail());

        var date = TodayInSalon().AddDays(1);
        var booking = await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(date, 11),
            BookingStatus.Confirmed);

        await app.AuthenticateAsync(foreigner.Email);

        using (var forbidden = await GetRescheduleSlotsAsync(app.Client, booking.Id, date))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var missing = await GetRescheduleSlotsAsync(app.Client, Guid.NewGuid(), date))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Contains(
                "booking.notFound",
                await missing.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        await app.AuthenticateAsync(customer.Email);

        using (var withoutDate = await app.Client.GetAsync(
                   $"api/bookings/{booking.Id}/reschedule-slots"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, withoutDate.StatusCode);
            Assert.Contains(
                "booking.reschedule.date",
                await withoutDate.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        using (var beyondHorizon = await GetRescheduleSlotsAsync(
                   app.Client,
                   booking.Id,
                   TodayInSalon().AddDays(91)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, beyondHorizon.StatusCode);
            Assert.Contains(
                "booking.reschedule.date",
                await beyondHorizon.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
    }

    private static string CustomerEmail() => $"reschedule-{Guid.NewGuid():N}@barbersearch.test";

    /// <summary>
    /// Местное время салона (Europe/Berlin) как момент UTC.
    /// </summary>
    private static DateTimeOffset Local(DateOnly date, int hour, int minute = 0)
    {
        var unspecified = date.ToDateTime(new TimeOnly(hour, minute));

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, SalonZone));
    }

    private static DateOnly TodayInSalon()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, SalonZone).DateTime);

    private static DateTimeOffset ParseInstant(string? value)
    {
        Assert.NotNull(value);

        return DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Task<HttpResponseMessage> RescheduleAsync(
        HttpClient client,
        Guid bookingId,
        DateTimeOffset startAt)
        => client.PostAsJsonAsync(
            $"api/bookings/{bookingId}/reschedule",
            new RescheduleBookingRequest(startAt));

    private static Task<HttpResponseMessage> GetRescheduleSlotsAsync(
        HttpClient client,
        Guid bookingId,
        DateOnly date)
        => client.GetAsync($"api/bookings/{bookingId}/reschedule-slots?date={date:yyyy-MM-dd}");

    private static async Task<BarberSlotsResponse> ReadRescheduleSlotsAsync(
        HttpClient client,
        Guid bookingId,
        DateOnly date)
    {
        using var response = await GetRescheduleSlotsAsync(client, bookingId, date);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BarberSlotsResponse>(JsonOptions);
        Assert.NotNull(body);

        return body;
    }

    private static async Task<PagedResponse<BookingResponse>> GetUpcomingAsync(HttpClient client)
    {
        using var response = await client.GetAsync("api/bookings?scope=upcoming");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<BookingResponse>>(JsonOptions);
        Assert.NotNull(page);

        return page;
    }

    private static async Task<BarberSlotsResponse> GetPublicSlotsAsync(
        HttpClient client,
        Guid barberProfileId,
        Guid salonId,
        DateOnly date)
    {
        using var response = await client.GetAsync(
            $"api/barbers/{barberProfileId}/slots?date={date:yyyy-MM-dd}&salonId={salonId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BarberSlotsResponse>(JsonOptions);
        Assert.NotNull(body);

        return body;
    }

    private static async Task AssertSlotConflictAsync(
        TestApp app,
        Guid bookingId,
        DateTimeOffset targetStart)
    {
        using var response = await RescheduleAsync(app.Client, bookingId, targetStart);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "\"errorCodes\":[\"booking.reschedule.slot\"]",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

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

    private static async Task SetDayOffAsync(TestApp app, Guid barberProfileId, DateOnly date)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var link = await db.BarberSalons
            .Include(barberSalon => barberSalon.ScheduleExceptions)
            .FirstAsync(barberSalon => barberSalon.BarberProfileId == barberProfileId);

        Require(link.SetScheduleException(
            date,
            isDayOff: true,
            TimeOnly.MinValue,
            TimeOnly.MinValue,
            reason: "Выходной"));
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

        Require(profile.AddTimeOff(startAt, endAt, reason: null));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Переписывает длительность услуги у барбера: перенос обязан этого не заметить.
    /// </summary>
    private static async Task SetBarberServiceDurationAsync(
        TestApp app,
        Guid barberServiceId,
        int durationMinutes)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE barber_services
            SET duration_minutes = {durationMinutes}
            WHERE id = {barberServiceId}
            """);
        Assert.Equal(1, updated);
    }

    private static async Task<Booking> ReadBookingAsync(TestApp app, Guid bookingId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Bookings
            .AsNoTracking()
            .SingleAsync(booking => booking.Id == bookingId);
    }

    /// <summary>
    /// Сколько активных броней у барбера в локальном дне: перенос не должен создавать вторую.
    /// </summary>
    private static async Task<int> CountBookingsOfDayAsync(
        TestApp app,
        Guid barberProfileId,
        DateOnly date)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dayStart = Local(date, 0);
        var dayEnd = Local(date.AddDays(1), 0);

        return await db.Bookings
            .AsNoTracking()
            .CountAsync(booking => booking.BarberProfileId == barberProfileId
                                   && booking.StartAt < dayEnd
                                   && dayStart < booking.EndAt);
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
