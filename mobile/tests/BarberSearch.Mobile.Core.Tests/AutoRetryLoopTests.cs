using BarberSearch.Mobile.Core.Api;
using Xunit;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки цикла автоповтора: интервалы, остановка и молчаливое поглощение сбоев.
/// </summary>
/// <remarks>
/// Время подменено делегатом ожидания: он записывает интервал и сразу завершается, поэтому
/// секунды расписания не тратятся. Ожидание, которое должно «висеть» (проверка Stop),
/// завершается отменой токена — тоже без таймера.
/// </remarks>
public sealed class AutoRetryLoopTests
{
    private static readonly TimeSpan[] FirstAndSecondIntervals =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    private static readonly TimeSpan[] RepeatedLastInterval =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)];

    private static readonly TimeSpan[] FirstIntervalTwice =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)];

    private static readonly bool[] RunningThenStopped = [true, false];

    private static readonly bool[] RunningOnly = [true];

    [Fact]
    public async Task Run_TransportThenTerminal_WaitsOneAndTwoSeconds()
    {
        var intervals = new List<TimeSpan>();
        using var loop = new AutoRetryLoop(delay: RecordingDelay(intervals));

        loop.Start(Sequence(ApiFailureKind.Transport, ApiFailureKind.Terminal));

        await loop.Completion;

        Assert.Equal(FirstAndSecondIntervals, intervals);
        Assert.Equal(2, loop.Attempts);
        Assert.False(loop.IsRunning);
    }

    [Fact]
    public async Task Run_ScheduleExhausted_RepeatsLastInterval()
    {
        var intervals = new List<TimeSpan>();
        using var loop = new AutoRetryLoop(
            delay: RecordingDelay(intervals),
            schedule: [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

        loop.Start(Sequence(
            ApiFailureKind.Transport,
            ApiFailureKind.Transport,
            ApiFailureKind.Transport,
            ApiFailureKind.Terminal));

        await loop.Completion;

        Assert.Equal(RepeatedLastInterval, intervals);
        Assert.Equal(4, loop.Attempts);
    }

    [Fact]
    public async Task Run_TerminalOnFirstAttempt_StopsAfterOneAttempt()
    {
        var intervals = new List<TimeSpan>();
        using var loop = new AutoRetryLoop(delay: RecordingDelay(intervals));

        loop.Start(Sequence(ApiFailureKind.Terminal));

        await loop.Completion;

        Assert.Single(intervals);
        Assert.Equal(1, loop.Attempts);
        Assert.False(loop.IsRunning);
    }

    [Fact]
    public async Task Run_AttemptThrowsUnknownException_StopsWithoutPropagating()
    {
        var intervals = new List<TimeSpan>();
        using var loop = new AutoRetryLoop(delay: RecordingDelay(intervals));

        var exception = Record.Exception(() => loop.Start(
            _ => Task.FromException<ApiFailureKind>(new InvalidOperationException("Unexpected."))));

        await loop.Completion;

        Assert.Null(exception);
        Assert.Single(intervals);
        Assert.Equal(1, loop.Attempts);
        Assert.False(loop.IsRunning);
    }

    [Fact]
    public async Task Run_RetryableFailures_CountsEveryAttempt()
    {
        var intervals = new List<TimeSpan>();
        using var loop = new AutoRetryLoop(
            delay: RecordingDelay(intervals),
            schedule: [TimeSpan.FromSeconds(1)]);

        loop.Start(Sequence(
            ApiFailureKind.RetryableServer,
            ApiFailureKind.RetryableServer,
            ApiFailureKind.None));

        await loop.Completion;

        Assert.Equal(3, loop.Attempts);
        Assert.Equal(3, intervals.Count);
        Assert.False(loop.IsRunning);
    }

    [Fact]
    public async Task Stop_DuringWait_CompletesWithoutFurtherAttempts()
    {
        var intervals = new List<TimeSpan>();
        var states = new List<bool>();
        using var loop = new AutoRetryLoop(
            delay: (interval, cancellationToken) =>
            {
                intervals.Add(interval);
                return WaitForCancellationAsync(cancellationToken);
            },
            schedule: [TimeSpan.FromSeconds(1)]);
        loop.StateChanged += (_, _) => states.Add(loop.IsRunning);

        loop.Start(Sequence(ApiFailureKind.Transport));

        Assert.True(loop.IsRunning);

        loop.Stop();

        Assert.False(loop.IsRunning);
        Assert.True(loop.Completion.IsCompleted);
        await loop.Completion;

        Assert.Single(intervals);
        Assert.Equal(0, loop.Attempts);
        Assert.Equal(RunningThenStopped, states);
    }

    [Fact]
    public async Task Start_WhenAlreadyRunning_DoesNotBeginAnotherRun()
    {
        var intervals = new List<TimeSpan>();
        var states = new List<bool>();
        using var loop = new AutoRetryLoop(
            delay: (interval, cancellationToken) =>
            {
                intervals.Add(interval);
                return WaitForCancellationAsync(cancellationToken);
            },
            schedule: [TimeSpan.FromSeconds(1)]);
        loop.StateChanged += (_, _) => states.Add(loop.IsRunning);

        loop.Start(Sequence(ApiFailureKind.Transport));
        loop.Start(Sequence(ApiFailureKind.Transport));

        Assert.Single(intervals);
        Assert.Equal(0, loop.Attempts);
        Assert.Equal(RunningOnly, states);

        loop.Stop();
        await loop.Completion;

        Assert.Equal(RunningThenStopped, states);
    }

    [Fact]
    public async Task Start_AfterStop_StartsFromFirstIntervalWithZeroAttempts()
    {
        var intervals = new List<TimeSpan>();
        var waits = new List<TaskCompletionSource>();
        using var loop = new AutoRetryLoop(
            delay: (interval, _) =>
            {
                intervals.Add(interval);

                var wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                waits.Add(wait);

                return wait.Task;
            },
            schedule: [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

        loop.Start(Sequence(ApiFailureKind.Transport));
        loop.Stop();
        waits[0].SetResult();
        await loop.Completion;

        loop.Start(Sequence(ApiFailureKind.Terminal));

        // Новый запуск снова ждёт первый интервал и ещё не сделал ни одной попытки.
        Assert.Equal(FirstIntervalTwice, intervals);
        Assert.Equal(0, loop.Attempts);

        waits[1].SetResult();
        await loop.Completion;

        Assert.Equal(1, loop.Attempts);
    }

    [Fact]
    public void Completion_BeforeFirstStart_IsCompleted()
    {
        using var loop = new AutoRetryLoop();

        Assert.False(loop.IsRunning);
        Assert.Equal(0, loop.Attempts);
        Assert.True(loop.Completion.IsCompleted);
    }

    [Fact]
    public void Constructor_EmptySchedule_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            using var loop = new AutoRetryLoop(schedule: []);
        });
    }

    [Fact]
    public void Start_NullAttempt_ThrowsArgumentNullException()
    {
        using var loop = new AutoRetryLoop();

        Assert.Throws<ArgumentNullException>(() => loop.Start(null!));
    }

    /// <summary>
    /// Делегат ожидания, который записывает интервал и не ждёт: тест не тратит секунды.
    /// </summary>
    private static Func<TimeSpan, CancellationToken, Task> RecordingDelay(List<TimeSpan> intervals) =>
        (interval, _) =>
        {
            intervals.Add(interval);
            return Task.CompletedTask;
        };

    /// <summary>
    /// Попытка, отдающая заданные виды по очереди; последний вид повторяется, если цикл
    /// сделает больше попыток, чем ожидает тест (тогда упадут проверки счётчика).
    /// </summary>
    private static Func<CancellationToken, Task<ApiFailureKind>> Sequence(params ApiFailureKind[] kinds)
    {
        var index = 0;

        return _ => Task.FromResult(kinds[Math.Min(index++, kinds.Length - 1)]);
    }

    /// <summary>
    /// Ожидание, которое заканчивается только отменой: так проверяется Stop без таймеров.
    /// </summary>
    private static Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));

        return pending.Task;
    }
}
