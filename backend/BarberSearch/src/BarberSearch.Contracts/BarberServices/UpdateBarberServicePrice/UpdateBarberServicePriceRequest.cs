namespace BarberSearch.Contracts.BarberServices.UpdateBarberServicePrice;

/// <summary>
/// Данные запроса на изменение цены услуги барбера.
/// </summary>
/// <remarks>
/// Идентификаторы профиля и услуги приходят из маршрута: они задают адрес внутри агрегата.
/// Допустимый диапазон цены проверяет домен.
/// </remarks>
public record UpdateBarberServicePriceRequest(decimal Price);
