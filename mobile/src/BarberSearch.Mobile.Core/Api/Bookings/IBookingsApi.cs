using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api.Bookings;

/// <summary>
/// Записи клиента: создание, список, отмена и перенос.
/// </summary>
/// <remarks>
/// Ходит через клиент с <c>AuthenticatedHttpHandler</c>: клиент брони сервер берёт из токена,
/// а продление пары и заголовок — работа обработчика, а не этого интерфейса. Отдельный клиент
/// намеренно отделён от публичных чтений карточки: они анонимные и не должны тащить токен.
/// </remarks>
public interface IBookingsApi
{
    /// <summary>
    /// Создаёт бронь или возвращает уже созданную по тому же ключу идемпотентности.
    /// </summary>
    /// <remarks>
    /// Повтор с тем же <see cref="CreateBookingRequest.IdempotencyKey"/> не создаёт вторую
    /// запись: сервер отвечает идентификатором первой. Это защита от двойного нажатия и от
    /// повтора после обрыва сети, поэтому ключ создаётся на попытку, а не на каждый запрос.
    /// </remarks>
    /// <param name="request">Данные брони; цена в нём не передаётся — её копирует сервер.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор созданной брони.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<Guid> CreateBookingAsync(
        CreateBookingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает страницу записей клиента.
    /// </summary>
    /// <remarks>
    /// <paramref name="userId"/> уходит всегда: без него администратор по контракту получил бы
    /// в «Моих записях» брони всей системы. <paramref name="cursor"/> непрозрачен — клиент
    /// возвращает его как есть и не разбирает; <c>null</c> в ответе означает последнюю страницу.
    /// </remarks>
    /// <param name="scope">Вкладка: предстоящие или история.</param>
    /// <param name="userId">Идентификатор вошедшего клиента.</param>
    /// <param name="cursor">Курсор следующей страницы из предыдущего ответа.</param>
    /// <param name="pageSize">Размер страницы; сервер по умолчанию отдаёт 20.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница записей без имён барбера и салона.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<PagedResponse<BookingResponse>> GetBookingsAsync(
        BookingScope scope,
        Guid userId,
        string? cursor = null,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отменяет запись.
    /// </summary>
    /// <remarks>
    /// Тело не отправляется: клиент причину не пишет, её оставляет только барбер, поэтому
    /// <c>cancellationReason</c> в ответе останется <c>null</c>. Успех — <c>204</c> без тела.
    /// </remarks>
    /// <param name="bookingId">Идентификатор брони.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Запись не найдена или уже не отменяется.</exception>
    Task CancelBookingAsync(Guid bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Свободные слоты для переноса записи на дату салона.
    /// </summary>
    /// <remarks>
    /// Сетка считается по длительности самой брони, а не по текущему прайсу: перенос обязан
    /// сохранить обещанное клиенту время. Дата — календарная строка в поясе салона, не инстант.
    /// </remarks>
    /// <param name="bookingId">Идентификатор брони.</param>
    /// <param name="date">Локальная дата салона.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сетка слотов вместе с поясом салона.</returns>
    /// <exception cref="BarberSearchApiException">Запись уже нельзя перенести или дата вне окна.</exception>
    Task<BarberSlotsResponse> GetRescheduleSlotsAsync(
        Guid bookingId,
        DateOnly slotDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Переносит визит на начало слота.
    /// </summary>
    /// <remarks>
    /// Меняются только <c>startAt</c>/<c>endAt</c>: услуга, салон, барбер и цена остаются
    /// прежними. <paramref name="startAt"/> отправляется ровно тем инстантом, который пришёл из
    /// <see cref="GetRescheduleSlotsAsync"/>: сервер сравнивает абсолютные моменты и сам берёт
    /// конец слота. Успех — <c>204</c> без тела.
    /// </remarks>
    /// <param name="bookingId">Идентификатор брони.</param>
    /// <param name="startAt">Новое начало визита в UTC — ровно из выбранного слота.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Время занято, недопустимо или запись изменилась.</exception>
    Task RescheduleBookingAsync(
        Guid bookingId,
        DateTimeOffset startAt,
        CancellationToken cancellationToken = default);
}
