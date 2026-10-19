using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;

namespace BarberSearch.Application.BarberProfiles.GetBarberProfile;

/// <summary>
/// Запрос карточки барбера по идентификатору профиля.
/// </summary>
/// <param name="BarberProfileId">Идентификатор профиля барбера.</param>
public record GetBarberProfileQuery(Guid BarberProfileId)
    : IQuery<BarberProfileResponse>;
