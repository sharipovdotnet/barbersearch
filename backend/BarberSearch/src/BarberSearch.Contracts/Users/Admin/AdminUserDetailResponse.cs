using BarberSearch.Domain.Users;

namespace BarberSearch.Contracts.Users.Admin;

/// <summary>
/// Админская карточка пользователя: контакты, доступ, подтверждения и профиль барбера.
/// </summary>
/// <remarks>
/// Читается по прямому URL <c>GET api/admin/users/{id}</c> и не пересекается с публичными
/// контрактами. Телефон и аватар показываются администратору, потому что без них
/// поддержка не может связаться с человеком.
/// </remarks>
/// <param name="Id">Идентификатор пользователя.</param>
/// <param name="Email">Адрес электронной почты.</param>
/// <param name="FirstName">Имя.</param>
/// <param name="LastName">Фамилия.</param>
/// <param name="PhoneNumber">Телефон либо <c>null</c>.</param>
/// <param name="AvatarUrl">Ссылка на аватар либо <c>null</c>.</param>
/// <param name="Role">Роль пользователя.</param>
/// <param name="IsActive">Открыт ли доступ к сервису.</param>
/// <param name="IsEmailVerified">Подтверждён ли email.</param>
/// <param name="IsPhoneVerified">Подтверждён ли телефон.</param>
/// <param name="BarberProfileId">Идентификатор привязанного профиля барбера.</param>
/// <param name="BarberProfile">Сам профиль барбера либо <c>null</c>, если он не привязан.</param>
/// <param name="LastLoginAt">Последний вход либо <c>null</c>.</param>
/// <param name="LockoutEnd">Конец блокировки входа после неудачных попыток либо <c>null</c>.</param>
/// <param name="FailedLoginAttempts">Сколько неудачных попыток входа подряд записано.</param>
/// <param name="CreatedAt">Когда пользователь создан.</param>
/// <param name="UpdatedAt">Когда пользователь последний раз менялся.</param>
public record AdminUserDetailResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? AvatarUrl,
    UserRole Role,
    bool IsActive,
    bool IsEmailVerified,
    bool IsPhoneVerified,
    Guid? BarberProfileId,
    AdminUserBarberProfileResponse? BarberProfile,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? LockoutEnd,
    int FailedLoginAttempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
