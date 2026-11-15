namespace BarberSearch.Mobile.Theming;

/// <summary>
/// Платформенная часть темы для iOS: переопределять нечего.
/// </summary>
/// <remarks>
/// На iOS цвет системных панелей следует за темой контроллера, поэтому общая часть
/// <see cref="PlatformTheme.Apply"/> остаётся пустой. Файл нужен как реализация разделяемого
/// метода с модификатором доступа: без неё iOS-цель не компилируется (CS8795), даже когда
/// платформенного кода нет.
/// </remarks>
public static partial class PlatformTheme
{
    private static partial void ApplyPlatform(bool isDark)
    {
        // Параметр остаётся в сигнатуре общей части: на iOS он не используется.
        _ = isDark;
    }
}
