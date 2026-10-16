using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.MarkUserPhoneAsVerified;

/// <summary>
/// Команда подтверждения номера телефона пользователя.
/// </summary>
public record MarkUserPhoneAsVerifiedCommand(Guid UserId) : ICommand;
