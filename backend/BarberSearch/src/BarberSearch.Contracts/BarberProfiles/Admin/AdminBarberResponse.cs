using BarberSearch.Domain.BarberProfiles.Enums;

namespace BarberSearch.Contracts.BarberProfiles.Admin;

/// <summary>
/// Строка админского списка барберов: кто ждёт проверки и кто уже виден в поиске.
/// </summary>
/// <remarks>
/// Отдельный контракт от публичной карточки: в ответ входит email владельца, а в список
/// попадают неподтверждённые и выключенные профили, которых публичная выдача не показывает.
/// Данные доступны только администратору.
/// </remarks>
/// <param name="Id">Идентификатор профиля барбера.</param>
/// <param name="DisplayName">Отображаемое имя мастера.</param>
/// <param name="Email">Email владельца профиля.</param>
/// <param name="Grade">Грейд барбера.</param>
/// <param name="Rating">Средний рейтинг либо <c>null</c>, если опубликованных отзывов нет.</param>
/// <param name="ReviewsCount">Количество опубликованных отзывов.</param>
/// <param name="IsActive">Виден ли профиль в поиске.</param>
/// <param name="IsVerified">Подтверждён ли профиль администратором.</param>
/// <param name="CreatedAt">Когда профиль создан.</param>
/// <param name="UpdatedAt">Когда профиль последний раз менялся.</param>
public record AdminBarberResponse(
    Guid Id,
    string DisplayName,
    string Email,
    Grade Grade,
    decimal? Rating,
    int ReviewsCount,
    bool IsActive,
    bool IsVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
