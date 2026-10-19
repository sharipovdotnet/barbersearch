using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Bookings;
using BarberSearch.Application.Reviews;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Reviews;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Reviews.CreateReview;

/// <summary>
/// Обработчик команды <see cref="CreateReviewCommand"/>.
/// </summary>
/// <remarks>
/// Отзыв создаётся из брони: барбер берётся из неё, поэтому два репозитория — брони и отзывов.
/// Автор отзыва — клиент брони: идентификатор приходит из токена, а не из тела, поэтому
/// оставить отзыв от чужого имени нельзя. Администратор здесь не обходит проверку — иначе
/// появились бы фиктивные отзывы. Уникальность отзыва на бронь обеспечивает
/// <c>IReviewRepository.AddAsync</c> (и уникальный индекс БД).
/// <para>
/// <see cref="Review.Create"/> сразу ставит <c>IsPublished = true</c>, поэтому создание
/// отзыва меняет публичную сводку. Вставка отзыва и пересчёт профиля выполняются в одной
/// транзакции: ошибка пересчёта откатывает и отзыв.
/// </para>
/// </remarks>
public class CreateReviewHandler : ICommandHandler<Guid, CreateReviewCommand>
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IReviewRepository _reviewRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ReviewRatingRecalculator _ratingRecalculator;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CreateReviewHandler> _logger;

    public CreateReviewHandler(
        IBookingRepository bookingRepository,
        IReviewRepository reviewRepository,
        IUnitOfWork unitOfWork,
        ReviewRatingRecalculator ratingRecalculator,
        ICurrentUser currentUser,
        ILogger<CreateReviewHandler> logger)
    {
        _bookingRepository = bookingRepository;
        _reviewRepository = reviewRepository;
        _unitOfWork = unitOfWork;
        _ratingRecalculator = ratingRecalculator;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт отзыв по завершённой брони, пересчитывает рейтинг барбера и возвращает
    /// идентификатор отзыва. Обе записи фиксируются одной транзакцией.
    /// </summary>
    public async Task<Result<Guid, Failure>> Handle(
        CreateReviewCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _unitOfWork.ExecuteAsync(async token =>
        {
            var bookingResult = await _bookingRepository.GetByIdAsync(command.BookingId, token);
            if (bookingResult.IsFailure)
            {
                return Result.Failure<Guid, Failure>(bookingResult.Error);
            }

            var booking = bookingResult.Value;

            // Сначала владение, потом состояние: чужой не должен узнать статус брони по коду ошибки.
            if (booking.UserId != _currentUser.UserId)
            {
                return Result.Failure<Guid, Failure>(new Failure(Error.Forbidden(
                    "auth.forbidden",
                    "A review can only be created by the client of the booking.")));
            }

            if (booking.Status != BookingStatus.Completed)
            {
                return Result.Failure<Guid, Failure>(new Failure(Error.Conflict(
                    "review.create.bookingNotCompleted",
                    "A review can only be created for a completed booking.")));
            }

            var review = Review.Create(
                booking,
                booking.UserId,
                command.Rating,
                command.Text);
            if (review.IsFailure)
            {
                return Result.Failure<Guid, Failure>(review.Error);
            }

            var added = await _reviewRepository.AddAsync(review.Value, token);
            if (added.IsFailure)
            {
                return Result.Failure<Guid, Failure>(added.Error);
            }

            var recalculated = await _ratingRecalculator.RecalculateAsync(
                review.Value.BarberProfileId,
                token);
            if (recalculated.IsFailure)
            {
                return Result.Failure<Guid, Failure>(recalculated.Error);
            }

            return Result.Success<Guid, Failure>(review.Value.Id);
        }, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Review {ReviewId} has been created.", result.Value);
        }

        return result;
    }
}
