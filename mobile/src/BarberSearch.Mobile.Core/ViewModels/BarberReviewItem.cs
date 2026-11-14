using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Строка списка отзывов барбера: звёзды, оценка, дата и текст.
/// </summary>
/// <remarks>
/// <para>
/// В <see cref="ReviewResponse"/> нет автора, услуги, салона и времени визита — строка их
/// не показывает и не догружает: карточка читает только то, что сервер отдал.
/// </para>
/// <para>
/// Дата считается в поясе основного салона: отзыв относится к барберу, а не к текущему
/// выбору в «Свободном времени». Пояс разрешается один раз при создании строки.
/// </para>
/// </remarks>
public sealed class BarberReviewItem
{
    /// <summary>
    /// Создаёт строку из отзыва.
    /// </summary>
    /// <param name="review">Опубликованный отзыв.</param>
    /// <param name="timeZone">Пояс основного салона; <c>null</c> — UTC.</param>
    public BarberReviewItem(ReviewResponse review, TimeZoneInfo? timeZone)
    {
        ArgumentNullException.ThrowIfNull(review);

        Id = review.Id;
        Stars = ReviewText.Stars(review.Rating);
        RatingText = BarberText.Rating(review.Rating);
        DateText = ReviewText.ReviewDate(review.CreatedAt, timeZone);
        BodyText = ReviewText.Body(review.Text);
        HasText = !string.IsNullOrWhiteSpace(review.Text);
    }

    /// <summary>
    /// Идентификатор отзыва.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Звёзды одной строкой: «★★★★☆».
    /// </summary>
    public string Stars { get; }

    /// <summary>
    /// Оценка с одним знаком после запятой: «5,0».
    /// </summary>
    public string RatingText { get; }

    /// <summary>
    /// Дата отзыва в поясе салона: «15 ноября 2026».
    /// </summary>
    public string DateText { get; }

    /// <summary>
    /// Текст отзыва либо «Без комментария.».
    /// </summary>
    public string BodyText { get; }

    /// <summary>
    /// Был ли у отзыва текст: подпись «Без комментария.» показывается приглушённой.
    /// </summary>
    public bool HasText { get; }
}
