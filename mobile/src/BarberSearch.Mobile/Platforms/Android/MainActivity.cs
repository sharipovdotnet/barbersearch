using Android.App;
using Android.Content.PM;
using Android.OS;
using BarberSearch.Mobile.Theming;

namespace BarberSearch.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// Красит статус-бар и навигационную панель под сохранённую тему.
    /// </summary>
    /// <remarks>
    /// Тема применяется в <c>App.CreateWindow</c>, но окна там ещё нет и <see cref="PlatformTheme"/>
    /// ничего не делает. Настоящее окно появляется здесь — после <c>base.OnCreate</c> панели
    /// можно покрасить.
    /// </remarks>
    /// <param name="savedInstanceState">Состояние предыдущего запуска; не используется.</param>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        PlatformTheme.Apply(
            Microsoft.Maui.Controls.Application.Current?.RequestedTheme
                == Microsoft.Maui.ApplicationModel.AppTheme.Dark);
    }
}
