using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.EmailVerifications.CleanupEmailVerificationTokens;

/// <summary>
/// Удаляет строки, истёкшие или использованные более
/// <see cref="EmailVerificationLifetime.TOKEN_RETENTION"/> назад.
/// </summary>
/// <remarks>
/// <para>
/// Активные и недавно истёкшие строки остаются: по активной строке считается кулдаун
/// переотправки, а использованная нужна для идемпотентного ответа на повторный клик по
/// ссылке. 30 дней — заведомо больше суточного TTL письма.
/// </para>
/// <para>
/// <c>DELETE</c> идемпотентен: несколько инстансов могут убирать одновременно, каждая
/// строка удалится один раз.
/// </para>
/// </remarks>
public sealed class CleanupEmailVerificationTokensHandler
    : ICommandHandler<CleanupEmailVerificationTokensResult, CleanupEmailVerificationTokensCommand>
{
    private readonly IEmailVerificationTokenRepository _tokens;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CleanupEmailVerificationTokensHandler> _logger;

    public CleanupEmailVerificationTokensHandler(
        IEmailVerificationTokenRepository tokens,
        TimeProvider timeProvider,
        ILogger<CleanupEmailVerificationTokensHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _tokens = tokens;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Удаляет строки, у которых истёк срок или которые использованы слишком давно.
    /// </summary>
    public async Task<Result<CleanupEmailVerificationTokensResult, Failure>> Handle(
        CleanupEmailVerificationTokensCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var deleted = await _tokens.DeleteExpiredAsync(
            _timeProvider.GetUtcNow(),
            EmailVerificationLifetime.TOKEN_RETENTION,
            cancellationToken);

        if (deleted > 0)
        {
            _logger.LogDebug(
                "Deleted {Count} stale email verification tokens.",
                deleted);
        }

        return new CleanupEmailVerificationTokensResult(deleted);
    }
}
