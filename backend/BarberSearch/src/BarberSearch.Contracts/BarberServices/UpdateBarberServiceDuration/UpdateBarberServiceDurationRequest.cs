namespace BarberSearch.Contracts.BarberServices.UpdateBarberServiceDuration;

/// <summary>
/// Данные запроса на изменение длительности услуги барбера.
/// </summary>
/// <remarks>
/// Идентификаторы профиля и услуги приходят из маршрута: они задают адрес внутри агрегата.
/// Допустимый диапазон длительности проверяет домен.
/// </remarks>
public record UpdateBarberServiceDurationRequest(int DurationMinutes);
