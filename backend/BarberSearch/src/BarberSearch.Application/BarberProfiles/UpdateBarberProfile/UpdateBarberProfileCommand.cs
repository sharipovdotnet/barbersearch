using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfile;

namespace BarberSearch.Application.BarberProfiles.UpdateBarberProfile;

/// <summary>
/// Команда обновления профиля барбера.
/// </summary>
/// <remarks>
/// Состав полей описан контрактом <see cref="BarberProfileUpdateRequest"/> и не дублируется.
/// Грейда в данных нет: он меняется только административным сценарием
/// (<c>ChangeBarberProfileGradeCommand</c>).
/// </remarks>
public record UpdateBarberProfileCommand(
    Guid BarberProfileId,
    BarberProfileUpdateRequest Request) : ICommand;
