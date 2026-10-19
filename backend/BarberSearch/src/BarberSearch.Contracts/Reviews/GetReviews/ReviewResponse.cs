namespace BarberSearch.Contracts.Reviews.GetReviews;

/// <summary>
/// Опубликованный отзыв в списке отзывов барбера.
/// </summary>
/// <remarks>
/// В ответе только идентификаторы и текстовые поля отзыва: навигаций на бронь,
/// клиента или барбера нет, поэтому циклический граф сериализовать невозможно.
/// </remarks>
/// <param name="Id">Идентификатор отзыва.</param>
/// <param name="BarberProfileId">Идентификатор профиля барбера.</param>
/// <param name="UserId">Идентификатор автора отзыва.</param>
/// <param name="BookingId">Идентификатор брони, по которой оставлен отзыв.</param>
/// <param name="Rating">Оценка от 1 до 5.</param>
/// <param name="Text">Текст отзыва.</param>
/// <param name="IsPublished">Опубликован ли отзыв (в этом ответе всегда <c>true</c>).</param>
/// <param name="CreatedAt">Когда отзыв создан.</param>
/// <param name="UpdatedAt">Когда отзыв последний раз менялся.</param>
public record ReviewResponse(
    Guid Id,
    Guid BarberProfileId,
    Guid UserId,
    Guid BookingId,
    int Rating,
    string? Text,
    bool IsPublished,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
