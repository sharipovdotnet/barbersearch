using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки локального фильтра справочника услуг: подстрока названия, регистр и «ё» как «е».
/// </summary>
/// <remarks>
/// Поиск по справочнику — это подстрока названия без учёта регистра, а не отдельный запрос к
/// серверу. «ё» и «е» считаются одной буквой в обе стороны: в справочнике стоит «бритьё», а
/// набирают «бритье», и наоборот. Строка из одних пробелов поиском не считается.
/// </remarks>
public sealed class BarberServiceCatalogFilterTests
{
    [Fact]
    public void Matches_QueryIsSubstringOfServiceName_ReturnsTrue()
    {
        Assert.True(BarberServiceCatalogFilter.Matches("Окантовка и бритьё шеи", "брить"));
        Assert.True(BarberServiceCatalogFilter.Matches("Окантовка и бритьё шеи", "и бритьё"));
    }

    [Fact]
    public void Matches_QueryDiffersInCase_ReturnsTrue()
    {
        Assert.True(BarberServiceCatalogFilter.Matches("Окантовка и бритьё шеи", "БРИТЬЁ"));
        Assert.True(BarberServiceCatalogFilter.Matches("Классика", "класс"));
    }

    [Fact]
    public void Matches_YoAndYe_AreTheSameLetterInBothDirections()
    {
        // Набирают «бритье», в справочнике — «бритьё».
        Assert.True(BarberServiceCatalogFilter.Matches("Окантовка и бритьё шеи", "бритье"));

        // И наоборот: «ё» в запросе находит название без «ё».
        Assert.True(BarberServiceCatalogFilter.Matches("Окантовка и бритье шеи", "бритьё"));
    }

    [Fact]
    public void Matches_WhitespaceOnlyQuery_MatchesEverything()
    {
        Assert.True(BarberServiceCatalogFilter.IsEmpty("   "));
        Assert.True(BarberServiceCatalogFilter.Matches("Фейд", "   "));
    }

    [Fact]
    public void Matches_QueryIsNotInName_ReturnsFalse()
    {
        Assert.False(BarberServiceCatalogFilter.Matches("Фейд", "борода"));
        Assert.False(BarberServiceCatalogFilter.Matches("Укладка", "стрижка"));
    }
}
