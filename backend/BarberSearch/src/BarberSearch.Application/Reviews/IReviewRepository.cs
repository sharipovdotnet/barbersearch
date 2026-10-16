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
    /// Сводка по опубликованным отзывам барбера — данные для пересчёта
    /// <c>BarberProfile.Rating</c> / <c>ReviewsCount</c> (AGENT.md §12.2).
    /// </summary>
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
