using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.Admin;

namespace BarberSearch.Application.BarberProfiles.Admin.GetAdminBarber;

/// <summary>
/// Запрос одного барбера для админской карточки.
/// </summary>
/// <param name="BarberProfileId">Идентификатор профиля барбера.</param>
public record GetAdminBarberQuery(Guid BarberProfileId)
    : IQuery<AdminBarberDetailResponse>;
