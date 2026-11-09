using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.Reviews;
using BarberSearch.Contracts.Reviews.UpdateReview;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Reviews.UpdateReview;

/// <summary>
/// Обработчик команды <see cref="UpdateReviewCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Правка опубликованного отзыва меняет среднюю оценку; правка его текста снимает отзыв
/// с публикации (<see cref="Domain.Reviews.Review.Update"/>), поэтому он выпадает из сводки
/// до нового решения администратора. Правка скрытого отзыва публичную сводку не трогает.
/// В обоих случаях сохранение отзыва и пересчёт профиля идут одной транзакцией, поэтому
/// отзыв не может остаться сохранённым без согласованного рейтинга.
/// </para>
/// <para>
/// Ответ несёт новый статус публикации: клиент не должен сам повторять доменное правило,
/// чтобы не показать «Опубликован» на отзыве, вернувшемся в очередь.
/// </para>
/// </remarks>
public class UpdateReviewHandler : ICommandHandler<UpdateReviewResponse, UpdateReviewCommand>
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
    public async Task<Result<UpdateReviewResponse, Failure>> Handle(
        UpdateReviewCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _unitOfWork.ExecuteAsync(async token =>
        {
            var reviewResult = await _repository.GetByIdAsync(command.ReviewId, token);
            if (reviewResult.IsFailure)
            {
                return Result.Failure<UpdateReviewResponse, Failure>(reviewResult.Error);
            }

            var review = reviewResult.Value;

            var allowed = _currentUser.EnsureCanManage(review.UserId);
            if (allowed.IsFailure)
            {
                return Result.Failure<UpdateReviewResponse, Failure>(allowed.Error);
            }

            var updated = review.Update(command.Rating, command.Text);
            if (updated.IsFailure)
            {
                return Result.Failure<UpdateReviewResponse, Failure>(updated.Error);
            }

            var saved = await _repository.UpdateAsync(review, token);
            if (saved.IsFailure)
            {
                return Result.Failure<UpdateReviewResponse, Failure>(saved.Error);
            }

            var recalculated = await _ratingRecalculator.RecalculateAsync(review.BarberProfileId, token);
            if (recalculated.IsFailure)
            {
                return Result.Failure<UpdateReviewResponse, Failure>(recalculated.Error);
            }

            return Result.Success<UpdateReviewResponse, Failure>(
                new UpdateReviewResponse(review.IsPublished, review.UpdatedAt));
        }, cancellationToken);

        if (result.IsSuccess)
        {
            if (result.Value.IsPublished)
            {
                _logger.LogInformation("Review {ReviewId} has been updated.", command.ReviewId);
            }
            else
            {
                _logger.LogInformation(
                    "Review {ReviewId} has been updated and returned to moderation.",
                    command.ReviewId);
            }
        }

        return result;
    }
}
