using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Bookings.Admin;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Application.Bookings.Admin.GetAdminBookings;

/// <summary>
/// Запрос админского списка броней: фильтр статуса, поиск и keyset-страница.
/// </summary>
/// <param name="Status">Фильтр статуса: <c>all</c> или имя <c>BookingStatus</c> в любом регистре.</param>
/// <param name="Search">Подстрока email клиента или точный идентификатор брони.</param>
/// <param name="Cursor">Непрозрачный курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
public record GetAdminBookingsQuery(
    string? Status,
    string? Search,
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<AdminBookingResponse>>;
