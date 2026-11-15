using BarberSearch.Mobile.Core.Theming;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Тема оформления: коды хранения, разбор сохранённого значения и хранилище в памяти.
/// </summary>
/// <remarks>
/// Коды — часть контракта с хранилищем: тест фиксирует их дословно, иначе переименование
/// члена перечисления молча стёрло бы выбор пользователя после обновления приложения.
/// </remarks>
public sealed class ThemingTests
{
    [Theory]
    [InlineData(ThemePreference.System, "system")]
    [InlineData(ThemePreference.Light, "light")]
    [InlineData(ThemePreference.Dark, "dark")]
    public void Code_KnownPreference_ReturnsStableStorageCode(ThemePreference preference, string expected)
    {
        Assert.Equal(expected, ThemePreferences.Code(preference));
    }

    [Theory]
    [InlineData("system", ThemePreference.System)]
    [InlineData("light", ThemePreference.Light)]
    [InlineData("dark", ThemePreference.Dark)]
    [InlineData("SYSTEM", ThemePreference.System)]
    [InlineData("LIGHT", ThemePreference.Light)]
    [InlineData("DARK", ThemePreference.Dark)]
    public void TryParse_KnownCodeInAnyCase_ReturnsTrue(string value, ThemePreference expected)
    {
        // Регистр не должен влиять: значения мог записать старый клиент или ручная правка.
        Assert.True(ThemePreferences.TryParse(value, out var preference));
        Assert.Equal(expected, preference);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sepia")]
    public void TryParse_UnknownOrMissingCode_ReturnsFalseAndSystem(string? value)
    {
        // Неизвестное значение означает «выбора нет», а не «светлая тема»: иначе пропала бы
        // системная тема ОС, которую пользователь как раз и не переопределял.
        Assert.False(ThemePreferences.TryParse(value, out var preference));
        Assert.Equal(ThemePreference.System, preference);
    }

    [Fact]
    public void Default_NoSavedChoice_IsSystem()
    {
        // И константа, и свежее хранилище обязаны отвечать одинаково: тема ОС по умолчанию.
        Assert.Equal(ThemePreference.System, ThemePreferences.Default);
        Assert.Equal(ThemePreference.System, new InMemoryThemeStore().Read());
    }

    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void InMemoryThemeStore_Write_ReadsBackTheSamePreference(ThemePreference preference)
    {
        var store = new InMemoryThemeStore();

        store.Write(preference);

        Assert.Equal(preference, store.Read());
    }
}
