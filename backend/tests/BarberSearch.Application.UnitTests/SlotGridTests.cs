using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.Common;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Чистая сетка слотов <see cref="SlotGrid"/>: границы рабочего дня, шаг, свободные окна
/// после отпусков и занятости и переход на летнее время.
/// </summary>
/// <remarks>
/// Функция не знает ни «сейчас», ни чужих сценариев: прошедшие слоты отсекает вызывающий.
/// Здесь проверяется только то, что сетка обязана дать одинаково и публичным слотам,
/// и чтению слотов переноса визита, и ручной записи барбера.
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
            [],
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
        var slots = SlotGrid.Legal(
            Date,
            "Not/AZone",
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            30,
            [],
            []);

        Assert.Empty(slots);
    }

    [Fact]
    public void Legal_WithNonPositiveDuration_ReturnsNoSlots()
    {
        var slots = SlotGrid.Legal(
            Date,
            "Europe/Berlin",
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            0,
            [],
            []);

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
            [],
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
            timeOffs,
            []);

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

    /// <summary>
    /// Занятая бронь делит день на два свободных окна: следующая часовая услуга начинается
    /// ровно в конце брони (12:30), а не на «круглом» часе от открытия, и последний старт
    /// примыкает к закрытию (17:00), хотя шаг от 12:30 туда не попадает.
    /// </summary>
    /// <remarks>
    /// Время в тесте задано в UTC, а работа — в Берлине: 11:00–11:30Z — это 12:00–12:30
    /// местного времени зимой (CET, UTC+1).
    /// </remarks>
    [Fact]
    public void Legal_AfterBusyInterval_StartsNextSlotAtItsEnd()
    {
        var busyStart = new DateTimeOffset(2026, 12, 1, 11, 0, 0, TimeSpan.Zero);
        var busyEnd = new DateTimeOffset(2026, 12, 1, 11, 30, 0, TimeSpan.Zero);
        var busy = new[] { new BusyInterval(busyStart, busyEnd) };

        var slots = SlotGrid.Legal(
            Date,
            "Europe/Berlin",
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            60,
            [],
            busy);

        // 12:30 и 17:00 по Берлину.
        Assert.Contains(
            slots,
            slot => slot.StartAt == new DateTimeOffset(2026, 12, 1, 11, 30, 0, TimeSpan.Zero));
        Assert.Contains(
            slots,
            slot => slot.StartAt == new DateTimeOffset(2026, 12, 1, 16, 0, 0, TimeSpan.Zero));

        // Свободное окно до брони по-прежнему начинается с открытия — 10:00 по Берлину.
        Assert.Contains(
            slots,
            slot => slot.StartAt == new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
        Assert.DoesNotContain(slots, slot => slot.StartAt == busyStart);
        Assert.DoesNotContain(
            slots,
            slot => slot.StartAt < busyEnd && busyStart < slot.EndAt);
    }

    /// <summary>
    /// Свободное окно короче длительности не даёт ни одного слота: ставить визит, который
    /// не помещается между бронью и открытием, нельзя.
    /// </summary>
    [Fact]
    public void Legal_FreeWindowShorterThanDuration_ProducesNoSlots()
    {
        // 10:00–11:00Z — это 11:00–12:00 по Берлину; окно 10:00–11:00 местного короче
        // двухчасового визита, поэтому первый слот появляется только после брони.
        var busy = new[]
        {
            new BusyInterval(
                new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 1, 11, 0, 0, TimeSpan.Zero))
        };

        var slots = SlotGrid.Legal(
            Date,
            "Europe/Berlin",
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            120,
            [],
            busy);

        Assert.Equal(3, slots.Count);
        Assert.DoesNotContain(
            slots,
            slot => slot.StartAt < new DateTimeOffset(2026, 12, 1, 11, 0, 0, TimeSpan.Zero));
        Assert.Contains(
            slots,
            slot => slot.StartAt == new DateTimeOffset(2026, 12, 1, 11, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Пересекающиеся отпуск и занятость сливаются в одно препятствие: внутри объединённого
    /// интервала нет ни одного старта, даже если он свободен по отдельности.
    /// </summary>
    [Fact]
    public void Legal_MergesOverlappingTimeOffAndBusy()
    {
        // Отпуск 12:00–13:00 и бронь 12:30–13:30 по Берлину: итоговый запрет 12:00–13:30.
        var mergedStart = new DateTimeOffset(2026, 12, 1, 11, 0, 0, TimeSpan.Zero);
        var mergedEnd = new DateTimeOffset(2026, 12, 1, 12, 30, 0, TimeSpan.Zero);
        var timeOffs = new[]
        {
            new TimeOffInterval(
                mergedStart,
                new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero))
        };
        var busy = new[]
        {
            new BusyInterval(
                new DateTimeOffset(2026, 12, 1, 11, 30, 0, TimeSpan.Zero),
                mergedEnd)
        };

        var slots = SlotGrid.Legal(
            Date,
            "Europe/Berlin",
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            60,
            timeOffs,
            busy);

        Assert.DoesNotContain(
            slots,
            slot => slot.StartAt < mergedEnd && mergedStart < slot.EndAt);

        // 10:00 по Берлину — до запрета; 13:30 — сразу после объединённого интервала.
        Assert.Contains(
            slots,
            slot => slot.StartAt == new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
        Assert.Contains(
            slots,
            slot => slot.StartAt == new DateTimeOffset(2026, 12, 1, 12, 30, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Ручная запись не обязана совпадать с сеткой: 12:07 по Берлину помещается в рабочий
    /// день, поэтому <see cref="SlotGrid.Fit"/> принимает время и считает конец в поясе салона.
    /// </summary>
    [Fact]
    public void Fit_OffGridStartInsideFreeWindow_Fits()
    {
        var startAt = new DateTimeOffset(2026, 12, 1, 11, 7, 0, TimeSpan.Zero);

        var fit = SlotGrid.Fit(
            Date,
            "Europe/Berlin",
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            60,
            [],
            [],
            startAt,
            out var slot);

        Assert.Equal(SlotFit.Fits, fit);
        Assert.NotNull(slot);
        Assert.Equal(startAt, slot.StartAt);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 12, 7, 0, TimeSpan.Zero), slot.EndAt);
    }

    [Fact]
    public void Fit_InsideBusyInterval_ReturnsOverlap()
    {
        var busy = new[]
        {
            new BusyInterval(
                new DateTimeOffset(2026, 12, 1, 11, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 1, 11, 30, 0, TimeSpan.Zero))
        };

        var fit = SlotGrid.Fit(
            Date,
            "Europe/Berlin",
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            60,
            [],
            busy,
            new DateTimeOffset(2026, 12, 1, 11, 10, 0, TimeSpan.Zero),
            out var slot);

        Assert.Equal(SlotFit.Overlap, fit);
        Assert.Null(slot);
    }

    [Fact]
    public void Fit_OutsideWorkingHours_ReturnsOutsideSchedule()
    {
        var fit = SlotGrid.Fit(
            Date,
            "Europe/Berlin",
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            60,
            [],
            [],
            new DateTimeOffset(2026, 12, 1, 8, 30, 0, TimeSpan.Zero),
            out var slot);

        Assert.Equal(SlotFit.OutsideSchedule, fit);
        Assert.Null(slot);
    }

    [Fact]
    public void Fit_InsideTimeOff_ReturnsOutsideSchedule()
    {
        var timeOffs = new[]
        {
            new TimeOffInterval(
                new DateTimeOffset(2026, 12, 1, 11, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero))
        };

        var fit = SlotGrid.Fit(
            Date,
            "Europe/Berlin",
            new TimeOnly(10, 0),
            new TimeOnly(18, 0),
            30,
            timeOffs,
            [],
            new DateTimeOffset(2026, 12, 1, 11, 10, 0, TimeSpan.Zero),
            out var slot);

        Assert.Equal(SlotFit.OutsideSchedule, fit);
        Assert.Null(slot);
    }
}
