using BarberSearch.Domain.Bookings;

namespace BarberSearch.Contracts.Bookings.Barber;

/// <summary>
/// Бронь в списке записей барбера.
/// </summary>
/// <remarks>
/// Отдельный контракт от <c>BookingResponse</c>: барберу нужен клиент и контекст визита,
/// а клиенту — свои идентификаторы и ключ идемпотентности. Наружу отдаётся минимум PII:
/// имя и телефон клиента, без email, <c>userId</c> и идентификаторов связей. Цена — снимок
/// из брони, а не текущая цена услуги.
/// </remarks>
/// <param name="Id">Идентификатор брони.</param>
/// <param name="Status">Текущий статус брони.</param>
/// <param name="StartAt">Начало визита в UTC.</param>
/// <param name="EndAt">Окончание визита в UTC.</param>
/// <param name="Price">Цена, зафиксированная в брони.</param>
/// <param name="CustomerNote">Заметка клиента при создании брони.</param>
/// <param name="BarberNote">Заметка барбера.</param>
/// <param name="Customer">Клиент: имя и телефон.</param>
/// <param name="Service">Услуга визита.</param>
/// <param name="Salon">Салон визита, включая часовой пояс для форматирования времени.</param>
public record BarberBookingResponse(
    Guid Id,
    BookingStatus Status,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    decimal Price,
    string? CustomerNote,
    string? BarberNote,
    BarberBookingCustomerResponse Customer,
    BarberBookingServiceResponse Service,
    BarberBookingSalonResponse Salon);
