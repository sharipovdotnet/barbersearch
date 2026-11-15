namespace BarberSearch.Mobile.Theming;

/// <summary>
/// Красит платформенную оболочку под текущую тему.
/// </summary>
/// <remarks>
/// <para>
/// <c>AppThemeBinding</c> меняет только контролы MAUI; статус-бар и навигационная панель
/// Android рисуются системой и о теме приложения не знают. Общая часть здесь одна, а
/// платформенная реализация — в <c>Platforms/Android</c>: на iOS цвета системных панелей
/// следуют за темой контроллера автоматически, поэтому там переопределять нечего.
/// </para>
/// <para>
/// Вызов до появления окна (например, из конструктора <c>App</c>) безопасен: без активного
/// окна метод ничего не делает, а при старте панели красит <c>MainActivity</c>.
/// </para>
/// </remarks>
public static partial class PlatformTheme
{
    /// <summary>
    /// Применяет тему к системным панелям.
    /// </summary>
    /// <param name="isDark"><c>true</c> — тёмная тема, <c>false</c> — светлая.</param>
    public static void Apply(bool isDark) => ApplyPlatform(isDark);

    private static partial void ApplyPlatform(bool isDark);
}
