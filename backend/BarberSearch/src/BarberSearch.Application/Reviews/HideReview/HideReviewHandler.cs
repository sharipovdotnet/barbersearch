using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Reviews.HideReview;

/// <summary>
/// Обработчик команды <see cref="HideReviewCommand"/>.
/// </summary>
public class HideReviewHandler : ICommandHandler<HideReviewCommand>
{
    private readonly IReviewRepository _repository;
    private readonly ILogger<HideReviewHandler> _logger;

    public HideReviewHandler(IReviewRepository repository, ILogger<HideReviewHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает отзыв, скрывает его и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        HideReviewCommand command,
        CancellationToken cancellationToken)
    {
        var reviewResult = await _repository.GetByIdAsync(command.ReviewId, cancellationToken);
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

        var saved = await _repository.UpdateAsync(review, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Review {ReviewId} has been hidden.", review.Id);

        return UnitResult.Success<Failure>();
    }
}
