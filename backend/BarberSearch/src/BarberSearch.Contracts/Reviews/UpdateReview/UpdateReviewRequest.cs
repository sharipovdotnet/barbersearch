namespace BarberSearch.Contracts.Reviews.UpdateReview;

/// <summary>
/// Данные запроса на правку отзыва.
/// </summary>
/// <remarks>
/// Автор и бронь не меняются: правка касается только оценки и текста.
/// </remarks>
public record UpdateReviewRequest(
    int Rating,
    string? Text);
