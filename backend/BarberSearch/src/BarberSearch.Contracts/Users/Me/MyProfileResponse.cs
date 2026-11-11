using BarberSearch.Domain.Common;
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
/// <param name="AvatarUrl">
/// Ссылка на аватар либо <c>null</c>. Ссылка сохраняется, даже если фото скрыто
/// администратором или ещё не одобрено; показывать её можно при
/// <see cref="IsAvatarBlocked"/> == <c>false</c> и статусе <c>Approved</c>/<c>Pending</c>.
/// </param>
/// <param name="Role">Роль пользователя.</param>
/// <param name="BarberProfileId">Привязанный профиль барбера либо <c>null</c>.</param>
/// <param name="IsEmailVerified">Подтверждён ли email.</param>
/// <param name="IsPhoneVerified">Подтверждён ли телефон.</param>
/// <param name="CreatedAt">Когда создан аккаунт.</param>
/// <param name="LastLoginAt">Последний вход либо <c>null</c>.</param>
/// <param name="IsAvatarBlocked">
/// Скрыт ли аватар администратором. Сырой <see cref="AvatarUrl"/> отдаётся всегда, чтобы
/// сохранение имени не стёрло фото; флаг — один из двух признаков того, что показывать его нельзя.
/// </param>
/// <param name="AvatarModerationStatus">
/// Допуск аватара к публикации. Кабинет показывает кадр при <c>Approved</c> и
/// <c>Pending</c> (превью с бейджем «На проверке»), при <c>Rejected</c> — инициалы.
/// </param>
/// <param name="AvatarRejectionReason">
/// Причина отказа либо <c>null</c>. Текст администратора; в аудит и логи не попадает.
/// </param>
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
    DateTimeOffset? LastLoginAt,
    bool IsAvatarBlocked,
    PhotoModerationStatus AvatarModerationStatus,
    string? AvatarRejectionReason);
