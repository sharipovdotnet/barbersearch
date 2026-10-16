using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Reviews.UpdateReview;

/// <summary>
/// Команда правки отзыва.
/// </summary>
public record UpdateReviewCommand(
    Guid ReviewId,
    int Rating,
    string? Text) : ICommand;
