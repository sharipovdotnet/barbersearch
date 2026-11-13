using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.Common;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Чистая сетка слотов <see cref="SlotGrid"/>: границы рабочего дня, шаг, отпуска и переход
/// на летнее время.
/// </summary>
/// <remarks>
/// Функция не знает ни «сейчас», ни броней: прошедшие и занятые слоты отсекает вызывающий.
/// Здесь проверяется только то, что сетка обязана дать одинаково и публичным слотам,
/// и чтению слотов переноса визита.
/// </remarks>
public class SlotGridTests
{
    private static readonly DateOnly Date = new(2026, 12, 1);

    [Fact]
    public void Legal_BuildsGridFromWorkingHoursWithTheGivenStep()
    {
        var slots = SlotGrid.Legal(
            Date,
            "Europe/Berlin",
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            30,
            []);

        Assert.Equal(18, slots.Count);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero), slots[0].StartAt);
        Assert.Equal(slots[0].StartAt.AddMinutes(30), slots[0].EndAt);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 17, 0, 0, TimeSpan.Zero), slots[^1].EndAt);

        // Сетка не выходит за конец дня: последний старт — ровно «конец минус шаг».
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 16, 30, 0, TimeSpan.Zero), slots[^1].StartAt);
    }

    [Fact]
    public void Legal_WithUnknownTimeZone_ReturnsNoSlots()
    {
        var slots = SlotGrid.Legal(Date, "Not/AZone", new TimeOnly(9, 0), new TimeOnly(18, 0), 30, []);

        Assert.Empty(slots);
    }

    [Fact]
    public void Legal_WithNonPositiveDuration_ReturnsNoSlots()
    {
        var slots = SlotGrid.Legal(Date, "Europe/Berlin", new TimeOnly(9, 0), new TimeOnly(18, 0), 0, []);

        Assert.Empty(slots);
    }

    [Fact]
    public void Legal_SkipsTheDstGapAndNeverRepeatsAnInstant()
    {
        // 29 марта 2026 в Берлине 02:00–03:00 не существует. Локальное 02:00 нельзя перевести
        // в UTC, поэтому пропускается и слот, который в него заканчивается (01:00–02:00),
        // и слот, который в нём начинается. Дыра не сдвигает сетку и не даёт дублей.
        var slots = SlotGrid.Legal(
            new DateOnly(2026, 3, 29),
            "Europe/Berlin",
            new TimeOnly(0, 0),
            new TimeOnly(6, 0),
            60,
            []);

        Assert.Equal(
            new[]
            {
                new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 29, 2, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.Zero)
            },
            slots.Select(slot => slot.StartAt));
        Assert.Equal(slots.Count, slots.Select(slot => slot.StartAt).Distinct().Count());
        Assert.All(slots, slot => Assert.Equal(slot.StartAt.AddMinutes(60), slot.EndAt));
    }

    [Fact]
    public void Legal_ExcludesTimeOffAndKeepsTouchingSlots()
    {
        var timeOffs = new[]
        {
            new TimeOffInterval(
                new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 1, 14, 0, 0, TimeSpan.Zero))
        };

        var slots = SlotGrid.Legal(
            Date,
            "Europe/Berlin",
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            30,
            timeOffs);

        Assert.DoesNotContain(
            slots,
            slot => slot.StartAt < timeOffs[0].EndAt && timeOffs[0].StartAt < slot.EndAt);

        // Касание границы отпуска слотом его не занимает: 11:30–12:00 и 14:00–14:30 легальны.
        Assert.Contains(
            slots,
            slot => slot.StartAt == new DateTimeOffset(2026, 12, 1, 11, 30, 0, TimeSpan.Zero));
        Assert.Contains(
            slots,
            slot => slot.StartAt == new DateTimeOffset(2026, 12, 1, 14, 0, 0, TimeSpan.Zero));
        Assert.Equal(14, slots.Count);
    }
}
