namespace BarberSearch.Contracts.Reviews.CreateReview;

/// <summary>
/// Данные запроса на создание отзыва.
/// </summary>
/// <remarks>
/// Барбер в запросе не передаётся: он выводится из брони <see cref="BookingId"/>,
/// поэтому привязать отзыв не к тому барберу невозможно.
/// </remarks>
public record CreateReviewRequest(
    Guid BookingId,
    Guid UserId,
    int Rating,
    string? Text);
