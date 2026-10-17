using BarberSearch.Domain.Users;

namespace BarberSearch.Contracts.Users.ChangeUserRole;

/// <summary>
/// Данные запроса на смену роли пользователя.
/// </summary>
/// <remarks>
/// Допустимость значения проверяет домен: неизвестное число в <see cref="Role"/>
/// возвращается ошибкой валидации, а не сохраняется в базу.
/// </remarks>
public record ChangeUserRoleRequest(UserRole Role);
