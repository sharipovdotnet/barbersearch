using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Bookings.Admin;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Bookings.Admin.GetAdminBooking;

/// <summary>
/// Обработчик карточки брони для администратора.
/// </summary>
/// <remarks>
/// Отдаёт бронь любого статуса: карточка нужна поддержке, чтобы объяснить клиенту, что
/// произошло с записью. Отсутствующая бронь — <c>404 admin.bookings.notFound</c> из
/// репозитория. Доступ проверяет контроллер политикой <c>AdminOnly</c>; обработчик решений
/// об авторизации не принимает.
/// </remarks>
public sealed class GetAdminBookingHandler
    : IQueryHandler<AdminBookingResponse, GetAdminBookingQuery>
{
    private readonly IBookingRepository _bookingRepository;

    public GetAdminBookingHandler(IBookingRepository bookingRepository)
    {
        ArgumentNullException.ThrowIfNull(bookingRepository);

        _bookingRepository = bookingRepository;
    }

    /// <summary>
    /// Возвращает бронь либо <c>404 admin.bookings.notFound</c>.
    /// </summary>
    public async Task<Result<AdminBookingResponse, Failure>> Handle(
        GetAdminBookingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await _bookingRepository.GetAdminByIdAsync(query.BookingId, cancellationToken);
    }
}
