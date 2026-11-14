namespace BarberSearch.Mobile.Core.Api.Bookings;

/// <summary>
/// Скоуп списка записей клиента.
/// </summary>
/// <remarks>
/// На проводе значения называются <c>upcoming</c> и <c>past</c>; <c>all</c> клиенту не нужен:
/// «Записи» — две вкладки с собственными курсорами, и смешанная выдача потребовала бы
/// раскладывать её на клиенте. Граница — <c>endAt</c>, а не <c>startAt</c>: визит, который
/// уже идёт, остаётся предстоящим.
/// </remarks>
public enum BookingScope
{
    /// <summary>
    /// Активные записи, которые ещё не закончились: <c>upcoming</c>.
    /// </summary>
    Upcoming,

    /// <summary>
    /// Всё остальное — отмены, неявки и завершённые визиты: <c>past</c>.
    /// </summary>
    Past,
}
