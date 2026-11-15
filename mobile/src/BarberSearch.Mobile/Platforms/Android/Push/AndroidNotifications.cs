using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Push;

namespace BarberSearch.Mobile.Push;

/// <summary>
/// Локальные уведомления Android: канал <c>reminders</c> и показ напоминания.
/// </summary>
/// <remarks>
/// <para>
/// Идентификатор канала <c>reminders</c> — часть контракта с сервером: FCM-сообщение приходит
/// с <c>channel_id=reminders</c>, и без такого канала Android 8+ показал бы уведомление
/// в безымянном канале по умолчанию. Имя и описание канала видны в настройках телефона,
/// поэтому берутся из каталога перевода, а не пишутся здесь по-русски. Канал создаёт
/// <c>App</c> после инициализации языка и только когда push настроен.
/// </para>
/// <para>
/// Нажатие открывает <c>MainActivity</c> с <c>SINGLE_TOP|CLEAR_TOP</c>: приложение не
/// пересоздаётся, а extras <c>type</c>/<c>bookingId</c> доходят до того же
/// <see cref="NotificationTapRouter"/>, который обрабатывает холодный старт.
/// </para>
/// </remarks>
public static class AndroidNotifications
{
    /// <summary>
    /// Идентификатор канала; совпадает с <c>channel_id</c> в сообщении сервера.
    /// </summary>
    public const string ChannelId = "reminders";

    /// <summary>
    /// Создаёт или обновляет канал напоминаний.
    /// </summary>
    /// <remarks>
    /// До Android 8 каналов нет, и вызов не нужен. Повторное создание канала с тем же
    /// идентификатором безопасно: Android обновляет имя и описание, а настройки канала,
    /// выставленные человеком, сохраняет.
    /// </remarks>
    /// <param name="context">Контекст приложения или службы.</param>
    /// <param name="name">Имя канала на языке интерфейса.</param>
    /// <param name="description">Описание канала на языке интерфейса.</param>
    public static void EnsureChannel(Context context, string name, string description)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        try
        {
            var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);

            if (manager is null)
            {
                return;
            }

            var channel = new NotificationChannel(
                ChannelId,
                name,
                NotificationImportance.Default)
            {
                Description = description,
            };

            manager.CreateNotificationChannel(channel);
        }
        catch (Exception)
        {
            // Канал — не повод не запустить приложение: без него FCM покажет уведомление
            // в канале по умолчанию, но вход и записи должны работать.
        }
    }

    /// <summary>
    /// Показывает напоминание в системной шторке.
    /// </summary>
    /// <param name="context">Контекст приложения или службы.</param>
    /// <param name="title">Заголовок; приходит с сервера готовым.</param>
    /// <param name="body">Текст; приходит с сервера готовым.</param>
    /// <param name="data">Data-payload сообщения: <c>type</c> и <c>bookingId</c>.</param>
    public static void Show(
        Context context,
        string title,
        string body,
        IDictionary<string, string>? data)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        // Канал мог ещё не существовать, если приложение пришло сюда без запуска Activity:
        // имя и описание — из текущего каталога перевода.
        EnsureChannel(context, Loc.T("Напоминания"), Loc.T("Напоминания о записях"));

        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);

        if (data is not null)
        {
            foreach (var pair in data)
            {
                intent.PutExtra(pair.Key, pair.Value);
            }
        }

        // Один и тот же bookingId даёт один и тот же requestCode: повторное напоминание
        // обновляет pending intent, а не плодит несовместимые переходы.
        var requestCode = data is not null
            && data.TryGetValue(PushNotificationPayload.BookingIdExtra, out var bookingId)
                ? StringComparer.Ordinal.GetHashCode(bookingId)
                : 0;

        var pendingIntent = PendingIntent.GetActivity(
            context,
            requestCode,
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        if (pendingIntent is null)
        {
            return;
        }

        var builder = new NotificationCompat.Builder(context, ChannelId);

        // Fluent-сеттеры AndroidX возвращают сам Builder, но привязка помечает возвращаемое
        // значение как возможно пустое. Вызовы по отдельности: значение возврата не нужно,
        // а локальная переменная остаётся непустой и цепочка не сеет предупреждений.
        builder.SetContentTitle(title);
        builder.SetContentText(body);

        var style = new NotificationCompat.BigTextStyle();
        style.BigText(body);
        builder.SetStyle(style);

        // Монохромная иконка из набора lucide (колокольчик): маленькая иконка уведомления
        // маскируется по альфе, поэтому цвет в ней не важен, а адаптивная иконка приложения
        // дала бы белое пятно. Тот же drawable задан FCM как иконка по умолчанию.
        builder.SetSmallIcon(Resource.Drawable.notification_bell);
        builder.SetAutoCancel(true);
        builder.SetContentIntent(pendingIntent);

        var manager = NotificationManagerCompat.From(context);

        if (manager is null)
        {
            return;
        }

        manager.Notify(unchecked((int)System.Environment.TickCount64), builder.Build());
    }
}
