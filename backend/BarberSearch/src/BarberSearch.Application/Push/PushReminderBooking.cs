namespace BarberSearch.Application.Push;

/// <summary>
/// Проекция визита, для которого пора создать push-напоминание.
/// </summary>
/// <remarks>
/// Имена мастера, салона и услуги — снимок на момент создания строки, поэтому дальше они
/// не пересчитываются. <see cref="BookingCreatedAt"/> нужен правилу «бронь, созданная
/// внутри окна напоминания, не получает „через час“»: запись на 13:50, сделанная в 13:40,
/// не должна прислать напоминание в 13:50.
/// </remarks>
/// <param name="BookingId">Бронь.</param>
/// <param name="UserId">Клиент-получатель; у гостевой брони его нет и она не попадает в выборку.</param>
/// <param name="StartAt">Начало визита в UTC.</param>
/// <param name="ServiceName">Название услуги.</param>
/// <param name="SalonName">Название салона.</param>
/// <param name="BarberName">Имя мастера.</param>
/// <param name="SalonTimeZoneId">Пояс салона (IANA).</param>
/// <param name="BookingCreatedAt">Момент создания брони в UTC.</param>
public sealed record PushReminderBooking(
    Guid BookingId,
    Guid UserId,
    DateTimeOffset StartAt,
    string? ServiceName,
    string? SalonName,
    string? BarberName,
    string? SalonTimeZoneId,
    DateTimeOffset BookingCreatedAt);
