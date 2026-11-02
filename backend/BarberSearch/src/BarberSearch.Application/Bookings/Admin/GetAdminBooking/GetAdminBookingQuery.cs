using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Bookings.Admin;

namespace BarberSearch.Application.Bookings.Admin.GetAdminBooking;

/// <summary>
/// Запрос карточки брони для администратора.
/// </summary>
/// <param name="BookingId">Идентификатор брони.</param>
public record GetAdminBookingQuery(Guid BookingId) : IQuery<AdminBookingResponse>;
