using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Specializations.ActivateSpecialization;

/// <summary>
/// Команда активации специализации.
/// </summary>
public record ActivateSpecializationCommand(Guid SpecializationId) : ICommand;
