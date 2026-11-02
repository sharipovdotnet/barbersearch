using BarberSearch.Domain.Users;

namespace BarberSearch.Contracts.Users.Me;

/// <summary>
/// Профиль текущего пользователя для личного кабинета.
/// </summary>
/// <remarks>
/// Читается только владельцем: <c>GET api/users/me</c> берёт идентификатор из claims и не
/// принимает чужой id. В отличие от <c>GET api/auth/me</c>, ответ приходит из базы, а не из
/// токена, поэтому показывает актуальные имя, фамилию, телефон и подтверждения.
/// </remarks>
/// <param name="Id">Идентификатор пользователя.</param>
/// <param name="Email">Адрес электронной почты.</param>
/// <param name="FirstName">Имя.</param>
/// <param name="LastName">Фамилия.</param>
/// <param name="PhoneNumber">Телефон либо <c>null</c>.</param>
/// <param name="AvatarUrl">Ссылка на аватар либо <c>null</c>.</param>
/// <param name="Role">Роль пользователя.</param>
/// <param name="BarberProfileId">Привязанный профиль барбера либо <c>null</c>.</param>
/// <param name="IsEmailVerified">Подтверждён ли email.</param>
/// <param name="IsPhoneVerified">Подтверждён ли телефон.</param>
/// <param name="CreatedAt">Когда создан аккаунт.</param>
/// <param name="LastLoginAt">Последний вход либо <c>null</c>.</param>
public record MyProfileResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? AvatarUrl,
    UserRole Role,
    Guid? BarberProfileId,
    bool IsEmailVerified,
    bool IsPhoneVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);
