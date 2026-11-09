using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Emails.CleanupSentEmails;

/// <summary>
/// Удаляет обработанные строки outbox старше ретенции.
/// </summary>
/// <remarks>
/// <para>
/// Тело письма содержит открытый токен, поэтому хранить его дольше, чем нужно для доставки,
/// нельзя. Отправленные и окончательно проваленные строки удаляются через
/// <c>EmailOutbox:RetentionDays</c> (по умолчанию 7 дней): TTL токена (24 часа / 30 минут)
/// истекает заметно раньше, и к моменту уборки строка уже не содержит рабочего секрета.
/// </para>
/// <para>
/// Уборка идемпотентна: <c>DELETE … WHERE sent_at &lt; cutoff OR failed_at &lt; cutoff</c>
/// можно запускать из нескольких инстансов одновременно.
/// </para>
/// </remarks>
public sealed class CleanupSentEmailsHandler
    : ICommandHandler<CleanupSentEmailsResult, CleanupSentEmailsCommand>
{
    private readonly IEmailOutboxRepository _outbox;
    private readonly EmailOutboxOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CleanupSentEmailsHandler> _logger;

    public CleanupSentEmailsHandler(
        IEmailOutboxRepository outbox,
        EmailOutboxOptions options,
        TimeProvider timeProvider,
        ILogger<CleanupSentEmailsHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _outbox = outbox;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Удаляет строки, обработанные раньше <c>now - retention</c>.
    /// </summary>
    public async Task<Result<CleanupSentEmailsResult, Failure>> Handle(
        CleanupSentEmailsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var retentionDays = _options.RetentionDays > 0
            ? _options.RetentionDays
            : EmailOutboxOptions.DEFAULT_RETENTION_DAYS;

        var deleted = await _outbox.DeleteProcessedAsync(
            _timeProvider.GetUtcNow(),
            TimeSpan.FromDays(retentionDays),
            cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Email outbox cleanup removed {Deleted} processed rows older than {RetentionDays} days.",
                deleted,
                retentionDays);
        }

        return new CleanupSentEmailsResult(deleted);
    }
}
