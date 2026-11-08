namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Клиенты периода. Идентификаторы и имена клиентов в контракт не входят.
/// </summary>
/// <param name="WithVisit">Уникальные клиенты с завершённым визитом в периоде.</param>
/// <param name="Returning">Из них у кого был завершённый визит до начала периода.</param>
/// <param name="FirstVisit">У кого первый завершённый визит попал в период.</param>
/// <param name="GuestBookings">Записи без аккаунта, созданные в периоде.</param>
/// <param name="GuestShare">Доля гостевых записей среди созданных или <c>null</c>.</param>
/// <param name="ReviewRate">Доля завершённых визитов с отзывом на сейчас или <c>null</c>.</param>
public record AdminStatisticsClientsResponse(
    int WithVisit,
    int Returning,
    int FirstVisit,
    int GuestBookings,
    decimal? GuestShare,
    decimal? ReviewRate);
