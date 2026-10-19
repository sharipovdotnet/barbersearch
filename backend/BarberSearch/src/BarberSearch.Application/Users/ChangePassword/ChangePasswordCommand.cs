using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.ChangePassword;

/// <summary>
/// Смена пароля вошедшим пользователем. Пароль пользователя задаётся в claims,
/// а не в маршруте: чужой пароль сменить нельзя.
/// </summary>
public record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword) : ICommand;
