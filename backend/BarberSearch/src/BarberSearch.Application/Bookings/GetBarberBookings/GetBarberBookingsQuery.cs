using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Application.Bookings.GetBarberBookings;

/// <summary>
/// Запрос списка записей барбера с keyset-пагинацией.
/// </summary>
/// <remarks>
/// Профиля барбера в запросе нет: он берётся из claim'а текущей сессии, поэтому запросить
/// чужой список нечем. <paramref name="Scope"/> — <c>upcoming</c> (по умолчанию) или
/// <c>past</c>; неизвестное значение отклоняет обработчик публичным кодом ошибки.
/// </remarks>
/// <param name="Scope">Скоуп: <c>upcoming</c> или <c>past</c>; <c>null</c> — ближайшие.</param>
/// <param name="Cursor">Курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; <c>null</c> — значение по умолчанию.</param>
public record GetBarberBookingsQuery(
    string? Scope,
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<BarberBookingResponse>>;
