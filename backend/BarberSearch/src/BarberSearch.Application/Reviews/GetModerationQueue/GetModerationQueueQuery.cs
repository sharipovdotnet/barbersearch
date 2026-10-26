using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetReviews;

namespace BarberSearch.Application.Reviews.GetModerationQueue;

/// <summary>
/// Запрос очереди модерации: скрытые отзывы всех барберов, старые сверху, keyset-пагинация.
/// </summary>
/// <remarks>
/// Барбер не фильтруется: администратор просматривает всё, что ждёт решения. Доступ проверяет
/// контроллер (<c>AdminOnly</c>), поэтому в запрос роль не попадает.
/// </remarks>
/// <param name="Cursor">Курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; <c>null</c> — значение по умолчанию.</param>
public record GetModerationQueueQuery(
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<ReviewResponse>>;
