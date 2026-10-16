using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.RemoveBarberProfileSalon;

/// <summary>
/// Команда удаления салона из профиля барбера.
/// </summary>
public record RemoveBarberProfileSalonCommand(
    Guid BarberProfileId,
    Guid SalonId) : ICommand;
