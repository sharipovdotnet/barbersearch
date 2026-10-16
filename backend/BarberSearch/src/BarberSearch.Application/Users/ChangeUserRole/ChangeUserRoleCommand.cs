using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.Users;

namespace BarberSearch.Application.Users.ChangeUserRole;

/// <summary>
/// Команда смены роли пользователя.
/// </summary>
public record ChangeUserRoleCommand(Guid UserId, UserRole Role) : ICommand;
