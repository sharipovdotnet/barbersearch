namespace BarberSearch.Application.BarberProfiles.GetBarberSlots;

/// <summary>
/// Данные для расчёта слотов: выбранный салон, его таймзона, расписание и длительность.
/// </summary>
/// <remarks>
/// Репозиторий отдаёт только факты из базы, а сетку слотов, границы дня и исключения
/// считает обработчик. <see cref="StartTime"/> и <see cref="EndTime"/> имеют смысл только
/// при <see cref="IsWorkingDay"/> = <c>true</c>.
/// </remarks>
/// <param name="BarberProfileId">Идентификатор профиля барбера.</param>
/// <param name="SalonId">Идентификатор активного салона.</param>
/// <param name="TimeZoneId">IANA-идентификатор часового пояса салона.</param>
/// <param name="Date">Локальная дата салона.</param>
/// <param name="IsWorkingDay">Есть ли в расписании рабочий день на эту дату.</param>
/// <param name="StartTime">Начало рабочего дня по локальному времени салона.</param>
/// <param name="EndTime">Окончание рабочего дня по локальному времени салона.</param>
/// <param name="DurationMinutes">
/// Шаг сетки: <c>BarberService.DurationMinutes</c> выбранной услуги либо
/// <c>Constants.DEFAULT_SLOT_DURATION_MINUTES</c> (30 минут), когда услуга не задана.
/// </param>
/// <param name="BarberServiceId">
/// Строка <c>barber_services</c>, из которой взята длительность; <c>null</c> — услуга не задана.
/// </param>
/// <param name="HairServiceId">
/// Услуга справочника, по которой фильтровались слоты; <c>null</c> — услуга не задана.
/// </param>
/// <param name="TimeOffs">Отпуска барбера, пересекающиеся с окном вокруг даты.</param>
public sealed record BarberSlotContext(
    Guid BarberProfileId,
    Guid SalonId,
    string TimeZoneId,
    DateOnly Date,
    bool IsWorkingDay,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int DurationMinutes,
    Guid? BarberServiceId,
    Guid? HairServiceId,
    IReadOnlyList<TimeOffInterval> TimeOffs);
