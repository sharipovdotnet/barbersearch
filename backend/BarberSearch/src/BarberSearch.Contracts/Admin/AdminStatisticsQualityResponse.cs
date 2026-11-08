namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Качество и модерация периода.
/// </summary>
/// <param name="Rating">Средняя оценка опубликованных отзывов периода или <c>null</c>.</param>
/// <param name="Reviews">Отзывы, созданные в периоде (включая ещё не опубликованные).</param>
/// <param name="Distribution">Пять счётчиков оценок 1–5 по опубликованным отзывам периода.</param>
/// <param name="PublishedShare">Доля опубликованных среди созданных в периоде или <c>null</c>.</param>
/// <param name="Requests">Заявки «Стать барбером».</param>
/// <param name="Salons">Заявки салонов на модерацию.</param>
public record AdminStatisticsQualityResponse(
    decimal? Rating,
    int Reviews,
    IReadOnlyList<int> Distribution,
    decimal? PublishedShare,
    AdminStatisticsModerationResponse Requests,
    AdminStatisticsModerationResponse Salons);

/// <summary>
/// Модерация одного типа: подано, одобрено, отклонено, доля одобрений и медиана.
/// </summary>
/// <remarks>
/// <c>approvalRate</c> — доля одобрений среди решённых
/// (<c>approved / (approved + rejected)</c>), а не среди поданных: заявки без
/// решения в знаменатель не входят. Медиана считается по решениям периода.
/// </remarks>
/// <param name="Submitted">Подано в периоде.</param>
/// <param name="Approved">Одобрено в периоде.</param>
/// <param name="Rejected">Отклонено в периоде.</param>
/// <param name="ApprovalRate">Доля одобрений <c>0..1</c> или <c>null</c>.</param>
/// <param name="MedianDecisionHours">Медиана решения в часах или <c>null</c>.</param>
public record AdminStatisticsModerationResponse(
    int Submitted,
    int Approved,
    int Rejected,
    decimal? ApprovalRate,
    decimal? MedianDecisionHours);
