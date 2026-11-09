using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Reviews;

/// <summary>
/// Отзыв — агрегатный корень. Всегда привязан к брони: и барбер, и клиент
/// выводятся из неё, поэтому привязать отзыв не к тому барберу невозможно.
/// </summary>
/// <remarks>
/// Отзыв создаётся на модерации (<see cref="IsPublished"/> = <c>false</c>) и не участвует
/// в публичной сводке, пока администратор не вызовет <see cref="Publish"/>. Скрыть
/// опубликованный отзыв можно через <see cref="Hide"/> (R-35). Правка текста опубликованного
/// отзыва тоже возвращает его на модерацию (<see cref="Update"/>): администратор проверял
/// прежний текст, а не новый.
/// </remarks>
public class Review : AggregateRoot
{
    /// <summary>
    /// Текст ошибки «отзыв длиннее <see cref="Constants.MAX_TEXT_LENGTH"/>».
    /// </summary>
    /// <remarks>
    /// <c>static readonly</c>, а не <c>const</c>: интерполяция с целочисленной константой
    /// не является константным выражением, поэтому значение вычисляется один раз при старте.
    /// </remarks>
    private static readonly string TEXT_TOO_LONG =
        $"Review text cannot be longer than {Constants.MAX_TEXT_LENGTH} characters.";

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

    /// <summary>
    /// Создаёт отзыв на модерации: <see cref="IsPublished"/> = <c>false</c>, поэтому новый
    /// отзыв не влияет на публичный рейтинг барбера. Публикует его администратор
    /// (<see cref="Publish"/>).
    /// </summary>
    /// <param name="booking">Завершённая бронь, из которой берутся барбер и клиент.</param>
    /// <param name="userId">Идентификатор автора отзыва.</param>
    /// <param name="rating">Оценка от 1 до 5.</param>
    /// <param name="text">Текст отзыва; <c>null</c> — без текста.</param>
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
                TEXT_TOO_LONG,
                nameof(text)));
        }

        return new Review
        {
            BookingId = booking.Id,
            BarberProfileId = booking.BarberProfileId,
            UserId = userId,
            Rating = rating,
            Text = text,
            IsPublished = false
        };
    }

    /// <summary>
    /// Меняет оценку и текст отзыва.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Правка текста опубликованного отзыва снимает его с публикации: проверенный
    /// администратором текст заменяется новым, которого никто не видел, поэтому отзыв
    /// возвращается в очередь модерации и выпадает из публичной сводки до нового решения
    /// (<see cref="Publish"/>). Правка только оценки публикацию сохраняет: целое 1..5
    /// не несёт произвольного контента и пересчитывается на месте.
    /// </para>
    /// <para>
    /// Текст сравнивается после обрезки пробелов по краям: «сохранить без изменений»
    /// не должно отправлять отзыв на повторную проверку. Само значение сохраняется как
    /// пришло — домен не переписывает ввод пользователя.
    /// </para>
    /// </remarks>
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
                TEXT_TOO_LONG,
                nameof(text)));
        }

        var textChanged = !HasSameText(Text, text);

        Rating = rating;
        Text = text;

        if (IsPublished && textChanged)
        {
            IsPublished = false;
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Сравнивает тексты так, как их видит читатель: пробелы по краям не считаются правкой.
    /// </summary>
    private static bool HasSameText(string? current, string? next)
        => string.Equals(current?.Trim(), next?.Trim(), StringComparison.Ordinal);

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
