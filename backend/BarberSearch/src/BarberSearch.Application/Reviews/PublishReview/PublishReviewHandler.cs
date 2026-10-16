using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Reviews.PublishReview;

/// <summary>
/// Обработчик команды <see cref="PublishReviewCommand"/>.
/// </summary>
public class PublishReviewHandler : ICommandHandler<PublishReviewCommand>
{
    private readonly IReviewRepository _repository;
    private readonly ILogger<PublishReviewHandler> _logger;

    public PublishReviewHandler(IReviewRepository repository, ILogger<PublishReviewHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает отзыв, публикует его и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        PublishReviewCommand command,
        CancellationToken cancellationToken)
    {
        var reviewResult = await _repository.GetByIdAsync(command.ReviewId, cancellationToken);
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

        var saved = await _repository.UpdateAsync(review, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Review {ReviewId} has been published.", review.Id);

        return UnitResult.Success<Failure>();
    }
}
