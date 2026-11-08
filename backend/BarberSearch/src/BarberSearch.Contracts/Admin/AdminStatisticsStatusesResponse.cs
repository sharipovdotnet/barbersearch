namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Статусы визитов по <c>start_at</c> в периоде и закрытие периода.
/// </summary>
/// <remarks>
/// <c>due</c> — визиты периода, которые уже начались; <c>closed</c> — из них
/// завершённые, отменённые и неявившиеся. Пара нужна, чтобы «закрыто 143 из 150»
/// было честным: будущие брони в знаменатель не входят.
/// </remarks>
/// <param name="Pending">Ждут подтверждения.</param>
/// <param name="Confirmed">Подтверждены.</param>
/// <param name="Completed">Завершены.</param>
/// <param name="Cancelled">Отменены.</param>
/// <param name="NoShow">Неявки.</param>
/// <param name="Due">Уже начались на момент расчёта.</param>
/// <param name="Closed">Из начавшихся: завершены, отменены или неявка.</param>
public record AdminStatisticsStatusesResponse(
    int Pending,
    int Confirmed,
    int Completed,
    int Cancelled,
    int NoShow,
    int Due,
    int Closed);
