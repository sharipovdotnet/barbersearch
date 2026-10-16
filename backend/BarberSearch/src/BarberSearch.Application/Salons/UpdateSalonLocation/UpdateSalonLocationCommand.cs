using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Salons.UpdateSalonLocation;

/// <summary>
/// Команда изменения адреса и координат салона.
/// </summary>
public record UpdateSalonLocationCommand(
    Guid SalonId,
    string Address,
    string City,
    string Country,
    string TimeZoneId,
    double Latitude,
    double Longitude,
    string? PostalCode) : ICommand;
