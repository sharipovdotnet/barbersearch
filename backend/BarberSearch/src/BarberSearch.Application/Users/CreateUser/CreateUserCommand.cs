using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.Users;

namespace BarberSearch.Application.Users.CreateUser;

/// <summary>
/// Команда создания пользователя.
/// </summary>
/// <remarks>
/// Хеш пароля, а не сам пароль: хеширование — задача слоя выше, домен получает готовый хеш.
/// </remarks>
public record CreateUserCommand(
    string Email,
    string PasswordHash,
    string FirstName,
    string LastName,
    UserRole Role = UserRole.Customer) : ICommand;
