namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Очередь модерации отзывов в админской сводке.
/// </summary>
/// <remarks>
/// <see cref="PendingCount"/> — ровно размер вкладки «На модерации»: отзывы с
/// <c>is_published = false</c>, включая скрытые администратором. В домене нет отдельного
/// состояния «скрыт администратором»: <c>Hide()</c> возвращает отзыв в ту же очередь.
/// <see cref="Oldest"/> — первая строка этой очереди (FIFO <c>created_at ASC, id ASC</c>)
/// или <c>null</c>, если очередь пуста.
/// </remarks>
/// <param name="PendingCount">Сколько отзывов ждёт публикации.</param>
/// <param name="Oldest">Самый старый отзыв очереди или <c>null</c>.</param>
public record AdminSummaryReviewsResponse(
    int PendingCount,
    AdminSummaryOldestReviewResponse? Oldest);
