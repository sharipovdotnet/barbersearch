using System.Net.Mail;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Entity = BarberSearch.Domain.Common.Entity;
using Shared;

namespace BarberSearch.Domain.EmailOutbox;

/// <summary>
/// Строка исходящего письма в transactional outbox.
/// </summary>
/// <remarks>
/// <para>
/// Письмо не отправляется в запросе: строка пишется в базу в той же транзакции, что и токен
/// (подтверждение email или сброс пароля), а отправляет её фоновый диспетчер. Так письмо
/// не теряется, когда SMTP недоступен в момент запроса.
/// </para>
/// <para>
/// Выбран вариант A среза: <see cref="HtmlBody"/> и <see cref="TextBody"/> хранятся готовыми.
/// Это ослабляет модель секрета — строка outbox содержит открытый токен внутри ссылки, —
/// поэтому тела удаляются уборкой через <c>email_outbox:RetentionDays</c> (по умолчанию
/// 7 дней), а TTL самого токена (24 часа / 30 минут) ограничивает окно его жизни. Подробнее —
/// раздел «Email outbox» в <c>AGENT.md</c>.
/// </para>
/// <para>
/// Доставка — at-least-once: <c>SMTP принял письмо, но отметка <see cref="SentAt"/> не успела
/// закоммититься</c> — штатный сценарий, при котором письмо уйдёт повторно. Потеря письма
/// недопустима, дубликат допустим.
/// </para>
/// <para>
/// Домен не читает часы: и момент создания, и момент операции приходят аргументом
/// (<c>TimeProvider</c> живёт в Application).
/// </para>
/// </remarks>
public sealed class EmailOutboxMessage : Entity
{
    /// <summary>
    /// Максимальная длина <see cref="DeduplicationKey"/>.
    /// </summary>
    public const int MAX_DEDUPLICATION_KEY_LENGTH = 128;

    /// <summary>
    /// Максимальная длина получателя: предел адреса электронной почты RFC 5321.
    /// </summary>
    public const int MAX_EMAIL_LENGTH = Constants.MAX_EMAIL_LENGTH;

    /// <summary>
    /// Максимальная длина темы письма.
    /// </summary>
    public const int MAX_SUBJECT_LENGTH = 256;

    /// <summary>
    /// Максимальная длина <see cref="LastError"/>: код ошибки транспорта, не текст исключения.
    /// </summary>
    public const int MAX_LAST_ERROR_LENGTH = 200;

    /// <summary>
    /// Ключ дедупликации: одно письмо — одна строка. В этом срезе это id выпущенного токена.
    /// </summary>
    public string DeduplicationKey { get; private set; } = null!;

    /// <summary>
    /// Адрес получателя. Персональные данные: в лог не попадает.
    /// </summary>
    public string ToEmail { get; private set; } = null!;

    /// <summary>
    /// Тема письма.
    /// </summary>
    public string Subject { get; private set; } = null!;

    /// <summary>
    /// HTML-версия тела с одноразовой ссылкой.
    /// </summary>
    public string HtmlBody { get; private set; } = null!;

    /// <summary>
    /// Текстовая версия тела с той же ссылкой.
    /// </summary>
    public string TextBody { get; private set; } = null!;

    /// <summary>
    /// Момент, начиная с которого строку можно отправлять. Сдвигается backoff'ом после
    /// неудачной попытки; для новой строки равен моменту создания.
    /// </summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>
    /// Число сделанных попыток отправки; при создании — 0.
    /// </summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// Момент, когда транспорт принял письмо; <c>null</c>, пока письмо не отправлено.
    /// </summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>
    /// Момент окончательного отказа: попытки исчерпаны, письмо больше не отправляется.
    /// </summary>
    public DateTimeOffset? FailedAt { get; private set; }

    /// <summary>
    /// Код последней ошибки транспорта (без PII и без тела письма); <c>null</c> до первой ошибки.
    /// </summary>
    public string? LastError { get; private set; }

    private EmailOutboxMessage()
    {
    }

    /// <summary>
    /// Создаёт готовую к отправке строку: попыток нет, <see cref="NextAttemptAt"/> — «сейчас».
    /// </summary>
    /// <param name="deduplicationKey">Ключ дедупликации: одно письмо — одна строка.</param>
    /// <param name="toEmail">Адрес получателя.</param>
    /// <param name="subject">Тема письма.</param>
    /// <param name="htmlBody">HTML-версия тела.</param>
    /// <param name="textBody">Текстовая версия тела.</param>
    /// <param name="now">Текущий момент по бизнес-часам; не <c>default</c>.</param>
    public static Result<EmailOutboxMessage, Failure> Create(
        string deduplicationKey,
        string toEmail,
        string subject,
        string htmlBody,
        string textBody,
        DateTimeOffset now)
    {
        var errors = DomainErrors.Collect();

        if (string.IsNullOrWhiteSpace(deduplicationKey) ||
            deduplicationKey.Length > MAX_DEDUPLICATION_KEY_LENGTH)
        {
            errors.Add(Error.Validation(
                "emailOutbox.create.deduplicationKey",
                $"Deduplication key must be non-empty and at most {MAX_DEDUPLICATION_KEY_LENGTH} characters.",
                nameof(deduplicationKey)));
        }

        if (string.IsNullOrWhiteSpace(toEmail) ||
            toEmail.Length > MAX_EMAIL_LENGTH ||
            !MailAddress.TryCreate(toEmail, out _))
        {
            errors.Add(Error.Validation(
                "emailOutbox.create.toEmail",
                "Recipient address must be a valid email address.",
                nameof(toEmail)));
        }

        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MAX_SUBJECT_LENGTH)
        {
            errors.Add(Error.Validation(
                "emailOutbox.create.subject",
                $"Subject must be non-empty and at most {MAX_SUBJECT_LENGTH} characters.",
                nameof(subject)));
        }

        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            errors.Add(Error.Validation(
                "emailOutbox.create.htmlBody",
                "HTML body cannot be empty.",
                nameof(htmlBody)));
        }

        if (string.IsNullOrWhiteSpace(textBody))
        {
            errors.Add(Error.Validation(
                "emailOutbox.create.textBody",
                "Text body cannot be empty.",
                nameof(textBody)));
        }

        if (now == default)
        {
            errors.Add(Error.Validation(
                "emailOutbox.create.now",
                "Current time cannot be default.",
                nameof(now)));
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new EmailOutboxMessage
        {
            DeduplicationKey = deduplicationKey,
            ToEmail = toEmail,
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = textBody,
            NextAttemptAt = now
        };
    }

    /// <summary>
    /// Строка уже обработана: отправлена или окончательно провалена.
    /// </summary>
    public bool IsProcessed => SentAt is not null || FailedAt is not null;

    /// <summary>
    /// Строку пора отправлять: она не обработана и <see cref="NextAttemptAt"/> уже наступил.
    /// </summary>
    /// <remarks>
    /// Сравнение нестрогое: ровно в <see cref="NextAttemptAt"/> попытка разрешена.
    /// </remarks>
    /// <param name="now">Текущий момент по бизнес-часам.</param>
    public bool IsReady(DateTimeOffset now) => !IsProcessed && NextAttemptAt <= now;

    /// <summary>
    /// Фиксирует, что транспорт принял письмо.
    /// </summary>
    /// <remarks>
    /// Идемпотентно: повторная отметка уже обработанной строки — успех без изменений.
    /// Код последней ошибки сбрасывается: успешная отправка после ретраев — нормальный итог.
    /// </remarks>
    /// <param name="now">Момент отправки по бизнес-часам.</param>
    public UnitResult<Failure> MarkSent(DateTimeOffset now)
    {
        if (IsProcessed)
        {
            return DomainErrors.Ok();
        }

        SentAt = now;
        LastError = null;
        MarkUpdated();

        return DomainErrors.Ok();
    }

    /// <summary>
    /// Записывает неудачную попытку: увеличивает счётчик, сохраняет код ошибки и планирует
    /// следующую попытку либо окончательно проваливает строку.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FailedAt"/> ставится, когда <paramref name="attempts"/> достигло
    /// <paramref name="maxAttempts"/>: политика (расписание backoff и предел попыток) приходит
    /// из Application, домен только согласованно применяет её.
    /// </para>
    /// <para>
    /// <paramref name="errorCode"/> — код ошибки транспорта, а не текст исключения:
    /// сообщение SMTP может содержать адрес получателя, а <see cref="LastError"/> —
    /// диагностика без персональных данных.
    /// </para>
    /// </remarks>
    /// <param name="errorCode">Код ошибки транспорта, не пустой.</param>
    /// <param name="now">Текущий момент по бизнес-часам.</param>
    /// <param name="nextAttemptAt">Момент следующей попытки; не раньше <paramref name="now"/>.</param>
    /// <param name="maxAttempts">Предел попыток; не меньше единицы.</param>
    public UnitResult<Failure> RecordFailure(
        string errorCode,
        DateTimeOffset now,
        DateTimeOffset nextAttemptAt,
        int maxAttempts)
    {
        if (IsProcessed)
        {
            return DomainErrors.Ok();
        }

        var errors = DomainErrors.Collect();

        if (string.IsNullOrWhiteSpace(errorCode) || errorCode.Length > MAX_LAST_ERROR_LENGTH)
        {
            errors.Add(Error.Validation(
                "emailOutbox.recordFailure.errorCode",
                $"Error code must be non-empty and at most {MAX_LAST_ERROR_LENGTH} characters.",
                nameof(errorCode)));
        }

        if (maxAttempts < 1)
        {
            errors.Add(Error.Validation(
                "emailOutbox.recordFailure.maxAttempts",
                "Maximum attempts must be at least one.",
                nameof(maxAttempts)));
        }

        if (nextAttemptAt < now)
        {
            errors.Add(Error.Validation(
                "emailOutbox.recordFailure.nextAttemptAt",
                "The next attempt cannot be scheduled in the past.",
                nameof(nextAttemptAt)));
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        Attempts++;
        LastError = errorCode;
        NextAttemptAt = nextAttemptAt;

        if (Attempts >= maxAttempts)
        {
            FailedAt = now;
        }

        MarkUpdated();

        return DomainErrors.Ok();
    }
}
