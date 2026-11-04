using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.GetBarberBookingOptions;

namespace BarberSearch.Application.BarberProfiles.GetBarberBookingOptions;

/// <summary>
/// Запрос данных формы ручной записи: услуги и салоны собственного профиля барбера.
/// </summary>
/// <remarks>
/// Идентификатор профиля берётся из claim'а сессии на уровне контроллера:
/// query-параметра с чужим профилем нет, поэтому прочитать коллекции другого
/// барбера нельзя.
/// </remarks>
/// <param name="BarberProfileId">Идентификатор собственного профиля барбера.</param>
public record GetBarberBookingOptionsQuery(Guid BarberProfileId)
    : IQuery<BarberBookingOptionsResponse>;
