using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.CreateBarberProfile;

namespace BarberSearch.Application.BarberProfiles.CreateBarberProfile;

/// <summary>
/// Команда создания профиля барбера.
/// </summary>
/// <remarks>
/// Состав полей описан контрактом <see cref="BarberProfileCreateRequest"/> и не дублируется:
/// так набор для создания остаётся ровно один и меняется только в контракте.
/// </remarks>
public record CreateBarberProfileCommand(BarberProfileCreateRequest Request) : ICommand;
