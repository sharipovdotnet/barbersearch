using BarberSearch.Domain.Users;

namespace BarberSearch.Contracts.Users.CreateUser;

/// <summary>
/// Данные запроса на создание пользователя.
/// </summary>
/// <remarks>
/// Пароль приходит открытым текстом, а хеширование — задача слоя Web: в
/// <c>CreateUserCommand</c> уходит уже готовый хеш, поэтому ни домен, ни Application
/// пароля не видят. <see cref="Role"/> по умолчанию — <see cref="UserRole.Customer"/>:
/// роль <c>Admin</c> не должна выдаваться по умолчанию.
/// </remarks>
public record CreateUserRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    UserRole Role = UserRole.Customer);
