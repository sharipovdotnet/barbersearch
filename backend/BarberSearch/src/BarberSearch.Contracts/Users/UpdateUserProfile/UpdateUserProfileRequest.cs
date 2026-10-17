namespace BarberSearch.Contracts.Users.UpdateUserProfile;

/// <summary>
/// Данные запроса на обновление профиля пользователя.
/// </summary>
/// <remarks>
/// Email здесь нет: смена адреса — отдельный сценарий с подтверждением, а не правка профиля.
/// <see cref="PhoneNumber"/> и <see cref="AvatarUrl"/> nullable, чтобы значение можно было очистить.
/// </remarks>
public record UpdateUserProfileRequest(
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? AvatarUrl);
