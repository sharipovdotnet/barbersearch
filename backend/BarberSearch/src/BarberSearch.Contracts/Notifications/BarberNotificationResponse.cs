using BarberSearch.Domain.Notifications;

namespace BarberSearch.Contracts.Notifications;

/// <summary>
/// Уведомление в инбоксе барбера.
/// </summary>
/// <remarks>
/// <para>
/// Ответ отдаёт снимок, а не текущее состояние услуги и салона: <c>serviceName</c>,
/// <c>salonName</c>, <c>salonTimeZoneId</c> и <c>startAt</c> — то, что было актуально
/// в момент создания уведомления. Время в тексте показывается в поясе салона, поэтому
/// <c>salonTimeZoneId</c> уходит клиенту: браузерный пояс тут не подсказка.
/// </para>
/// <para>
/// Персональных данных клиента в контракте нет и не появится: уведомление хранит только
/// время, услугу и салон (152-ФЗ, решение D3).
/// </para>
/// <para>
/// <see cref="Kind"/> и <see cref="Delivery"/> сериализуются именами значений
/// (<c>Upcoming</c>, <c>Sent</c>) — целые числа в JSON запрещены настройками сериализации.
/// </para>
/// </remarks>
/// <param name="Id">Идентификатор уведомления.</param>
/// <param name="Kind">Вид: <c>Upcoming</c> — напоминание о визите, <c>Digest</c> — утренний дайджест.</param>
/// <param name="CreatedAt">Когда уведомление создано, в UTC.</param>
/// <param name="ReadAt">Когда прочитано; <c>null</c> — не прочитано.</param>
/// <param name="BookingId">Бронь напоминания; у дайджеста <c>null</c>.</param>
/// <param name="StartAt">Начало визита в UTC; у дайджеста — начало первой записи дня.</param>
/// <param name="ServiceName">Снимок названия услуги.</param>
/// <param name="SalonName">Снимок названия салона.</param>
/// <param name="SalonTimeZoneId">Снимок пояса салона (IANA) для показа времени.</param>
/// <param name="BookingsCount">Число записей в дайджесте; у напоминания о визите <c>null</c>.</param>
/// <param name="Delivery">Состояние доставки: <c>Pending</c>, <c>Sent</c>, <c>Skipped</c>, <c>Failed</c>.</param>
public record BarberNotificationResponse(
    Guid Id,
    BarberNotificationKind Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    Guid? BookingId,
    DateTimeOffset? StartAt,
    string? ServiceName,
    string? SalonName,
    string? SalonTimeZoneId,
    int? BookingsCount,
    BarberNotificationDelivery Delivery);
