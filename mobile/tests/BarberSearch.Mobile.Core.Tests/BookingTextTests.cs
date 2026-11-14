using BarberSearch.Domain.Bookings;
using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки текстов записи: статусы, даты, момент визита, обратный отсчёт и заметки.
/// </summary>
/// <remarks>
/// Строки сверяются дословно с вебом (<c>frontend/src/lib/booking-status.ts</c>,
/// <c>frontend/src/lib/format.ts</c>) и с разделом «Тексты и форматы» <c>prompts/03-bookings.md</c>.
/// Культура задана в самом <see cref="BookingText"/> (<c>ru-RU</c>), поэтому результат не зависит
/// от настроек машины, а 15 ноября 2026 года — воскресенье («вс, 15 нояб.»).
/// </remarks>
public sealed class BookingTextTests
{
    /// <summary>
    /// 15 ноября 2026, 15:30 UTC — это 18:30 в московском поясе салона.
    /// </summary>
    private static readonly DateTimeOffset Instant = new(2026, 11, 15, 15, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(BookingStatus.Pending, "Ждёт подтверждения")]
    [InlineData(BookingStatus.Confirmed, "Подтверждена")]
    [InlineData(BookingStatus.Cancelled, "Отменена")]
    [InlineData(BookingStatus.Completed, "Завершена")]
    [InlineData(BookingStatus.NoShow, "Неявка")]
    public void Status_EveryBookingStatus_ShowsRussianCaption(BookingStatus status, string expected) =>
        Assert.Equal(expected, BookingText.Status(status));

    [Fact]
    public void ShortDate_NovemberDate_ShowsWeekdayDayAndShortMonth() =>
        Assert.Equal("вс, 15 нояб.", BookingText.ShortDate(new DateOnly(2026, 11, 15)));

    [Fact]
    public void Weekday_NovemberDate_ShowsShortWeekday() =>
        Assert.Equal("вс", BookingText.Weekday(new DateOnly(2026, 11, 15)));

    [Fact]
    public void MonthTitle_NovemberDate_CapitalizesMonthAndKeepsYear() =>
        Assert.Equal("Ноябрь 2026", BookingText.MonthTitle(new DateOnly(2026, 11, 15)));

    [Fact]
    public void Moment_SalonZone_ShowsDateAndTimeInSalonZone()
    {
        var moscow = SalonClock.TryResolve("Europe/Moscow");

        Assert.NotNull(moscow);
        Assert.Equal("вс, 15 ноября в 18:30", BookingText.Moment(Instant, moscow));
    }

    [Fact]
    public void Moment_NoTimeZone_ShowsUtcTime()
    {
        // Тот же момент, но со сдвигом салона: без пояса он обязан показаться в UTC.
        var moscowInstant = new DateTimeOffset(2026, 11, 15, 18, 30, 0, TimeSpan.FromHours(3));

        Assert.Equal(Instant, moscowInstant);
        Assert.Equal("вс, 15 ноября в 15:30", BookingText.Moment(moscowInstant, null));
    }

    [Fact]
    public void Countdown_SecondsRemaining_ShowsLessThanMinute()
    {
        // Меньше половины минуты: округление до ближайшей минуты даёт ноль.
        Assert.Equal("меньше минуты", BookingText.Countdown(Instant.AddSeconds(20), Instant));
    }

    [Fact]
    public void Countdown_OneMinute_ShowsOneMinute() =>
        Assert.Equal("через 1 минуту", BookingText.Countdown(Instant.AddMinutes(1), Instant));

    [Fact]
    public void Countdown_TwoHours_ShowsTwoHours() =>
        Assert.Equal("через 2 часа", BookingText.Countdown(Instant.AddHours(2), Instant));

    [Fact]
    public void Countdown_ThreeDays_ShowsThreeDays() =>
        Assert.Equal("через 3 дня", BookingText.Countdown(Instant.AddDays(3), Instant));

    [Fact]
    public void Countdown_FiveDays_ShowsFiveDays() =>
        Assert.Equal("через 5 дней", BookingText.Countdown(Instant.AddDays(5), Instant));

    [Fact]
    public void Countdown_EightDays_ReturnsNull()
    {
        // Правило веба: отсчёт существует только до семи дней.
        Assert.Null(BookingText.Countdown(Instant.AddDays(8), Instant));
    }

    [Fact]
    public void Countdown_StartedVisit_ReturnsNull() =>
        Assert.Null(BookingText.Countdown(Instant.AddMinutes(-1), Instant));

    [Fact]
    public void BarberNote_NoNote_ReturnsNull()
    {
        Assert.Null(BookingText.BarberNote(null));
        Assert.Null(BookingText.BarberNote("   "));
    }

    [Fact]
    public void BarberNote_Text_TrimsAndSignsNote() =>
        Assert.Equal("Барбер: x", BookingText.BarberNote("  x "));

    [Fact]
    public void CustomerNote_Text_TrimsAndSignsNote() =>
        Assert.Equal("Вы: x", BookingText.CustomerNote("  x "));

    [Fact]
    public void CustomerNote_NoNote_ReturnsNull() =>
        Assert.Null(BookingText.CustomerNote(null));

    [Fact]
    public void CancellationReason_NoReason_ShowsPlaceholder() =>
        Assert.Equal("Причина отмены не указана", BookingText.CancellationReason(null));

    [Fact]
    public void CancellationReason_Text_ShowsReason() =>
        Assert.Equal("Причина отмены: Барбер заболел", BookingText.CancellationReason("Барбер заболел"));

    [Fact]
    public void CancelAnnouncement_Moment_ReturnsBanner() =>
        Assert.Equal(
            "Запись на сб, 15 ноября в 18:30 отменена.",
            BookingText.CancelAnnouncement("сб, 15 ноября в 18:30"));

    [Fact]
    public void RescheduleAnnouncement_Moment_ReturnsBanner() =>
        Assert.Equal(
            "Визит перенесён на сб, 15 ноября в 18:30.",
            BookingText.RescheduleAnnouncement("сб, 15 ноября в 18:30"));
}
