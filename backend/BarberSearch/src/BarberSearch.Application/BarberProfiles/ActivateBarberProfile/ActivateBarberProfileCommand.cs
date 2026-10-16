using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.ActivateBarberProfile;

/// <summary>
/// Команда активации профиля барбера.
/// </summary>
public record ActivateBarberProfileCommand(Guid BarberProfileId) : ICommand;
