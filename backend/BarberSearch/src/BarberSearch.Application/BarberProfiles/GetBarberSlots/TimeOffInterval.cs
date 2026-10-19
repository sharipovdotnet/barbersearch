namespace BarberSearch.Application.BarberProfiles.GetBarberSlots;

/// <summary>
/// Нерабочий интервал барбера, пересекающийся с запрошенным днём.
/// </summary>
/// <remarks>
/// Домен хранит отпуска в UTC (<c>DateTimeOffset</c>); сравнение со слотами идёт
/// по абсолютному времени, поэтому переход на летнее время не меняет смысл интервала.
/// </remarks>
/// <param name="StartAt">Начало интервала в UTC.</param>
/// <param name="EndAt">Окончание интервала в UTC.</param>
public sealed record TimeOffInterval(DateTimeOffset StartAt, DateTimeOffset EndAt);
