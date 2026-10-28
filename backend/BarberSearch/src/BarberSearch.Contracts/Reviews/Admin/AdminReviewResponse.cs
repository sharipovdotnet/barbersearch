namespace BarberSearch.Contracts.Reviews.Admin;

/// <summary>
/// Отзыв в админском списке модерации.
/// </summary>
/// <remarks>
/// Отдельный контракт от публичного <c>ReviewResponse</c>: здесь есть имена барбера
/// и клиента, контекст визита и текущая сводка рейтинга. Эти данные доступны только
/// администратору и не должны попадать в публичную выдачу.
/// </remarks>
/// <param name="Id">Идентификатор отзыва.</param>
/// <param name="Rating">Оценка от 1 до 5.</param>
/// <param name="Text">Текст отзыва; <c>null</c> — оценка без комментария.</param>
/// <param name="IsPublished">Опубликован ли отзыв.</param>
/// <param name="CreatedAt">Когда отзыв создан.</param>
/// <param name="UpdatedAt">Когда отзыв последний раз менялся.</param>
/// <param name="Barber">Барбер, которому оставлен отзыв.</param>
/// <param name="Customer">Клиент, автор отзыва.</param>
/// <param name="Visit">Визит, по которому оставлен отзыв.</param>
public record AdminReviewResponse(
    Guid Id,
    int Rating,
    string? Text,
    bool IsPublished,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    AdminReviewBarberResponse Barber,
    AdminReviewCustomerResponse Customer,
    AdminReviewVisitResponse Visit);
