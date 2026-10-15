using System;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Reviews;

/// <summary>
/// Отзыв — агрегатный корень. Всегда привязан к брони: и барбер, и клиент
/// выводятся из неё, поэтому привязать отзыв не к тому барберу невозможно.
/// </summary>
public class Review : AggregateRoot
{
    /// <summary>
    /// Барбер, которому оставлен отзыв. Денормализовано из брони — нужно для поиска,
    /// но задать значение снаружи нельзя.
    /// </summary>
    public Guid BarberProfileId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid BookingId { get; private set; }

    public int Rating { get; private set; }

    public string? Text { get; private set; }

    public bool IsPublished { get; private set; }

    private Review()
    {
    }

    public static Result<Review, Failure> Create(
        Booking booking,
        Guid userId,
        int rating,
        string? text)
    {
        ArgumentNullException.ThrowIfNull(booking);

        if (rating is < Constants.MIN_REVIEW_RATING or > Constants.MAX_REVIEW_RATING)
        {
            return new Failure(Error.Validation(
                "review.rating",
                $"""
                 Rating must be between {Constants.MIN_REVIEW_RATING} 
                 and {Constants.MAX_REVIEW_RATING}, got {rating}.
                 """,
                nameof(rating)));
        }

        if (text is not null && text.Length > Constants.MAX_TEXT_LENGTH)
        {
            return new Failure(Error.Validation(
                "review.text.tooLong",
                $"Review text cannot be longer than {Constants.MAX_TEXT_LENGTH} characters.",
                nameof(text)));
        }

        return new Review
        {
            BookingId = booking.Id,
            BarberProfileId = booking.BarberProfileId,
            UserId = userId,
            Rating = rating,
            Text = text,
            IsPublished = true
        };
    }

    public UnitResult<Failure> Update(int rating, string? text)
    {
        if (rating is < Constants.MIN_REVIEW_RATING or > Constants.MAX_REVIEW_RATING)
        {
            return new Failure(Error.Validation(
                "review.update.rating",
                $"""
                 Rating must be between {Constants.MIN_REVIEW_RATING} 
                 and {Constants.MAX_REVIEW_RATING}, got {rating}.
                 """,
                nameof(rating)));
        }

        if (text is not null && text.Length > Constants.MAX_TEXT_LENGTH)
        {
            return new Failure(Error.Validation(
                "review.update.text.tooLong",
                $"Review text cannot be longer than {Constants.MAX_TEXT_LENGTH} characters.",
                nameof(text)));
        }

        Rating = rating;
        Text = text;

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Публикует отзыв (в том числе ранее скрытый).
    /// </summary>
    public UnitResult<Failure> Publish()
    {
        if (IsPublished)
        {
            return DomainErrors.Ok();
        }

        IsPublished = true;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Скрывает отзыв из выдачи.
    /// </summary>
    public UnitResult<Failure> Hide()
    {
        if (!IsPublished)
        {
            return DomainErrors.Ok();
        }

        IsPublished = false;
        MarkUpdated();

        return DomainErrors.Ok();
    }
}
