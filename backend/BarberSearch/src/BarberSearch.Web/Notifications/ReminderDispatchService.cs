using BarberSearch.Application.Notifications;
using BarberSearch.Application.Notifications.CleanupOldNotifications;
using BarberSearch.Application.Notifications.CreateDueNotifications;
using BarberSearch.Application.Notifications.DeliverPendingNotifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Web.Notifications;

/// <summary>
/// Фоновый воркер напоминаний: создание, доставка и суточная уборка раз в минуту.
/// </summary>
/// <remarks>
/// <para>
/// Образец — <c>RefreshTokenCleanupService</c>: первый тик сразу при старте (частые рестарты
/// не должны откладывать напоминания), затем <see cref="PeriodicTimer"/> на 60 секунд.
/// Каждый шаг выполняется в собственном scope через <see cref="IServiceScopeFactory"/>:
/// обработчики scoped и держат <c>ApplicationDbContext</c>, а scope хоста для этого не
/// подходит — он живёт всё время работы приложения.
/// </para>
/// <para>
/// <c>Reminders:Enabled=false</c> (по умолчанию в разработке, решение D8) выключает воркер
/// целиком: он только ждёт тики, не создаёт строк и не отправляет сообщений. Без
/// <c>Telegram:BotToken</c> воркер тоже не падает: шаг доставки пропускается, строки остаются
/// <c>Pending</c> в инбоксе, а предупреждение пишется один раз.
/// </para>
/// <para>
/// Исключение одного шага логируется и не убивает цикл: следующий тик повторит работу.
/// Отмена <c>stoppingToken</c> — штатный выход. Уборка старше <c>RetentionDays</c> запускается
/// не чаще раза в сутки.
/// </para>
/// </remarks>
public sealed class ReminderDispatchService : BackgroundService
{
    /// <summary>
    /// Период тика: раз в минуту, как требует окно напоминания (§3.1).
    /// </summary>
    public static readonly TimeSpan TICK_INTERVAL = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RemindersOptions _remindersOptions;
    private readonly ITelegramBot _telegramBot;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReminderDispatchService> _logger;
    private readonly TaskCompletionSource _initialTickCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private DateOnly? _lastCleanupDate;
    private bool _telegramUnconfiguredLogged;

    public ReminderDispatchService(
        IServiceScopeFactory scopeFactory,
        RemindersOptions remindersOptions,
        ITelegramBot telegramBot,
        TimeProvider timeProvider,
        ILogger<ReminderDispatchService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(remindersOptions);
        ArgumentNullException.ThrowIfNull(telegramBot);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _remindersOptions = remindersOptions;
        _telegramBot = telegramBot;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Завершение первого тика, запущенного при старте хоста.
    /// </summary>
    /// <remarks>
    /// Нужен интеграционным тестам: стартовый тик идёт параллельно запуску хоста, и тест,
    /// который сидирует настройки сразу после старта, должен дождаться его окончания.
    /// Задача завершается и при выключенном воркере, и при обработанной ошибке шага.
    /// </remarks>
    public Task InitialTickCompleted => _initialTickCompleted.Task;

    /// <summary>
    /// Один тик: создание, доставка и (раз в сутки) уборка. Публичный, чтобы тест мог
    /// прогнать тик без ожидания таймера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<ReminderTickResult> RunTickAsync(CancellationToken cancellationToken)
    {
        if (!_remindersOptions.Enabled)
        {
            return ReminderTickResult.Empty;
        }

        var created = await RunStepAsync(
            "create",
            CreateDueAsync,
            0,
            cancellationToken);

        var (sent, skipped, failed) = _telegramBot.IsConfigured
            ? await RunStepAsync(
                "deliver",
                DeliverPendingAsync,
                (Sent: 0, Skipped: 0, Failed: 0),
                cancellationToken)
            : SkipDelivery();

        await RunCleanupIfDueAsync(cancellationToken);

        // Одна строка на тик — по ней видно, что воркер жив и что он сделал.
        _logger.LogInformation(
            "Reminder tick finished: created {Created}, sent {Sent}, skipped {Skipped}, failed {Failed}.",
            created,
            sent,
            skipped,
            failed);

        return new ReminderTickResult(created, sent, skipped, failed);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            try
            {
                // Первый тик — сразу: после рестарта напоминание не должно ждать минуту.
                await RunTickAsync(stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                _initialTickCompleted.TrySetResult();
            }

            using var timer = new PeriodicTimer(TICK_INTERVAL);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (!_remindersOptions.Enabled)
                {
                    // Выключенный воркер не создаёт ни строк, ни сообщений; он лишь ждёт тики,
                    // чтобы включение флага не требовало перезапуска процесса.
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
    /// Создаёт уведомления, созревшие на текущей минуте.
    /// </summary>
    private async Task<int> CreateDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateDueNotificationsHandler>();

        var result = await handler.Handle(
            new CreateDueNotificationsCommand(),
            cancellationToken);

        if (result.IsFailure)
        {
            LogStepFailure("create", result.Error);

            return 0;
        }

        return result.Value.Created;
    }

    /// <summary>
    /// Отправляет очередную пачку ожидающих уведомлений.
    /// </summary>
    private async Task<(int Sent, int Skipped, int Failed)> DeliverPendingAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<DeliverPendingNotificationsHandler>();

        var result = await handler.Handle(
            new DeliverPendingNotificationsCommand(),
            cancellationToken);

        if (result.IsFailure)
        {
            LogStepFailure("deliver", result.Error);

            return (0, 0, 0);
        }

        return (result.Value.Sent, result.Value.Skipped, result.Value.Failed);
    }

    /// <summary>
    /// Пропускает доставку без настроенного Telegram и пишет об этом один раз.
    /// </summary>
    /// <remarks>
    /// Строки остаются <c>Pending</c>: когда токен появится, ближайший тик их отправит.
    /// Повторять предупреждение каждый тик нельзя — журнал превратится в шум.
    /// </remarks>
    private (int Sent, int Skipped, int Failed) SkipDelivery()
    {
        if (!_telegramUnconfiguredLogged)
        {
            _telegramUnconfiguredLogged = true;

            _logger.LogWarning(
                "Telegram is not configured: reminder delivery is skipped, notifications stay pending.");
        }

        return (0, 0, 0);
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
    /// Удаляет уведомления старше <c>Reminders:RetentionDays</c>.
    /// </summary>
    private async Task<int> CleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<CleanupOldNotificationsHandler>();

        var result = await handler.Handle(
            new CleanupOldNotificationsCommand(),
            cancellationToken);

        if (result.IsFailure)
        {
            LogStepFailure("cleanup", result.Error);

            return -1;
        }

        return result.Value.Deleted;
    }

    /// <summary>
    /// Выполняет шаг с логированием ошибки: сбой одного шага не останавливает тик и цикл.
    /// </summary>
    /// <remarks>
    /// Отмена при остановке хоста пробрасывается — это штатный выход, а не ошибка шага.
    /// Остальные исключения логируются: без этого неизвестный дефект одного шага убил бы
    /// фоновый сервис, и напоминания перестали бы приходить до рестарта.
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
#pragma warning disable CA1031 // Ошибка шага не должна убивать фоновый цикл (§5).
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogError(
                exception,
                "Reminder step {Step} failed; the next tick will retry.",
                step);

            return fallback;
        }
    }

    /// <summary>
    /// Логирует <c>Failure</c> шага без текста сообщений и данных клиента.
    /// </summary>
    private void LogStepFailure(string step, Failure failure)
    {
        var codes = failure.Errors.Count == 0
            ? "unknown"
            : string.Join(", ", failure.Errors.Select(error => error.Code));

        _logger.LogWarning(
            "Reminder step {Step} failed: {Codes}.",
            step,
            codes);
    }
}
