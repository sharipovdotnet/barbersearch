using System.Net;
using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.Users;

namespace BarberSearch.Application.Users.CreateUser;

/// <summary>
/// Команда создания пользователя.
/// </summary>
/// <remarks>
/// Хеш пароля, а не сам пароль: хеширование — задача слоя выше, домен получает готовый хеш.
/// <para>
/// <c>RequireConsent</c> отделяет публичную регистрацию от административного создания:
/// администратор не получает согласие за человека, поэтому у такого аккаунта записи нет.
/// <c>ClientIp</c> читает Web-контроллер: Application не знает про <c>HttpContext</c>.
/// </para>
/// </remarks>
public record CreateUserCommand(
    string Email,
    string PasswordHash,
    string FirstName,
    string LastName,
    UserRole Role = UserRole.Customer,
    string? ConsentVersion = null,
    IPAddress? ClientIp = null,
    bool RequireConsent = false) : ICommand;
