using BarberSearch.Domain.Bookings;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Забронированная строка: тесту нужны идентификатор, начало и статус, а не агрегат целиком.
/// </summary>
/// <param name="Id">Идентификатор созданной брони.</param>
/// <param name="StartAt">Начало визита в UTC.</param>
/// <param name="Status">Статус после всех доменных переходов при сидировании.</param>
internal sealed record SeededBooking(Guid Id, DateTimeOffset StartAt, BookingStatus Status);
