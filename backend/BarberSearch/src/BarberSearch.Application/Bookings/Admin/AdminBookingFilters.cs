using BarberSearch.Domain.Bookings;

namespace BarberSearch.Application.Bookings.Admin;

/// <summary>
/// Разобранные фильтры админского списка броней.
/// </summary>
/// <remarks>
/// Обработчик переводит строки query string в этот тип: репозиторий не решает, что считать
/// пустым фильтром и как разбирать статус. <see cref="Search"/> уже обрезан по краям:
/// <c>null</c> означает «без поиска».
/// </remarks>
/// <param name="Status">Фильтр статуса; <c>null</c> — все статусы.</param>
/// <param name="Search">Поиск: подстрока email клиента или точный id брони; <c>null</c> — без фильтра.</param>
public record AdminBookingFilters(BookingStatus? Status, string? Search);
