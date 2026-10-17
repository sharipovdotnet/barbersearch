namespace BarberSearch.Contracts.WorkingSchedules.SetWorkingScheduleWorkingHours;

/// <summary>
/// Данные запроса на изменение рабочих часов в расписании салона барбера.
/// </summary>
/// <remarks>
/// Идентификаторы профиля, салона и записи расписания приходят из маршрута: они задают
/// адрес внутри агрегата. Установка часов снимает признак выходного дня — это делает домен.
/// </remarks>
public record SetWorkingScheduleWorkingHoursRequest(
    TimeOnly StartTime,
    TimeOnly EndTime);
