using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Bookings;
using BarberSearch.Application.Common;
using BarberSearch.Application.Reviews;
using BarberSearch.Contracts.Common;
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
/// С R-35 действует пре-модерация: <see cref="Review.Create"/> ставит
/// <c>IsPublished = false</c>, поэтому создание отзыва не меняет публичную сводку барбера.
/// Рейтинг пересчитывается только при публикации/скрытии или правке опубликованного отзыва;
/// здесь профиль не читается и не блокируется.
/// </para>
/// </remarks>
public class CreateReviewHandler : ICommandHandler<Guid, CreateReviewCommand>
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IReviewRepository _reviewRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CreateReviewHandler> _logger;

    public CreateReviewHandler(
        IBookingRepository bookingRepository,
        IReviewRepository reviewRepository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ILogger<CreateReviewHandler> logger)
    {
        _bookingRepository = bookingRepository;
        _reviewRepository = reviewRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт скрытый отзыв по завершённой брони и возвращает его идентификатор.
    /// Отзыв ждёт модерации и на публичный рейтинг барбера не влияет.
    /// </summary>
    public async Task<Result<Guid, Failure>> Handle(
        CreateReviewCommand command,
        CancellationToken cancellationToken)
    {
        var userIdResult = _currentUser.RequireUserId();
        if (userIdResult.IsFailure)
        {
            return userIdResult.Error;
        }

        var currentUserId = userIdResult.Value;

        var result = await _unitOfWork.ExecuteAsync(async token =>
        {
            var bookingResult = await _bookingRepository.GetByIdAsync(command.BookingId, token);
            if (bookingResult.IsFailure)
            {
                return Result.Failure<Guid, Failure>(bookingResult.Error);
            }

            var booking = bookingResult.Value;

            // Сначала владение, потом состояние: чужой не должен узнать статус брони по коду ошибки.
            if (booking.UserId != currentUserId)
            {
                return Result.Failure<Guid, Failure>(new Failure(Error.Forbidden(
                    ApiErrorCodes.Auth.Forbidden,
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

            return Result.Success<Guid, Failure>(review.Value.Id);
        }, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Review {ReviewId} has been created and awaits moderation.",
                result.Value);
        }

        return result;
    }
}
