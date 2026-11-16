using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки новых текстов карточки мастера: стаж, языки, оплата, инициалы, портфолио и диапазон.
/// </summary>
/// <remarks>
/// Строки сверяются дословно с вебом (<c>frontend/src/lib/barber-grade.ts</c>,
/// <c>frontend/src/components/barber/*</c>): одна копия текста на два клиента. Русские формы
/// зависят от числа, поэтому стаж проверяется на границах плюрала, а не на одном значении.
/// </remarks>
public sealed class BarberTextTests
{
    [Theory]
    [InlineData(0, "0 лет")]
    [InlineData(1, "1 год")]
    [InlineData(2, "2 года")]
    [InlineData(5, "5 лет")]
    [InlineData(10, "10 лет")]
    [InlineData(11, "11 лет")]
    [InlineData(21, "21 год")]
    [InlineData(22, "22 года")]
    [InlineData(25, "25 лет")]
    public void Experience_VariousYears_ReturnsRussianPluralForm(int years, string expected)
    {
        // 11–14 всегда «лет», дальше форма определяется последней цифрой: 21 — «год»,
        // 22 — «года», 25 — «лет». Ошибка здесь видна сразу в шапке карточки.
        Assert.Equal(expected, BarberText.Experience(years));
    }

    [Theory]
    [InlineData(Language.Ru, "Русский")]
    [InlineData(Language.En, "English")]
    [InlineData(Language.De, "Deutsch")]
    [InlineData(Language.Kg, "Кыргызча")]
    [InlineData(Language.Tj, "Тоҷикӣ")]
    [InlineData(Language.Uz, "Oʻzbekcha")]
    public void LanguageText_KnownLanguage_ReturnsSelfName(Language language, string expected)
    {
        // Самоназвание, а не перевод: так язык подписан в вебе и так его узнаёт носитель.
        Assert.Equal(expected, BarberText.LanguageText(language));
    }

    [Theory]
    [InlineData(PaymentMethod.Cash, "Наличные")]
    [InlineData(PaymentMethod.Card, "Банковская карта")]
    [InlineData(PaymentMethod.DigitalWallet, "Цифровой кошелёк")]
    [InlineData(PaymentMethod.Sbp, "СБП")]
    [InlineData(PaymentMethod.BankTransfer, "Перевод")]
    public void PaymentText_KnownMethod_ReturnsRussianCaption(PaymentMethod method, string expected)
    {
        Assert.Equal(expected, BarberText.PaymentText(method));
    }

    [Theory]
    [InlineData("Тимур", "Т")]
    [InlineData("Тимур Мастеров", "ТМ")]
    [InlineData("Тимур Ахмедович Мастеров", "ТА")]
    public void Initials_NameWithWords_TakesFirstLettersOfFirstTwo(string displayName, string expected)
    {
        // Инициалы — заглушка аватара: двух букв достаточно, третье слово отбрасывается.
        Assert.Equal(expected, BarberText.Initials(displayName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Initials_EmptyName_ReturnsQuestionMark(string displayName)
    {
        // Пустое имя без буквы выглядело бы поломкой аватара, поэтому «?», как в вебе.
        Assert.Equal("?", BarberText.Initials(displayName));
    }

    [Fact]
    public void PhotoAlt_DescriptionPresent_ReturnsTrimmedDescription()
    {
        // Подпись владельца важнее шаблона: она описывает кадр, а не его место в галерее.
        Assert.Equal(
            "Текстурный затылок",
            BarberText.PhotoAlt("Тимур", 3, "  Текстурный затылок  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PhotoAlt_NoDescription_BuildsTemplateWithNameAndNumber(string? description)
    {
        Assert.Equal(
            "Работа мастера Тимур, фото 3",
            BarberText.PhotoAlt("Тимур", 3, description));
    }

    [Fact]
    public void PhotoOpenHint_Caption_ReturnsOpenPhrase() =>
        Assert.Equal("Открыть фото: Текстурный затылок", BarberText.PhotoOpenHint("Текстурный затылок"));

    [Fact]
    public void PhotoCounter_ThirdOfTwelve_ShowsNumberAndTotal() =>
        Assert.Equal("3 из 12", BarberText.PhotoCounter(3, 12));

    [Fact]
    public void PageRange_FirstFullPage_ShowsFirstTenOfTwelve()
    {
        // Смещение нулевое, а показывает диапазон с единицы и среднее тире, как в макете.
        Assert.Equal("1–10 из 12", BarberText.PageRange(offset: 0, count: 10, total: 12));
    }

    [Fact]
    public void PageRange_LastPartialPage_ShowsTailOfTwelve()
    {
        // Общее число берётся из профиля: последняя страница неполная, и длина страницы
        // не должна подменять total.
        Assert.Equal("11–12 из 12", BarberText.PageRange(offset: 10, count: 2, total: 12));
    }

    [Fact]
    public void PageRange_EmptyPage_ShowsZeroOfTwelve()
    {
        // Пустая страница — «0 из 12», как formatPageRange веба, а не «1–0 из 12».
        Assert.Equal("0 из 12", BarberText.PageRange(offset: 0, count: 0, total: 12));
    }

    [Theory]
    [InlineData(1, "Показан 1 мастер")]
    [InlineData(2, "Показаны 2 мастера")]
    [InlineData(4, "Показаны 4 мастера")]
    [InlineData(5, "Показаны 5 мастеров")]
    [InlineData(11, "Показаны 11 мастеров")]
    [InlineData(21, "Показан 21 мастер")]
    [InlineData(22, "Показаны 22 мастера")]
    public void ShownBarbers_VariousCounts_ReturnsWebPluralForm(int count, string expected)
    {
        // Формы повторяют плюрал shownBarbers веба: 11–14 всегда «мастеров», дальше —
        // по последней цифре. Счётчик стоит над выдачей, поэтому ошибка видна сразу.
        Assert.Equal(expected, BarberText.ShownBarbers(count));
    }

    [Fact]
    public void Verified_Constant_ReturnsRussianCaption() =>
        Assert.Equal("Проверен", BarberText.Verified);

    [Theory]
    [InlineData(0.0, "10 м")]
    [InlineData(0.05, "50 м")]
    [InlineData(0.854, "850 м")]
    [InlineData(0.995, "1,0 км")]
    [InlineData(1.0, "1,0 км")]
    [InlineData(1.24, "1,2 км")]
    [InlineData(9.96, "10,0 км")]
    [InlineData(12.4, "12 км")]
    public void Distance_Kilometers_ShowsMetersOrRoundedKilometers(double distanceKm, string expected)
    {
        // До километра — метры с округлением до 10 м (точность серверной формулы не выше),
        // до 10 км — один знак, дальше — целые километры, чтобы подпись не скакала.
        Assert.Equal(expected, BarberText.Distance(distanceKm));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Distance_UnknownOrInvalid_ReturnsEmpty(double? distanceKm)
    {
        // Нет координат — нет подписи: карточка не показывает «NaN км» и не занимает строку.
        Assert.Equal(string.Empty, BarberText.Distance(distanceKm));
    }
}
