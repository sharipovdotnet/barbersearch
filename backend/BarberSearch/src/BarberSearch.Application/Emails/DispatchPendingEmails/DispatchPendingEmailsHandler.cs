using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.EmailOutbox;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Emails.DispatchPendingEmails;

/// <summary>
/// Обработчик тика dispatching: отправляет пачку готовых строк <c>email_outbox</c>.
/// </summary>
/// <remarks>
/// <para>
/// Пачка читается внутри одной транзакции с <c>FOR UPDATE SKIP LOCKED</c>: второй инстанс
/// приложения или параллельный тик той же пачки не получит. Транзакция держится, пока идут
/// SMTP-вызовы, и коммитится один раз — вместе с отметками <c>sent_at</c> /
/// <c>attempts</c> / <c>failed_at</c>. Аварийное завершение снимает блокировку, строка
/// возвращается в очередь и уходит повторно: доставка at-least-once, потеря письма
/// недопустима, дубликат допустим.
/// </para>
/// <para>
/// Расписание повторов — 1, 5, 15 минут, 1 и 6 часов; после <c>EmailOutbox:MaxAttempts</c>
/// (по умолчанию пяти) строка получает <c>failed_at</c> и больше не отправляется. Ошибка
/// одного письма не останавливает пачку и не откатывает уже отправленные отметки.
/// </para>
/// <para>
/// В лог попадают только <c>outboxId</c>, номер попытки и код ошибки: адрес получателя,
/// тема и тело письма — персональные данные и секрет.
/// </para>
/// </remarks>
public sealed class DispatchPendingEmailsHandler
    : ICommandHandler<DispatchPendingEmailsResult, DispatchPendingEmailsCommand>
{
    /// <summary>
    /// Код ошибки, если транспорт не вернул ни одного кода.
    /// </summary>
    private const string UNKNOWN_ERROR_CODE = "emailOutbox.send.unknown";

    /// <summary>
    /// Задержки повторов в минутах: после первой, второй, … неудачной попытки.
    /// </summary>
    private static readonly int[] BackoffMinutes = [1, 5, 15, 60, 360];

    private readonly IEmailOutboxRepository _outbox;
    private readonly IEmailSender _sender;
    private readonly IUnitOfWork _unitOfWork;
    private readonly EmailOutboxOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DispatchPendingEmailsHandler> _logger;

    public DispatchPendingEmailsHandler(
        IEmailOutboxRepository outbox,
        IEmailSender sender,
        IUnitOfWork unitOfWork,
        EmailOutboxOptions options,
        TimeProvider timeProvider,
        ILogger<DispatchPendingEmailsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _outbox = outbox;
        _sender = sender;
        _unitOfWork = unitOfWork;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Отправляет очередную пачку и возвращает счётчики для лога воркера.
    /// </summary>
    public async Task<Result<DispatchPendingEmailsResult, Failure>> Handle(
        DispatchPendingEmailsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _timeProvider.GetUtcNow();
        var batchSize = _options.BatchSize > 0
            ? _options.BatchSize
            : EmailOutboxOptions.DEFAULT_BATCH_SIZE;
        var maxAttempts = _options.MaxAttempts > 0
            ? _options.MaxAttempts
            : EmailOutboxOptions.DEFAULT_MAX_ATTEMPTS;

        return await _unitOfWork.ExecuteAsync<DispatchPendingEmailsResult>(async transactionToken =>
        {
            var ready = await _outbox.GetReadyForDispatchAsync(now, batchSize, transactionToken);
            if (ready.IsFailure)
            {
                return ready.Error;
            }

            var sent = 0;
            var retried = 0;
            var failed = 0;

            foreach (var message in ready.Value)
            {
                var delivery = await _sender.SendAsync(
                    new EmailMessage(message.ToEmail, message.Subject, message.HtmlBody, message.TextBody),
                    transactionToken);

                if (delivery.IsSuccess)
                {
                    var markedSent = message.MarkSent(now);
                    if (markedSent.IsFailure)
                    {
                        return markedSent.Error;
                    }

                    sent++;
                    continue;
                }

                var attempt = message.Attempts + 1;
                var errorCode = FirstErrorCode(delivery.Error);
                var nextAttemptAt = now.AddMinutes(Backoff(attempt));

                var recorded = message.RecordFailure(errorCode, now, nextAttemptAt, maxAttempts);
                if (recorded.IsFailure)
                {
                    return recorded.Error;
                }

                if (message.FailedAt is not null)
                {
                    failed++;

                    // Переход в failed — единственный исход, который требует внимания:
                    // письмо больше не уйдёт само, нужен разбор причины.
                    _logger.LogError(
                        "Email outbox row {OutboxId} failed permanently after {Attempts} attempts: {ErrorCode}.",
                        message.Id,
                        message.Attempts,
                        errorCode);
                }
                else
                {
                    retried++;

                    _logger.LogWarning(
                        "Email outbox row {OutboxId} attempt {Attempts} failed: {ErrorCode}; next attempt at {NextAttemptAt}.",
                        message.Id,
                        message.Attempts,
                        errorCode,
                        nextAttemptAt);
                }
            }

            if (sent + retried + failed == 0)
            {
                return DispatchPendingEmailsResult.Empty;
            }

            // Отметки коммитятся одной транзакцией с блокировкой пачки: строки не вернутся
            // в очередь между отправкой и записью результата.
            var saved = await _outbox.SaveChangesAsync(transactionToken);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            return new DispatchPendingEmailsResult(sent, retried, failed);
        }, cancellationToken);
    }

    /// <summary>
    /// Задержка следующей попытки в минутах: 1, 5, 15, 60, 360.
    /// </summary>
    private static int Backoff(int attempt)
    {
        var index = Math.Clamp(attempt - 1, 0, BackoffMinutes.Length - 1);

        return BackoffMinutes[index];
    }

    /// <summary>
    /// Код первой ошибки транспорта — <c>last_error</c> без текста исключения и PII.
    /// </summary>
    private static string FirstErrorCode(Failure failure)
    {
        var code = failure.Errors
            .Select(error => error.Code)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (string.IsNullOrWhiteSpace(code))
        {
            return UNKNOWN_ERROR_CODE;
        }

        return code.Length > EmailOutboxMessage.MAX_LAST_ERROR_LENGTH
            ? code[..EmailOutboxMessage.MAX_LAST_ERROR_LENGTH]
            : code;
    }
}
