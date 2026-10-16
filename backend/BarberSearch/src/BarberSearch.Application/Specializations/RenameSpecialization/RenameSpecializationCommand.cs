using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Specializations.RenameSpecialization;

/// <summary>
/// Команда переименования специализации и смены её описания.
/// </summary>
public record RenameSpecializationCommand(
    Guid SpecializationId,
    string Name,
    string? Description) : ICommand;
