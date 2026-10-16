using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Bookings;
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
/// Уникальность отзыва на бронь обеспечивает <c>IReviewRepository.AddAsync</c> (и уникальный индекс БД).
/// </remarks>
public class CreateReviewHandler : ICommandHandler<Guid, CreateReviewCommand>
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IReviewRepository _reviewRepository;
    private readonly ILogger<CreateReviewHandler> _logger;

    public CreateReviewHandler(
        IBookingRepository bookingRepository,
        IReviewRepository reviewRepository,
        ILogger<CreateReviewHandler> logger)
    {
        _bookingRepository = bookingRepository;
        _reviewRepository = reviewRepository;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт отзыв по брони и возвращает его идентификатор.
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

        var review = Review.Create(
            bookingResult.Value,
            command.UserId,
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
