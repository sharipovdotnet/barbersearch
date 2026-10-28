using BarberSearch.Application.Common;
using BarberSearch.Contracts.Reviews.Admin;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Domain.Reviews;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Reviews;

/// <summary>
/// Доступ к агрегату <see cref="Review"/>: чтение и запись.
/// </summary>
public interface IReviewRepository
{
    /// <summary>
    /// Отзыв по идентификатору. Сущность отслеживается контекстом.
    /// </summary>
    Task<Result<Review, Failure>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отзыв конкретной брони. У брони не больше одного отзыва (уникальный индекс в БД).
    /// </summary>
    Task<Result<Review, Failure>> GetByBookingIdAsync(Guid bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница отзывов барбера, свежие сверху. Читается без отслеживания.
    /// </summary>
    /// <remarks>
    /// Глубина offset-выдачи ограничена: <c>page * pageSize</c> не может превышать 10 000,
    /// иначе <c>400</c> с кодом <c>review.paging.depth</c>. Проверка глубины выполняется после
    /// проверки параметров, поэтому при недопустимых <c>page</c>/<c>pageSize</c> клиент получает
    /// прежние <c>review.paging.page</c> / <c>review.paging.pageSize</c>.
    /// </remarks>
    /// <param name="onlyPublished">
    /// <c>true</c> — только опубликованные (карточка барбера для клиентов),
    /// <c>false</c> — включая скрытые (модерация).
    /// </param>
    Task<Result<IReadOnlyList<Review>, Failure>> GetByBarberProfileIdAsync(
        Guid barberProfileId,
        int page,
        int pageSize,
        bool onlyPublished = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница опубликованных отзывов барбера в порядке <c>created_at DESC, id</c>.
    /// </summary>
    /// <remarks>
    /// Скрытые отзывы в выдачу не попадают. Keyset-условие берётся из
    /// <paramref name="cursor"/>; <paramref name="limit"/> — это pageSize + 1.
    /// Читается без отслеживания и проецируется сразу в <see cref="ReviewResponse"/>,
    /// без навигаций на бронь и пользователя.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля барбера.</param>
    /// <param name="cursor">Позиция продолжения или <c>null</c> для первой страницы.</param>
    /// <param name="limit">Сколько строк прочитать.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<ReviewResponse>, Failure>> GetPublishedPageAsync(
        Guid barberProfileId,
        ReviewCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Очередь модерации — страница скрытых отзывов всех барберов в порядке
    /// <c>created_at ASC, id</c> (FIFO: старые сверху).
    /// </summary>
    /// <remarks>
    /// В выдачу попадают только <c>is_published = false</c>, независимо от барбера: администратор
    /// видит всё, что ждёт решения. Порядок обратен карточке барбера, поэтому порядок задаёт
    /// запрос, а курсор хранит пару <c>(created_at, id)</c> без направления. Keyset-условие
    /// берётся из <paramref name="cursor"/>; <paramref name="limit"/> — это pageSize + 1.
    /// <paramref name="limit"/> &lt;= 0 — <c>400</c> с кодом <c>review.page.limit</c>, как у
    /// <see cref="GetPublishedPageAsync"/>. Читается без отслеживания и проецируется сразу
    /// в <see cref="ReviewResponse"/>.
    /// </remarks>
    /// <param name="cursor">Позиция продолжения или <c>null</c> для первой страницы.</param>
    /// <param name="limit">Сколько строк прочитать.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<ReviewResponse>, Failure>> GetModerationPageAsync(
        ReviewCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Страница отзывов для админского экрана: скрытые (FIFO) или опубликованные (свежие сверху).
    /// </summary>
    /// <remarks>
    /// Отдельная проекция от публичного <see cref="ReviewResponse"/>: в ответ входят имя барбера,
    /// клиент и контекст визита. Данные доступны только администратору. Фильтр по статусу
    /// и порядок задаёт <paramref name="isPublished"/>; курсор хранит пару <c>(created_at, id)</c>,
    /// направление меняет только запрос. <paramref name="limit"/> — это pageSize + 1, поэтому
    /// <c>limit &lt;= 0</c> — <c>400 review.page.limit</c>.
    /// </remarks>
    /// <param name="isPublished"><c>true</c> — опубликованные, <c>false</c> — очередь модерации.</param>
    /// <param name="cursor">Позиция продолжения или <c>null</c> для первой страницы.</param>
    /// <param name="limit">Сколько строк прочитать.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<AdminReviewResponse>, Failure>> GetAdminPageAsync(
        bool isPublished,
        ReviewCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Один отзыв любого статуса для админской карточки.
    /// </summary>
    /// <param name="id">Идентификатор отзыва.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<AdminReviewResponse, Failure>> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сводка по опубликованным отзывам барбера — данные для пересчёта
    /// <c>BarberProfile.Rating</c> / <c>ReviewsCount</c> (AGENT.md §12.2).
    /// </summary>
    /// <remarks>
    /// Реализация читает количество и среднее одним агрегирующим SQL-запросом
    /// (R-19): сводка запрашивается под <c>SELECT … FOR UPDATE</c> на профиле,
    /// поэтому лишний round-trip удлинял бы блокировку. Нет опубликованных отзывов —
    /// <see cref="ReviewRatingSummary.AverageRating"/> = <c>null</c>,
    /// <see cref="ReviewRatingSummary.Count"/> = 0.
    /// </remarks>
    Task<Result<ReviewRatingSummary, Failure>> GetRatingSummaryAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет отзыв. Второй отзыв на ту же бронь — <c>Error.Conflict</c>.
    /// </summary>
    Task<UnitResult<Failure>> AddAsync(Review review, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменения отзыва (правка, публикация, скрытие).
    /// </summary>
    Task<UnitResult<Failure>> UpdateAsync(Review review, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет отзыв. Нужно, чтобы клиент мог удалить отзыв и написать новый:
    /// уникальный индекс по брони иначе этого не позволит.
    /// </summary>
    Task<UnitResult<Failure>> DeleteAsync(Review review, CancellationToken cancellationToken = default);
}
