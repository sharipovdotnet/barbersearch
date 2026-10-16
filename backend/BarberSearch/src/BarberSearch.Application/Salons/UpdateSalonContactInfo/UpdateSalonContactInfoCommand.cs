using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Salons.UpdateSalonContactInfo;

/// <summary>
/// Команда изменения контактных данных салона.
/// </summary>
public record UpdateSalonContactInfoCommand(
    Guid SalonId,
    string? PhoneNumber,
    string? WebsiteUrl) : ICommand;
