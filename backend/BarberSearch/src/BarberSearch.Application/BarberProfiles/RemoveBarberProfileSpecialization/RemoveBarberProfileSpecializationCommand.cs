using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.RemoveBarberProfileSpecialization;

/// <summary>
/// Команда удаления специализации из профиля барбера.
/// </summary>
public record RemoveBarberProfileSpecializationCommand(
    Guid BarberProfileId,
    Guid SpecializationId) : ICommand;
