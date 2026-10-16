using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Users.RecordUserLogin;

/// <summary>
/// Команда фиксации входа пользователя в систему.
/// </summary>
public record RecordUserLoginCommand(Guid UserId) : ICommand;
