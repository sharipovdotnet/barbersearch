using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.BarberProfiles.Enums;

namespace BarberSearch.Application.BarberProfiles.ChangeBarberProfileGrade;

/// <summary>
/// Команда смены грейда барбера.
/// </summary>
/// <remarks>
/// Грейд — административное действие, поэтому команда отдельная от обновления профиля.
/// </remarks>
public record ChangeBarberProfileGradeCommand(
    Guid BarberProfileId,
    Grade Grade) : ICommand;
