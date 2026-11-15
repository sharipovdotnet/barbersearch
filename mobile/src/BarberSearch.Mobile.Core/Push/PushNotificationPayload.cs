namespace BarberSearch.Mobile.Core.Push;

/// <summary>
/// Машинные данные уведомления, вытащенные из extras intent'а.
/// </summary>
/// <remarks>
/// <para>
/// Сервер кладёт в data-payload только тип сообщения и идентификатор записи
/// (<c>{"type":"booking.reminder","bookingId":"&lt;guid&gt;"}</c>). Текст — заголовок и тело —
/// уже показан системой либо локальным уведомлением; для перехода он не нужен.
/// </para>
/// <para>
/// Отдельной deep link-страницы записи нет: <c>GET api/bookings/{id}</c> в API отсутствует,
/// а список «Записи» и так содержит нужную запись, поэтому напоминание открывает вкладку,
/// а не карточку. Разбор чистый: ни MAUI, ни Shell, поэтому проверяется в тестах без
/// эмулятора.
/// </para>
/// </remarks>
/// <param name="Type">Тип сообщения на проводе, например <see cref="ReminderType"/>.</param>
/// <param name="BookingId">Запись, к которой относится напоминание, если сервер её прислал.</param>
public sealed record PushNotificationPayload(string Type, Guid? BookingId)
{
    /// <summary>
    /// Тип напоминания клиенту за час до подтверждённого визита.
    /// </summary>
    public const string ReminderType = "booking.reminder";

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
    public string? Route => Type switch
    {
        ReminderType => BookingsRoute,
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
            || !string.Equals(type, ReminderType, StringComparison.Ordinal))
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
}
