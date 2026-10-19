using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Reviews.CreateReview;

/// <summary>
/// Команда создания отзыва.
/// </summary>
/// <remarks>
/// Барбер и клиент в команде не передаются: барбер выводится из брони, а автор отзыва —
/// из токена (<c>ICurrentUser</c>), поэтому привязать отзыв не к тому барберу или оставить
/// его от чужого имени невозможно.
/// </remarks>
public record CreateReviewCommand(
    Guid BookingId,
    int Rating,
    string? Text) : ICommand;
