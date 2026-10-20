namespace BarberSearch.Contracts.Reviews.CreateReview;

/// <summary>
/// Данные запроса на создание отзыва.
/// </summary>
/// <remarks>
/// Ни барбер, ни автор в запросе не передаются: барбер выводится из брони
/// <see cref="BookingId"/>, а автор — из токена. Поэтому привязать отзыв не к тому барберу
/// или оставить его от чужого имени невозможно.
/// </remarks>
public record CreateReviewRequest(
    Guid BookingId,
    int Rating,
    string? Text);
