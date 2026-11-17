using BarberSearch.Application.Push;
using BarberSearch.Application.Push.CleanupPushNotifications;
using BarberSearch.Application.Push.CreateDuePushNotifications;
using BarberSearch.Application.Push.DeliverPendingPushNotifications;
using Shared;

namespace BarberSearch.Web.Push;

/// <summary>
/// Фоновый воркер push-уведомлений клиентам: создание напоминаний, доставка и суточная уборка.
/// </summary>
/// <remarks>
/// <para>
/// Отдельный воркер, а не шаг <c>ReminderDispatchService</c>: у канала свой интервал
/// (<c>Push:IntervalSeconds</c>), свой флаг и своя очередь, а итог тика —
/// <see cref="PushTickResult"/>. Так отключение push не трогает Telegram-напоминания
/// барберам и наоборот.
/// </para>
/// <para>
/// Образец — <c>EmailDispatchService</c>: первый тик сразу при старте (рестарт не должен
/// откладывать напоминание), затем <see cref="PeriodicTimer"/> на
/// <c>Push:IntervalSeconds</c>. Каждый шаг выполняется в собственном scope через
/// <see cref="IServiceScopeFactory"/>: обработчики scoped и держат <c>ApplicationDbContext</c>.
/// </para>
/// <para>
/// <c>Push:Enabled=false</c> (значение по умолчанию в <c>appsettings.json</c> до поправки
/// Политики конфиденциальности) выключает воркер целиком: он только ждёт тики, не создаёт
/// строк и не отправляет сообщений. <c>Enabled=true</c> без ключей в Development — режим
/// разработки: текст пишется в журнал, строка становится <c>Skipped</c>. В остальных
/// окружениях такие строки остаются <c>Pending</c> до починки конфигурации (см.
/// <c>PushOptions.DevMode</c>).
/// </para>
/// <para>
/// Исключение одного шага логируется и не убивает цикл: следующий тик повторит работу.
/// Отмена <c>stoppingToken</c> — штатный выход. Уборка запускается не чаще раза в сутки.
/// </para>
/// </remarks>
public sealed class PushDispatchService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PushOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PushDispatchService> _logger;
    private readonly TaskCompletionSource _initialTickCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan _tickInterval;

    private DateOnly? _lastCleanupDate;

    public PushDispatchService(
        IServiceScopeFactory scopeFactory,
        PushOptions options,
        TimeProvider timeProvider,
        ILogger<PushDispatchService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _tickInterval = options.IntervalSeconds > 0
            ? TimeSpan.FromSeconds(options.IntervalSeconds)
            : TimeSpan.FromSeconds(PushOptions.DEFAULT_INTERVAL_SECONDS);
    }

    /// <summary>
    /// Завершение первого тика, запущенного при старте хоста.
    /// </summary>
    /// <remarks>
    /// Нужен интеграционным тестам: стартовый тик идёт параллельно запуску хоста, и тест,
    /// который сидирует бронь сразу после старта, должен дождаться его окончания. Задача
    /// завершается и при выключенном воркере, и при обработанной ошибке шага.
    /// </remarks>
    public Task InitialTickCompleted => _initialTickCompleted.Task;

    /// <summary>
    /// Один тик: создание, доставка и (раз в сутки) уборка. Публичный, чтобы тест мог
    /// прогнать тик без ожидания таймера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<PushTickResult> RunTickAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return PushTickResult.Empty;
        }

        var created = await RunStepAsync("create", CreateDueAsync, 0, cancellationToken);

        var (sent, skipped, failed) = await RunStepAsync(
            "deliver",
            DeliverPendingAsync,
            (Sent: 0, Skipped: 0, Failed: 0),
            cancellationToken);

        await RunCleanupIfDueAsync(cancellationToken);

        // Одна строка на тик — по ней видно, что воркер жив и что он сделал.
        _logger.LogInformation(
            "Push tick finished: created {Created}, sent {Sent}, skipped {Skipped}, failed {Failed}.",
            created,
            sent,
            skipped,
            failed);

        return new PushTickResult(created, sent, skipped, failed);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            try
            {
                // Первый тик — сразу: после рестарта напоминание не должно ждать интервал.
                await RunTickAsync(stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                _initialTickCompleted.TrySetResult();
            }

            using var timer = new PeriodicTimer(_tickInterval);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!_options.Enabled)
                {
                    // Выключенный воркер не создаёт ни строк, ни сообщений; он лишь ждёт тики.
                    // PushOptions — синглтон, прочитанный на старте: включение флага требует
                    // перезапуска процесса, а не подхватывается на лету.
                    continue;
                }

                await RunTickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка хоста: ожидание тика или текущий шаг отменены.
        }
    }

    /// <summary>
    /// Создаёт напоминания, созревшие на текущем тике.
    /// </summary>
    private async Task<int> CreateDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateDuePushNotificationsHandler>();

        var result = await handler.Handle(
            new CreateDuePushNotificationsCommand(),
            cancellationToken);

        if (result.IsFailure)
        {
            LogStepFailure("create", result.Error);

            return 0;
        }

        return result.Value.Created;
    }

    /// <summary>
    /// Отправляет очередную пачку уведомлений.
    /// </summary>
    private async Task<(int Sent, int Skipped, int Failed)> DeliverPendingAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<DeliverPendingPushNotificationsHandler>();

        var result = await handler.Handle(
            new DeliverPendingPushNotificationsCommand(),
            cancellationToken);

        if (result.IsFailure)
        {
            LogStepFailure("deliver", result.Error);

            return (0, 0, 0);
        }

        return (result.Value.Sent, result.Value.Skipped, result.Value.Failed);
    }

    /// <summary>
    /// Запускает уборку, если сегодня её ещё не было.
    /// </summary>
    /// <remarks>
    /// Дата берётся из <see cref="TimeProvider"/>: в тестах сутки двигаются без ожидания.
    /// При ошибке дата не записывается, и следующий тик повторит уборку.
    /// </remarks>
    private async Task RunCleanupIfDueAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        if (_lastCleanupDate == today)
        {
            return;
        }

        // -1 — шаг не выполнился: дату не записываем, следующий тик попробует снова.
        var deleted = await RunStepAsync(
            "cleanup",
            CleanupAsync,
            -1,
            cancellationToken);

        if (deleted >= 0)
        {
            _lastCleanupDate = today;
        }
    }

    /// <summary>
    /// Удаляет старые уведомления и давно не подтверждённые токены.
    /// </summary>
    private async Task<int> CleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<CleanupPushNotificationsHandler>();

        var result = await handler.Handle(
            new CleanupPushNotificationsCommand(),
            cancellationToken);

        if (result.IsFailure)
        {
            LogStepFailure("cleanup", result.Error);

            return -1;
        }

        return result.Value.DeletedNotifications + result.Value.DeletedDeviceTokens;
    }

    /// <summary>
    /// Выполняет шаг с логированием ошибки: сбой одного шага не останавливает тик и цикл.
    /// </summary>
    /// <remarks>
    /// Отмена при остановке хоста пробрасывается — это штатный выход, а не ошибка шага.
    /// Остальные исключения логируются: без этого неизвестный дефект одного шага убил бы
    /// фоновый сервис, и push перестали бы приходить до рестарта.
    /// </remarks>
    private async Task<T> RunStepAsync<T>(
        string step,
        Func<CancellationToken, Task<T>> action,
        T fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Ошибка шага не должна убивать фоновый цикл.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogError(
                exception,
                "Push step {Step} failed; the next tick will retry.",
                step);

            return fallback;
        }
    }

    /// <summary>
    /// Логирует <c>Failure</c> шага без токенов и текста сообщений.
    /// </summary>
    private void LogStepFailure(string step, Failure failure)
    {
        var codes = failure.Errors.Count == 0
            ? "unknown"
            : string.Join(", ", failure.Errors.Select(error => error.Code));

        _logger.LogWarning(
            "Push step {Step} failed: {Codes}.",
            step,
            codes);
    }
}
