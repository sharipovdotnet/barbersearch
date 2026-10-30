namespace BarberSearch.Contracts.Reviews.GetMyReviews;

/// <summary>
/// Барбер в отзыве автора: достаточно, чтобы подписать отзыв и дать ссылку на профиль.
/// </summary>
/// <param name="Id">Идентификатор профиля барбера — для перехода на <c>/barbers/{id}</c>.</param>
/// <param name="DisplayName">Отображаемое имя мастера.</param>
public record MyReviewBarberResponse(
    Guid Id,
    string DisplayName);
