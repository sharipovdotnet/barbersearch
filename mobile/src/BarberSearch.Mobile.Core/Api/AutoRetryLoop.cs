namespace BarberSearch.Mobile.Core.Api;

/// <summary>
/// Фоновый цикл автоповтора запроса с нарастающими паузами.
/// </summary>
/// <remarks>
/// <para>
/// Экрану не нужно само расписание: он передаёт делегат запроса, который возвращает
/// <see cref="ApiFailureKind"/>, а цикл решает — ждать и повторять или остановиться.
/// Расписание 1, 2, 4, 8, 15 и 30 секунд, последний интервал повторяется: быстрый первый
/// шанс ловит случайный обрыв, а редкие поздние повторы не долбят сервер, пока человек
/// всё ещё смотрит на экран.
/// </para>
/// <para>
/// Пауза приходит делегатом ожидания, а не берётся из <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
/// напрямую: тесты записывают интервалы и останавливают цикл без настоящего времени.
/// <see cref="Start"/> и <see cref="Stop"/> зовутся из UI-потока, а итерации остаются на
/// контексте вызывающего: попытка — это код ViewModel, и её продолжения должны возвращаться
/// на UI-поток, иначе привязки менялись бы не с того потока (на iOS это падение).
/// <c>ConfigureAwait(false)</c> здесь поэтому запрещён. Ни одно исключение не выходит
/// наружу: цикл не должен ронять приложение, поэтому неизвестный сбой просто останавливает
/// повторы.
/// </para>
/// <para>
/// На каждый запуск создаётся свой <see cref="Run"/>: после <see cref="Stop"/> цикл можно
/// запустить заново, а фоновая задача прошлого запуска ещё может доигрывать свой
/// <c>finally</c> — она не имеет права трогать состояние нового запуска.
/// </para>
/// </remarks>
public sealed class AutoRetryLoop : IDisposable
{
    /// <summary>
    /// Расписание по умолчанию: 1, 2, 4, 8, 15 и 30 секунд.
    /// </summary>
    /// <remarks>
    /// Последний интервал повторяется бесконечно, пока запрос не удастся, не станет
    /// окончательным отказом или экран не вызовет <see cref="Stop"/>. Рост от секунды даёт
    /// быстрый первый шанс и не перегружает сервер при затяжном сбое.
    /// </remarks>
    private static readonly TimeSpan[] DefaultSchedule =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
    ];

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan[] _schedule;

    /// <summary>
    /// Сериализует переходы состояния: цикл останавливается в фоне, а <see cref="Start"/> и
    /// <see cref="Stop"/> зовутся из UI-потока, и оба не должны увидеть полусостояние.
    /// </summary>
    private readonly object _gate = new();

    private Run? _run;
    private Task _completion = Task.CompletedTask;
    private bool _isRunning;
    private int _attempts;

    /// <summary>
    /// Создаёт цикл с заданным ожиданием и расписанием.
    /// </summary>
    /// <remarks>
    /// Расписание копируется: вызывающий код не должен менять интервалы на ходу — иначе
    /// <see cref="Attempts"/> перестанет соответствовать паузам, которые уже прошли.
    /// </remarks>
    /// <param name="delay">
    /// Ожидание паузы; <c>null</c> — настоящий <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
    /// </param>
    /// <param name="schedule">
    /// Интервалы пауз; <c>null</c> — расписание по умолчанию, пустой список — ошибка.
    /// </param>
    /// <exception cref="ArgumentException">Расписание пустое.</exception>
    public AutoRetryLoop(
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IReadOnlyList<TimeSpan>? schedule = null)
    {
        _schedule = schedule is null ? DefaultSchedule : schedule.ToArray();

        if (_schedule.Length == 0)
        {
            throw new ArgumentException("Расписание повторов не может быть пустым.", nameof(schedule));
        }

        _delay = delay ?? (static (interval, cancellationToken) => Task.Delay(interval, cancellationToken));
    }

    /// <summary>
    /// Поднимается при запуске и при остановке цикла.
    /// </summary>
    /// <remarks>
    /// Событие синхронное и поднимается на том потоке, который вызвал <see cref="Start"/> или
    /// <see cref="Stop"/>, — то есть на UI-потоке. Экран по нему переключает индикатор
    /// повтора; сам Core про диспетчер MAUI не знает.
    /// </remarks>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Цикл сейчас работает.
    /// </summary>
    /// <remarks>
    /// После <see cref="Stop"/> становится <c>false</c> сразу, не дожидаясь, пока фоновая
    /// задача доиграет: экран не должен показывать работающий повтор поверх уже нажатой
    /// отмены.
    /// </remarks>
    public bool IsRunning => Volatile.Read(ref _isRunning);

    /// <summary>
    /// Сколько попыток сделано в текущем запуске; до первого — <c>0</c>.
    /// </summary>
    /// <remarks>
    /// Счётчик показывает, что было на самом деле, а не что запланировано: попытка
    /// засчитывается в момент, когда цикл начал её выполнять. По нему экран может показать
    /// «попытка № N» и понять, что повторы не бесполезны.
    /// </remarks>
    public int Attempts => Volatile.Read(ref _attempts);

    /// <summary>
    /// Задача, которая завершается, когда цикл останавливается.
    /// </summary>
    /// <remarks>
    /// До первого <see cref="Start"/> уже завершена: ждать нечего. <see cref="Stop"/>
    /// завершает её сразу, даже если запрос ещё не отменился, — экран не должен зависнуть
    /// на ожидании цикла.
    /// </remarks>
    public Task Completion => Volatile.Read(ref _completion);

    /// <summary>
    /// Запускает цикл: первая попытка уйдёт через первый интервал расписания.
    /// </summary>
    /// <remarks>
    /// Метод не асинхронный: он только поднимает фон и сразу возвращает управление, чтобы
    /// экран не блокировался. Повторный вызов при работающем цикле ничего не делает —
    /// второй цикл поверх первого удвоил бы запросы. Остановка — только через
    /// <see cref="Stop"/> или <see cref="Dispose"/>.
    /// </remarks>
    /// <param name="attemptAsync">
    /// Одна попытка запроса; её результат говорит, нужен ли ещё один повтор.
    /// </param>
    /// <exception cref="ArgumentNullException">Делегат попытки не передан.</exception>
    public void Start(Func<CancellationToken, Task<ApiFailureKind>> attemptAsync)
    {
        ArgumentNullException.ThrowIfNull(attemptAsync);

        Run run;

        lock (_gate)
        {
            if (_run is not null)
            {
                return;
            }

            run = new Run();
            _run = run;
            _attempts = 0;
            _isRunning = true;

            // Задача завершения назначается до запуска работы: тест (и экран) сразу после
            // Start читает Completion и не должен пропустить цикл, закончившийся синхронно.
            _completion = run.Completion.Task;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);

        _ = RunAsync(attemptAsync, run);
    }

    /// <summary>
    /// Останавливает цикл: отменяет ожидание и запрос, если он уже в полёте.
    /// </summary>
    /// <remarks>
    /// Идемпотентен и безопасен, когда цикл не работает. Ничего не пробрасывает наружу:
    /// закрытие экрана не должно зависеть от того, успел ли отреагировать запрос.
    /// <see cref="IsRunning"/> становится <c>false</c> сразу, не дожидаясь фоновой задачи.
    /// </remarks>
    public void Stop()
    {
        Run? run;

        lock (_gate)
        {
            run = _run;

            if (run is null)
            {
                return;
            }

            _run = null;
            _isRunning = false;

            try
            {
                // Коллбэки отмены — чужой код (делегат ожидания и запрос), и их сбой не
                // должен вылетать из Stop: человек всего лишь закрыл экран.
                run.Cancellation.Cancel();
            }
            catch (AggregateException)
            {
                // Ожидание всё равно завершено, а причина сбоя коллбэка — не наша.
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);

        run.Completion.TrySetResult();
    }

    /// <summary>
    /// Останавливает цикл.
    /// </summary>
    /// <remarks>
    /// Отдельного освобождения нет: <see cref="Dispose"/> делает то же, что
    /// <see cref="Stop"/>, потому что источник отмены принадлежит запуску и освобождается
    /// вместе с ним. Метод существует ради анализатора и тестов.
    /// </remarks>
    public void Dispose() => Stop();

    /// <summary>
    /// Крутит попытки, пока результат остаётся повторяемым.
    /// </summary>
    /// <param name="attemptAsync">Одна попытка запроса.</param>
    /// <param name="run">Запуск, которому принадлежит этот цикл.</param>
    private async Task RunAsync(
        Func<CancellationToken, Task<ApiFailureKind>> attemptAsync,
        Run run)
    {
        var cancellationToken = run.Cancellation.Token;

        try
        {
            while (true)
            {
                try
                {
                    // Последний интервал расписания повторяется: Math.Min не даёт выйти за
                    // границу, когда попыток стало больше, чем интервалов.
                    var interval = _schedule[Math.Min(Volatile.Read(ref _attempts), _schedule.Length - 1)];

                    await _delay(interval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Экран закрыли или человек нажал «Отмена»: это не ошибка и не повод шуметь.
                    return;
                }
                catch (Exception)
                {
                    // Делегат ожидания сломался — повторять вслепую нельзя, и наружу это
                    // тоже не выходит: цикл не должен ронять приложение.
                    return;
                }

                if (!TryBeginAttempt(run))
                {
                    // Пока ждали, цикл остановили или запустили заново: этот запуск больше
                    // не хозяин состояния и не имеет права делать попытку.
                    return;
                }

                ApiFailureKind kind;

                try
                {
                    kind = await attemptAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Отмена попытки — тихая остановка, а не сбой.
                    return;
                }
                catch (Exception)
                {
                    // Неизвестный сбой повторять вслепую нельзя: остановимся и оставим
                    // решение экрану.
                    return;
                }

                if (!kind.IsRetryable() || cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
        finally
        {
            Finish(run);
        }
    }

    /// <summary>
    /// Засчитывает попытку, если запуск всё ещё текущий.
    /// </summary>
    /// <param name="run">Запуск, к которому относится попытка.</param>
    /// <returns><c>false</c>, если цикл уже остановлен или заменён новым запуском.</returns>
    private bool TryBeginAttempt(Run run)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_run, run))
            {
                return false;
            }

            _attempts++;
            return true;
        }
    }

    /// <summary>
    /// Переводит цикл в остановленное состояние и завершает <see cref="Completion"/>.
    /// </summary>
    /// <param name="run">Запуск, который закончился.</param>
    private void Finish(Run run)
    {
        var stopped = false;

        lock (_gate)
        {
            if (ReferenceEquals(_run, run))
            {
                _run = null;
                _isRunning = false;
                stopped = true;
            }
        }

        if (stopped)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        run.Completion.TrySetResult();

        // Источник отмены принадлежит запуску: у нового запуска свой, а этот больше не нужен.
        run.Dispose();
    }

    /// <summary>
    /// Один запуск цикла: свой источник отмены и своя задача завершения.
    /// </summary>
    /// <remarks>
    /// Запуск отделён от <see cref="AutoRetryLoop"/> намеренно: после <see cref="Stop"/>
    /// цикл можно запустить заново, а фоновая задача прошлого запуска ещё доигрывает свой
    /// <c>finally</c> — она должна видеть состояние именно своего запуска.
    /// </remarks>
    private sealed class Run : IDisposable
    {
        /// <summary>
        /// Отмена ожидания и запроса текущего запуска.
        /// </summary>
        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>
        /// Завершается, когда запуск остановлен.
        /// </summary>
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public void Dispose() => Cancellation.Dispose();
    }
}
