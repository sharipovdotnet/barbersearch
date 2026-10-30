using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Reviews.DeleteReview;

/// <summary>
/// Обработчик удаления своего отзыва.
/// </summary>
/// <remarks>
/// Удаление жёсткое: уникальный индекс <c>ux_reviews_booking_id</c> освобождается, и автор
/// снова может оставить отзыв по той же брони. Владелец проверяется строго по
/// <c>review.UserId</c>: <c>EnsureCanManage</c> здесь не подходит, потому что дал бы
/// администратору удалять чужие отзывы в обход скрытия.
/// <para>
/// Удаление опубликованного отзыва меняет публичную сводку, поэтому идёт одной транзакцией
/// с пересчётом рейтинга. Скрытый отзыв в сводку не входит — пересчёт не нужен.
/// </para>
/// </remarks>
public sealed class DeleteReviewHandler : ICommandHandler<DeleteReviewCommand>
{
    private const string FORBIDDEN_MESSAGE =
        "Only the author can delete this review.";

    private readonly IReviewRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ReviewRatingRecalculator _ratingRecalculator;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<DeleteReviewHandler> _logger;

    public DeleteReviewHandler(
        IReviewRepository repository,
        IUnitOfWork unitOfWork,
        ReviewRatingRecalculator ratingRecalculator,
        ICurrentUser currentUser,
        ILogger<DeleteReviewHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(ratingRecalculator);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _unitOfWork = unitOfWork;
        _ratingRecalculator = ratingRecalculator;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Загружает отзыв, проверяет автора, удаляет отзыв и пересчитывает рейтинг,
    /// если отзыв был опубликован.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        DeleteReviewCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await _unitOfWork.ExecuteAsync(async token =>
        {
            var reviewResult = await _repository.GetByIdAsync(command.ReviewId, token);
            if (reviewResult.IsFailure)
            {
                return reviewResult.Error;
            }

            var review = reviewResult.Value;

            var userIdResult = _currentUser.RequireUserId();
            if (userIdResult.IsFailure)
            {
                return userIdResult.Error;
            }

            if (review.UserId != userIdResult.Value)
            {
                return new Failure(Error.Forbidden(
                    ApiErrorCodes.Auth.Forbidden,
                    FORBIDDEN_MESSAGE));
            }

            // Запоминаем до удаления: после него сущности уже не из чего читать.
            var barberProfileId = review.BarberProfileId;
            var wasPublished = review.IsPublished;

            var deleted = await _repository.DeleteAsync(review, token);
            if (deleted.IsFailure)
            {
                return deleted.Error;
            }

            return wasPublished
                ? await _ratingRecalculator.RecalculateAsync(barberProfileId, token)
                : UnitResult.Success<Failure>();
        }, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Review {ReviewId} has been deleted.", command.ReviewId);
        }

        return result;
    }
}
