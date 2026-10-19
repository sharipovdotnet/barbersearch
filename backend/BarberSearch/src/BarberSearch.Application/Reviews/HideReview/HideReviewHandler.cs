using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Reviews;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Reviews.HideReview;

/// <summary>
/// Обработчик команды <see cref="HideReviewCommand"/>.
/// </summary>
/// <remarks>
/// Скрытие убирает отзыв из публичной сводки. Изменение отзыва и пересчёт рейтинга
/// выполняются одной транзакцией, а row lock на профиле сериализует конкурентные
/// публикации и скрытия.
/// </remarks>
public class HideReviewHandler : ICommandHandler<HideReviewCommand>
{
    private readonly IReviewRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ReviewRatingRecalculator _ratingRecalculator;
    private readonly ILogger<HideReviewHandler> _logger;

    public HideReviewHandler(
        IReviewRepository repository,
        IUnitOfWork unitOfWork,
        ReviewRatingRecalculator ratingRecalculator,
        ILogger<HideReviewHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _ratingRecalculator = ratingRecalculator;
        _logger = logger;
    }

    /// <summary>
    /// Загружает отзыв, скрывает его, сохраняет и пересчитывает рейтинг барбера.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        HideReviewCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _unitOfWork.ExecuteAsync(async token =>
        {
            var reviewResult = await _repository.GetByIdAsync(command.ReviewId, token);
            if (reviewResult.IsFailure)
            {
                return reviewResult.Error;
            }

            var review = reviewResult.Value;

            var hidden = review.Hide();
            if (hidden.IsFailure)
            {
                return hidden.Error;
            }

            var saved = await _repository.UpdateAsync(review, token);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            return await _ratingRecalculator.RecalculateAsync(review.BarberProfileId, token);
        }, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Review {ReviewId} has been hidden.", command.ReviewId);
        }

        return result;
    }
}
