namespace BarberSearch.Domain.Push;

/// <summary>
/// Вид push-уведомления клиенту.
/// </summary>
/// <remarks>
/// <para>
/// Значения явные (AGENT.md §10.10) и попадают в базу как <c>smallint</c>: по ним построен
/// уникальный индекс <c>(booking_id, kind)</c>, поэтому числовые значения менять нельзя.
/// Одно напоминание и одно событие создания могут сосуществовать для одной брони; два
/// события создания одной и той же брони — уже дубль.
/// </para>
/// <para>
/// Вид определяет и текст, и правило перепроверки перед доставкой: напоминание требует
/// <c>Confirmed</c>, а событие создания доставляется и для <c>Pending</c> самозаписи клиента
/// (AGENT.md §20).
/// </para>
/// </remarks>
public enum PushNotificationKind
{
    /// <summary>
    /// Напоминание за <c>Push:ReminderLeadMinutes</c> до подтверждённого визита.
    /// </summary>
    Reminder = 1,

    /// <summary>
    /// Клиент записался сам: бронь создана публичным путём (<c>CreateBookingHandler</c> без
    /// <c>CustomerId</c> и без гостевых полей). Бронь при этом ещё <c>Pending</c>.
    /// </summary>
    BookingCreatedByCustomer = 2,

    /// <summary>
    /// Барбер записал клиента вручную: <c>CreateBookingCommand.CustomerId</c> задан либо
    /// передан гость. Бронь создаётся сразу <c>Confirmed</c>.
    /// </summary>
    BookingCreatedByBarber = 3
}
