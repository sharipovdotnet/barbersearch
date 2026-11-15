using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using BarberSearch.Mobile.Push;
using BarberSearch.Mobile.Theming;

namespace BarberSearch.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// Находится ли приложение на экране.
    /// </summary>
    /// <remarks>
    /// Нужно службе FCM: в фоне уведомление показывает система, и локальный показ дал бы
    /// дубль, а в переднем плане система молчит и напоминание нужно показать самим.
    /// Флаг ставят <see cref="OnResume"/> и <see cref="OnPause"/> — единственные места,
    /// где Android точно знает ответ.
    /// </remarks>
    internal static bool IsInForeground { get; private set; }

    /// <summary>
    /// Красит статус-бар и навигационную панель под сохранённую тему и разбирает нажатие
    /// на push-напоминание, с которого приложение запустилось.
    /// </summary>
    /// <remarks>
    /// Тема применяется в <c>App.CreateWindow</c>, но окна там ещё нет и <see cref="PlatformTheme"/>
    /// ничего не делает. Настоящее окно появляется здесь — после <c>base.OnCreate</c> панели
    /// можно покрасить. Здесь же extras холодного старта: Shell в этот момент может быть ещё
    /// не собран, поэтому переход ставится в очередь главного потока.
    /// </remarks>
    /// <param name="savedInstanceState">Состояние предыдущего запуска; не используется.</param>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        PlatformTheme.Apply(
            Microsoft.Maui.Controls.Application.Current?.RequestedTheme
                == Microsoft.Maui.ApplicationModel.AppTheme.Dark);

        // Extras холодного старта разбираются только при настоящем запуске: при восстановлении
        // процесса после смерти системы intent может быть тем же, и повторный переход увёл бы
        // человека на «Записи» из середины работы.
        if (savedInstanceState is null)
        {
            HandleNotificationTap(Intent);
        }
    }

    /// <summary>
    /// Разбирает нажатие на напоминание, когда приложение уже запущено (тёплый старт).
    /// </summary>
    /// <remarks>
    /// <c>LaunchMode.SingleTop</c> не пересоздаёт Activity: новое нажатие приходит сюда, а не
    /// в <see cref="OnCreate"/>. Extras переносятся в текущий intent, чтобы повторное
    /// открытие не потеряло их.
    /// </remarks>
    /// <param name="intent">Intent нового нажатия.</param>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);

        if (intent is not null)
        {
            Intent = intent;
        }

        HandleNotificationTap(intent);
    }

    /// <summary>
    /// Отмечает, что приложение на экране.
    /// </summary>
    protected override void OnResume()
    {
        base.OnResume();

        IsInForeground = true;
    }

    /// <summary>
    /// Отмечает, что приложение ушло с экрана.
    /// </summary>
    protected override void OnPause()
    {
        IsInForeground = false;

        base.OnPause();
    }

    /// <summary>
    /// Передаёт extras intent'а регистратору переходов и просит выполнить переход.
    /// </summary>
    private static void HandleNotificationTap(Intent? intent)
    {
        var router = IPlatformApplication.Current?.Services.GetService<NotificationTapRouter>();

        if (router is null)
        {
            return;
        }

        router.HandleIntent(Extras(intent));
        router.ScheduleFlush();
    }

    /// <summary>
    /// Собирает extras intent'а в обычный словарь.
    /// </summary>
    /// <remarks>
    /// FCM кладёт значения строками, но <see cref="Bundle"/> не гарантирует тип: приводим
    /// через <c>ToString()</c>, чтобы разбор payload'а не зависел от того, чем именно
    /// отправитель представил <c>bookingId</c>.
    /// </remarks>
    private static Dictionary<string, string>? Extras(Intent? intent)
    {
        var bundle = intent?.Extras;

        if (bundle is null)
        {
            return null;
        }

        var extras = new Dictionary<string, string>(StringComparer.Ordinal);
        var keys = bundle.KeySet();

        if (keys is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (key is null)
            {
                continue;
            }

            var value = bundle.Get(key);

            if (value is not null)
            {
                extras[key] = value.ToString() ?? string.Empty;
            }
        }

        return extras.Count == 0 ? null : extras;
    }
}
