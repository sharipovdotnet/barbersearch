namespace BarberSearch.Contracts.Salons.UpdateSalonLocation;

/// <summary>
/// Данные запроса на изменение адреса и координат салона.
/// </summary>
/// <remarks>
/// <see cref="TimeZoneId"/> — идентификатор зоны IANA: слоты записи считаются в местном
/// времени салона, а хранятся в UTC.
/// </remarks>
public record UpdateSalonLocationRequest(
    string Address,
    string City,
    string Country,
    string TimeZoneId,
    double Latitude,
    double Longitude,
    string? PostalCode);
