using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Common;

namespace BarberSearch.Contracts.BarberProfiles.Admin;

/// <summary>
/// Карточка барбера для админского экрана: контакты, статусы и готовность профиля.
/// </summary>
/// <remarks>
/// Отдельно от публичного <c>BarberProfileResponse</c>: здесь email и телефон владельца,
/// а также профили, которые публичная выдача не показывает — неподтверждённые и выключенные.
/// </remarks>
/// <param name="Id">Идентификатор профиля барбера.</param>
/// <param name="UserId">Идентификатор пользователя-владельца профиля.</param>
/// <param name="DisplayName">Отображаемое имя мастера.</param>
/// <param name="Email">Email владельца профиля.</param>
/// <param name="PhoneNumber">Телефон владельца профиля либо <c>null</c>.</param>
/// <param name="AvatarUrl">
/// Ссылка на аватар владельца либо <c>null</c>, если он не загружен. Скрытое и неодобренное
/// фото администратор видит; состояния — в <paramref name="IsAvatarBlocked"/>,
/// <paramref name="AvatarModerationStatus"/> и <paramref name="AvatarRejectionReason"/>.
/// </param>
/// <param name="Grade">Грейд барбера.</param>
/// <param name="Bio">Короткое описание о себе.</param>
/// <param name="YearsOfExperience">Стаж в годах.</param>
/// <param name="Rating">Средний рейтинг либо <c>null</c>, если опубликованных отзывов нет.</param>
/// <param name="ReviewsCount">Количество опубликованных отзывов.</param>
/// <param name="IsActive">Виден ли профиль в поиске.</param>
/// <param name="IsVerified">Подтверждён ли профиль администратором.</param>
/// <param name="Readiness">Чек-лист готовности профиля.</param>
/// <param name="CreatedAt">Когда профиль создан.</param>
/// <param name="UpdatedAt">Когда профиль последний раз менялся.</param>
/// <param name="Photos">
/// Все фотографии портфолио, включая скрытые, заблокированные и ещё не проверенные:
/// администратор должен видеть, что именно скрыто. Форма элемента — публичная
/// <see cref="BarberProfilePhotoResponse"/> с реальными <c>moderationStatus</c>
/// и <c>rejectionReason</c>.
/// </param>
/// <param name="IsAvatarBlocked">Скрыт ли аватар владельца администратором.</param>
/// <param name="AvatarModerationStatus">
/// Состояние проверки аватара владельца: ждёт решения, одобрен или отклонён.
/// </param>
/// <param name="AvatarRejectionReason">
/// Причина отказа по аватару либо <c>null</c>. Текст администратора; в аудит и логи не попадает.
/// </param>
public record AdminBarberDetailResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string Email,
    string? PhoneNumber,
    string? AvatarUrl,
    Grade Grade,
    string? Bio,
    int YearsOfExperience,
    decimal? Rating,
    int ReviewsCount,
    bool IsActive,
    bool IsVerified,
    AdminBarberReadinessResponse Readiness,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<BarberProfilePhotoResponse> Photos,
    bool IsAvatarBlocked,
    PhotoModerationStatus AvatarModerationStatus,
    string? AvatarRejectionReason);
