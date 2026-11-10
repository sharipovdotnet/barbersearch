namespace BarberSearch.Contracts.BarberProfiles.UpdateBarberProfileService;

/// <summary>
/// Данные правки услуги в прайсе барбера: цена и длительность.
/// </summary>
/// <remarks>
/// Услуга адресуется парой «профиль + услуга справочника» в маршруте, поэтому в теле только
/// изменяемые поля. Скрытие услуги — отдельный <c>DELETE</c> (деактивация), а не отсутствие
/// строки в этом запросе: правка прайса не меняет видимость услуги.
/// </remarks>
/// <param name="Price">Цена для конкретного барбера; не может быть отрицательной.</param>
/// <param name="DurationMinutes">Длительность услуги в минутах, от 1 до 1440.</param>
public record UpdateBarberProfileServiceRequest(
    decimal Price,
    int DurationMinutes);
