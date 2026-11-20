using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.Admin;

namespace BarberSearch.Mobile.Core.Api.Admin;

/// <summary>
/// Какая половина очереди отзывов показана: ждущие решения или уже опубликованные.
/// </summary>
/// <remarks>
/// Значения — не адреса: сервер ждёт строки <c>pending</c> и <c>published</c>
/// (<c>GetAdminReviewsQuery</c>), и переводит их в текст клиент. Перечисление живёт рядом
/// с клиентом, потому что им пользуются двое: список и его ViewModel.
/// </remarks>
public enum AdminReviewStatusFilter
{
    /// <summary>
    /// Очередь модерации: отзывы, которых ещё нет на карточке мастера.
    /// </summary>
    Pending,

    /// <summary>
    /// Опубликованные отзывы: они видны на карточке мастера, но их можно скрыть.
    /// </summary>
    Published,
}

/// <summary>
/// Админские отзывы: очередь по статусу, карточка по идентификатору и решение модератора.
/// </summary>
/// <remarks>
/// <para>
/// Список и карточка идут под политикой <c>AdminOnly</c> (<c>GET api/admin/reviews</c>),
/// а публикация и скрытие — существующие ручки записи отзыва
/// (<c>POST</c>/<c>DELETE api/reviews/{id}/publication</c>): администратор проходит по ним
/// той же политикой, и отдельного админского действия «скрыть» сервер не заводит.
/// </para>
/// <para>
/// Ответ карточки — тот же контракт, что у строки списка, поэтому экран детали не собирает
/// данные из двух чтений. Курсор непрозрачен и возвращается серверу как есть; пустой курсор
/// не передаётся вовсе.
/// </para>
/// </remarks>
public interface IAdminReviewsApi
{
    /// <summary>
    /// Читает страницу очереди отзывов выбранного статуса.
    /// </summary>
    /// <param name="status">
    /// Очередь модерации (сначала старые) или опубликованные (свежие сверху).
    /// </param>
    /// <param name="cursor">
    /// Курсор следующей страницы из предыдущего ответа; <c>null</c> — первая страница.
    /// </param>
    /// <param name="pageSize">Размер страницы; сервер принимает от 1 до 100.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Строки страницы и курсор продолжения либо <c>null</c> на последней.</returns>
    /// <exception cref="BarberSearchApiException">
    /// Сессия истекла, роль не администратор или API ответил ошибкой.
    /// </exception>
    Task<PagedResponse<AdminReviewResponse>> GetReviewsAsync(
        AdminReviewStatusFilter status,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает один отзыв любого статуса для карточки.
    /// </summary>
    /// <param name="reviewId">Идентификатор отзыва.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отзыв с клиентом, мастером и контекстом визита.</returns>
    /// <exception cref="BarberSearchApiException">
    /// Отзыв не найден, сессия истекла, роль не администратор или API ответил ошибкой.
    /// </exception>
    Task<AdminReviewResponse> GetReviewAsync(
        Guid reviewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Публикует отзыв: он появляется на карточке мастера и участвует в рейтинге.
    /// </summary>
    /// <param name="reviewId">Идентификатор отзыва.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача записи; успех — <c>204</c> без тела.</returns>
    /// <exception cref="BarberSearchApiException">
    /// Отзыв не найден, сессия истекла, роль не администратор или API ответил ошибкой.
    /// </exception>
    Task PublishReviewAsync(Guid reviewId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Скрывает отзыв: он исчезает с карточки мастера и возвращается в очередь модерации.
    /// </summary>
    /// <remarks>
    /// Причина скрытия серверу не передаётся: <c>HideReviewCommand</c> её не принимает,
    /// и тело запроса не отправляется.
    /// </remarks>
    /// <param name="reviewId">Идентификатор отзыва.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача записи; успех — <c>204</c> без тела.</returns>
    /// <exception cref="BarberSearchApiException">
    /// Отзыв не найден, сессия истекла, роль не администратор или API ответил ошибкой.
    /// </exception>
    Task HideReviewAsync(Guid reviewId, CancellationToken cancellationToken = default);
}
