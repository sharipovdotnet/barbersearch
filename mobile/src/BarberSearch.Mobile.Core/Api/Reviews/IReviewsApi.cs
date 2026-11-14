using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetMyReviews;
using BarberSearch.Contracts.Reviews.UpdateReview;

namespace BarberSearch.Mobile.Core.Api.Reviews;

/// <summary>
/// Отзывы клиента: создание, список своих отзывов, правка и удаление.
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

    /// <summary>
    /// Читает страницу собственных отзывов автора.
    /// </summary>
    /// <remarks>
    /// <para>
    /// В строке есть всё для списка — оценка, текст, статус, барбер и визит: догружать
    /// карточку барбера или бронь не нужно, поэтому экран обходится одним запросом.
    /// </para>
    /// <para>
    /// Курсор непрозрачен: он возвращается на сервер как есть и не разбирается. Пустой
    /// курсор в адрес не попадает — так первая страница запрашивается без него.
    /// </para>
    /// </remarks>
    /// <param name="cursor">Курсор следующей страницы либо <c>null</c> для первой.</param>
    /// <param name="pageSize">Размер страницы, 1..100.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница отзывов и курсор следующей либо <c>null</c>.</returns>
    Task<PagedResponse<MyReviewResponse>> GetMyReviewsAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Меняет оценку и текст отзыва.
    /// </summary>
    /// <remarks>
    /// Правило «правка текста снимает с публикации» клиент не повторяет: статус берётся из
    /// ответа (<c>isPublished</c>), иначе строка «Опубликован» врала бы до перезагрузки.
    /// </remarks>
    /// <param name="reviewId">Отзыв; сервер отклонит чужой идентификатор.</param>
    /// <param name="rating">Оценка от 1 до 5.</param>
    /// <param name="text">Комментарий либо <c>null</c>, чтобы стереть его.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Новый статус публикации и время правки.</returns>
    Task<UpdateReviewResponse> UpdateReviewAsync(
        Guid reviewId,
        int rating,
        string? text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет отзыв автора.
    /// </summary>
    /// <remarks>
    /// Ответ <c>204</c> несёт только успех; <c>404</c> означает, что отзыв уже удалён
    /// (в том числе другим устройством), и для списка это тот же результат.
    /// </remarks>
    /// <param name="reviewId">Удаляемый отзыв.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task DeleteReviewAsync(Guid reviewId, CancellationToken cancellationToken = default);
}
