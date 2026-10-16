using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Reviews.UpdateReview;

/// <summary>
/// Обработчик команды <see cref="UpdateReviewCommand"/>.
/// </summary>
public class UpdateReviewHandler : ICommandHandler<UpdateReviewCommand>
{
    private readonly IReviewRepository _repository;
    private readonly ILogger<UpdateReviewHandler> _logger;

    public UpdateReviewHandler(IReviewRepository repository, ILogger<UpdateReviewHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает отзыв, применяет к нему правку и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UpdateReviewCommand command,
        CancellationToken cancellationToken)
    {
        var reviewResult = await _repository.GetByIdAsync(command.ReviewId, cancellationToken);
        if (reviewResult.IsFailure)
        {
            return reviewResult.Error;
        }

        var review = reviewResult.Value;

        var updated = review.Update(command.Rating, command.Text);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(review, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Review {ReviewId} has been updated.", review.Id);

        return UnitResult.Success<Failure>();
    }
}
