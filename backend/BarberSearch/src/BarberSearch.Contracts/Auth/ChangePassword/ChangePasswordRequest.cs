namespace BarberSearch.Contracts.Auth.ChangePassword;

/// <summary>
/// Смена пароля вошедшим пользователем. Пользователь берётся из токена, а не из тела.
/// </summary>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
