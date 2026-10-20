using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Application.Bookings.GetBookings;

/// <summary>
/// Запрос списка броней с keyset-пагинацией.
/// </summary>
/// <remarks>
/// Обычный пользователь всегда видит только свои брони. Администратор видит все;
/// <paramref name="UserId"/> позволяет ему сузить выдачу до одного клиента.
/// Без <paramref name="UserId"/> не-администратор получает собственный список.
/// </remarks>
/// <param name="UserId">Фильтр по клиенту; <c>null</c> — все брони для admin или свои для остальных.</param>
/// <param name="Cursor">Курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; <c>null</c> — значение по умолчанию.</param>
public record GetBookingsQuery(
    Guid? UserId,
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<BookingResponse>>;
