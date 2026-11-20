using System.Collections.ObjectModel;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Экран «Перенести запись» кабинета мастера: новая дата, свободное время и подтверждение.
/// </summary>
/// <remarks>
/// <para>
/// Экран повторяет веб (<c>frontend/src/components/reschedule-booking-dialog.tsx</c>):
/// меняется только время, а услуга, салон, цена, статус и длительность остаются прежними.
/// Сетку считает сервер по длительности самой брони, поэтому прайса экран не знает и
/// отправляет ровно начало выбранного слота — тот момент, который пришёл в ответе.
/// </para>
/// <para>
/// Строка «переносим» приходит из раздела «Записи» вместе с владельцем: после успеха
/// страница применяет слот к строке (<see cref="BarberBookingsViewModel.ApplyRescheduled"/>)
/// и закрывается. Core не знает ни про Shell, ни про MAUI.
/// </para>
/// <para>
/// Дата ограничена окном записи в поясе салона: «сегодня» в UTC может быть вчерашним днём
/// салона. Дата по умолчанию — день текущего визита, но время не предвыбрано: перенос
/// всегда осознанный выбор, и подставлять первый слот значило бы предлагать его как решение.
/// </para>
/// <para>
/// Занятое время и устаревший слот требуют перечитать сетку — причём именно той даты, на
/// которую ушёл запрос, а не той, что человек успел выбрать после: иначе экран показал бы
/// свободное время дня, к которому перенос не относится. Остальные отказы оставляют сетку
/// и выбор на месте.
/// </para>
/// <para>
/// Чтение сетки повторяется само, пока экран виден (<see cref="StartAutoRetry"/> и
/// <see cref="StopAutoRetry"/> зовёт страница по видимости), и повтор несёт дату упавшего
/// запроса. Отправка переноса — запись, её автоповтор не касается: после сбоя исход
/// неизвестен и повторять его молча нельзя.
/// </para>
/// </remarks>
public sealed partial class BarberBookingRescheduleViewModel : ObservableObject
{
    /// <summary>
    /// Горизонт переноса: тридцать дней от сегодняшнего дня салона.
    /// </summary>
    public const int MaxDaysAhead = 30;

    private static string SlotsFailedText => Loc.T("Не удалось загрузить свободное время.");
    private static string SubmitFailedText => Loc.T("Не удалось перенести запись.");
    private static string NoWorkingDayText => Loc.T("В этот день вы не работаете.");
    private static string NoFreeTimeText => Loc.T("Свободного времени на этот день нет.");
    private static string NetworkErrorText => Loc.T(
        "Нет связи с сервером. Проверьте подключение и повторите попытку.");

    private readonly IBarberBookingsApi _bookingsApi;
    private readonly TimeProvider _timeProvider;
    private readonly AutoRetryLoop _autoRetry;

    private BarberBookingItem? _item;
    private TimeZoneInfo? _timeZone;
    private DateOnly? _loadedDate;
    private CancellationTokenSource? _slotsCancellation;
    private Task _slotsOperation = Task.CompletedTask;
    private bool _isApplyingDate;
    private bool _isPageVisible;

    /// <summary>
    /// Вид последнего сбоя сетки: по нему цикл решает, повторять ли чтение сам.
    /// </summary>
    private ApiFailureKind _slotsFailureKind;

    /// <summary>
    /// Салонная дата упавшего чтения: повтор несёт именно её, а не текущий выбор календаря.
    /// </summary>
    private DateOnly? _retrySlotsDate;

    /// <summary>
    /// Создаёт экран переноса.
    /// </summary>
    /// <param name="bookingsApi">Слоты переноса и сама операция переноса.</param>
    /// <param name="timeProvider">Часы: границы дат считаются от «сегодня» салона.</param>
    /// <param name="autoRetry">
    /// Цикл повтора сетки слотов; <c>null</c> — собственный цикл с обычным расписанием.
    /// Параметр необязательный и стоит последним, чтобы DI и тесты не зависели от его порядка.
    /// </param>
    public BarberBookingRescheduleViewModel(
        IBarberBookingsApi bookingsApi,
        TimeProvider timeProvider,
        AutoRetryLoop? autoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(bookingsApi);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _bookingsApi = bookingsApi;
        _timeProvider = timeProvider;
        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;
    }

    /// <summary>
    /// Перенос подтверждён сервером: страница применяет слот к строке и закрывается.
    /// </summary>
    /// <remarks>
    /// Событие несёт границы выбранного слота — ровно то, что нужно
    /// <see cref="BarberBookingsViewModel.ApplyRescheduled"/>; повторный <c>PUT</c> после
    /// этого невозможен.
    /// </remarks>
    public event EventHandler<BarberSlotResponse>? Rescheduled;

    /// <summary>
    /// Свободное время выбранной даты.
    /// </summary>
    public ObservableCollection<BarberSlotOption> Slots { get; } = [];

    /// <summary>
    /// Имя клиента из строки записи.
    /// </summary>
    [ObservableProperty]
    private string _customerName = string.Empty;

    /// <summary>
    /// Текущий момент визита в поясе салона: «сб, 15 ноября в 18:30».
    /// </summary>
    [ObservableProperty]
    private string _momentText = string.Empty;

    /// <summary>
    /// Услуга и салон визита: «Стрижка · Hairo».
    /// </summary>
    [ObservableProperty]
    private string _serviceSalonText = string.Empty;

    /// <summary>
    /// Выбранная дата; привязывается к <c>DatePicker</c>.
    /// </summary>
    [ObservableProperty]
    private DateTime _selectedDate;

    /// <summary>
    /// Минимальная дата: сегодня в поясе салона.
    /// </summary>
    [ObservableProperty]
    private DateTime _minimumDate;

    /// <summary>
    /// Максимальная дата: сегодня плюс тридцать дней.
    /// </summary>
    [ObservableProperty]
    private DateTime _maximumDate;

    /// <summary>
    /// Идёт чтение сетки слотов.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingSlots;

    /// <summary>
    /// Причина неудачи чтения сетки; <c>null</c>, когда сетка загрузилась.
    /// </summary>
    [ObservableProperty]
    private string? _slotsError;

    /// <summary>
    /// Рабочий ли день у мастера: от этого зависит текст пустого дня.
    /// </summary>
    [ObservableProperty]
    private bool _isWorkingDay;

    /// <summary>
    /// Сетка на выбранную дату уже загрузилась.
    /// </summary>
    [ObservableProperty]
    private bool _isDayLoaded;

    /// <summary>
    /// Цикл сам повторяет упавшее чтение сетки.
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Выбранный слот; <c>null</c>, пока время не выбрано.
    /// </summary>
    [ObservableProperty]
    private BarberSlotOption? _selectedSlot;

    /// <summary>
    /// Идёт отправка переноса: кнопка неактивна, повторное нажатие игнорируется.
    /// </summary>
    [ObservableProperty]
    private bool _isSubmitting;

    /// <summary>
    /// Ошибка отправки; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Строка записи принята: экран можно показывать.
    /// </summary>
    public bool IsInitialized => _item is not null;

    /// <summary>
    /// Можно ли нажать «Перенести визит».
    /// </summary>
    public bool CanSubmit => IsInitialized && SelectedSlot is not null && !IsSubmitting;

    /// <summary>
    /// Надпись на кнопке: во время запроса — «Переносим…».
    /// </summary>
    public string SubmitText => IsSubmitting ? Loc.T("Переносим…") : Loc.T("Перенести визит");

    /// <summary>
    /// Есть ошибка отправки.
    /// </summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Показывать сетку слотов.
    /// </summary>
    public bool ShowSlots => IsDayLoaded
        && !IsLoadingSlots
        && SlotsError is null
        && Slots.Count > 0;

    /// <summary>
    /// Показывать пустой день: рабочих часов нет или свободного времени не осталось.
    /// </summary>
    public bool ShowEmptyDay => IsDayLoaded
        && !IsLoadingSlots
        && SlotsError is null
        && Slots.Count == 0;

    /// <summary>
    /// Объяснение пустого дня.
    /// </summary>
    public string EmptyDayText => IsWorkingDay ? NoFreeTimeText : NoWorkingDayText;

    /// <summary>
    /// Показывать причину неудачи чтения сетки.
    /// </summary>
    public bool ShowSlotsError => SlotsError is not null;

    /// <summary>
    /// Цикл автоповтора: тестам нужен счётчик попыток.
    /// </summary>
    internal AutoRetryLoop AutoRetry => _autoRetry;

    /// <summary>
    /// Завершается, когда цикл повтора останавливается: тесты ждут его вместо пауз.
    /// </summary>
    internal Task AutoRetryCompletion => _autoRetry.Completion;

    /// <summary>
    /// Завершение последнего чтения сетки — тестам, чтобы не ждать ответа.
    /// </summary>
    internal Task SlotsOperation => _slotsOperation;

    /// <summary>
    /// Салонная дата показанной сетки: тестам нужно знать, что было перечитано.
    /// </summary>
    internal DateOnly? LoadedDate => _loadedDate;

    /// <summary>
    /// Принимает строку записи, которую переносят.
    /// </summary>
    /// <remarks>
    /// Дата по умолчанию — день текущего визита, границы — сегодня и сегодня плюс тридцать
    /// дней в поясе салона. Ничего не читает: чтение запускает <see cref="InitializeAsync"/>
    /// или страница через <see cref="LoadSlotsAsync"/>.
    /// </remarks>
    /// <param name="item">Строка списка записей.</param>
    public void Initialize(BarberBookingItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _item = item;
        _timeZone = item.TimeZone;

        CustomerName = item.CustomerName;
        MomentText = item.MomentText;
        ServiceSalonText = item.ServiceSalonText;

        var today = SalonClock.Today(_timeZone, _timeProvider.GetUtcNow());
        var visitDate = SalonClock.Today(_timeZone, item.StartAt);

        _isApplyingDate = true;

        try
        {
            MinimumDate = today.ToDateTime(TimeOnly.MinValue);
            MaximumDate = MinimumDate.AddDays(MaxDaysAhead);
            SelectedDate = ClampDate(visitDate.ToDateTime(TimeOnly.MinValue));
        }
        finally
        {
            _isApplyingDate = false;
        }

        Slots.Clear();
        SelectedSlot = null;
        IsDayLoaded = false;
        _loadedDate = null;

        NotifyViewState();
    }

    /// <summary>
    /// Принимает строку и сразу читает сетку на день визита.
    /// </summary>
    /// <param name="item">Строка списка записей.</param>
    /// <param name="cancellationToken">Токен отмены страницы.</param>
    /// <returns>Задача чтения.</returns>
    public async Task InitializeAsync(
        BarberBookingItem item,
        CancellationToken cancellationToken = default)
    {
        Initialize(item);

        await LoadSlotsAsync(cancellationToken);
    }

    /// <summary>
    /// Сообщает, что экран виден, и запускает автоповтор.
    /// </summary>
    /// <remarks>
    /// Если экран вернулся с выбранной датой, но без сетки (уход в фон отменил запрос),
    /// сетка читается заново: иначе экран остался бы с пустым блоком времени.
    /// </remarks>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartAutoRetry();

        if (_item is not null
            && !IsDayLoaded
            && !IsLoadingSlots
            && SlotsError is null
            && Slots.Count == 0)
        {
            _slotsOperation = LoadSlotsAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Сообщает, что экран скрыт, и останавливает автоповтор и чтение сетки.
    /// </summary>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        _autoRetry.Stop();
        CancelSlotsRequest();

        IsLoadingSlots = false;

        NotifyViewState();
    }

    /// <summary>
    /// Читает сетку слотов на выбранную дату.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены страницы.</param>
    /// <returns>Задача чтения сетки; ошибки разбираются внутри.</returns>
    public Task LoadSlotsAsync(CancellationToken cancellationToken = default) =>
        ChangeDateAsync(SelectedDate, cancellationToken);

    /// <summary>
    /// Переходит на другую дату: сбрасывает выбор и перезапрашивает сетку.
    /// </summary>
    /// <remarks>
    /// Прошлый запрос отменяется: два ответа на разные даты не должны смешаться. Смена даты —
    /// действие человека, поэтому цикл останавливается приватно (экран остаётся видимым), а
    /// после чтения запускается заново только при новом повторяемом сбое.
    /// </remarks>
    /// <param name="date">Выбранная дата.</param>
    /// <param name="cancellationToken">Токен отмены страницы.</param>
    /// <returns>Задача чтения сетки.</returns>
    public async Task ChangeDateAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        if (_item is null)
        {
            return;
        }

        _autoRetry.Stop();

        var clamped = ClampDate(date);

        _isApplyingDate = true;

        try
        {
            SelectedDate = clamped;
        }
        finally
        {
            _isApplyingDate = false;
        }

        SelectedSlot = null;
        Slots.Clear();
        IsDayLoaded = false;
        IsLoadingSlots = true;
        SlotsError = null;
        _loadedDate = null;
        _retrySlotsDate = null;

        NotifyViewState();

        await LoadSlotsCoreAsync(
            DateOnly.FromDateTime(clamped),
            silent: false,
            cancellationToken);

        TryStartAutoRetry();
    }

    /// <summary>
    /// Выбирает слот; повторное нажатие на выбранный слот ничего не меняет.
    /// </summary>
    /// <param name="slot">Слот сетки.</param>
    [RelayCommand]
    private void SelectSlot(BarberSlotOption? slot)
    {
        if (slot is null || IsSubmitting)
        {
            return;
        }

        foreach (var option in Slots)
        {
            option.IsSelected = ReferenceEquals(option, slot);
        }

        SelectedSlot = slot;
    }

    /// <summary>
    /// Повторяет чтение сетки по кнопке — сразу, без паузы цикла.
    /// </summary>
    [RelayCommand]
    private async Task RetrySlotsAsync()
    {
        _autoRetry.Stop();

        _slotsOperation = LoadSlotsAsync();

        await _slotsOperation;

        TryStartAutoRetry();
    }

    /// <summary>
    /// Переносит визит на выбранный слот.
    /// </summary>
    /// <remarks>
    /// Отправляется ровно начало выбранного слота, без пересчёта в пояс устройства: сервер
    /// сравнивает абсолютные моменты и сам берёт конец. Контракт переноса не принимает ключ
    /// идемпотентности (<c>RescheduleBookingRequest</c> — только <c>startAt</c>), поэтому
    /// повтор тем же временем ничего не создаёт второй раз: операция идемпотентна по слоту.
    /// Занятое время перечитывает сетку даты, которая упала, и оставляет выбор строки.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены страницы.</param>
    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync(CancellationToken cancellationToken)
    {
        var slot = SelectedSlot;
        var item = _item;

        if (IsSubmitting || slot is null || item is null)
        {
            return;
        }

        // Дату фиксируем до запроса: после отказа сетку перечитываем именно её, а не то,
        // что человек успел выбрать, пока запрос был в полёте.
        var failedDate = _loadedDate ?? DateOnly.FromDateTime(SelectedDate);

        IsSubmitting = true;
        ErrorMessage = null;

        try
        {
            await _bookingsApi.RescheduleAsync(item.Id, slot.StartAt, cancellationToken);

            Rescheduled?.Invoke(this, new BarberSlotResponse(slot.StartAt, slot.EndAt));
        }
        catch (BarberSearchApiException exception)
        {
            var failure = BarberCabinetText.RescheduleError(exception, SubmitFailedText);

            ErrorMessage = failure.Message;

            if (failure.ReloadSlots)
            {
                SelectedSlot = null;

                _slotsOperation = LoadSlotsCoreAsync(failedDate, silent: true, CancellationToken.None);

                await _slotsOperation;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Страница ушла: исход переноса неизвестен, повторять его молча нельзя.
        }
        catch (Exception exception)
        {
            // Таймаут транспорта приходит отменой при живом токене: для человека это
            // отсутствие связи.
            ErrorMessage = exception is HttpRequestException or OperationCanceledException
                ? NetworkErrorText
                : BarberCabinetText.ReadError(exception, SubmitFailedText);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    /// <summary>
    /// Читает сетку на заданную салонную дату — вручную или тихой попыткой цикла.
    /// </summary>
    /// <remarks>
    /// Тихая попытка не стирает причину и не показывает индикатор: причина, строки и кнопка
    /// остаются до ответа. Снимок даты запоминается на случай неудачи, чтобы повтор ушёл
    /// ровно за упавшим запросом.
    /// </remarks>
    /// <param name="day">Салонная дата запроса.</param>
    /// <param name="silent">Тихая попытка: не показывать индикатор и не чистить ошибку.</param>
    /// <param name="cancellationToken">Токен отмены страницы или цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private async Task<ApiFailureKind> LoadSlotsCoreAsync(
        DateOnly day,
        bool silent,
        CancellationToken cancellationToken)
    {
        var item = _item;

        if (item is null)
        {
            return ApiFailureKind.None;
        }

        CancelSlotsRequest();

        var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _slotsCancellation = request;

        var token = request.Token;
        var kind = ApiFailureKind.None;

        if (!silent)
        {
            IsLoadingSlots = true;
            SlotsError = null;
        }

        NotifyViewState();

        try
        {
            var response = await _bookingsApi.GetRescheduleSlotsAsync(item.Id, day, token);

            if (!ReferenceEquals(_slotsCancellation, request))
            {
                // Пока читали, дату сменили: ответ на старую дату сетку не подменяет.
                return ApiFailureKind.None;
            }

            ApplySlots(response, day);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return ApiFailureKind.None;
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(_slotsCancellation, request))
            {
                return ApiFailureKind.None;
            }

            kind = ApiFailureClassifier.Classify(exception, token);

            if (kind != ApiFailureKind.None)
            {
                SlotsError = BarberCabinetText.ReadError(exception, SlotsFailedText);
                _slotsFailureKind = kind;
                _retrySlotsDate = kind.IsRetryable() ? day : null;
            }
        }
        finally
        {
            if (ReferenceEquals(_slotsCancellation, request))
            {
                _slotsCancellation = null;

                if (!silent)
                {
                    IsLoadingSlots = false;
                }

                request.Dispose();

                NotifyViewState();
            }
        }

        if (kind.IsRetryable())
        {
            TryStartAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Собирает строки сетки и запоминает дату показанного дня.
    /// </summary>
    /// <param name="response">Ответ сетки слотов.</param>
    /// <param name="day">Салонная дата запроса.</param>
    private void ApplySlots(BarberSlotsResponse response, DateOnly day)
    {
        var zone = SalonClock.TryResolve(response.TimeZoneId) ?? _timeZone;

        Slots.Clear();

        foreach (var slot in response.Slots)
        {
            Slots.Add(new BarberSlotOption(
                slot.StartAt,
                slot.EndAt,
                SalonClock.FormatTimeRange(slot.StartAt, slot.EndAt, zone))
            {
                SelectCommand = SelectSlotCommand,
            });
        }

        _loadedDate = day;
        IsWorkingDay = response.IsWorkingDay;
        IsDayLoaded = true;
        SlotsError = null;
        _slotsFailureKind = ApiFailureKind.None;
        _retrySlotsDate = null;

        NotifyViewState();
    }

    /// <summary>
    /// Запускает цикл повтора сетки, если он может помочь.
    /// </summary>
    private void TryStartAutoRetry()
    {
        if (!_isPageVisible
            || IsLoadingSlots
            || SlotsError is null
            || _retrySlotsDate is null
            || !_slotsFailureKind.IsRetryable())
        {
            return;
        }

        _autoRetry.Start(RetrySlotsAttemptAsync);
    }

    /// <summary>
    /// Одна попытка цикла: повторить сетку с датой упавшего запроса.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetrySlotsAttemptAsync(CancellationToken cancellationToken)
    {
        if (!_isPageVisible
            || _retrySlotsDate is not { } day
            || IsLoadingSlots
            || SlotsError is null)
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadSlotsCoreAsync(day, silent: true, cancellationToken);
    }

    /// <summary>
    /// Отменяет незавершённое чтение сетки.
    /// </summary>
    private void CancelSlotsRequest()
    {
        var previous = _slotsCancellation;

        _slotsCancellation = null;

        previous?.Cancel();
        previous?.Dispose();
    }

    /// <summary>
    /// Держит дату внутри окна переноса.
    /// </summary>
    /// <param name="date">Дата из календаря.</param>
    /// <returns>Дата внутри окна.</returns>
    private DateTime ClampDate(DateTime date)
    {
        if (MaximumDate == default)
        {
            return date;
        }

        if (date < MinimumDate)
        {
            return MinimumDate;
        }

        return date > MaximumDate ? MaximumDate : date;
    }

    /// <summary>
    /// Поднимает уведомления обо всём, что зависит от сетки и отправки.
    /// </summary>
    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(IsInitialized));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(ShowSlots));
        OnPropertyChanged(nameof(ShowEmptyDay));
        OnPropertyChanged(nameof(ShowSlotsError));
        OnPropertyChanged(nameof(EmptyDayText));

        SubmitCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    /// <param name="sender">Цикл повтора.</param>
    /// <param name="e">Аргументы события.</param>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Дата из календаря: экран читает сетку на неё.
    /// </summary>
    /// <param name="value">Новая дата.</param>
    partial void OnSelectedDateChanged(DateTime value)
    {
        if (_isApplyingDate)
        {
            return;
        }

        _slotsOperation = ChangeDateAsync(value);
    }

    /// <summary>
    /// Выбранный слот изменился: пересчитывается доступность отправки.
    /// </summary>
    /// <param name="value">Слот либо <c>null</c>.</param>
    partial void OnSelectedSlotChanged(BarberSlotOption? value) => NotifyViewState();

    /// <summary>
    /// Отправка началась или закончилась: кнопка меняет подпись.
    /// </summary>
    /// <param name="value">Идёт отправка.</param>
    partial void OnIsSubmittingChanged(bool value) => NotifyViewState();

    /// <summary>
    /// Чтение сетки началось или закончилось.
    /// </summary>
    /// <param name="value">Идёт чтение.</param>
    partial void OnIsLoadingSlotsChanged(bool value) => NotifyViewState();

    /// <summary>
    /// Причина неудачи чтения сетки появилась или исчезла.
    /// </summary>
    /// <param name="value">Текст причины.</param>
    partial void OnSlotsErrorChanged(string? value) => NotifyViewState();

    /// <summary>
    /// Признак рабочего дня изменился: меняется текст пустого дня.
    /// </summary>
    /// <param name="value">Рабочий ли день.</param>
    partial void OnIsWorkingDayChanged(bool value) => NotifyViewState();

    /// <summary>
    /// Сетка загрузилась: переключаются пустое состояние и список.
    /// </summary>
    /// <param name="value">Сетка загружена.</param>
    partial void OnIsDayLoadedChanged(bool value) => NotifyViewState();

    /// <summary>
    /// Ошибка отправки появилась или исчезла.
    /// </summary>
    /// <param name="value">Текст ошибки.</param>
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
}
