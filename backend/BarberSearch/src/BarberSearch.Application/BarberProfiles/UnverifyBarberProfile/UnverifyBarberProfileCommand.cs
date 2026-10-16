using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.UnverifyBarberProfile;

/// <summary>
/// Команда снятия верификации профиля барбера.
/// </summary>
public record UnverifyBarberProfileCommand(Guid BarberProfileId) : ICommand;
