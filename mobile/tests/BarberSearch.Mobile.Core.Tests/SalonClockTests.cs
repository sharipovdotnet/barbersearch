using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки времени салона: пояс, календарные даты и форматы показа.
/// </summary>
/// <remarks>
/// Пояс создаётся вручную (<see cref="TimeZoneInfo.CreateCustomTimeZone"/>), чтобы тесты не
/// зависели от базы часовых поясов машины: сдвиг +5 проверяется одинаково везде.
/// </remarks>
public sealed class SalonClockTests
{
    private static readonly TimeZoneInfo PlusFive = TimeZoneInfo.CreateCustomTimeZone(
        "Test/Plus5",
        TimeSpan.FromHours(5),
        "Тест +5",
        "Тест +5");

    [Fact]
    public void ToLocal_CustomZone_KeepsInstantAndMovesClock()
    {
        var instant = new DateTimeOffset(2026, 11, 15, 13, 30, 0, TimeSpan.Zero);

        var local = SalonClock.ToLocal(instant, PlusFive);

        Assert.Equal(instant, local);
        Assert.Equal(TimeSpan.FromHours(5), local.Offset);
        Assert.Equal(new DateTime(2026, 11, 15, 18, 30, 0, DateTimeKind.Unspecified), local.DateTime);
    }

    [Fact]
    public void TryResolve_UnknownZone_ReturnsNull()
    {
        Assert.Null(SalonClock.TryResolve("Nowhere/Atlantis"));
        Assert.Null(SalonClock.TryResolve(string.Empty));
        Assert.Null(SalonClock.TryResolve(null));
    }

    [Fact]
    public void TryResolve_Utc_ReturnsZone()
    {
        Assert.NotNull(SalonClock.TryResolve("UTC"));
    }

    [Fact]
    public void Today_AcrossSalonMidnight_ReturnsSalonDate()
    {
        // 22:00 UTC 14 ноября — это уже 03:00 15 ноября в поясе +5.
        var now = new DateTimeOffset(2026, 11, 14, 22, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 11, 15), SalonClock.Today(PlusFive, now));
        Assert.Equal(new DateOnly(2026, 11, 14), SalonClock.Today(null, now));
    }

    [Fact]
    public void FormatTime_SalonZone_ReturnsSalonClock()
    {
        var instant = new DateTimeOffset(2026, 11, 15, 13, 30, 0, TimeSpan.Zero);

        Assert.Equal("18:30", SalonClock.FormatTime(instant, PlusFive));
    }

    [Fact]
    public void FormatTime_UnknownZone_FallsBackToUtc()
    {
        var instant = new DateTimeOffset(2026, 11, 15, 13, 30, 0, TimeSpan.Zero);

        Assert.Equal("13:30", SalonClock.FormatTime(instant, null));
    }

    [Fact]
    public void FormatTimeRange_SalonZone_UsesEnDash()
    {
        var start = new DateTimeOffset(2026, 11, 15, 13, 30, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 11, 15, 14, 15, 0, TimeSpan.Zero);

        Assert.Equal("18:30–19:15", SalonClock.FormatTimeRange(start, end, PlusFive));
    }

    [Fact]
    public void FormatDate_DateOnly_ReturnsRussianWeekday()
    {
        Assert.Equal("воскресенье, 15 ноября", SalonClock.FormatDate(new DateOnly(2026, 11, 15)));
    }
}
