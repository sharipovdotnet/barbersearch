using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Bookings.CreateBarberBooking;
using BarberSearch.Contracts.Bookings.RescheduleBooking;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api.Barber;

/// <summary>
/// Записи в кабинете мастера: список, ручная запись клиента и переходы статуса.
/// </summary>
/// <remarks>
/// <para>
/// Клиент ходит через <see cref="HttpClient"/> с <c>AuthenticatedHttpHandler</c>: профиль
/// мастера сервер берёт из claim'а токена, поэтому в запросах нет параметра с идентификатором
/// профиля и прочитать чужие записи нечем. <c>X-CSRF-TOKEN</c> здесь не отправляется —
/// Bearer-клиент не проходит antiforgery-фильтр, которым закрыты cookie-мутации.
/// </para>
/// <para>
/// <c>X-Client-Surface: app</c> тоже не ставится. Этот заголовок помечает собственные действия
/// клиента, чтобы сервер не дублировал их push'ем; действия мастера push'ят всегда, и пометка
/// «приложение» изменила бы поведение уведомлений.
/// </para>
/// </remarks>
public interface IBarberBookingsApi
{
    /// <summary>
    /// Читает страницу записей текущего мастера.
    /// </summary>
    /// <remarks>
    /// <paramref name="scope"/> — <c>upcoming</c> или <c>past</c>; <c>null</c> — серверный
    /// скоуп по умолчанию (<c>upcoming</c>), и тогда параметр в адрес не попадает. Курсор
    /// непрозрачен: клиент возвращает его из предыдущего ответа как есть и не разбирает, а
    /// <c>null</c> в <see cref="PagedResponse{T}.NextCursor"/> означает последнюю страницу.
    /// <paramref name="pageSize"/> по умолчанию равен серверным 20; <c>null</c> — не передавать
    /// размер вовсе и положиться на серверное значение.
    /// </remarks>
    /// <param name="scope">Скоуп: <c>upcoming</c>, <c>past</c> или <c>null</c>.</param>
    /// <param name="cursor">Курсор следующей страницы из предыдущего ответа.</param>
    /// <param name="pageSize">Размер страницы; <c>null</c> — серверный по умолчанию.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница записей с клиентом, услугой и салоном визита.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<PagedResponse<BarberBookingResponse>> GetAsync(
        string? scope = null,
        string? cursor = null,
        int? pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Записывает клиента или гостя к текущему мастеру; бронь создаётся сразу подтверждённой.
    /// </summary>
    /// <remarks>
    /// Профиль мастера в теле не передаётся — сервер берёт его из сессии. Бронь создаётся
    /// в статусе <c>Confirmed</c>: мастер и есть подтверждающая сторона, поэтому отдельного
    /// <see cref="ConfirmAsync"/> после создания не требуется. Цена не передаётся: её, как
    /// и в публичной записи, определяет услуга мастера. Повтор с тем же ключом
    /// идемпотентности возвращает идентификатор уже созданной брони.
    /// </remarks>
    /// <param name="request">Клиент или гость, услуга, салон и время визита.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор созданной брони.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<Guid> CreateAsync(
        CreateBarberBookingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Подтверждает бронь: <c>Pending → Confirmed</c>.
    /// </summary>
    /// <remarks>
    /// Успех — <c>204</c> без тела: нового состояния экран не читает, список перечитывается
    /// опросом, а статус уже известен из действия.
    /// </remarks>
    /// <param name="bookingId">Идентификатор брони.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Запись не найдена или уже не подтверждается.</exception>
    Task ConfirmAsync(Guid bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Завершает визит: <c>Confirmed → Completed</c>.
    /// </summary>
    /// <param name="bookingId">Идентификатор брони.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Запись не найдена или уже не завершается.</exception>
    Task CompleteAsync(Guid bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отменяет бронь и освобождает слот.
    /// </summary>
    /// <remarks>
    /// Мастер обязан передать причину: без неё сервер отвечает <c>400
    /// booking.cancel.reasonRequired</c>. Тело отправляется всегда, даже с <c>null</c>
    /// причиной: так отказ приходит доменным кодом из контракта, а не зависит от того,
    /// как биндер разберёт пустой запрос. Успех — <c>204</c>.
    /// </remarks>
    /// <param name="bookingId">Идентификатор брони.</param>
    /// <param name="reason">Причина отмены; сервер требует непустую.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Причина не передана или запись уже не отменяется.</exception>
    Task CancelAsync(
        Guid bookingId,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Помечает бронь как неявку клиента: <c>Confirmed → NoShow</c>.
    /// </summary>
    /// <remarks>
    /// Слишком ранняя неявка — <c>400</c>: правило принадлежит серверу, клиент его не повторяет.
    /// </remarks>
    /// <param name="bookingId">Идентификатор брони.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Время неявки ещё не наступило или запись не найдена.</exception>
    Task MarkNoShowAsync(Guid bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет заметку мастера к брони.
    /// </summary>
    /// <remarks>
    /// Пустая строка допустима и очищает заметку: обрезку по краям и превращение пустой строки
    /// в <c>null</c> делает сервер, поэтому клиент не подменяет правило своим.
    /// </remarks>
    /// <param name="bookingId">Идентификатор брони.</param>
    /// <param name="note">Текст заметки; пустая строка очищает её.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Запись не найдена или заметка отклонена.</exception>
    Task SaveNoteAsync(
        Guid bookingId,
        string note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Меняет цену визита, не трогая услугу, салон, время и статус.
    /// </summary>
    /// <remarks>
    /// <c>0</c> — допустимая цена, поэтому клиент не проверяет знак и не подставляет значение
    /// по умолчанию: границы суммы объявлены на сервере (<c>400 booking.price.*</c>).
    /// Успех — <c>204</c> без тела.
    /// </remarks>
    /// <param name="bookingId">Идентификатор брони.</param>
    /// <param name="price">Новая цена визита.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Запись не найдена или цена недопустима.</exception>
    Task ChangePriceAsync(
        Guid bookingId,
        decimal price,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Свободные слоты для переноса записи на дату салона.
    /// </summary>
    /// <remarks>
    /// Рука записи та же, что у клиента: маршрут <c>api/bookings/{id}/reschedule-slots</c>
    /// принадлежит <c>BookingsController</c> и закрыт владельцем брони, поэтому кабинет мастера
    /// читает его без отдельной ручки. Сетка считается по длительности самой брони, а не по
    /// текущему прайсу: перенос обязан сохранить обещанное клиенту время.
    /// </remarks>
    /// <param name="bookingId">Идентификатор брони.</param>
    /// <param name="slotDate">Локальная дата салона.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сетка слотов вместе с поясом салона.</returns>
    /// <exception cref="BarberSearchApiException">Запись уже нельзя перенести или дата вне окна.</exception>
    Task<BarberSlotsResponse> GetRescheduleSlotsAsync(
        Guid bookingId,
        DateOnly slotDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Переносит визит на начало выбранного слота.
    /// </summary>
    /// <remarks>
    /// <paramref name="startAt"/> отправляется ровно тем инстантом, который пришёл из
    /// <see cref="GetRescheduleSlotsAsync"/>: сервер сравнивает абсолютные моменты и сам
    /// берёт конец слота. Успех — <c>204</c>.
    /// </remarks>
    /// <param name="bookingId">Идентификатор брони.</param>
    /// <param name="startAt">Новое начало визита — ровно из выбранного слота.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Время занято, недопустимо или запись изменилась.</exception>
    Task RescheduleAsync(
        Guid bookingId,
        DateTimeOffset startAt,
        CancellationToken cancellationToken = default);
}
