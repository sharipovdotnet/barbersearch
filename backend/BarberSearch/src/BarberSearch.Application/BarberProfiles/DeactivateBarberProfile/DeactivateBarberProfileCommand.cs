using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.DeactivateBarberProfile;

/// <summary>
/// Команда деактивации профиля барбера.
/// </summary>
public record DeactivateBarberProfileCommand(Guid BarberProfileId) : ICommand;
