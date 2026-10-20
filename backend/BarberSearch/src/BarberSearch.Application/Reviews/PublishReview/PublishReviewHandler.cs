using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Reviews;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Reviews.PublishReview;

/// <summary>
/// Обработчик команды <see cref="PublishReviewCommand"/>.
/// </summary>
/// <remarks>
/// Публикация добавляет отзыв в публичную сводку. Изменение отзыва и пересчёт рейтинга
/// выполняются одной транзакцией, а row lock на профиле не даёт двум параллельным
/// публикациям потерять обновление.
/// </remarks>
public class PublishReviewHandler : ICommandHandler<PublishReviewCommand>
{
    private readonly IReviewRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ReviewRatingRecalculator _ratingRecalculator;
    private readonly ILogger<PublishReviewHandler> _logger;

    public PublishReviewHandler(
        IReviewRepository repository,
        IUnitOfWork unitOfWork,
        ReviewRatingRecalculator ratingRecalculator,
        ILogger<PublishReviewHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _ratingRecalculator = ratingRecalculator;
        _logger = logger;
    }

    /// <summary>
    /// Загружает отзыв, публикует его, сохраняет и пересчитывает рейтинг барбера.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        PublishReviewCommand command,
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

            var published = review.Publish();
            if (published.IsFailure)
            {
                return published.Error;
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
            _logger.LogInformation("Review {ReviewId} has been published.", command.ReviewId);
        }

        return result;
    }
}
