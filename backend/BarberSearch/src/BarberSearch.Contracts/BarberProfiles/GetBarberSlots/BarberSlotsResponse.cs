namespace BarberSearch.Contracts.BarberProfiles.GetBarberSlots;

/// <summary>
/// Свободные слоты барбера на дату с явными границами дня и часовым поясом.
/// </summary>
/// <remarks>
/// День считается в часовом поясе салона (<see cref="TimeZoneId"/>): <see cref="DayStart"/>
/// и <see cref="DayEnd"/> — уже переведённые в UTC границы локального дня. Слоты
/// <see cref="Slots"/> лежат внутри этих границ и не пересекаются с отпусками
/// и активными бронями. Пустой список означает «в этот день нет свободного времени»,
/// а не ошибку.
/// </remarks>
/// <param name="BarberProfileId">Идентификатор профиля барбера.</param>
/// <param name="SalonId">Идентификатор салона, в котором считались слоты.</param>
/// <param name="Date">Локальная дата салона, на которую считались слоты.</param>
/// <param name="TimeZoneId">IANA-идентификатор часового пояса салона.</param>
/// <param name="DayStart">Начало локального дня в UTC.</param>
/// <param name="DayEnd">Окончание локального дня в UTC (начало следующего дня).</param>
/// <param name="DurationMinutes">Шаг сетки: длительность услуги либо 30 минут по умолчанию.</param>
/// <param name="IsWorkingDay">Есть ли у барбера рабочий день в этот день недели.</param>
/// <param name="Slots">Свободные слоты в порядке возрастания времени.</param>
public record BarberSlotsResponse(
    Guid BarberProfileId,
    Guid SalonId,
    DateOnly Date,
    string TimeZoneId,
    DateTimeOffset DayStart,
    DateTimeOffset DayEnd,
    int DurationMinutes,
    bool IsWorkingDay,
    IReadOnlyList<BarberSlotResponse> Slots);
