using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;

namespace BarberSearch.Application.BarberProfiles.GetOwnBarberProfile;

/// <summary>
/// Запрос собственной карточки барбера по идентификатору профиля.
/// </summary>
/// <remarks>
/// Отдельно от публичного <c>GetBarberProfileQuery</c>: тот читает только активный и
/// подтверждённый профиль, а кабинету нужна карточка любого статуса — барбер заполняет её
/// до верификации. Владельца проверяет обработчик.
/// </remarks>
/// <param name="BarberProfileId">Идентификатор профиля барбера.</param>
public record GetOwnBarberProfileQuery(Guid BarberProfileId)
    : IQuery<BarberProfileResponse>;
