namespace BarberSearch.Application.BarberProfiles.GetBarberSlots;

/// <summary>
/// Занятый интервал барбера: активная бронь, которая не даёт поставить визит.
/// </summary>
/// <remarks>
/// <para>
/// Хранится в UTC, как и <see cref="TimeOffInterval"/>: сравнение идёт по абсолютному
/// времени, поэтому переход на летнее время не меняет смысл интервала. В сетку слотов
/// занятость и отпуска попадают одним списком препятствий — для доступности это одно и то же.
/// </para>
/// <para>
/// Отпуск — это <see cref="TimeOffInterval"/>, бронь — <see cref="BusyInterval"/>: два имени
/// описывают разные причины, хотя форма у них одна.
/// </para>
/// </remarks>
/// <param name="StartAt">Начало занятости в UTC.</param>
/// <param name="EndAt">Окончание занятости в UTC.</param>
public sealed record BusyInterval(DateTimeOffset StartAt, DateTimeOffset EndAt);
