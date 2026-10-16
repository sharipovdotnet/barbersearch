using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Specializations.CreateSpecialization;

/// <summary>
/// Команда создания специализации.
/// </summary>
public record CreateSpecializationCommand(
    string Name,
    string? Description) : ICommand;
