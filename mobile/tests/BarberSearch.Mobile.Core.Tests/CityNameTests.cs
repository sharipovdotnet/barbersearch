using BarberSearch.Mobile.Core.Location;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки приведения названия города от геокодера к виду, которым фильтрует сервер.
/// </summary>
/// <remarks>
/// Сервер сравнивает <c>Salon.City</c> точно, поэтому «г. Москва» из обратного геокодирования
/// не нашло бы ни одного салона: приставки, которые добавляют платформенные геокодеры,
/// снимаются здесь, а не на сервере.
/// </remarks>
public sealed class CityNameTests
{
    [Fact]
    public void Normalize_NoValue_ReturnsNull()
    {
        Assert.Null(CityName.Normalize(null));
        Assert.Null(CityName.Normalize(string.Empty));
        Assert.Null(CityName.Normalize("   "));
    }

    [Fact]
    public void Normalize_PlainCity_KeepsName()
    {
        Assert.Equal("Москва", CityName.Normalize("Москва"));
        Assert.Equal("Москва", CityName.Normalize("  Москва  "));
    }

    [Theory]
    [InlineData("г. Москва")]
    [InlineData("Г. Москва")]
    [InlineData("ГОРОД Москва")]
    [InlineData("город Москва")]
    [InlineData("гор. Москва")]
    [InlineData("  г. Москва  ")]
    [InlineData("г.Москва")]
    public void Normalize_PrefixedCity_StripsPrefix(string value)
    {
        Assert.Equal("Москва", CityName.Normalize(value));
    }

    [Theory]
    [InlineData("Гатчина")]
    [InlineData("Грозный")]
    public void Normalize_NameStartingWithG_KeepsName(string value)
    {
        // Однобуквенное «г» без точки — не приставка: отличить её от названия было бы нельзя.
        Assert.Equal(value, CityName.Normalize(value));
    }

    [Theory]
    [InlineData("г.")]
    [InlineData("гор.")]
    [InlineData("  г.  ")]
    public void Normalize_PrefixOnly_ReturnsNull(string value)
    {
        Assert.Null(CityName.Normalize(value));
    }
}
