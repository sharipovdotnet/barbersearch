using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Navigation;

/// <summary>
/// Полные названия вкладок для подсказки, которую Android показывает по долгому нажатию.
/// </summary>
/// <remarks>
/// <para>
/// В полосе вкладок подписи остаются короткими («Поиск», «Карта», «Записи», «Профиль»):
/// четыре длинных названия на узком экране пришлось бы обрезать. Долгое нажатие — единственное
/// место, где название раздела помещается целиком, поэтому подсказка задаётся отдельно от
/// <c>ShellContent.Title</c>.
/// </para>
/// <para>
/// Ключ подсказки — маршрут содержимого из <c>AppShell.xaml</c>: после смены языка Shell
/// пересобирается, и вкладки уже другие экземпляры, а маршруты остаются прежними.
/// </para>
/// </remarks>
internal static class TabHints
{
    /// <summary>
    /// Подсказка вкладки по маршруту её содержимого.
    /// </summary>
    /// <param name="route">Маршрут <c>ShellContent</c> из <c>AppShell</c>.</param>
    /// <returns>Текст подсказки; пустая строка — у вкладки нет подсказки.</returns>
    public static string For(string? route) => route switch
    {
        "search" => Loc.T("Поиск мастера"),
        "map" => Loc.T("Карта с мастерами"),
        "bookings" => Loc.T("Мои записи"),
        "profile" => Loc.T("Мой профиль"),
        _ => string.Empty,
    };
}
