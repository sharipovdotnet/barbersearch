using BarberSearch.Application.BarberProfiles.ScheduleConflicts;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain;
using BarberSearch.Domain.Bookings;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Чистый вычислитель конфликтов: границы дня, салон, локальная дата, переход на летнее время
/// и пересечение с отпуском. Базы и обработчиков здесь нет.
/// </summary>
public class ScheduleConflictDetectorTests
{
    private const string ZONE = "Asia/Yekaterinburg";

    private static readonly Guid SalonId = Guid.CreateVersion7();

    private static readonly DateOnly Date = new(2026, 11, 20);

    [Fact]
    public void FindOutsideHours_TouchingDayBoundaries_IsNotAConflict()
    {
        var windows = new[] { Window(new TimeOnly(10, 0), new TimeOnly(18, 0)) };

        // Визит ровно до закрытия и ровно с открытия — норма, а не конфликт.
        var bookings = new[]
        {
            Booking(Utc(17, 0), minutes: 60),
            Booking(Utc(10, 0), minutes: 60)
        };

        Assert.Empty(ScheduleConflictDetector.FindOutsideHours(windows, bookings));
    }

    [Fact]
    public void FindOutsideHours_BookingAfterClosing_IsAConflict()
    {
        var windows = new[] { Window(new TimeOnly(10, 0), new TimeOnly(18, 0)) };
        var booking = Booking(Utc(17, 30), minutes: 60);

        var conflicts = ScheduleConflictDetector.FindOutsideHours(windows, [booking]);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(booking.BookingId, conflict.Id);
        Assert.Equal("Иван Петров", conflict.CustomerName);
        Assert.Equal("Стрижка", conflict.ServiceName);
        Assert.Equal(BookingStatus.Confirmed, conflict.Status);
    }

    [Fact]
    public void FindOutsideHours_DayOff_FlagsEveryBookingOfThatDate()
    {
        var windows = new[] { Window(TimeOnly.MinValue, TimeOnly.MinValue, isDayOff: true) };

        var bookings = new[]
        {
            Booking(Utc(9, 0)),
            Booking(Utc(14, 0))
        };

        var conflicts = ScheduleConflictDetector.FindOutsideHours(windows, bookings);

        Assert.Equal(2, conflicts.Count);
        Assert.All(conflicts, conflict => Assert.Equal(Date, DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(conflict.StartAt, TimeZoneInfo.FindSystemTimeZoneById(ZONE)).DateTime)));
    }

    [Fact]
    public void FindOutsideHours_CustomHours_CanBeNarrowerOrWiderThanTheTemplate()
    {
        var wide = new[] { Window(new TimeOnly(9, 0), new TimeOnly(18, 0)) };
        var narrow = new[] { Window(new TimeOnly(12, 0), new TimeOnly(18, 0)) };
        var morningVisit = Booking(Utc(10, 0), minutes: 60);

        Assert.Empty(ScheduleConflictDetector.FindOutsideHours(wide, [morningVisit]));
        Assert.Single(ScheduleConflictDetector.FindOutsideHours(narrow, [morningVisit]));
    }

    [Fact]
    public void FindOutsideHours_AnotherSalonOfTheSameBarber_IsIgnored()
    {
        var windows = new[] { Window(new TimeOnly(12, 0), new TimeOnly(18, 0)) };
        var otherSalon = Booking(Utc(10, 0), minutes: 60, salonId: Guid.CreateVersion7());

        Assert.Empty(ScheduleConflictDetector.FindOutsideHours(windows, [otherSalon]));
    }

    [Fact]
    public void FindOutsideHours_AnotherLocalDay_IsIgnored()
    {
        var windows = new[] { Window(new TimeOnly(12, 0), new TimeOnly(18, 0)) };

        // 10:00 местного следующего дня — другой локальный день, эта правка его не касается.
        var nextDay = Booking(Utc(10, 0, dayOffset: 1), minutes: 60);

        Assert.Empty(ScheduleConflictDetector.FindOutsideHours(windows, [nextDay]));
    }

    [Fact]
    public void FindOutsideHours_OrdersByStartAndReturnsEmptyForEmptyInput()
    {
        var windows = new[] { Window(TimeOnly.MinValue, TimeOnly.MinValue, isDayOff: true) };
        var later = Booking(Utc(15, 0));
        var earlier = Booking(Utc(11, 0));

        var conflicts = ScheduleConflictDetector.FindOutsideHours(windows, [later, earlier]);

        Assert.Equal(new[] { earlier.BookingId, later.BookingId }, conflicts.Select(c => c.Id));
        Assert.Empty(ScheduleConflictDetector.FindOutsideHours([], [later]));
        Assert.Empty(ScheduleConflictDetector.FindOutsideHours(windows, []));
    }

    [Fact]
    public void FindOutsideHours_DaylightSavingGap_SkipsTheDayAndLogsAWarning()
    {
        // 2026-03-08 в Нью-Йорке 02:30 не существует: ToUtc вернёт null, день не проверяется.
        var window = new ScheduleDayWindow(
            SalonId,
            new DateOnly(2026, 3, 8),
            IsDayOff: false,
            new TimeOnly(2, 30),
            new TimeOnly(6, 0),
            "America/New_York");

        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var startAt = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(
                new DateTime(2026, 3, 8, 4, 0, 0, DateTimeKind.Unspecified),
                newYork));
        var booking = new ScheduleConflictBooking(
            Guid.CreateVersion7(),
            SalonId,
            startAt,
            startAt.AddHours(1),
            BookingStatus.Pending,
            "Клиент",
            "Стрижка",
            "America/New_York");

        var logger = new RecordingLogger<ScheduleConflictDetectorTests>();

        var conflicts = ScheduleConflictDetector.FindOutsideHours([window], [booking], logger);

        Assert.Empty(conflicts);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("daylight saving", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindOutsideHours_UnresolvedZone_SkipsTheDayAndLogsAnError()
    {
        var window = new ScheduleDayWindow(
            SalonId,
            Date,
            IsDayOff: true,
            TimeOnly.MinValue,
            TimeOnly.MinValue,
            "Unknown/Zone");

        var booking = new ScheduleConflictBooking(
            Guid.CreateVersion7(),
            SalonId,
            Utc(10, 0),
            Utc(11, 0),
            BookingStatus.Confirmed,
            "Клиент",
            "Стрижка",
            "Unknown/Zone");

        var logger = new RecordingLogger<ScheduleConflictDetectorTests>();

        var conflicts = ScheduleConflictDetector.FindOutsideHours([window], [booking], logger);

        Assert.Empty(conflicts);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("could not be resolved", StringComparison.Ordinal));
    }

    [Fact]
    public void FindInsideTimeOff_FullAndPartialDays_AreConflicts()
    {
        var dayStart = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        var morning = Booking(new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.Zero), minutes: 60);
        var evening = Booking(new DateTimeOffset(2026, 11, 20, 19, 0, 0, TimeSpan.Zero), minutes: 60);

        var fullDay = ScheduleConflictDetector.FindInsideTimeOff(dayStart, dayEnd, [morning, evening]);
        var halfDay = ScheduleConflictDetector.FindInsideTimeOff(
            dayStart.AddHours(9),
            dayStart.AddHours(12),
            [morning, evening]);

        Assert.Equal(2, fullDay.Count);
        Assert.Equal(new[] { morning.BookingId }, halfDay.Select(conflict => conflict.Id));
    }

    [Fact]
    public void FindInsideTimeOff_TouchingBoundaries_IsNotAConflict()
    {
        var startAt = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var endAt = startAt.AddHours(2);

        // Визит заканчивается ровно в начале отпуска и начинается ровно в его конце.
        var before = Booking(startAt.AddHours(-1), minutes: 60);
        var after = Booking(endAt, minutes: 60);

        Assert.Empty(ScheduleConflictDetector.FindInsideTimeOff(startAt, endAt, [before, after]));
        Assert.Empty(ScheduleConflictDetector.FindInsideTimeOff(startAt, endAt, []));
        Assert.Empty(ScheduleConflictDetector.FindInsideTimeOff(endAt, startAt, [before]));
    }

    [Fact]
    public void WindowFactory_WeekdayStopsAtTheHorizonAndSkipsExceptedDates()
    {
        var today = new DateOnly(2026, 11, 9);
        var excepted = new HashSet<DateOnly> { today.AddDays(7) };

        var windows = ScheduleWindowFactory.ForWeekday(
            SalonId,
            ZONE,
            DayOfWeek.Monday,
            isDayOff: false,
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            today,
            excepted);

        Assert.Equal(8, windows.Count);
        Assert.All(windows, window => Assert.Equal(DayOfWeek.Monday, window.Date.DayOfWeek));
        Assert.All(
            windows,
            window => Assert.True(window.Date <= today.AddDays(Constants.SCHEDULE_CONFLICT_HORIZON_DAYS)));
        Assert.DoesNotContain(windows, window => window.Date == today.AddDays(7));
        Assert.DoesNotContain(windows, window => window.Date > today.AddDays(60));
    }

    [Fact]
    public void WindowFactory_ExplicitDate_IsBuiltBeyondTheHorizon()
    {
        var today = new DateOnly(2026, 11, 9);
        var explicitDate = today.AddDays(75);

        var window = ScheduleWindowFactory.ForDate(
            SalonId,
            ZONE,
            explicitDate,
            isDayOff: true,
            TimeOnly.MinValue,
            TimeOnly.MinValue);

        Assert.Equal(explicitDate, window.Date);
        Assert.True(window.IsDayOff);
    }

    private static ScheduleDayWindow Window(
        TimeOnly start,
        TimeOnly end,
        bool isDayOff = false,
        string zone = ZONE)
        => new(SalonId, Date, isDayOff, start, end, zone);

    private static ScheduleConflictBooking Booking(
        DateTimeOffset startAt,
        int minutes = 60,
        Guid? salonId = null)
        => new(
            Guid.CreateVersion7(),
            salonId ?? SalonId,
            startAt,
            startAt.AddMinutes(minutes),
            BookingStatus.Confirmed,
            "Иван Петров",
            "Стрижка",
            ZONE);

    /// <summary>
    /// Местное время в Екатеринбурге (UTC+5) как момент UTC.
    /// </summary>
    private static DateTimeOffset Utc(int hour, int minute = 0, int dayOffset = 0)
        => new(
            new DateTime(2026, 11, 20, hour, minute, 0, DateTimeKind.Utc).AddDays(dayOffset)
                .AddHours(-5),
            TimeSpan.Zero);
}
