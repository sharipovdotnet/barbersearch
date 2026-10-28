namespace BarberSearch.Contracts.Reviews.Admin;

/// <summary>
/// Барбер в админском отзыве: публичное имя и текущая сводка рейтинга.
/// </summary>
/// <param name="Id">Идентификатор профиля барбера.</param>
/// <param name="DisplayName">Отображаемое имя мастера.</param>
/// <param name="Rating">Средний рейтинг либо <c>null</c>, если опубликованных отзывов нет.</param>
/// <param name="ReviewsCount">Количество опубликованных отзывов.</param>
/// <param name="IsActive">Виден ли профиль в поиске.</param>
/// <param name="IsVerified">Подтверждён ли профиль администратором.</param>
public record AdminReviewBarberResponse(
    Guid Id,
    string DisplayName,
    decimal? Rating,
    int ReviewsCount,
    bool IsActive,
    bool IsVerified);
