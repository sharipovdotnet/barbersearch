namespace BarberSearch.Contracts.BarberSalons.AddBarberSalonWorkingSchedule;

/// <summary>
/// Данные запроса на добавление рабочего дня в расписание салона барбера.
/// </summary>
/// <remarks>
/// Идентификаторы профиля и салона приходят из маршрута: они задают адрес внутри агрегата.
/// Время — местное время салона; порядок границ проверяет домен.
/// </remarks>
public record AddBarberSalonWorkingScheduleRequest(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);
