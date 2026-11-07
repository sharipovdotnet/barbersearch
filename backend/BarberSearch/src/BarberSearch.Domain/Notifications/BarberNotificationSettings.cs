using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Notifications;

/// <summary>
/// Настройки напоминаний барбера — агрегат с ключом <see cref="UserId"/>.
/// </summary>
/// <remarks>
/// <para>
/// Строка одна на барбера (<c>user_id</c> — первичный ключ), потому что дайджест считается
/// на человека, а не на салон: барбер может работать в нескольких салонах, а сообщение
/// в день должно быть одно (решение D5). По той же причине это ключ, а не отдельный
/// <see cref="Entity.Id"/>: у настроек нет второй идентичности, кроме владельца.
/// </para>
/// <para>
/// Часы сюда не передаются: технические метки <see cref="UpdatedAt"/> ставятся системными
/// часами, как и в <see cref="Entity"/> (AGENT.md §7.6). Момент привязки Telegram и срок
/// кода приходят параметрами — это бизнес-«сейчас», его решает Application.
/// </para>
/// <para>
/// <see cref="IsEnabled"/> = <c>false</c> не удаляет историю: инбокс остаётся читаемым,
/// просто новые строки не создаются и доставка не идёт.
/// </para>
/// </remarks>
public class BarberNotificationSettings
{
    /// <summary>
    /// Публичный код ошибки «недопустимый интервал напоминания»; <c>400</c>.
    /// </summary>
    public const string LEAD_MINUTES_CODE = "notificationSettings.leadMinutes";

    /// <summary>
    /// Публичный код ошибки «недопустимое время дайджеста»; <c>400</c>.
    /// </summary>
    public const string DIGEST_TIME_CODE = "notificationSettings.digestTime";

    /// <summary>
    /// Публичный код ошибки «пустой идентификатор владельца»; <c>400</c>.
    /// </summary>
    private const string CREATE_USER_ID_CODE = "notificationSettings.create.userId";

    /// <summary>
    /// Публичный код ошибки «некорректный идентификатор чата Telegram»; <c>400</c>.
    /// </summary>
    private const string LINK_TELEGRAM_CHAT_ID_CODE = "notificationSettings.linkTelegram.chatId";

    /// <summary>
    /// Публичный код ошибки «хеш кода привязки — не SHA-256 в hex»; <c>400</c>.
    /// </summary>
    private const string ISSUE_CODE_HASH_CODE = "notificationSettings.issueTelegramCode.hash";

    /// <summary>
    /// Публичный код ошибки «не задан срок жизни кода привязки»; <c>400</c>.
    /// </summary>
    private const string ISSUE_CODE_EXPIRES_AT_CODE = "notificationSettings.issueTelegramCode.expiresAt";

    /// <summary>
    /// Время дайджеста по умолчанию — 08:00 локального пояса основного салона (решение D2).
    /// </summary>
    public const int DEFAULT_DIGEST_HOUR = 8;

    /// <summary>
    /// Длина SHA-256 в шестнадцатеричной записи.
    /// </summary>
    private const int LINK_CODE_HASH_LENGTH = 64;

    /// <summary>
    /// Владелец настроек и он же первичный ключ.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Выключает и создание новых уведомлений, и доставку. История инбокса не удаляется.
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// Включён ли утренний дайджест. Действует только при <see cref="IsEnabled"/>.
    /// </summary>
    public bool DigestEnabled { get; private set; }

    /// <summary>
    /// Локальное время салона, когда отправляется дайджест: от 06:00 до 12:00 включительно,
    /// кратно 15 минутам.
    /// </summary>
    public TimeOnly DigestTime { get; private set; }

    /// <summary>
    /// За сколько минут до начала визита напоминать: 15, 30 или 60.
    /// </summary>
    public int LeadMinutes { get; private set; }

    /// <summary>
    /// Идентификатор чата Telegram; <c>null</c> — канал не привязан, уведомления только в инбоксе.
    /// </summary>
    /// <remarks>
    /// Наружу это значение не отдаётся: контракт настроек сообщает только
    /// <c>telegramLinked</c>/<c>telegramLinkedAt</c>. Идентификатор чата — не пароль,
    /// но и не данные клиента, поэтому в ответ он не попадает.
    /// </remarks>
    public long? TelegramChatId { get; private set; }

    /// <summary>
    /// Момент привязки чата; <c>null</c> — канал не привязан.
    /// </summary>
    public DateTimeOffset? TelegramLinkedAt { get; private set; }

    /// <summary>
    /// SHA-256 (hex, нижний регистр) одноразового кода привязки; открытый код в базе не хранится.
    /// </summary>
    public string? TelegramLinkCodeHash { get; private set; }

    /// <summary>
    /// Срок действия кода привязки; после него код не принимается, даже если хеш ещё в строке.
    /// </summary>
    public DateTimeOffset? TelegramLinkCodeExpiresAt { get; private set; }

    /// <summary>
    /// Техническая метка последнего изменения — системные часы, не бизнес-«сейчас».
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Привязан ли чат Telegram: уведомления уходят и в инбокс, и в канал.
    /// </summary>
    public bool IsTelegramLinked => TelegramChatId is not null;

    private BarberNotificationSettings()
    {
    }

    /// <summary>
    /// Создаёт настройки по умолчанию при первом обращении барбера: напоминания включены,
    /// дайджест включён, 08:00 локального пояса салона, за 30 минут до визита.
    /// </summary>
    /// <remarks>
    /// Настройки создаёт Application при первом чтении или первой правке: отдельной операции
    /// «создать настройки» в API нет, клиент всегда видит объект.
    /// </remarks>
    /// <param name="userId">Барбер-владелец настроек.</param>
    public static Result<BarberNotificationSettings, Failure> CreateDefault(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return new Failure(Error.Validation(
                CREATE_USER_ID_CODE,
                "The notification settings owner cannot be empty.",
                nameof(userId)));
        }

        return new BarberNotificationSettings
        {
            UserId = userId,
            IsEnabled = true,
            DigestEnabled = true,
            DigestTime = new TimeOnly(DEFAULT_DIGEST_HOUR, 0),
            LeadMinutes = Constants.DEFAULT_NOTIFICATION_LEAD_MINUTES,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Меняет флаги, время дайджеста и интервал напоминания.
    /// </summary>
    /// <remarks>
    /// Валидация собирает все ошибки сразу: недопустимый интервал — публичный код
    /// <c>notificationSettings.leadMinutes</c>, недопустимое время —
    /// <c>notificationSettings.digestTime</c> (оба <c>400</c>).
    /// </remarks>
    /// <param name="isEnabled">Присылать ли напоминания вообще.</param>
    /// <param name="digestEnabled">Присылать ли утренний дайджест.</param>
    /// <param name="digestTime">Локальное время дайджеста, 06:00..12:00 с шагом 15 минут.</param>
    /// <param name="leadMinutes">Интервал напоминания: 15, 30 или 60 минут.</param>
    public UnitResult<Failure> Update(
        bool isEnabled,
        bool digestEnabled,
        TimeOnly digestTime,
        int leadMinutes)
    {
        var errors = DomainErrors.Collect();

        if (leadMinutes is not (
                Constants.MIN_NOTIFICATION_LEAD_MINUTES
                or Constants.DEFAULT_NOTIFICATION_LEAD_MINUTES
                or Constants.MAX_NOTIFICATION_LEAD_MINUTES))
        {
            errors.Add(Error.Validation(
                LEAD_MINUTES_CODE,
                $"""
                 The reminder interval must be one of
                 {Constants.MIN_NOTIFICATION_LEAD_MINUTES},
                 {Constants.DEFAULT_NOTIFICATION_LEAD_MINUTES},
                 {Constants.MAX_NOTIFICATION_LEAD_MINUTES} minutes.
                 """,
                nameof(leadMinutes)));
        }

        if (!IsValidDigestTime(digestTime))
        {
            errors.Add(Error.Validation(
                DIGEST_TIME_CODE,
                $"""
                 The digest time must be between
                 {Constants.MIN_NOTIFICATION_DIGEST_HOUR:00}:00 and
                 {Constants.MAX_NOTIFICATION_DIGEST_HOUR:00}:00 with a
                 {Constants.NOTIFICATION_DIGEST_TIME_STEP_MINUTES}-minute step.
                 """,
                nameof(digestTime)));
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        IsEnabled = isEnabled;
        DigestEnabled = digestEnabled;
        DigestTime = digestTime;
        LeadMinutes = leadMinutes;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Привязывает чат Telegram и очищает использованный код: повторно тот же код не сработает.
    /// </summary>
    /// <param name="chatId">Идентификатор чата из обновления Telegram.</param>
    /// <param name="now">Момент привязки в UTC — часы передаёт Application.</param>
    public UnitResult<Failure> LinkTelegram(long chatId, DateTimeOffset now)
    {
        if (chatId == 0)
        {
            return DomainErrors.Fail(Error.Validation(
                LINK_TELEGRAM_CHAT_ID_CODE,
                "The Telegram chat identifier cannot be zero.",
                nameof(chatId)));
        }

        TelegramChatId = chatId;
        TelegramLinkedAt = now;
        TelegramLinkCodeHash = null;
        TelegramLinkCodeExpiresAt = null;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Отвязывает чат Telegram. Идемпотентно: отвязка непривязанного чата — успех без изменений,
    /// поэтому повторный вызов из интерфейса не отвечает ошибкой.
    /// </summary>
    public UnitResult<Failure> UnlinkTelegram()
    {
        if (TelegramChatId is null
            && TelegramLinkedAt is null
            && TelegramLinkCodeHash is null
            && TelegramLinkCodeExpiresAt is null)
        {
            return DomainErrors.Ok();
        }

        TelegramChatId = null;
        TelegramLinkedAt = null;
        TelegramLinkCodeHash = null;
        TelegramLinkCodeExpiresAt = null;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Кладёт хеш одноразового кода привязки и его срок. Открытый код остаётся только у клиента:
    /// в базе лежит SHA-256, поэтому утечка таблицы не даёт готовой ссылки.
    /// </summary>
    /// <param name="hash">SHA-256 в hex, нижний регистр.</param>
    /// <param name="expiresAt">Срок действия кода в UTC.</param>
    public UnitResult<Failure> IssueTelegramCode(string hash, DateTimeOffset expiresAt)
    {
        var errors = DomainErrors.Collect();

        if (string.IsNullOrWhiteSpace(hash)
            || hash.Length != LINK_CODE_HASH_LENGTH
            || !hash.All(char.IsAsciiHexDigit))
        {
            errors.Add(Error.Validation(
                ISSUE_CODE_HASH_CODE,
                $"The link code hash must be a {LINK_CODE_HASH_LENGTH}-character hexadecimal string.",
                nameof(hash)));
        }

        if (expiresAt == default)
        {
            errors.Add(Error.Validation(
                ISSUE_CODE_EXPIRES_AT_CODE,
                "The link code expiration time cannot be empty.",
                nameof(expiresAt)));
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        TelegramLinkCodeHash = hash;
        TelegramLinkCodeExpiresAt = expiresAt;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Очищает код привязки, сохраняя саму привязку чата. Идемпотентно.
    /// </summary>
    public UnitResult<Failure> ClearTelegramCode()
    {
        if (TelegramLinkCodeHash is null && TelegramLinkCodeExpiresAt is null)
        {
            return DomainErrors.Ok();
        }

        TelegramLinkCodeHash = null;
        TelegramLinkCodeExpiresAt = null;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Проверяет время дайджеста: 06:00..12:00 включительно, кратно 15 минутам, без секунд.
    /// </summary>
    private static bool IsValidDigestTime(TimeOnly time)
        => time.Second == 0
           && time.Millisecond == 0
           && time.Minute % Constants.NOTIFICATION_DIGEST_TIME_STEP_MINUTES == 0
           && time >= new TimeOnly(Constants.MIN_NOTIFICATION_DIGEST_HOUR, 0)
           && time <= new TimeOnly(Constants.MAX_NOTIFICATION_DIGEST_HOUR, 0);

    private void MarkUpdated() => UpdatedAt = DateTimeOffset.UtcNow;
}
