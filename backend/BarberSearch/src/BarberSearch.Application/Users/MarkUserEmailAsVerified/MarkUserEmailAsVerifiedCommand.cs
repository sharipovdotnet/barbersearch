using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.MarkUserEmailAsVerified;

/// <summary>
/// Команда подтверждения адреса электронной почты пользователя.
/// </summary>
public record MarkUserEmailAsVerifiedCommand(Guid UserId) : ICommand;
