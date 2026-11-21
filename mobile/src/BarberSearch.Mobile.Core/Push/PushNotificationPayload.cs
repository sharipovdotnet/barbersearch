using BarberSearch.Mobile.Core.Navigation;

namespace BarberSearch.Mobile.Core.Push;

/// <summary>
/// Машинные данные уведомления, вытащенные из extras intent'а.
/// </summary>
/// <remarks>
/// <para>
/// Сервер кладёт в data-payload только тип сообщения и, когда он есть, идентификатор записи
/// (<c>{"type":"booking.reminder","bookingId":"&lt;guid&gt;"}</c> либо
/// <c>{"type":"booking.created"}</c>). Текст — заголовок и тело — уже показан системой либо
/// локальным уведомлением; для перехода он не нужен. Канал несёт семь клиентских событий:
/// напоминание за час до визита (<see cref="ReminderType"/>); создание записи
/// (<see cref="BookingCreatedType"/>) — и когда клиент записался сам, и когда клиента записал
/// мастер; изменения записи мастером — подтверждение (<see cref="BookingConfirmedType"/>),
/// отмена (<see cref="BookingCancelledType"/>) и перенос (<see cref="BookingRescheduledType"/>);
/// и те же отмена и перенос, но сделанные клиентом (<see cref="BookingCancelledByCustomerType"/>,
/// <see cref="BookingRescheduledByCustomerType"/>).
/// </para>
/// <para>
/// Тем же каналом мастеру приходят два события кабинета: новая запись клиента
/// (<see cref="BarberBookingCreatedType"/>) и её отмена
/// (<see cref="BarberBookingCancelledType"/>). Они адресованы мастеру, а не клиенту,
/// и открывают кабинет, а не вкладку «Записи» — см. <see cref="Route"/>.
/// </para>
/// <para>
/// Собственное действие клиента push порождает только тогда, когда оно сделано вне приложения,
/// на веб-поверхности: запрос из приложения несёт заголовок <c>X-Client-Surface: app</c>, и
/// сервер по нему уведомление не создаёт — экран уже показал результат, а письмо об изменении
/// уходит отдельно. Запрос без заголовка (веб, кабинет барбера, неизвестный клиент) уведомляет,
/// поэтому типы <c>booking.cancelledByCustomer</c> и <c>booking.rescheduledByCustomer</c>
/// встречаются реже остальных, но разбираются наравне с ними.
/// </para>
/// <para>
/// Отдельной deep link-страницы записи нет: <c>GET api/bookings/{id}</c> в API отсутствует,
/// а список «Записи» и так содержит нужную запись, поэтому все клиентские события открывают
/// вкладку, а не карточку. События мастера открывают кабинет, у которого раздел записей
/// клиентов — раздел по умолчанию; отдельной карточки записи и там нет. Разбор чистый:
/// ни MAUI, ни Shell, поэтому проверяется в тестах без эмулятора.
/// </para>
/// </remarks>
/// <param name="Type">
/// Тип сообщения на проводе, например <see cref="ReminderType"/> или <see cref="BookingConfirmedType"/>.
/// </param>
/// <param name="BookingId">Запись, к которой относится сообщение, если сервер её прислал.</param>
public sealed record PushNotificationPayload(string Type, Guid? BookingId)
{
    /// <summary>
    /// Тип напоминания клиенту за час до подтверждённого визита.
    /// </summary>
    public const string ReminderType = "booking.reminder";

    /// <summary>
    /// Тип события о создании записи: клиент записался сам либо мастера записал клиента.
    /// </summary>
    public const string BookingCreatedType = "booking.created";

    /// <summary>
    /// Тип события о подтверждении записи мастером.
    /// </summary>
    public const string BookingConfirmedType = "booking.confirmed";

    /// <summary>
    /// Тип события об отмене записи мастером.
    /// </summary>
    public const string BookingCancelledType = "booking.cancelled";

    /// <summary>
    /// Тип события о переносе записи мастером.
    /// </summary>
    public const string BookingRescheduledType = "booking.rescheduled";

    /// <summary>
    /// Тип события об отмене записи клиентом вне приложения.
    /// </summary>
    public const string BookingCancelledByCustomerType = "booking.cancelledByCustomer";

    /// <summary>
    /// Тип события о переносе записи клиентом вне приложения.
    /// </summary>
    public const string BookingRescheduledByCustomerType = "booking.rescheduledByCustomer";

    /// <summary>
    /// Тип события о новой записи, адресованного мастеру, а не клиенту.
    /// </summary>
    /// <remarks>
    /// Событие кабинета, а не клиентское: сервер сообщает мастеру, что у него появилась
    /// новая запись клиента. Не путать с <see cref="BookingCreatedType"/>: тот адресован
    /// клиенту.
    /// </remarks>
    public const string BarberBookingCreatedType = "barber.booking.created";

    /// <summary>
    /// Тип события об отмене записи, адресованного мастеру, а не клиенту.
    /// </summary>
    /// <remarks>
    /// Событие кабинета, а не клиентское: сервер сообщает мастеру, что запись отменили.
    /// Не путать с <see cref="BookingCancelledType"/>: тот адресован клиенту и сообщает
    /// об отмене, сделанной мастером.
    /// </remarks>
    public const string BarberBookingCancelledType = "barber.booking.cancelled";

    /// <summary>
    /// Маршрут вкладки «Записи».
    /// </summary>
    /// <remarks>
    /// Абсолютный маршрут Shell (<c>//</c>) переключает вкладку, а не кладёт страницу поверх
    /// текущей: после возврата из уведомления человек остаётся в обычном приложении.
    /// </remarks>
    public const string BookingsRoute = "//bookings";

    /// <summary>
    /// Имя extra с типом сообщения.
    /// </summary>
    public const string TypeExtra = "type";

    /// <summary>
    /// Имя extra с идентификатором записи.
    /// </summary>
    public const string BookingIdExtra = "bookingId";

    /// <summary>
    /// Куда перейти по нажатию; <c>null</c> — у этого сообщения перехода нет.
    /// </summary>
    /// <remarks>
    /// Клиентские события открывают «Записи»: напоминание, создание и изменения записи ведут
    /// в один и тот же список. События мастера открывают кабинет, чей раздел по умолчанию —
    /// записи клиентов. Отдельной страницы записи нет ни для клиента, ни для мастера.
    /// </remarks>
    public string? Route => Type switch
    {
        ReminderType or BookingCreatedType or BookingConfirmedType
            or BookingCancelledType or BookingRescheduledType
            or BookingCancelledByCustomerType or BookingRescheduledByCustomerType => BookingsRoute,
        BarberBookingCreatedType or BarberBookingCancelledType => BarberCabinetRoutes.Cabinet,
        _ => null,
    };

    /// <summary>
    /// Разбирает extras intent'а.
    /// </summary>
    /// <remarks>
    /// Неизвестный, пустой или отсутствующий <c>type</c> — не ошибка устройства, а сообщение,
    /// которое этот клиент не умеет открывать: <c>false</c> и <c>null</c>, чтобы вызывающий
    /// ничего не навигировал. <c>bookingId</c> необязателен: цель — список записей, а не
    /// карточка, поэтому переход корректен и без него.
    /// </remarks>
    /// <param name="extras">Extras intent'а либо <c>null</c>.</param>
    /// <param name="payload">Разобранные данные, если тип известен.</param>
    /// <returns><c>true</c>, если сообщение известно клиенту.</returns>
    public static bool TryParse(
        IDictionary<string, string>? extras,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PushNotificationPayload? payload)
    {
        payload = null;

        if (extras is null
            || !extras.TryGetValue(TypeExtra, out var type)
            || !IsKnownType(type))
        {
            return false;
        }

        Guid? bookingId = extras.TryGetValue(BookingIdExtra, out var rawBookingId)
            && Guid.TryParse(rawBookingId, out var parsed)
                ? parsed
                : null;

        payload = new PushNotificationPayload(type, bookingId);

        return true;
    }

    private static bool IsKnownType(string type) =>
        type is ReminderType or BookingCreatedType or BookingConfirmedType
            or BookingCancelledType or BookingRescheduledType
            or BookingCancelledByCustomerType or BookingRescheduledByCustomerType
            or BarberBookingCreatedType or BarberBookingCancelledType;
}
