using BarberSearch.Contracts.Bookings.CreateBooking;

namespace BarberSearch.Mobile.Core.Api.Bookings;

/// <summary>
/// Создание брони клиентом.
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
}
