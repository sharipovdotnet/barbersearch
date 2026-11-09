using BarberSearch.Application.Emails;
using BarberSearch.Application.Emails.CleanupSentEmails;
using BarberSearch.Application.Emails.DispatchPendingEmails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Web.Emails;

/// <summary>
/// Фоновый воркер outbox: отправляет готовые письма и раз в сутки убирает обработанные.
/// </summary>
/// <remarks>
/// <para>
/// Образец — <see cref="BarberSearch.Web.Notifications.ReminderDispatchService"/>: первый тик
/// сразу при старте (после рестарта письма не должны ждать интервал), затем
/// <see cref="PeriodicTimer"/> на <c>EmailOutbox:IntervalSeconds</c>. Каждый шаг выполняется
/// в собственном scope через <see cref="IServiceScopeFactory"/>: обработчики scoped и держат
/// <c>ApplicationDbContext</c>.
/// </para>
/// <para>
/// <c>EmailOutbox:Enabled=false</c> выключает только воркер: обработчики запросов всё равно
/// пишут строки в таблицу, поэтому письма копятся и уходят после включения флага без
/// перезапуска процесса. Health-check воркер не проверяет и из-за выключенного флага
/// не падает.
/// </para>
/// <para>
/// Исключение одного шага логируется и не убивает цикл: следующий тик повторит работу.
/// Отмена <c>stoppingToken</c> — штатный выход.
/// </para>
/// </remarks>
public sealed class EmailDispatchService : BackgroundService
{
    /// <summary>
    /// Интервал тика по умолчанию, если конфигурация пуста.
    /// </summary>
    public static readonly TimeSpan DEFAULT_TICK_INTERVAL =
        TimeSpan.FromSeconds(EmailOutboxOptions.DEFAULT_INTERVAL_SECONDS);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailOutboxOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmailDispatchService> _logger;
    private readonly TaskCompletionSource _initialTickCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan _tickInterval;

    private DateOnly? _lastCleanupDate;

    public EmailDispatchService(
        IServiceScopeFactory scopeFactory,
        EmailOutboxOptions options,
        TimeProvider timeProvider,
        ILogger<EmailDispatchService> logger)
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
            : DEFAULT_TICK_INTERVAL;
    }

    /// <summary>
    /// Завершение первого тика, запущенного при старте хоста.
    /// </summary>
    /// <remarks>
    /// Нужен интеграционным тестам: стартовый тик идёт параллельно запуску хоста, и тест,
    /// который сидирует outbox сразу после старта, должен дождаться его окончания.
    /// Задача завершается и при выключенном воркере, и при обработанной ошибке шага.
    /// </remarks>
    public Task InitialTickCompleted => _initialTickCompleted.Task;

    /// <summary>
    /// Один тик: отправка пачки и (раз в сутки) уборка. Публичный, чтобы тест мог прогнать
    /// тик без ожидания таймера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<EmailTickResult> RunTickAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return EmailTickResult.Empty;
        }

        var (sent, retried, failed) = await RunStepAsync(
            "dispatch",
            DispatchAsync,
            (Sent: 0, Retried: 0, Failed: 0),
            cancellationToken);

        await RunCleanupIfDueAsync(cancellationToken);

        // Одна строка на тик — по ней видно, что воркер жив и что он сделал.
        _logger.LogInformation(
            "Email outbox tick finished: sent {Sent}, retried {Retried}, failed {Failed}.",
            sent,
            retried,
            failed);

        return new EmailTickResult(sent, retried, failed);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            try
            {
                // Первый тик — сразу: письмо не должно ждать интервал после рестарта.
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
                    // Выключенный воркер не отправляет ничего; он лишь ждёт тики, чтобы
                    // включение флага не требовало перезапуска процесса.
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
    /// Отправляет очередную пачку готовых писем.
    /// </summary>
    private async Task<(int Sent, int Retried, int Failed)> DispatchAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<DispatchPendingEmailsHandler>();

        var result = await handler.Handle(
            new DispatchPendingEmailsCommand(),
            cancellationToken);

        if (result.IsFailure)
        {
            LogStepFailure("dispatch", result.Error);

            return (0, 0, 0);
        }

        return (result.Value.Sent, result.Value.Retried, result.Value.Failed);
    }

    /// <summary>
    /// Запускает уборку обработанных строк, если сегодня её ещё не было.
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
    /// Удаляет обработанные строки старше <c>EmailOutbox:RetentionDays</c>.
    /// </summary>
    private async Task<int> CleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<CleanupSentEmailsHandler>();

        var result = await handler.Handle(
            new CleanupSentEmailsCommand(),
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
    /// фоновый сервис, и письма перестали бы уходить до рестарта.
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
                "Email outbox step {Step} failed; the next tick will retry.",
                step);

            return fallback;
        }
    }

    /// <summary>
    /// Логирует <c>Failure</c> шага без адреса получателя и тела письма.
    /// </summary>
    private void LogStepFailure(string step, Failure failure)
    {
        var codes = failure.Errors.Count == 0
            ? "unknown"
            : string.Join(", ", failure.Errors.Select(error => error.Code));

        _logger.LogWarning(
            "Email outbox step {Step} failed: {Codes}.",
            step,
            codes);
    }
}
