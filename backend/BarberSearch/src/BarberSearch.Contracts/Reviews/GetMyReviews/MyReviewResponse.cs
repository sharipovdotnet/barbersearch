namespace BarberSearch.Contracts.Reviews.GetMyReviews;

/// <summary>
/// Отзыв автора в списке «мои отзывы»: оценка, текст и контекст визита.
/// </summary>
/// <remarks>
/// Ответ адресован самому автору отзыва, поэтому в нём нет ни его идентификатора, ни имени,
/// ни email: это данные вызывающего, а не ресурс, который нужно показывать. Имя барбера,
/// услуга, салон, время и цена визита — данные автора о его собственной записи, а не PII
/// других людей.
/// <para>
/// <see cref="IsPublished"/> — единственный статус, который различает бэкенд: в таблице
/// только boolean. <c>false</c> означает «на модерации» и для никогда не опубликованного
/// отзыва, и для скрытого администратором; отличить их нельзя, поэтому UI не должен
/// обещать «скрыт администратором» (AGENT.md §12.9).
/// </para>
/// </remarks>
/// <param name="Id">Идентификатор отзыва.</param>
/// <param name="Rating">Оценка от 1 до 5.</param>
/// <param name="Text">Текст отзыва; <c>null</c> — оценка без комментария.</param>
/// <param name="IsPublished">
/// Опубликован ли отзыв; <c>false</c> — «на модерации» (в том числе скрыт администратором).
/// </param>
/// <param name="CreatedAt">Когда отзыв создан.</param>
/// <param name="UpdatedAt">Когда отзыв последний раз менялся.</param>
/// <param name="Barber">Барбер, которому оставлен отзыв.</param>
/// <param name="Visit">Визит, по которому оставлен отзыв.</param>
public record MyReviewResponse(
    Guid Id,
    int Rating,
    string? Text,
    bool IsPublished,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    MyReviewBarberResponse Barber,
    MyReviewVisitResponse Visit);
