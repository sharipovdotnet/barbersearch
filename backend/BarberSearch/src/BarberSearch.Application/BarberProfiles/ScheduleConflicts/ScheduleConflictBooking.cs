using BarberSearch.Domain.Bookings;

namespace BarberSearch.Application.BarberProfiles.ScheduleConflicts;

/// <summary>
/// Плоская строка активной брони для проверки конфликтов: время, клиент, услуга и пояс салона.
/// </summary>
/// <remarks>
/// Это проекция, а не агрегат <see cref="Booking"/>: проверке нужны имя клиента, название
/// услуги и часовой пояс салона, а телефон, email и заметки в ответ <c>409</c> не попадают.
/// </remarks>
/// <param name="BookingId">Идентификатор брони.</param>
/// <param name="SalonId">Салон, в котором оказана услуга (<c>Salon.Id</c>).</param>
/// <param name="StartAt">Начало визита в UTC.</param>
/// <param name="EndAt">Окончание визита в UTC.</param>
/// <param name="Status">Статус брони: <c>Pending</c> или <c>Confirmed</c>.</param>
/// <param name="CustomerName">Имя клиента; «Гость», если имени нет.</param>
/// <param name="ServiceName">Название услуги.</param>
/// <param name="TimeZoneId">Пояс салона для показа времени.</param>
public sealed record ScheduleConflictBooking(
    Guid BookingId,
    Guid SalonId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    BookingStatus Status,
    string CustomerName,
    string ServiceName,
    string TimeZoneId);
