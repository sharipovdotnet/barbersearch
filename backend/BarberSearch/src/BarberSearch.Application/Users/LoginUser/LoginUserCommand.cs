using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.LoginUser;

/// <summary>
/// Вход в систему по адресу электронной почты и паролю.
/// </summary>
public record LoginUserCommand(string Email, string Password) : ICommand;
