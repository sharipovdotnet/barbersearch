namespace BarberSearch.Mobile.Theming;

/// <summary>
/// Подсвечивает выбранный сегмент в переключателях «Профиля»: языке и теме.
/// </summary>
/// <remarks>
/// Стили <c>SegmentButton</c> и <c>SegmentButtonSelected</c> объявлены в
/// <c>Resources/Styles/Styles.xaml</c> и красят контрол через <c>AppThemeBinding</c>.
/// Поэтому при смене темы сегмент перекрашивается сам: прямые <c>BackgroundColor</c> и
/// <c>TextColor</c> из code-behind, как было у языкового переключателя, запомнили бы цвет
/// на момент клика и остались светлыми в тёмной теме.
/// </remarks>
internal static class SegmentedButtonStyle
{
    private const string SelectedKey = "SegmentButtonSelected";
    private const string UnselectedKey = "SegmentButton";

    /// <summary>
    /// Выбирает стиль сегмента по его состоянию.
    /// </summary>
    /// <param name="button">Кнопка сегмента.</param>
    /// <param name="isSelected"><c>true</c> — выбранный сегмент.</param>
    public static void Apply(Button button, bool isSelected)
    {
        ArgumentNullException.ThrowIfNull(button);

        var key = isSelected ? SelectedKey : UnselectedKey;

        if (Application.Current?.Resources.TryGetValue(key, out var value) == true
            && value is Style style)
        {
            button.Style = style;
        }
    }
}
