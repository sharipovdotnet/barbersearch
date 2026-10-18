namespace BarberSearch.Contracts.Auth.Me;

/// <summary>
/// Текущий пользователь по данным аутентификации.
/// </summary>
/// <remarks>
/// Ответ строится из claims, а не из базы: он показывает ровно то, что видит сервер
/// в текущем токене или cookie. После смены роли старый токен продолжит отдавать старое значение
/// до истечения срока — это ожидаемо.
/// </remarks>
public record MeResponse(
    Guid Id,
    string Email,
    string Role,
    Guid? BarberProfileId,
    bool IsEmailVerified);
