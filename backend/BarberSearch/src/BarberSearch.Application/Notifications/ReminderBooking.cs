namespace BarberSearch.Application.Notifications;

/// <summary>
/// Бронь, попавшая в окно напоминания, либо запись локальных суток салона для дайджеста.
/// </summary>
/// <remarks>
/// Плоская проекция вместо агрегата <c>Booking</c>: напоминанию нужны только начало визита,
/// название услуги и название салона с его поясом, а подтягивать ради них три агрегата
/// (бронь, профиль, салон) незачем. Снимок собирается на момент создания уведомления
/// и дальше не пересчитывается.
/// </remarks>
/// <param name="BookingId">Идентификатор брони.</param>
/// <param name="StartAt">Начало визита в UTC.</param>
/// <param name="ServiceName">Название услуги.</param>
/// <param name="SalonName">Название салона визита.</param>
/// <param name="SalonTimeZoneId">Пояс салона визита (IANA).</param>
public sealed record ReminderBooking(
    Guid BookingId,
    DateTimeOffset StartAt,
    string? ServiceName,
    string? SalonName,
    string? SalonTimeZoneId);
