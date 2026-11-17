namespace BarberSearch.Mobile.Core.Push;

/// <summary>
/// Машинные данные уведомления, вытащенные из extras intent'а.
/// </summary>
/// <remarks>
/// <para>
/// Сервер кладёт в data-payload только тип сообщения и, когда он есть, идентификатор записи
/// (<c>{"type":"booking.reminder","bookingId":"&lt;guid&gt;"}</c> либо
/// <c>{"type":"booking.created"}</c>). Текст — заголовок и тело — уже показан системой либо
/// локальным уведомлением; для перехода он не нужен. Канал несёт два события: напоминание за час
/// до визита (<see cref="ReminderType"/>) и создание записи (<see cref="BookingCreatedType"/>) —
/// и когда клиент записался сам, и когда клиента записал мастер.
/// </para>
/// <para>
/// Отдельной deep link-страницы записи нет: <c>GET api/bookings/{id}</c> в API отсутствует,
/// а список «Записи» и так содержит нужную запись, поэтому оба события открывают вкладку,
/// а не карточку. Разбор чистый: ни MAUI, ни Shell, поэтому проверяется в тестах без
/// эмулятора.
/// </para>
/// </remarks>
/// <param name="Type">
/// Тип сообщения на проводе, например <see cref="ReminderType"/> или <see cref="BookingCreatedType"/>.
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
    /// Оба известных события открывают «Записи»: напоминание и создание записи ведут в один и
    /// тот же список, отдельной страницы записи нет.
    /// </remarks>
    public string? Route => Type switch
    {
        ReminderType or BookingCreatedType => BookingsRoute,
        _ => null,
    };

    /// <summary>
    /// Разбирает extras intent'а.
    /// </summary>
    /// <remarks>
    /// Неизвестный, пустой или отсутствующий <c>type</c> — не ошибка устройства, а сообщение,
    /// которое этот клиент не умеет открывать: <c>false</c> и <c>null</c>, чтобы вызывающий
    /// ничего не навигировал. <c>bookingId</c> необязателен: без него переход на вкладку
    /// «Записи» всё равно корректен.
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
        type is ReminderType or BookingCreatedType;
}
