namespace BarberSearch.Application.Push;

/// <summary>
/// Готовое push-сообщение: заголовок, текст и визит, к которому оно относится.
/// </summary>
/// <remarks>
/// <para>
/// Текст собирается на сервере (<see cref="PushMessages"/>) и приходит сюда готовым:
/// транспорт не форматирует даты и не знает русского языка. <see cref="BookingId"/> —
/// единственные машинные данные payload'а; тип сообщения фиксирован
/// <see cref="TYPE"/> и одинаков для всех напоминаний среза.
/// </para>
/// <para>
/// Персональных данных клиента в сообщении нет (152-ФЗ): ни имени, ни телефона, ни заметки.
/// Это не проверка «на выходе», а отсутствие таких полей во входных данных.
/// </para>
/// </remarks>
/// <param name="Title">Заголовок уведомления.</param>
/// <param name="Body">Текст уведомления.</param>
/// <param name="BookingId">Визит, о котором напоминаем.</param>
public sealed record PushMessage(string Title, string Body, Guid BookingId)
{
    /// <summary>
    /// Тип push-сообщения в data-payload'е; по нему клиент выбирает обработчик перехода.
    /// </summary>
    public const string TYPE = "booking.reminder";
}
