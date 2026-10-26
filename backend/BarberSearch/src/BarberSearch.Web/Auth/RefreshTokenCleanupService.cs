using System.Data.Common;
using BarberSearch.Application.RefreshTokens;
using BarberSearch.Domain.RefreshTokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BarberSearch.Web.Auth;

/// <summary>
/// Настройки фоновой уборки refresh-токенов (R-50).
/// </summary>
/// <param name="Interval">Интервал между запусками уборки; должен быть больше нуля.</param>
public sealed record RefreshTokenCleanupOptions(TimeSpan Interval);

/// <summary>
/// Фоновая уборка истёкших refresh-токенов (R-50).
/// </summary>
/// <remarks>
/// <para>
/// Без уборки таблица <c>refresh_tokens</c> растёт линейно: ротация добавляет строку на каждый
/// refresh, а удалять старые было некому. Сервис запускает <see cref="CleanupOnceAsync"/>
/// сразу при старте (частые рестарты не должны откладывать уборку на целый интервал), затем
/// повторяет её каждые <see cref="RefreshTokenCleanupOptions.Interval"/>. Репозиторий
/// удаляет строки с <c>expires_at &lt; now - 30 дней</c>.
/// </para>
/// <para>
/// Граница в 30 дней (<see cref="RefreshToken.MAX_LIFETIME_DAYS"/>) — не произвольный срок
/// хранения, а инвариант ссылки <c>replaced_by</c>: замена выпускается раньше истечения
/// родителя и живёт не дольше 30 дней, поэтому у каждой удаляемой строки все потомки уже
/// истекли. FK <c>fk_refresh_tokens_refresh_tokens_replaced_by</c> с
/// <c>ON DELETE SET NULL</c> никогда не обнулит ссылку живого токена. Отозванные, но ещё не
/// истёкшие строки остаются нужны reuse-detection.
/// </para>
/// <para>
/// <c>DELETE</c> идемпотентен и не требует распределённой блокировки: несколько инстансов
/// приложения могут убирать истёкшие строки одновременно, каждая строка удалится один раз.
/// Ошибки БД на старте не роняют хост: они логируются, а следующий тик повторяет попытку.
/// </para>
/// </remarks>
public sealed class RefreshTokenCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly RefreshTokenCleanupOptions _options;
    private readonly ILogger<RefreshTokenCleanupService> _logger;
    private readonly TaskCompletionSource _initialCleanupCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Создаёт сервис уборки.
    /// </summary>
    /// <param name="scopeFactory">Фабрика scope: репозиторий scoped и требует свой контекст.</param>
    /// <param name="timeProvider">Часы приложения: граница уборки управляема в тестах.</param>
    /// <param name="options">Интервал между запусками; больше нуля.</param>
    /// <param name="logger">Журнал уборки.</param>
    /// <exception cref="ArgumentOutOfRangeException">Интервал не больше нуля.</exception>
    public RefreshTokenCleanupService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        RefreshTokenCleanupOptions options,
        ILogger<RefreshTokenCleanupService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (options.Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Interval,
                "Refresh token cleanup interval must be greater than zero.");
        }

        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Один прогон уборки: удаляет строки с <c>expires_at &lt; now - 30 дней</c> и возвращает
    /// их количество.
    /// </summary>
    /// <remarks>
    /// Публичный, чтобы интеграционный тест мог проверить уборку без ожидания интервала.
    /// Каждый вызов создаёт собственный scope: <see cref="IRefreshTokenRepository"/> scoped
    /// и держит <c>ApplicationDbContext</c>.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<int> CleanupOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();

        // Ротация выпускает замену до истечения родителя, поэтому 30 дней после expiry —
        // достаточный запас: все потомки удаляемой строки к этому моменту уже истекли,
        // и ON DELETE SET NULL не тронет ссылку живого токена.
        var cutoff = _timeProvider.GetUtcNow()
            - TimeSpan.FromDays(RefreshToken.MAX_LIFETIME_DAYS);

        return await repository.DeleteExpiredAsync(cutoff, cancellationToken);
    }

    /// <summary>
    /// Завершение первого прогона уборки, запущенного при старте хоста.
    /// </summary>
    /// <remarks>
    /// Нужен тестам и диагностике: стартовая уборка выполняется параллельно запуску хоста,
    /// поэтому тест, который сидирует истёкшие строки, должен дождаться её завершения —
    /// иначе DELETE может забрать засеянные строки раньше проверки. Задача завершается и при
    /// обработанной ошибке БД: сервис продолжает работать и повторит уборку по таймеру.
    /// </remarks>
    public Task InitialCleanupCompleted => _initialCleanupCompleted.Task;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            try
            {
                // Первый прогон — сразу: при частых рестартах (деплой, перезапуск pod'а)
                // ожидание интервала откладывало бы уборку на неопределённое время.
                await RunCleanupAsync(stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                // Сигнал снимается и при штатной отмене, и при обработанном сбое БД:
                // ожидающий тест не должен зависеть от того, чем закончился прогон.
                _initialCleanupCompleted.TrySetResult();
            }

            using var timer = new PeriodicTimer(_options.Interval);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunCleanupAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка хоста: текущий DELETE или ожидание тика отменены.
        }
    }

    /// <summary>
    /// Обёртка одного прогона: эксплуатационные сбои БД логируются, но не останавливают хост.
    /// </summary>
    /// <remarks>
    /// Ловятся только <see cref="DbException"/> (нет соединения, SQLSTATE, таймаут команды)
    /// и <see cref="TimeoutException"/>: следующий тик повторит уборку. Общий
    /// <see cref="Exception"/> не перехватывается (CA1031): неизвестная ошибка — дефект кода,
    /// и она должна быть видна, а не превращаться в бесконечный warning раз в интервал.
    /// </remarks>
    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await CleanupOnceAsync(cancellationToken).ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.LogInformation(
                    "Deleted {Count} expired refresh tokens (R-50).",
                    deleted);
            }
        }
        catch (DbException exception)
        {
            // База может быть недоступна на старте: приложение обязано подняться,
            // а уборка повторится на следующем тике.
            _logger.LogWarning(
                exception,
                "Refresh token cleanup failed; the next run will retry (R-50).");
        }
        catch (TimeoutException exception)
        {
            _logger.LogWarning(
                exception,
                "Refresh token cleanup timed out; the next run will retry (R-50).");
        }
    }
}
