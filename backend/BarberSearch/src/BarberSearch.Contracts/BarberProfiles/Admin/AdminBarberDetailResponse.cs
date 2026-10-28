using BarberSearch.Domain.BarberProfiles.Enums;

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
/// <param name="AvatarUrl">Ссылка на аватар владельца либо <c>null</c>.</param>
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
    DateTimeOffset UpdatedAt);
