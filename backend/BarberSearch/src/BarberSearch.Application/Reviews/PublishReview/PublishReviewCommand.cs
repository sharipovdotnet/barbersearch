using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Reviews.PublishReview;

/// <summary>
/// Команда публикации отзыва.
/// </summary>
public record PublishReviewCommand(Guid ReviewId) : ICommand;
