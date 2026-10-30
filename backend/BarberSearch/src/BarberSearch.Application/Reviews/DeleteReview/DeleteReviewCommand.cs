using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Reviews.DeleteReview;

/// <summary>
/// Команда удаления своего отзыва.
/// </summary>
/// <remarks>
/// Удалять может только автор: администратор скрывает отзыв через
/// <c>DELETE api/reviews/{id}/publication</c>, а не удаляет его след.
/// </remarks>
/// <param name="ReviewId">Идентификатор отзыва.</param>
public record DeleteReviewCommand(Guid ReviewId) : ICommand;
