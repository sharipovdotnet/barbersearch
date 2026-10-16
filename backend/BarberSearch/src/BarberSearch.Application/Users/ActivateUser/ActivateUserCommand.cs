using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.ActivateUser;

/// <summary>
/// Команда активации пользователя.
/// </summary>
public record ActivateUserCommand(Guid UserId) : ICommand;
