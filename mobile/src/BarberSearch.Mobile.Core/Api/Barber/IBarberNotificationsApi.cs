using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Notifications;
using BarberSearch.Contracts.Notifications.CreateTelegramLink;
using BarberSearch.Contracts.Notifications.UpdateNotificationSettings;

namespace BarberSearch.Mobile.Core.Api.Barber;

/// <summary>
/// Инбокс напоминаний мастера, его настройки и привязка Telegram.
/// </summary>
/// <remarks>
/// <para>
/// Клиент ходит через <see cref="HttpClient"/> с <c>AuthenticatedHttpHandler</c>: получатель
/// всегда берётся из сессии, поэтому чужой инбокс запросить нечем. <c>X-CSRF-TOKEN</c> не
/// отправляется: Bearer-клиент не проходит antiforgery-фильтр cookie-мутаций.
/// </para>
/// <para>
/// Маршрут <c>{id}</c> ограничен <c>guid</c> на сервере: <c>read-all</c> и <c>unread-count</c>
/// не могут быть разобраны как идентификатор уведомления.
/// </para>
/// </remarks>
public interface IBarberNotificationsApi
{
    /// <summary>
    /// Читает страницу инбокса: свежие уведомления сверху, keyset-курсор.
    /// </summary>
    /// <remarks>
    /// Курсор непрозрачен и возвращается в следующую страницу как есть; <c>null</c> — первая
    /// страница. <paramref name="pageSize"/> по умолчанию 20, как у сервера.
    /// </remarks>
    /// <param name="cursor">Курсор следующей страницы; <c>null</c> — первая.</param>
    /// <param name="pageSize">Размер страницы; <c>null</c> — серверный по умолчанию.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница уведомлений; <c>NextCursor = null</c> означает последнюю.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<PagedResponse<BarberNotificationResponse>> GetAsync(
        string? cursor = null,
        int? pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отмечает одно уведомление прочитанным.
    /// </summary>
    /// <remarks>
    /// Чужое или уже удалённое уведомление — <c>404 notification.notFound</c>: повторять
    /// запрос бессмысленно, но и скрывать строку до ответа нельзя. Успех — <c>204</c>.
    /// </remarks>
    /// <param name="id">Идентификатор уведомления.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Уведомление не найдено или API ответил ошибкой.</exception>
    Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отмечает все уведомления прочитанными.
    /// </summary>
    /// <remarks>
    /// Идемпотентно: повторный вызов не меняет уже прочитанные строки. Успех — <c>204</c>.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task MarkAllReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает число непрочитанных для бейджа колокольчика.
    /// </summary>
    /// <remarks>
    /// Сервер отдаёт точное число, а не «99+»: усечение — дело клиента, и оно должно быть
    /// одинаковым в бейдже и в подписи.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число непрочитанных уведомлений.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает настройки напоминаний, создавая их по умолчанию при первом чтении.
    /// </summary>
    /// <remarks>
    /// <c>digestTime</c> приходит строкой <c>HH:mm</c>, готовой для поля времени: клиент
    /// не разбирает <see cref="TimeOnly"/> и не форматирует значение сам.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Текущие настройки напоминаний.</returns>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task<NotificationSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Заменяет настройки напоминаний целиком и возвращает сохранённое состояние.
    /// </summary>
    /// <remarks>
    /// <c>PUT</c> отвечает <c>200</c> с полным телом, а не <c>204</c>: сервер нормализует
    /// значения (например, <c>digestTime</c>), и экран показывает их, не перечитывая настройки.
    /// </remarks>
    /// <param name="request">Новое состояние всех четырёх полей.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённые и нормализованные настройки.</returns>
    /// <exception cref="BarberSearchApiException">Поле отклонено или API ответил ошибкой.</exception>
    Task<NotificationSettingsResponse> UpdateSettingsAsync(
        UpdateNotificationSettingsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Выдаёт одноразовый код привязки Telegram и готовую ссылку на бота.
    /// </summary>
    /// <remarks>
    /// Уже привязанный чат — <c>409 notification.telegram.alreadyLinked</c>, ненастроенный
    /// бот — <c>409 notification.telegram.notConfigured</c>. Код живёт 10 минут, и кэшировать
    /// ответ дольше нельзя.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Код, deep link и срок действия кода.</returns>
    /// <exception cref="BarberSearchApiException">Чат уже привязан, бот не настроен или API ответил ошибкой.</exception>
    Task<TelegramLinkResponse> CreateTelegramLinkAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Отвязывает чат Telegram.
    /// </summary>
    /// <remarks>
    /// Идемпотентно: отсутствие привязки — успех. Успех — <c>204</c>.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">API ответил ошибкой.</exception>
    Task UnlinkTelegramAsync(CancellationToken cancellationToken = default);
}
