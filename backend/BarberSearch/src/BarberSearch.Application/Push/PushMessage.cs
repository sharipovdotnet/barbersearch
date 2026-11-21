namespace BarberSearch.Application.Push;

/// <summary>
/// Готовое push-сообщение: заголовок, текст, визит и тип для data-payload'а.
/// </summary>
/// <remarks>
/// <para>
/// Текст собирается на сервере (<see cref="PushMessages"/>) и приходит сюда готовым:
/// транспорт не форматирует даты и не знает русского языка. <see cref="BookingId"/> и
/// <see cref="Type"/> — единственные машинные данные payload'а: по типу клиент выбирает
/// обработчик перехода, идентификатор визита говорит, куда переходить. Тип — единственное,
/// чем мобильное приложение различает события; все типы (напоминание, создание, события
/// статуса) открывают вкладку «Записи», поэтому новый тип не требует нового экрана.
/// </para>
/// <para>
/// В клиентских сообщениях персональных данных нет (152-ФЗ): ни имени, ни телефона, ни
/// заметки. События мастеру добавляют имя клиента — без него мастер не понимает, кто придёт;
/// телефона и заметки не добавляют и они. Это не проверка «на выходе», а отсутствие таких
/// полей во входных данных.
/// </para>
/// <para>
/// В <c>data</c> по-прежнему только тип и идентификатор визита: имя клиента есть только в
/// тексте уведомления и только у событий мастеру.
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

    /// <summary>
    /// Тип события «барбер подтвердил запись».
    /// </summary>
    public const string BOOKING_CONFIRMED_TYPE = "booking.confirmed";

    /// <summary>
    /// Тип события «барбер или администратор отменил запись».
    /// </summary>
    public const string BOOKING_CANCELLED_TYPE = "booking.cancelled";

    /// <summary>
    /// Тип события «барбер или администратор перенёс запись».
    /// </summary>
    public const string BOOKING_RESCHEDULED_TYPE = "booking.rescheduled";

    /// <summary>
    /// Тип события «клиент отменил запись сам с другой поверхности».
    /// </summary>
    public const string BOOKING_CANCELLED_BY_CUSTOMER_TYPE = "booking.cancelledByCustomer";

    /// <summary>
    /// Тип события «клиент перенёс визит сам с другой поверхности».
    /// </summary>
    public const string BOOKING_RESCHEDULED_BY_CUSTOMER_TYPE = "booking.rescheduledByCustomer";

    /// <summary>
    /// Тип события мастеру «клиент записался».
    /// </summary>
    public const string BARBER_BOOKING_CREATED_TYPE = "barber.booking.created";

    /// <summary>
    /// Тип события мастеру «запись отменена клиентом или администратором».
    /// </summary>
    public const string BARBER_BOOKING_CANCELLED_TYPE = "barber.booking.cancelled";
}
