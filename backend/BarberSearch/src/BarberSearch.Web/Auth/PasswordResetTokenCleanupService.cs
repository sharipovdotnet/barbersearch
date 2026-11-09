using System.Data.Common;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.PasswordResets;
using BarberSearch.Application.PasswordResets.CleanupPasswordResetTokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Фоновая уборка старых одноразовых токенов сброса пароля.
/// </summary>
/// <remarks>
/// <para>
/// Без уборки <c>password_reset_tokens</c> растёт на каждый запрос сброса и переотправку.
/// Сервис удаляет строки, истёкшие или использованные более
/// <see cref="PasswordResetLifetime.TOKEN_RETENTION"/> назад, сразу при старте (частые
/// рестарты не откладывают уборку на час) и далее раз в <see cref="INTERVAL"/>.
/// </para>
/// <para>
/// <c>DELETE</c> идемпотентен и не требует блокировки: несколько инстансов могут убирать
/// одновременно. Ошибки БД на старте не роняют хост — они логируются, следующий тик
/// повторит попытку. Уборка идёт через Application-обработчик: бизнес-граница ретенции
/// живёт там же, где TTL.
/// </para>
/// </remarks>
public sealed class PasswordResetTokenCleanupService : BackgroundService
{
    /// <summary>
    /// Интервал между запусками уборки — 60 минут.
    /// </summary>
    public static readonly TimeSpan INTERVAL = TimeSpan.FromMinutes(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PasswordResetTokenCleanupService> _logger;

    private readonly TaskCompletionSource _initialCleanupCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Создаёт сервис уборки.
    /// </summary>
    /// <param name="scopeFactory">Фабрика scope: репозиторий scoped и требует свой контекст.</param>
    /// <param name="logger">Журнал уборки.</param>
    public PasswordResetTokenCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<PasswordResetTokenCleanupService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Завершение первого прогона уборки, запущенного при старте хоста.
    /// </summary>
    /// <remarks>
    /// Интеграционному тесту нужно дождаться стартовой уборки, иначе DELETE может гоняться
    /// с сидированием строк. Задача завершается и при обработанной ошибке БД.
    /// </remarks>
    public Task InitialCleanupCompleted => _initialCleanupCompleted.Task;

    /// <summary>
    /// Один прогон уборки и возвращает количество удалённых строк.
    /// </summary>
    /// <remarks>
    /// Публичный, чтобы тест мог проверить уборку без ожидания интервала. Каждый вызов
    /// создаёт собственный scope: обработчик и репозиторий scoped.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<int> CleanupOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var handler = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<CleanupPasswordResetTokensResult, CleanupPasswordResetTokensCommand>>();

        var result = await handler.Handle(
            new CleanupPasswordResetTokensCommand(),
            cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Password reset token cleanup failed: {ErrorCodes}.",
                string.Join(", ", result.Error.Errors.Select(error => error.Code)));

            return 0;
        }

        return result.Value.Deleted;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            try
            {
                await RunCleanupAsync(stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                _initialCleanupCompleted.TrySetResult();
            }

            using var timer = new PeriodicTimer(INTERVAL);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunCleanupAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка хоста.
        }
    }

    /// <summary>
    /// Обёртка одного прогона: эксплуатационные сбои БД логируются, но не останавливают хост.
    /// </summary>
    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await CleanupOnceAsync(cancellationToken).ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.LogInformation(
                    "Deleted {Count} stale password reset tokens.",
                    deleted);
            }
        }
        catch (DbException exception)
        {
            _logger.LogWarning(
                exception,
                "Password reset token cleanup failed; the next run will retry.");
        }
        catch (TimeoutException exception)
        {
            _logger.LogWarning(
                exception,
                "Password reset token cleanup timed out; the next run will retry.");
        }
    }
}
