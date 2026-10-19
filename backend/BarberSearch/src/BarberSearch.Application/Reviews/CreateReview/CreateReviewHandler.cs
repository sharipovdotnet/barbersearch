using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Bookings;
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
/// </remarks>
public class CreateReviewHandler : ICommandHandler<Guid, CreateReviewCommand>
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IReviewRepository _reviewRepository;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CreateReviewHandler> _logger;

    public CreateReviewHandler(
        IBookingRepository bookingRepository,
        IReviewRepository reviewRepository,
        ICurrentUser currentUser,
        ILogger<CreateReviewHandler> logger)
    {
        _bookingRepository = bookingRepository;
        _reviewRepository = reviewRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт отзыв по завершённой брони и возвращает его идентификатор.
    /// </summary>
    public async Task<Result<Guid, Failure>> Handle(
        CreateReviewCommand command,
        CancellationToken cancellationToken)
    {
        var bookingResult = await _bookingRepository.GetByIdAsync(command.BookingId, cancellationToken);
        if (bookingResult.IsFailure)
        {
            return bookingResult.Error;
        }

        var booking = bookingResult.Value;

        // Сначала владение, потом состояние: чужой не должен узнать статус брони по коду ошибки.
        if (booking.UserId != _currentUser.UserId)
        {
            return new Failure(Error.Forbidden(
                "auth.forbidden",
                "A review can only be created by the client of the booking."));
        }

        if (booking.Status != BookingStatus.Completed)
        {
            return new Failure(Error.Conflict(
                "review.create.bookingNotCompleted",
                "A review can only be created for a completed booking."));
        }

        var review = Review.Create(
            booking,
            booking.UserId,
            command.Rating,
            command.Text);
        if (review.IsFailure)
        {
            return review.Error;
        }

        var added = await _reviewRepository.AddAsync(review.Value, cancellationToken);
        if (added.IsFailure)
        {
            return added.Error;
        }

        _logger.LogInformation("Review {ReviewId} has been created.", review.Value.Id);

        return review.Value.Id;
    }
}
