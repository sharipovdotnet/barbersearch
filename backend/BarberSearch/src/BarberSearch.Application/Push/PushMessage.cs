namespace BarberSearch.Application.Push;

/// <summary>
/// Готовое push-сообщение: заголовок, текст, визит и тип для data-payload'а.
/// </summary>
/// <remarks>
/// <para>
/// Текст собирается на сервере (<see cref="PushMessages"/>) и приходит сюда готовым:
/// транспорт не форматирует даты и не знает русского языка. <see cref="BookingId"/> и
/// <see cref="Type"/> — единственные машинные данные payload'а: по типу клиент выбирает
/// обработчик перехода, идентификатор визита говорит, куда переходить.
/// </para>
/// <para>
/// Персональных данных клиента в сообщении нет (152-ФЗ): ни имени, ни телефона, ни заметки.
/// Это не проверка «на выходе», а отсутствие таких полей во входных данных.
/// </para>
/// </remarks>
/// <param name="Title">Заголовок уведомления.</param>
/// <param name="Body">Текст уведомления.</param>
/// <param name="BookingId">Визит, к которому относится уведомление.</param>
/// <param name="Type">Тип сообщения в <c>data.type</c>.</param>
public sealed record PushMessage(string Title, string Body, Guid BookingId, string Type)
{
    /// <summary>
    /// Тип напоминания за час до визита.
    /// </summary>
    public const string REMINDER_TYPE = "booking.reminder";

    /// <summary>
    /// Тип события создания брони — оба вида (клиент записался сам и записал барбер)
    /// приходят с одним типом: действие клиента на телефоне одно и то же — открыть запись.
    /// </summary>
    public const string BOOKING_CREATED_TYPE = "booking.created";
}
