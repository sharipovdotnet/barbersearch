using System.Globalization;
using BarberSearch.Contracts.Notifications;
using BarberSearch.Domain.Notifications;

namespace BarberSearch.Application.Notifications;

/// <summary>
/// Проекция доменных объектов напоминаний в контракты ответов.
/// </summary>
/// <remarks>
/// <para>
/// Одна копия преобразований на три обработчика чтения и правки: список инбокса, чтение
/// настроек и сохранение настроек обязаны отдавать одну и ту же форму, иначе клиент увидит
/// разные <c>digestTime</c> после <c>PUT</c> и <c>GET</c>.
/// </para>
/// <para>
/// <see cref="TimeOnly"/> форматируется как <c>HH:mm</c> в invariant culture: значение должно
/// совпадать с <c>&lt;input type="time"&gt;</c> независимо от культуры сервера.
/// </para>
/// </remarks>
public static class NotificationMapping
{
    /// <summary>
    /// Формат времени дайджеста в контракте: часы и минуты без секунд.
    /// </summary>
    public const string DIGEST_TIME_FORMAT = "HH:mm";

    /// <summary>
    /// Проекция уведомления в ответ инбокса.
    /// </summary>
    /// <param name="notification">Доменная сущность.</param>
    public static BarberNotificationResponse ToResponse(BarberNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return new BarberNotificationResponse(
            notification.Id,
            notification.Kind,
            notification.CreatedAt,
            notification.ReadAt,
            notification.BookingId,
            notification.StartAt,
            notification.ServiceName,
            notification.SalonName,
            notification.SalonTimeZoneId,
            notification.BookingsCount,
            notification.Delivery);
    }

    /// <summary>
    /// Проекция настроек в ответ. Идентификатор чата наружу не попадает — только факт привязки.
    /// </summary>
    /// <param name="settings">Доменный агрегат настроек.</param>
    /// <param name="botConfigured">Настроен ли бот Telegram на сервере.</param>
    public static NotificationSettingsResponse ToSettingsResponse(
        BarberNotificationSettings settings,
        bool botConfigured)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new NotificationSettingsResponse(
            settings.IsEnabled,
            settings.DigestEnabled,
            settings.DigestTime.ToString(DIGEST_TIME_FORMAT, CultureInfo.InvariantCulture),
            settings.LeadMinutes,
            settings.IsTelegramLinked,
            settings.TelegramLinkedAt,
            botConfigured);
    }
}
