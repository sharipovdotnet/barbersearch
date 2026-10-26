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
/// <param name="BarberServiceId">
/// Идентификатор услуги барбера (<c>barber_services.id</c>), по которой считалась
/// длительность; <c>null</c>, если услуга не запрошена.
/// </param>
/// <param name="HairServiceId">
/// Идентификатор услуги справочника (<c>hair_services.id</c>); <c>null</c>, если услуга
/// не запрошена.
/// </param>
/// <param name="Date">Локальная дата салона, на которую считались слоты.</param>
/// <param name="TimeZoneId">IANA-идентификатор часового пояса салона.</param>
/// <param name="DayStart">Начало локального дня в UTC.</param>
/// <param name="DayEnd">Окончание локального дня в UTC (начало следующего дня).</param>
/// <param name="DurationMinutes">
/// Шаг сетки: длительность услуги из <c>barber_services</c> либо
/// <c>Constants.DEFAULT_SLOT_DURATION_MINUTES</c> (30 минут) по умолчанию.
/// Если задана услуга, она всегда читается из той же строки, чей
/// <paramref name="BarberServiceId"/> возвращается, поэтому пару можно передать в бронь.
/// </param>
/// <param name="IsWorkingDay">Есть ли у барбера рабочий день в этот день недели.</param>
/// <param name="Slots">Свободные слоты в порядке возрастания времени.</param>
public record BarberSlotsResponse(
    Guid BarberProfileId,
    Guid SalonId,
    Guid? BarberServiceId,
    Guid? HairServiceId,
    DateOnly Date,
    string TimeZoneId,
    DateTimeOffset DayStart,
    DateTimeOffset DayEnd,
    int DurationMinutes,
    bool IsWorkingDay,
    IReadOnlyList<BarberSlotResponse> Slots);
