using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.CreateBarberBooking;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Users;
using BarberSearch.Domain.WorkingSchedules;
using BarberSearch.Infrastructure.Postgresql;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Сетка слотов, «прижатая» к свободным окнам дня, и проверка присланного клиентом времени
/// на реальной PostgreSQL: конец визита считает сервер, произвольная минута отклоняется
/// у публичной записи, а ручная запись барбера принимает любое время внутри окна.
/// </summary>
/// <remarks>
/// Сценарии идут через HTTP-конвейер: для публичного клиента проверяются коды
/// <c>booking.create.startAt.notASlot</c> / <c>.outsideSchedule</c> / <c>.overlap</c>,
/// для ручного пути — <c>booking.create.startAt.overlap</c>. Бронь в прошлом сидируется
/// напрямую <see cref="TestData.CreateBookingAsync"/>, потому что обработчик такую не создаст,
/// а сетке нужна именно чужая занятость. Время задаётся в поясе салона (Europe/Berlin),
/// а не «сейчас плюс N часов»: иначе тест плавал бы на границе суток и на шаге сетки.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class BookingSlotPackingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly TimeZoneInfo SalonZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private readonly PostgresFixture _database;

    public BookingSlotPackingTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Old_client_endAt_is_ignored_and_server_derives_it_from_the_service()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Legacy EndAt Barber");
        var customer = await app.CreateUserAsync(CustomerEmail("legacy"));

        await app.AuthenticateAsync(customer.Email.Value);

        var date = TodayInSalon().AddDays(2);
        var startAt = Local(date, 10);

        // Старый клиент присылает endAt = start + 4 часа. Поля в контракте уже нет:
        // десериализатор его игнорирует, а конец считает сервер из длительности услуги.
        var legacyBody = new
        {
            barberProfileId = barber.Profile.Id,
            barberServiceId = barber.Service.Id,
            salonId = barber.Salon.Id,
            startAt,
            endAt = startAt.AddMinutes(240),
            idempotencyKey = $"legacy-end-at-{Guid.NewGuid():N}",
            customerNote = (string?)null
        };

        Guid bookingId;

        using (var content = new StringContent(
                   JsonSerializer.Serialize(legacyBody, JsonOptions),
                   Encoding.UTF8,
                   "application/json"))
        using (var response = await app.Client.PostAsync("api/bookings", content))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>();
            Assert.NotNull(body);
            bookingId = body.Id;
        }

        var booking = await ReadBookingAsync(app, bookingId);

        Assert.Equal(startAt, booking.StartAt);
        Assert.Equal(startAt.AddMinutes(30), booking.EndAt);
        Assert.Equal(30, (booking.EndAt - booking.StartAt).TotalMinutes);
    }

    [Fact(Timeout = 180_000)]
    public async Task Public_booking_at_an_off_grid_start_is_rejected_as_not_a_slot()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Off Grid Public Barber");
        var customer = await app.CreateUserAsync(CustomerEmail("off-grid"));

        await app.AuthenticateAsync(customer.Email.Value);

        var date = TodayInSalon().AddDays(2);
        var slots = await GetSlotsAsync(app.Client, barber, date);

        // 11:00 — обычный старт 30-минутной сетки от открытия дня.
        var offered = Assert.Single(slots.Slots, slot => slot.StartAt == Local(date, 11));
        var offGrid = offered.StartAt.AddMinutes(7);

        using var response = await PostBookingAsync(
            app.Client,
            barber,
            offGrid,
            $"off-grid-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "\"errorCodes\":[\"booking.create.startAt.notASlot\"]",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact(Timeout = 180_000)]
    public async Task Slots_are_anchored_to_existing_bookings_not_to_a_fixed_grid()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Packing Slots Barber");
        var customer = await app.CreateUserAsync(CustomerEmail("packing"));

        var date = TodayInSalon().AddDays(2);

        // Занятость сидируется напрямую: обработчик такую бронь уже не создаст (время прошло
        // бы только после начала визита), а сетке нужна именно чужая запись на 12:07–12:37.
        await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(date, 12, 7),
            BookingStatus.Confirmed);

        var slots = await GetSlotsAsync(app.Client, barber, date);

        // Окно после брони начинается в 12:37: визит примыкает к её концу без дыры.
        Assert.Contains(slots.Slots, slot => slot.StartAt == Local(date, 12, 37));

        // Окно до брони заканчивается в 12:07: 11:37 + 30 минут = 12:07, тоже без дыры.
        Assert.Contains(slots.Slots, slot => slot.StartAt == Local(date, 11, 37));

        // 12:30 пересекается с бронью 12:07–12:37 и в сетке отсутствует.
        Assert.DoesNotContain(slots.Slots, slot => slot.StartAt == Local(date, 12, 30));

        Assert.All(
            slots.Slots,
            slot => Assert.Equal(30, (slot.EndAt - slot.StartAt).TotalMinutes));
    }

    [Fact(Timeout = 180_000)]
    public async Task Manual_barber_booking_accepts_an_off_grid_minute_but_not_an_overlap()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Manual Off Grid Barber");
        await LinkBarberProfileAsync(app, barber);

        var customer = await app.CreateUserAsync(CustomerEmail("manual"));

        var date = TodayInSalon().AddDays(2);

        await data.CreateBookingAsync(
            barber,
            customer.Id,
            Local(date, 12, 7),
            BookingStatus.Confirmed);

        await app.AuthenticateAsync(barber.User.Email.Value);

        // Ручной путь принимает 10:07: клиент пришёл не по сетке, но визит помещается в день.
        Guid bookingId;

        using (var created = await PostManualAsync(
                   app.Client,
                   barber,
                   customer.Id,
                   Local(date, 10, 7)))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var body = await created.Content.ReadFromJsonAsync<CreatedResourceResponse>();
            Assert.NotNull(body);
            bookingId = body.Id;
        }

        var booking = await ReadBookingAsync(app, bookingId);
        Assert.Equal(Local(date, 10, 7), booking.StartAt);
        Assert.Equal(Local(date, 10, 37), booking.EndAt);

        // 12:07 пересекается с засеянной бронью 12:07–12:37: это 409 overlap, а не 500.
        using (var overlap = await PostManualAsync(
                   app.Client,
                   barber,
                   customer.Id,
                   Local(date, 12, 7)))
        {
            Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
            Assert.Contains(
                "\"errorCodes\":[\"booking.create.startAt.overlap\"]",
                await overlap.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Public_booking_on_the_barbers_day_off_is_rejected_as_outside_schedule()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Day Off Booking Barber");
        var customer = await app.CreateUserAsync(CustomerEmail("day-off"));

        await app.AuthenticateAsync(customer.Email.Value);

        var date = TodayInSalon().AddDays(2);
        await MakeDayOffAsync(app, barber.Profile.Id, date.DayOfWeek);

        using var response = await PostBookingAsync(
            app.Client,
            barber,
            Local(date, 10),
            $"day-off-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "\"errorCodes\":[\"booking.create.startAt.outsideSchedule\"]",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    private static string CustomerEmail(string scenario)
        => $"slot-packing-{scenario}-{Guid.NewGuid():N}@barbersearch.test";

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

    private static async Task<BarberSlotsResponse> GetSlotsAsync(
        HttpClient client,
        SeededBarber barber,
        DateOnly date)
    {
        using var response = await client.GetAsync(
            $"api/barbers/{barber.Profile.Id}/slots"
            + $"?date={date:yyyy-MM-dd}&hairServiceId={barber.HairService.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<BarberSlotsResponse>(JsonOptions);
        Assert.NotNull(body);

        return body;
    }

    private static Task<HttpResponseMessage> PostBookingAsync(
        HttpClient client,
        SeededBarber barber,
        DateTimeOffset startAt,
        string idempotencyKey)
        => client.PostAsJsonAsync(
            "api/bookings",
            new CreateBookingRequest(
                barber.Profile.Id,
                barber.Service.Id,
                barber.Salon.Id,
                startAt,
                idempotencyKey,
                "slot packing test"));

    private static Task<HttpResponseMessage> PostManualAsync(
        HttpClient client,
        SeededBarber barber,
        Guid customerId,
        DateTimeOffset startAt)
        => client.PostAsJsonAsync(
            "api/barber/bookings",
            new CreateBarberBookingRequest(
                CustomerId: customerId,
                GuestFirstName: null,
                GuestLastName: null,
                GuestPhone: null,
                BarberServiceId: barber.Service.Id,
                SalonId: barber.Salon.Id,
                StartAt: startAt,
                IdempotencyKey: $"manual-{Guid.NewGuid():N}",
                CustomerNote: null));

    private static async Task<Booking> ReadBookingAsync(TestApp app, Guid bookingId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Bookings
            .AsNoTracking()
            .SingleAsync(booking => booking.Id == bookingId);
    }

    /// <summary>
    /// Переписывает недельную неделю так, чтобы целевой день недели стал выходным:
    /// остальные дни остаются рабочими 00:00–23:59.
    /// </summary>
    private static async Task MakeDayOffAsync(
        TestApp app,
        Guid barberProfileId,
        DayOfWeek dayOff)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var link = await db.BarberSalons
            .Include(barberSalon => barberSalon.WorkingSchedules)
            .FirstAsync(barberSalon => barberSalon.BarberProfileId == barberProfileId);

        var days = Enum.GetValues<DayOfWeek>()
            .Select(day => day == dayOff
                ? new WorkingDaySchedule(day, IsDayOff: true, TimeOnly.MinValue, TimeOnly.MinValue)
                : new WorkingDaySchedule(day, IsDayOff: false, TimeOnly.MinValue, new TimeOnly(23, 59)))
            .ToList();

        Require(link.ReplaceWorkingSchedules(days));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Привязывает профиль к аккаунту барбера: claim <c>barber_profile_id</c> появляется только
    /// после сохранения связи, поэтому ручной путь идёт после неё.
    /// </summary>
    private static async Task LinkBarberProfileAsync(TestApp app, SeededBarber barber)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(item => item.Id == barber.User.Id);

        Require(user.LinkBarberProfile(barber.Profile.Id));
        await db.SaveChangesAsync();
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
