using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Reviews.HideReview;

/// <summary>
/// Команда скрытия отзыва.
/// </summary>
public record HideReviewCommand(Guid ReviewId) : ICommand;
