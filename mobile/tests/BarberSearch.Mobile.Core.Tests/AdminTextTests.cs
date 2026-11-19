using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CoreLocalization = BarberSearch.Mobile.Core.Localization.Localization;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки текстов администрирования: бейдж, ожидание в очереди, самый старый отзыв
/// и время снимка сводки.
/// </summary>
/// <remarks>
/// <para>
/// Строки сверяются дословно: это одна копия текста с вебом (<c>formatAdminBadgeCount</c>,
/// <c>formatWaitingSince</c>), и расхождение здесь — не опечатка, а разные слова одного
/// продукта.
/// </para>
/// <para>
/// «Сейчас» передаётся параметром везде, где это возможно: тест не ждёт часов. Время снимка
/// форматируется в поясе устройства, поэтому момент строится с <see cref="DateTimeKind.Local"/>,
/// и ожидаемая строка не зависит от пояса машины.
/// </para>
/// </remarks>
public sealed class AdminTextTests
{
    /// <summary>
    /// Опорный момент: 20 ноября 2026, 12:00 UTC.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    public void BadgeCount_EveryRange_CapsAbove99(int count, string expected) =>
        Assert.Equal(expected, AdminText.BadgeCount(count));

    [Fact]
    public void WaitingSince_UnderAMinute_ShowsJustNow() =>
        // Ожидание меньше минуты словами не измеряется: «ждёт 0 минут» не читается.
        Assert.Equal("только что", AdminText.WaitingSince(Now.AddSeconds(-30), Now));

    [Theory]
    [InlineData(-5, "только что")]
    [InlineData(1, "ждёт 1 минуту")]
    [InlineData(3, "ждёт 3 минуты")]
    [InlineData(5, "ждёт 5 минут")]
    [InlineData(59, "ждёт 59 минут")]
    [InlineData(60, "ждёт 1 час")]
    [InlineData(120, "ждёт 2 часа")]
    [InlineData(300, "ждёт 5 часов")]
    [InlineData(1380, "ждёт 23 часа")]
    [InlineData(1440, "ждёт 1 день")]
    [InlineData(4320, "ждёт 3 дня")]
    [InlineData(7200, "ждёт 5 дней")]
    [InlineData(15840, "ждёт 11 дней")]
    [InlineData(30240, "ждёт 21 день")]
    public void WaitingSince_Russian_UsesPluralForms(int minutes, string expected)
    {
        // Отрицательная разница — момент из будущего: часы сервера и телефона расходятся,
        // и «-5 минут» в очереди читались бы как поломка, поэтому ожидание прижимается к нулю.
        var awaitingSince = Now.AddMinutes(-minutes);

        Assert.Equal(expected, AdminText.WaitingSince(awaitingSince, Now));
    }

    [Theory]
    [InlineData(-5, "Самый старый — только что")]
    [InlineData(1, "Самый старый ждёт 1 минуту")]
    [InlineData(1440, "Самый старый ждёт 1 день")]
    [InlineData(4320, "Самый старый ждёт 3 дня")]
    public void OldestReview_Russian_ChoosesJustNowOrWaiting(int minutes, string expected)
    {
        // «Самый старый ждёт только что» не читается, поэтому у только что попавшего
        // отзыва слово «ждёт» не ставится.
        var awaitingSince = Now.AddMinutes(-minutes);

        Assert.Equal(expected, AdminText.OldestReview(awaitingSince, Now));
    }

    [Fact]
    public void WaitingSince_English_UsesEnglishForms()
    {
        using var _ = UseLocale(Locale.En);

        Assert.Equal("just now", AdminText.WaitingSince(Now.AddSeconds(-30), Now));
        Assert.Equal("waiting for 1 minute", AdminText.WaitingSince(Now.AddMinutes(-1), Now));
        Assert.Equal("waiting for 3 days", AdminText.WaitingSince(Now.AddDays(-3), Now));
    }

    [Fact]
    public void OldestReview_English_UsesEnglishForms()
    {
        using var _ = UseLocale(Locale.En);

        Assert.Equal("Oldest — just now", AdminText.OldestReview(Now.AddSeconds(-30), Now));
        Assert.Equal("Oldest waiting for 3 days", AdminText.OldestReview(Now.AddDays(-3), Now));
    }

    [Fact]
    public void UpdatedAt_Moment_ShowsLocalTimeWithMinutes() =>
        Assert.Equal("Обновлено в 14:32", AdminText.UpdatedAt(LocalMoment(14, 32)));

    [Fact]
    public void UpdatedAt_English_TranslatesPrefix()
    {
        using var _ = UseLocale(Locale.En);

        Assert.Equal("Updated at 14:32", AdminText.UpdatedAt(LocalMoment(14, 32)));
    }

    /// <summary>
    /// Момент в поясе устройства: <see cref="DateTimeOffset.ToLocalTime"/> вернёт те же
    /// часы и минуты, в каком бы поясе ни шёл тест.
    /// </summary>
    /// <param name="hour">Час местного времени.</param>
    /// <param name="minute">Минута местного времени.</param>
    /// <returns>Момент с местным смещением.</returns>
    private static DateTimeOffset LocalMoment(int hour, int minute) =>
        new(new DateTime(2026, 11, 20, hour, minute, 0, DateTimeKind.Local));

    /// <summary>
    /// Включает язык на время теста и возвращает прежний: <see cref="Loc"/> — модульный.
    /// </summary>
    /// <param name="locale">Язык теста.</param>
    /// <returns>Восстановление прежнего языка.</returns>
    private static RestoreLocale UseLocale(Locale locale)
    {
        var previous = Loc.Current;

        var localization = new CoreLocalization(
            new InMemoryLocaleStore(locale),
            initialLocale: locale);

        localization.Initialize();
        Loc.Current = localization;

        return new RestoreLocale(previous);
    }

    /// <summary>
    /// Возвращает прежний модульный перевод после теста.
    /// </summary>
    /// <param name="previous">Сервис перевода, который был до теста.</param>
    private sealed class RestoreLocale(ILocalization previous) : IDisposable
    {
        public void Dispose() => Loc.Current = previous;
    }
}
