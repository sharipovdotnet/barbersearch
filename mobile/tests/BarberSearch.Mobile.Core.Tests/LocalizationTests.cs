using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Users;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CoreLocalization = BarberSearch.Mobile.Core.Localization.Localization;
using Xunit;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Перевод, плюрализация и культура показа.
/// </summary>
public sealed class LocalizationTests
{
    [Fact]
    public void T_English_TranslatesRussianSource()
    {
        using var _ = UseLocale(Locale.En);

        Assert.Equal("Search", Loc.T("Поиск"));
        Assert.Equal("No bookings yet", Loc.T("Пока ни одной записи"));
        Assert.Equal(
            "Booking for 15 November at 18:30 cancelled.",
            Loc.T("Запись на {0} отменена.", "15 November at 18:30"));
    }

    [Fact]
    public void T_Russian_ReturnsSource()
    {
        using var _ = UseLocale(Locale.Ru);

        Assert.Equal("Поиск", Loc.T("Поиск"));
        Assert.Equal(
            "Запись на 15 ноября отменена.",
            Loc.T("Запись на {0} отменена.", "15 ноября"));
    }

    [Fact]
    public void T_MissingTranslation_FallsBackToRussian()
    {
        using var _ = UseLocale(Locale.En);

        Assert.Equal("Строка без перевода", Loc.T("Строка без перевода"));
    }

    [Theory]
    [InlineData(1, "1 review")]
    [InlineData(2, "2 reviews")]
    [InlineData(5, "5 reviews")]
    [InlineData(11, "11 reviews")]
    public void Plural_English_UsesSingularAndPlural(int count, string expected)
    {
        using var _ = UseLocale(Locale.En);

        Assert.Equal(expected, Loc.Plural(PluralKey.Reviews, count));
    }

    [Theory]
    [InlineData(1, "1 отзыв")]
    [InlineData(2, "2 отзыва")]
    [InlineData(5, "5 отзывов")]
    [InlineData(11, "11 отзывов")]
    [InlineData(21, "21 отзыв")]
    public void Plural_Russian_UsesThreeForms(int count, string expected)
    {
        using var _ = UseLocale(Locale.Ru);

        Assert.Equal(expected, Loc.Plural(PluralKey.Reviews, count));
    }

    [Fact]
    public async Task SetLocale_PersistsChoiceAndRaisesEvent()
    {
        var store = new InMemoryLocaleStore();
        var localization = new CoreLocalization(store);
        localization.Initialize();

        var raised = 0;
        localization.LocaleChanged += (_, _) => raised++;

        await localization.SetLocaleAsync(Locale.En);

        Assert.Equal(Locale.En, localization.Locale);
        Assert.Equal(Locale.En, store.Read());
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Initialize_ReadsStoredLocale()
    {
        var store = new InMemoryLocaleStore(Locale.En);
        var localization = new CoreLocalization(store);

        localization.Initialize();

        Assert.Equal(Locale.En, localization.Locale);
        Assert.Equal("Search", localization.T("Поиск"));
    }

    [Fact]
    public void BarberText_English_UsesWebConventions()
    {
        using var _ = UseLocale(Locale.En);

        Assert.Equal("RUB 2,400", BarberText.Price(2400m));
        Assert.Equal("2 reviews", BarberText.Reviews(2));
        Assert.Equal("1 h 30 min", BarberText.Duration(90));
        Assert.Equal("8 years", BarberText.Experience(8));
        Assert.Equal("Showing 2 barbers", BarberText.ShownBarbers(2));
        Assert.Equal("Senior barber", BarberText.GradeText(Grade.SeniorBarber));
        Assert.Equal("4.8", BarberText.Rating(4.8m));
    }

    [Fact]
    public void BookingText_English_FormatsDatesAndCountdown()
    {
        using var _ = UseLocale(Locale.En);

        var moment = new DateTimeOffset(2026, 11, 15, 18, 30, 0, TimeSpan.Zero);

        Assert.Equal("Sun, 15 November at 18:30", BookingText.Moment(moment, null));
        Assert.Equal("Sun, 15 Nov", BookingText.ShortDate(DateOnly.FromDateTime(moment.UtcDateTime)));
        Assert.Equal("Awaiting confirmation", BookingText.Status(BookingStatus.Pending));
        Assert.Equal(
            "in 2 hours",
            BookingText.Countdown(moment, moment.AddHours(-2)));
    }

    [Fact]
    public void AccountText_English_FormatsRolesAndMoments()
    {
        using var _ = UseLocale(Locale.En);

        Assert.Equal("Administrator", AccountText.RoleText(UserRole.Admin));
        Assert.Equal("Not yet", AccountText.Never);
        Assert.Equal(
            "Sun, 15 November at 18:30",
            AccountText.Moment(
                new DateTimeOffset(2026, 11, 15, 18, 30, 0, TimeSpan.Zero),
                TimeZoneInfo.Utc));
    }

    [Fact]
    public void ReviewText_English_FormatsStatusAndDates()
    {
        using var _ = UseLocale(Locale.En);

        Assert.Equal("Published", ReviewText.Status(true));
        Assert.Equal("Pending review", ReviewText.Status(false));
        Assert.Equal(
            "15 November 2026",
            ReviewText.ReviewDate(
                new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero),
                TimeZoneInfo.Utc));
    }

    [Fact]
    public void SalonClock_English_FormatsCalendarDate()
    {
        using var _ = UseLocale(Locale.En);

        Assert.Equal("Sunday, 15 November", SalonClock.FormatDate(new DateOnly(2026, 11, 15)));
        Assert.Equal("15 November", SalonClock.FormatDayMonth(new DateOnly(2026, 11, 15)));
    }

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

    private sealed class RestoreLocale(ILocalization previous) : IDisposable
    {
        public void Dispose() => Loc.Current = previous;
    }
}
