using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.Reviews;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Reviews.UpdateReview;

/// <summary>
/// Обработчик команды <see cref="UpdateReviewCommand"/>.
/// </summary>
/// <remarks>
/// Правка опубликованного отзыва меняет среднюю оценку; правка скрытого публичную сводку
/// не трогает. В обоих случаях сохранение отзыва и пересчёт профиля идут одной транзакцией,
/// поэтому отзыв не может остаться сохранённым без согласованного рейтинга.
/// </remarks>
public class UpdateReviewHandler : ICommandHandler<UpdateReviewCommand>
{
    private readonly IReviewRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ReviewRatingRecalculator _ratingRecalculator;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<UpdateReviewHandler> _logger;

    public UpdateReviewHandler(
        IReviewRepository repository,
        IUnitOfWork unitOfWork,
        ReviewRatingRecalculator ratingRecalculator,
        ICurrentUser currentUser,
        ILogger<UpdateReviewHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _ratingRecalculator = ratingRecalculator;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Загружает отзыв, применяет к нему правку, сохраняет отзыв и пересчитывает рейтинг.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        UpdateReviewCommand command,
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

            var allowed = _currentUser.EnsureCanManage(review.UserId);
            if (allowed.IsFailure)
            {
                return allowed.Error;
            }

            var updated = review.Update(command.Rating, command.Text);
            if (updated.IsFailure)
            {
                return updated.Error;
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
            _logger.LogInformation("Review {ReviewId} has been updated.", command.ReviewId);
        }

        return result;
    }
}
