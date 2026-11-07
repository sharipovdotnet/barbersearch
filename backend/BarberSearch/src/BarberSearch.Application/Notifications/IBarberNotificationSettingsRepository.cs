using BarberSearch.Domain.Notifications;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Notifications;

/// <summary>
/// Доступ к агрегату <see cref="BarberNotificationSettings"/>: чтение, создание по умолчанию, правка.
/// </summary>
/// <remarks>
/// Ключ агрегата — <c>user_id</c>, поэтому «получить настройки пользователя» возвращает
/// либо строку, либо <c>null</c>: отсутствие настроек — нормальное состояние первого входа,
/// и создаёт их Application (<c>GetNotificationSettings</c>, <c>UpdateNotificationSettings</c>,
/// <c>CreateTelegramLink</c>), а не хранилище.
/// </remarks>
public interface IBarberNotificationSettingsRepository
{
    /// <summary>
    /// Настройки барбера или <c>null</c>, если их ещё нет. Сущность отслеживается контекстом.
    /// </summary>
    /// <param name="userId">Барбер-владелец настроек.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<BarberNotificationSettings?, Failure>> FindByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Настройки всех барберов с включёнными напоминаниями (<c>is_enabled = true</c>).
    /// </summary>
    /// <remarks>
    /// Вход тика создания уведомлений: выключенный барбер не получает ни новых строк инбокса,
    /// ни доставки. Читается без отслеживания — обработчик только читает настройки.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<IReadOnlyList<BarberNotificationSettings>, Failure>> GetEnabledAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Настройки по хешу одноразового кода привязки Telegram — вход вебхука бота.
    /// </summary>
    /// <remarks>
    /// Возвращает <c>null</c>, если код неизвестен или уже истёк: вебхук в этом случае молчит
    /// и отвечает <c>200</c>. Срок (<c>TelegramLinkCodeExpiresAt</c>) сравнивается с текущим
    /// моментом внутри реализации, чтобы вебхук не делал вторую проверку.
    /// </remarks>
    /// <param name="codeHash">SHA-256 кода в hex, нижний регистр.</param>
    /// <param name="now">Текущий момент UTC.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<BarberNotificationSettings?, Failure>> FindByTelegramLinkCodeHashAsync(
        string codeHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Привязан ли чат Telegram к другому барберу.
    /// </summary>
    /// <remarks>
    /// Проверка перед привязкой: один чат — один кабинет. Окончательная защита — уникальный
    /// индекс по <c>telegram_chat_id</c>, предпроверка лишь даёт понятный ответ вместо <c>500</c>.
    /// </remarks>
    /// <param name="chatId">Идентификатор чата из обновления Telegram.</param>
    /// <param name="exceptUserId">Владелец настроек, который привязывает чат; его строка не считается.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<bool, Failure>> IsChatLinkedToAnotherUserAsync(
        long chatId,
        Guid exceptUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет настройки по умолчанию. Повтор по первичному ключу — <c>Error.Conflict</c>
    /// с кодом <c>notificationSettings.add.uniqueViolation</c>.
    /// </summary>
    /// <param name="settings">Новые настройки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<UnitResult<Failure>> AddAsync(
        BarberNotificationSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменения настроек (флаги, время, привязка Telegram, код привязки).
    /// </summary>
    /// <param name="settings">Отслеживаемая сущность.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<UnitResult<Failure>> UpdateAsync(
        BarberNotificationSettings settings,
        CancellationToken cancellationToken = default);
}
