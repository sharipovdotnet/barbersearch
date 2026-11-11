namespace BarberSearch.Contracts.Admin;

/// <summary>
/// Фотографии на проверке в админской сводке: два независимых счётчика очередей.
/// </summary>
/// <remarks>
/// Числа — ровно размеры вкладок экрана <c>/admin/photos</c> и считаются теми же предикатами:
/// <see cref="PendingAvatarsCount"/> — <c>avatar_moderation_status = Pending</c> при непустых
/// <c>avatar_url</c> и <c>avatar_submitted_at</c>; <see cref="PendingPortfolioCount"/> —
/// <c>moderation_status = Pending</c>. Отдельного общего числа нет: вкладки разные, и
/// смешивать их в одно «фото на проверке» значило бы скрыть, куда идти.
/// </remarks>
/// <param name="PendingAvatarsCount">Сколько аватаров ждёт проверки.</param>
/// <param name="PendingPortfolioCount">Сколько работ портфолио ждёт проверки.</param>
public record AdminSummaryPhotosResponse(int PendingAvatarsCount, int PendingPortfolioCount);
