using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.RemoveBarberProfileService;

/// <summary>
/// Команда удаления услуги из профиля барбера.
/// </summary>
public record RemoveBarberProfileServiceCommand(
    Guid BarberProfileId,
    Guid HairServiceId) : ICommand;
