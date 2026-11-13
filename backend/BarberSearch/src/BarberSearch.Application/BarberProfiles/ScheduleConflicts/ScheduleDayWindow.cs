namespace BarberSearch.Application.BarberProfiles.ScheduleConflicts;

/// <summary>
/// Эффективные часы одного календарного дня в одном салоне — вход проверки конфликтов.
/// </summary>
/// <remarks>
/// Окно строится из правки расписания, а не из текущего состояния: при проверке недели
/// это новые часы изменённого дня недели, при исключении — часы самой даты. <see cref="SalonId"/>
/// — это <c>Salon.Id</c> (агрегат салона), а не <c>BarberSalon.Id</c>: бронь хранит именно
/// идентификатор салона, и окно обязано совпадать с ним.
/// </remarks>
/// <param name="SalonId">Салон, к которому относится день.</param>
/// <param name="Date">Локальная дата в поясе салона.</param>
/// <param name="IsDayOff">Выходной: проверяются все записи даты.</param>
/// <param name="Start">Начало своих часов; для выходного не используется.</param>
/// <param name="End">Окончание своих часов; для выходного не используется.</param>
/// <param name="TimeZoneId">Идентификатор пояса салона (IANA или исторический Windows).</param>
public sealed record ScheduleDayWindow(
    Guid SalonId,
    DateOnly Date,
    bool IsDayOff,
    TimeOnly Start,
    TimeOnly End,
    string TimeZoneId);
