using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Specializations.DeactivateSpecialization;

/// <summary>
/// Команда деактивации специализации.
/// </summary>
public record DeactivateSpecializationCommand(Guid SpecializationId) : ICommand;
