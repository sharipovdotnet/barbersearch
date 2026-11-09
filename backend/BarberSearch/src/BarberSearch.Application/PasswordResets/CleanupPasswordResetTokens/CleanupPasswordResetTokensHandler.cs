using System.Data.Common;
using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.PasswordResets.CleanupPasswordResetTokens;

/// <summary>
/// Удаляет строки, истёкшие или использованные более
/// <see cref="PasswordResetLifetime.TOKEN_RETENTION"/> назад.
/// </summary>
/// <remarks>
/// <para>
/// Активная и недавняя использованная строки остаются: по активной считается кулдаун
/// переотправки, а недавняя использованная нужна для истории. 30 дней — заведомо больше
/// 30-минутного TTL ссылки.
/// </para>
/// <para>
/// <c>DELETE</c> идемпотентен: несколько инстансов могут убирать одновременно. Ошибки БД
/// логируются и не бросаются наружу — следующий тик повторит попытку, а хост не упадёт.
/// </para>
/// </remarks>
public sealed class CleanupPasswordResetTokensHandler
    : ICommandHandler<CleanupPasswordResetTokensResult, CleanupPasswordResetTokensCommand>
{
    private readonly IPasswordResetTokenRepository _tokens;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CleanupPasswordResetTokensHandler> _logger;

    public CleanupPasswordResetTokensHandler(
        IPasswordResetTokenRepository tokens,
        TimeProvider timeProvider,
        ILogger<CleanupPasswordResetTokensHandler> logger)
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
    public async Task<Result<CleanupPasswordResetTokensResult, Failure>> Handle(
        CleanupPasswordResetTokensCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var deleted = await _tokens.DeleteExpiredAsync(
                _timeProvider.GetUtcNow(),
                PasswordResetLifetime.TOKEN_RETENTION,
                cancellationToken);

            if (deleted > 0)
            {
                _logger.LogDebug(
                    "Deleted {Count} stale password reset tokens.",
                    deleted);
            }

            return new CleanupPasswordResetTokensResult(deleted);
        }
        catch (DbException exception)
        {
            _logger.LogWarning(
                exception,
                "Password reset token cleanup failed; the next run will retry.");

            return new CleanupPasswordResetTokensResult(0);
        }
        catch (TimeoutException exception)
        {
            _logger.LogWarning(
                exception,
                "Password reset token cleanup timed out; the next run will retry.");

            return new CleanupPasswordResetTokensResult(0);
        }
    }
}
