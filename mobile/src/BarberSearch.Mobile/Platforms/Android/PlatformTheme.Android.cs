using Android.App;
using AndroidX.Core.View;
using Microsoft.Maui.ApplicationModel;

namespace BarberSearch.Mobile.Theming;

/// <summary>
/// Статус-бар и навигационная панель Android в цвете текущей темы.
/// </summary>
/// <remarks>
/// <para>
/// Цвета — те же токены <c>Background</c> и <c>BackgroundDark</c> из <c>Colors.xaml</c>,
/// но литералами: ресурсы Android и словарь MAUI друг друга не видят, как и в
/// <c>Platforms/Android/Resources/values/colors.xml</c>.
/// </para>
/// <para>
/// Начиная с Android 15 окно рисуется edge-to-edge, а <c>SetStatusBarColor</c> система
/// игнорирует: полосу статус-бара и её значки она рисует сама, и подменить цвет без
/// перевода приложения на собственный night mode нельзя. Поэтому на 15+ вызов
/// пропускается целиком: переключить одни значки значило бы получить тёмные значки на
/// тёмной полосе. На 24–34 цвет полос и значков выставляется как раньше.
/// </para>
/// <para>
/// Заодно переключается цвет значков: на светлом фоне они тёмные, на тёмном — светлые.
/// Без этого приложение выглядело бы сломанным на границе с системой.
/// </para>
/// </remarks>
public static partial class PlatformTheme
{
    private static readonly Android.Graphics.Color LightBar = Android.Graphics.Color.Rgb(255, 255, 255);

    private static readonly Android.Graphics.Color DarkBar = Android.Graphics.Color.Rgb(10, 10, 10);

    private static partial void ApplyPlatform(bool isDark)
    {
        // На 35+ полоса статус-бара принадлежит системе: не трогаем ни цвет, ни значки.
        if (OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            return;
        }

        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is not Activity activity
            || activity.Window is not { } window
            || window.DecorView is not { } decorView)
        {
            return;
        }

        var barColor = isDark ? DarkBar : LightBar;

        window.SetStatusBarColor(barColor);
        window.SetNavigationBarColor(barColor);

        var controller = WindowCompat.GetInsetsController(window, decorView);

        if (controller is not null)
        {
            controller.AppearanceLightStatusBars = !isDark;
            controller.AppearanceLightNavigationBars = !isDark;
        }
    }
}
