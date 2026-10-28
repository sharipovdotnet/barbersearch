namespace BarberSearch.Contracts.Users.Admin;

/// <summary>
/// Профиль барбера, привязанный к пользователю: только то, что нужно админской карточке.
/// </summary>
/// <remarks>
/// Отдельная вложенная запись, а не <c>BarberProfileResponse</c>: публичный контракт
/// не должен тянуть PII и админские флаги, а карточке нужны имя и два статуса.
/// </remarks>
/// <param name="Id">Идентификатор профиля барбера.</param>
/// <param name="DisplayName">Отображаемое имя мастера.</param>
/// <param name="IsVerified">Подтверждён ли профиль администратором.</param>
/// <param name="IsActive">Виден ли профиль в поиске.</param>
public record AdminUserBarberProfileResponse(
    Guid Id,
    string DisplayName,
    bool IsVerified,
    bool IsActive);
