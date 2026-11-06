namespace BarberSearch.Contracts.Salons.GetSalonBarbers;

/// <summary>
/// Барбер, прикреплённый к салону: строка списка «Барберы салона» и подсказки поиска.
/// </summary>
/// <remarks>
/// Ответ доступен только роли <c>Barber</c> и только владельцу (или администратору)
/// салона. <see cref="UserId"/> нужен экрану, чтобы отличить владельца салона:
/// владение — это связь <c>Salon.OwnerUserId</c>, а не признак в привязке барбера.
/// <see cref="Email"/> — контакт владельца профиля; он не входит в публичные
/// контракты барбера, но нужен владельцу салона, чтобы различать одноимённых мастеров.
/// </remarks>
/// <param name="BarberProfileId">Идентификатор профиля барбера.</param>
/// <param name="UserId">Идентификатор учётной записи владельца профиля.</param>
/// <param name="DisplayName">Отображаемое имя мастера.</param>
/// <param name="Email">Адрес электронной почты владельца профиля.</param>
/// <param name="IsActive">Участвует ли профиль в поиске и записи.</param>
/// <param name="IsVerified">Подтверждён ли профиль администратором.</param>
public record SalonBarberResponse(
    Guid BarberProfileId,
    Guid UserId,
    string DisplayName,
    string Email,
    bool IsActive,
    bool IsVerified);
