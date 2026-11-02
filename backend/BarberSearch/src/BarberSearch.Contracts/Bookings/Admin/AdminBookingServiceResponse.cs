namespace BarberSearch.Contracts.Bookings.Admin;

/// <summary>
/// Услуга админской брони.
/// </summary>
/// <remarks>
/// Название берётся из справочника услуг через связь «барбер — услуга»; цена визита лежит
/// в брони (<c>Price</c>) и не дублируется здесь, потому что это разные величины: снимок
/// на момент записи и текущий прайс.
/// </remarks>
/// <param name="Name">Название услуги из справочника.</param>
public record AdminBookingServiceResponse(string Name);
