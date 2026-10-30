namespace BarberSearch.Contracts.Bookings.Barber;

/// <summary>
/// Услуга в записи барбера.
/// </summary>
/// <remarks>
/// Барберу нужно название услуги из справочника; идентификаторы и длительность ему
/// не нужны — он сам предоставляет эту услугу.
/// </remarks>
/// <param name="Name">Название услуги из справочника <c>hair_services</c>.</param>
public record BarberBookingServiceResponse(string Name);
