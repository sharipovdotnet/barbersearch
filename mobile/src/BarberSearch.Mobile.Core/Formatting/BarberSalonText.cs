using System.Net;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Localization;

namespace BarberSearch.Mobile.Core.Formatting;

/// <summary>
/// Тексты раздела «Мои салоны»: модерация, каталог и ошибки привязки.
/// </summary>
/// <remarks>
/// <para>
/// Подписи повторяют веб (<c>frontend/src/lib/salon-moderation.ts</c>,
/// <c>barber-salon-card.tsx</c>, <c>barber-salon-picker-dialog.tsx</c>), но живут в Core:
/// страница не собирает строки сама, а тест проверяет их без разметки.
/// </para>
/// <para>
/// Статус модерации — это не видимость: салон может быть одобрен и всё равно скрыт из
/// каталога, поэтому «Не в каталоге» показывается отдельным признаком, а не вместо статуса.
/// </para>
/// </remarks>
public static class BarberSalonText
{
    /// <summary>
    /// Русские подписи состояния проверки: статус всегда словом, а не только цветом.
    /// </summary>
    private static readonly Dictionary<SalonModerationStatus, string> ModerationStatuses = new()
    {
        [SalonModerationStatus.Active] = "Одобрен",
        [SalonModerationStatus.Pending] = "На проверке",
        [SalonModerationStatus.Rejected] = "Отклонён",
    };

    /// <summary>
    /// Ответ <c>401</c>: токен истёк, нужен повторный вход.
    /// </summary>
    private static string SessionExpiredValue =>
        Loc.T("Сессия истекла — войдите снова.");

    /// <summary>
    /// Ответ <c>403</c>: профиль принадлежит другому пользователю.
    /// </summary>
    private static string AccessDeniedValue => Loc.T("Нет доступа к профилю.");

    /// <summary>
    /// Ответ <c>429</c>: сервер просит подождать.
    /// </summary>
    private static string RateLimitValue =>
        Loc.T("Слишком много запросов. Подождите минуту и повторите.");

    /// <summary>
    /// Транспортный сбой: до ответа дело не дошло.
    /// </summary>
    private static string NetworkValue =>
        Loc.T("Нет связи с сервером. Проверьте подключение и повторите попытку.");

    /// <summary>
    /// Подпись «Не в каталоге»: салон активен, но скрыт из поиска.
    /// </summary>
    public static string NotListed => Loc.T("Не в каталоге");

    /// <summary>
    /// Подсказка салону на проверке: почему его ещё нет в поиске.
    /// </summary>
    public static string PendingHint =>
        Loc.T("Салон появится в поиске после проверки администратором.");

    /// <summary>
    /// Подпись состояния проверки: «Одобрен», «На проверке», «Отклонён».
    /// </summary>
    /// <param name="status">Состояние из ответа API.</param>
    /// <returns>Русская подпись; неизвестное значение показывается как есть.</returns>
    public static string ModerationText(SalonModerationStatus status) =>
        Loc.T(ModerationStatuses.TryGetValue(status, out var text) ? text : status.ToString());

    /// <summary>
    /// Причина отказа администратора: «Причина отказа: …».
    /// </summary>
    /// <param name="reason">Причина из ответа API.</param>
    /// <returns>Подписанная строка; пустая, если причины нет.</returns>
    public static string Rejection(string? reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? string.Empty
            : Loc.T("Причина отказа: {0}", reason.Trim());

    /// <summary>
    /// Текст ошибки записи салона.
    /// </summary>
    /// <remarks>
    /// Публичный код важнее статуса: <c>addSalon.duplicate</c> и <c>addSalon.salonNotFound</c>
    /// объясняют причину точнее, чем общий <c>400</c>/<c>404</c>. Серверный <c>detail</c> не
    /// показывается — он английский.
    /// </remarks>
    /// <param name="exception">Исключение записи.</param>
    /// <param name="fallback">Подпись раздела, если ответ незнаком.</param>
    /// <returns>Причина для человека.</returns>
    public static string WriteError(Exception exception, string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(fallback);

        if (exception is BarberSearchApiException apiException)
        {
            if (apiException.ErrorCodes.Contains(
                "barberProfile.addSalon.duplicate",
                StringComparer.Ordinal))
            {
                return Loc.T("Этот салон уже добавлен.");
            }

            if (apiException.ErrorCodes.Contains(
                "barberProfile.addSalon.salonNotFound",
                StringComparer.Ordinal))
            {
                return Loc.T("Салон больше не доступен.");
            }

            return apiException.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => RateLimitValue,
                HttpStatusCode.Unauthorized => SessionExpiredValue,
                HttpStatusCode.Forbidden => AccessDeniedValue,
                HttpStatusCode.NotFound => Loc.T("Салон больше не доступен."),
                _ => fallback,
            };
        }

        return exception is HttpRequestException or OperationCanceledException
            ? NetworkValue
            : fallback;
    }
}
