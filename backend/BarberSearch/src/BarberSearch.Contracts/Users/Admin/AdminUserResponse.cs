using BarberSearch.Domain.Users;

namespace BarberSearch.Contracts.Users.Admin;

/// <summary>
/// Строка админского списка пользователей: роль, доступ и подтверждения.
/// </summary>
/// <remarks>
/// Отдельный контракт от профиля клиента: читать список пользователей может только
/// администратор, а в ответ входят email, роль и статус доступа. Публичных DTO
/// пользователей не существует, поэтому PII здесь не пересекается с витриной.
/// </remarks>
/// <param name="Id">Идентификатор пользователя.</param>
/// <param name="Email">Адрес электронной почты.</param>
/// <param name="FirstName">Имя.</param>
/// <param name="LastName">Фамилия.</param>
/// <param name="Role">Роль пользователя.</param>
/// <param name="IsActive">Открыт ли доступ к сервису.</param>
/// <param name="IsEmailVerified">Подтверждён ли email.</param>
/// <param name="IsPhoneVerified">Подтверждён ли телефон.</param>
/// <param name="BarberProfileId">Профиль барбера, если он привязан.</param>
/// <param name="LastLoginAt">Последний вход либо <c>null</c>, если пользователь не входил.</param>
/// <param name="CreatedAt">Когда пользователь создан.</param>
/// <param name="AvatarUrl">
/// Ссылка на аватар либо <c>null</c>, если он не загружен. Админская поверхность показывает
/// и скрытое фото: значение остаётся сырым, а состояние передаёт <paramref name="IsAvatarBlocked"/>.
/// </param>
/// <param name="IsAvatarBlocked">Скрыт ли аватар администратором.</param>
public record AdminUserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    UserRole Role,
    bool IsActive,
    bool IsEmailVerified,
    bool IsPhoneVerified,
    Guid? BarberProfileId,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    string? AvatarUrl,
    bool IsAvatarBlocked);
