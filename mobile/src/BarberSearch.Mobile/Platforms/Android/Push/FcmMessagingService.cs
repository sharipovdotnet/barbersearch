using Android.App;
using Android.Content;
using BarberSearch.Mobile.Core.Push;
using Firebase.Messaging;

namespace BarberSearch.Mobile.Push;

/// <summary>
/// Служба FCM: новый токен устройства и входящее сообщение.
/// </summary>
/// <remarks>
/// <para>
/// <c>[Service(Exported = false)]</c> и фильтр <c>MESSAGING_EVENT</c> — то, что ожидает FCM:
/// служба не экспортируется наружу, а системный брокер находит её по действию. Запись в
/// манифесте генерируется атрибутами, руками её поддерживать не нужно.
/// </para>
/// <para>
/// Уведомление в фоне показывает сама система (сообщение содержит <c>notification</c>), и
/// <c>OnMessageReceived</c> для него не вызывается. В переднем плане система молчит, поэтому
/// напоминание показывается локально через <see cref="AndroidNotifications"/> — иначе
/// человек, смотрящий в приложение, не увидел бы напоминание вовсе.
/// </para>
/// </remarks>
[Service(Exported = false)]
[IntentFilter(new[] { MessagingEventAction })]
public sealed class FcmMessagingService : FirebaseMessagingService
{
    /// <summary>
    /// Действие, по которому Android передаёт сообщения этой службе.
    /// </summary>
    public const string MessagingEventAction = "com.google.firebase.MESSAGING_EVENT";

    /// <inheritdoc />
    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);

        var services = IPlatformApplication.Current?.Services;

        if (services is null)
        {
            return;
        }

        // Регистратор создаётся до события: если процесс поднят самой службой FCM, до этого
        // момента никто не подписан на TokenRefreshed, и обновлённый токен молча потерялся бы.
        // Регистратор сам проверит сессию и разрешение и не бросает исключений.
        services.GetService<IPushRegistrar>();
        services.GetService<FcmPushTokenProvider>()?.NotifyTokenRefreshed(token);
    }

    /// <inheritdoc />
    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        if (message is null || !MainActivity.IsInForeground)
        {
            return;
        }

        var notification = message.GetNotification();
        var title = notification?.Title;

        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        AndroidNotifications.Show(this, title, notification?.Body ?? string.Empty, message.Data);
    }
}
