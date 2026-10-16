using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.DeactivateUser;

/// <summary>
/// Команда деактивации пользователя.
/// </summary>
public record DeactivateUserCommand(Guid UserId) : ICommand;
