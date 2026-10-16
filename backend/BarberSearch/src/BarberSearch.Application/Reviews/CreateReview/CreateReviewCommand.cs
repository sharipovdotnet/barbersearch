using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Reviews.CreateReview;

/// <summary>
/// Команда создания отзыва.
/// </summary>
/// <remarks>
/// Барбер и клиент в команде не передаются: и то и другое выводится из брони,
/// поэтому привязать отзыв не к тому барберу невозможно.
/// </remarks>
public record CreateReviewCommand(
    Guid BookingId,
    Guid UserId,
    int Rating,
    string? Text) : ICommand;
