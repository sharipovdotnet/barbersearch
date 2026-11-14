namespace BarberSearch.Mobile.Core.Api.Reviews;

/// <summary>
/// Отзывы клиента: создание отзыва о завершённом визите.
/// </summary>
/// <remarks>
/// Ходит через клиент с <c>AuthenticatedHttpHandler</c>: автора сервер берёт из токена, а
/// барбера — из брони, поэтому в запросе их нет. Отзыв создаётся скрытым и уходит на
/// модерацию: рейтинг барбера меняется только после публикации.
/// </remarks>
public interface IReviewsApi
{
    /// <summary>
    /// Создаёт отзыв о визите.
    /// </summary>
    /// <remarks>
    /// Один отзыв на бронь: повтор сервер отвергает <c>409 review.add.uniqueViolation</c>.
    /// Пустой комментарий отправляется как <c>null</c>, а не пустой строкой.
    /// </remarks>
    /// <param name="bookingId">Бронь, к которой относится отзыв.</param>
    /// <param name="rating">Оценка от 1 до 5.</param>
    /// <param name="text">Комментарий не длиннее 2000 символов либо <c>null</c>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор созданного отзыва.</returns>
    /// <exception cref="BarberSearchApiException">Визит не завершён, отзыв уже есть или API ответил ошибкой.</exception>
    Task<Guid> CreateReviewAsync(
        Guid bookingId,
        int rating,
        string? text,
        CancellationToken cancellationToken = default);
}
