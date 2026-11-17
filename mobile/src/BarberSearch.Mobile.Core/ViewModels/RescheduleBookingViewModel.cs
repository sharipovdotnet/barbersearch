using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Bookings;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран «Перенести запись»: дата, свободные слоты брони и подтверждение.
/// </summary>
/// <remarks>
/// <para>
/// Сетка считается по длительности самой брони, поэтому экран не знает прайса: он показывает
/// текущий визит, выбирает дату в поясе салона и отправляет ровно начало выбранного слота.
/// Цена и длительность при переносе не меняются — это обещание экрана, а не логика клиента.
/// </para>
/// <para>
/// Смена даты отменяет прошлый запрос: ответ на старую дату не должен подменить сетку новой.
/// Ошибки разбираются по кодам (как <c>describeRescheduleFailure</c> в вебе): занятое время и
/// устаревший слот требуют перечитать сетку, а «слишком далеко» — нет.
/// </para>
/// <para>
/// Пока экран виден, повторяемый сбой сетки лечится сам: сеть, <c>5xx</c> и таймаут клиента
/// повторяются с нарастающей паузой (<see cref="Api.AutoRetryLoop"/>), а под причиной ошибки
/// видна строка «Пробуем снова…». Повтор несёт снимок упавшего запроса — салонную дату, на
/// которую он ушёл, — а не текущий выбор в календаре. «Повторить» остаётся и значит
/// «повторить сейчас»: останавливает цикл приватно, читает выбранную дату сразу и запускает
/// цикл заново, только если новый сбой снова повторяемый. Отправка переноса — запись, её
/// автоповтор не касается.
/// </para>
/// </remarks>
public sealed partial class RescheduleBookingViewModel : ObservableObject, IDisposable
{
    private const int MaxDaysAhead = 90;

    private static string SlotsLoadErrorText => Loc.T("Не удалось загрузить свободное время.");
    private static string SubmitFailedText => Loc.T("Не удалось перенести запись.");
    private static string NoWorkingDayText => Loc.T("В этот день мастер не работает.");
    private static string NoFreeTimeText => Loc.T("Свободного времени на этот день нет.");
    private static string NetworkErrorText => Loc.T(
        "Нет связи с сервером. Проверьте подключение и повторите попытку.");
    private static string TooManyRequestsText => Loc.T(
        "Слишком много запросов. Подождите минуту и повторите.");
    private static string SessionExpiredText => Loc.T("Сессия истекла — войдите снова.");

    private const string RescheduleOverlapCode = "booking.update.overlap";

    private readonly IBookingsApi _bookingsApi;
    private readonly TimeProvider _timeProvider;
    private readonly AutoRetryLoop _autoRetry;

    private BookingListItem? _item;
    private TimeZoneInfo? _timeZone;
    private CancellationTokenSource? _slotsCancellation;
    private int _generation;
    private bool _isApplyingDate;
    private bool _disposed;

    /// <summary>
    /// Вид последнего сбоя сетки: по нему <see cref="StartAutoRetry"/> решает, повторять ли
    /// чтение сам.
    /// </summary>
    /// <remarks>
    /// Запоминается для каждого неуспеха, а не только для повторяемого: иначе «транспорт,
    /// потом 404» оставил бы в памяти старый <see cref="ApiFailureKind.Transport"/>, и кнопка
    /// запустила бы цикл на отказе, который не изменится.
    /// </remarks>
    private ApiFailureKind _slotsFailureKind;

    /// <summary>
    /// Салонная дата упавшего чтения: повтор несёт именно её, а не текущий выбор календаря.
    /// </summary>
    /// <remarks>
    /// Пока видна ошибка, человек может сменить дату — но это уже другое чтение, и цикл после
    /// него запускается заново только по своему сбою. Тихая попытка обязана уйти с той датой,
    /// на которую упал запрос, иначе она подменила бы запрос правкой, которую никто не просил.
    /// </remarks>
    private DateOnly? _retrySlotsDate;

    /// <summary>
    /// Экран сейчас виден: без этого сигнала цикл повторов не запускается.
    /// </summary>
    private bool _isPageVisible;

    /// <summary>
    /// Создаёт экран переноса.
    /// </summary>
    /// <param name="bookingsApi">Слоты переноса и сама операция переноса.</param>
    /// <param name="timeProvider">Часы: границы дат считаются от «сегодня» салона.</param>
    /// <param name="autoRetry">
    /// Цикл автоповтора сетки слотов; <c>null</c> — собственный цикл с обычным расписанием.
    /// Параметр необязательный и стоит последним, чтобы существующие вызовы не менялись.
    /// </param>
    public RescheduleBookingViewModel(
        IBookingsApi bookingsApi,
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
    /// Перенос подтверждён сервером: страница применяет слот к строке списка и закрывается.
    /// </summary>
    public event EventHandler<BarberSlotResponse>? Rescheduled;

    /// <summary>
    /// Свободные слоты выбранной даты.
    /// </summary>
    public ObservableCollection<BarberSlotOption> Slots { get; } = [];

    /// <summary>
    /// Имя мастера из карточки: «Тимур Мастеров».
    /// </summary>
    [ObservableProperty]
    private string _barberName = BookingText.BarberFallback;

    /// <summary>
    /// Услуга визита: она при переносе не меняется.
    /// </summary>
    [ObservableProperty]
    private string _serviceName = BookingText.ServiceFallback;

    /// <summary>
    /// Салон визита: он при переносе не меняется.
    /// </summary>
    [ObservableProperty]
    private string _salonName = BookingText.SalonFallback;

    /// <summary>
    /// Текущий момент визита: «сб, 15 ноября в 18:30».
    /// </summary>
    [ObservableProperty]
    private string _momentText = string.Empty;

    /// <summary>
    /// Цена визита: она при переносе не меняется.
    /// </summary>
    [ObservableProperty]
    private string _priceText = string.Empty;

    /// <summary>
    /// Длительность визита: она при переносе не меняется.
    /// </summary>
    [ObservableProperty]
    private string _durationText = string.Empty;

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
    /// Максимальная дата: сегодня плюс девяносто дней.
    /// </summary>
    [ObservableProperty]
    private DateTime _maximumDate;

    /// <summary>
    /// Идёт чтение сетки слотов.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyDay))]
    private bool _isLoadingSlots;

    /// <summary>
    /// Ошибка чтения сетки; <c>null</c>, когда сетка загрузилась.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyDay))]
    private string? _slotsError;

    /// <summary>
    /// Рабочий ли день у мастера: от этого зависит текст пустого дня.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyDayText))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyDay))]
    private bool _isWorkingDay;

    /// <summary>
    /// Сетка на выбранную дату уже загрузилась.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyDay))]
    private bool _isDayLoaded;

    /// <summary>
    /// Цикл сам повторяет чтение сетки: под причиной ошибки видна строка «Пробуем снова…».
    /// </summary>
    /// <remarks>
    /// Состояние цикла, а не экрана: свойство переключается подпиской на
    /// <see cref="AutoRetryLoop.StateChanged"/> и ничего не решает само.
    /// </remarks>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Выбранный слот; <c>null</c>, пока пользователь не выбрал время.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    private BarberSlotOption? _selectedSlot;

    /// <summary>
    /// Идёт отправка переноса: кнопка неактивна, повтор игнорируется.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(SubmitText))]
    private bool _isSubmitting;

    /// <summary>
    /// Ошибка отправки; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>
    /// Есть ли ошибка отправки.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Можно ли нажать «Перенести визит».
    /// </summary>
    public bool CanSubmit => SelectedSlot is not null && !IsSubmitting;

    /// <summary>
    /// Надпись на кнопке: во время запроса — «Переносим…».
    /// </summary>
    public string SubmitText => IsSubmitting ? Loc.T("Переносим…") : Loc.T("Перенести визит");

    /// <summary>
    /// Показывать пустой день: рабочих часов нет или свободного времени не осталось.
    /// </summary>
    public bool ShowEmptyDay =>
        IsDayLoaded && !IsLoadingSlots && SlotsError is null && Slots.Count == 0;

    /// <summary>
    /// Объяснение пустого дня.
    /// </summary>
    public string EmptyDayText => IsWorkingDay ? NoFreeTimeText : NoWorkingDayText;

    /// <summary>
    /// Заполняет экран данными визита и границами дат.
    /// </summary>
    /// <remarks>
    /// Минимум и максимум считаются в поясе салона: пояс устройства к записи отношения не
    /// имеет, а «сегодня» в UTC может быть вчерашним днём салона. Дата по умолчанию — та,
    /// на которую назначен визит.
    /// </remarks>
    /// <param name="item">Строка списка, открытая для переноса.</param>
    public void Initialize(BookingListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _item = item;
        _timeZone = item.TimeZone;

        BarberName = item.BarberName;
        ServiceName = item.ServiceName;
        SalonName = item.SalonName;
        MomentText = item.MomentText;
        PriceText = item.PriceText;
        DurationText = item.DurationText;

        var today = SalonClock.Today(_timeZone, _timeProvider.GetUtcNow());

        MinimumDate = today.ToDateTime(TimeOnly.MinValue);
        MaximumDate = MinimumDate.AddDays(MaxDaysAhead);

        var visitDate = item.StartDate.ToDateTime(TimeOnly.MinValue);

        _isApplyingDate = true;

        try
        {
            SelectedDate = Clamp(visitDate);
        }
        finally
        {
            _isApplyingDate = false;
        }

        NotifySlotsState();
    }

    /// <summary>
    /// Сообщает, что экран виден, и запускает автоповтор, если показывать есть что.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Зовётся страницей при появлении и каждом возврате из фона. Метод идемпотентен: при
    /// работающем цикле повторный вызов ничего не делает, а без повторяемого сбоя лишь
    /// запоминает, что экран виден. «Видна» — сигнал, а не команда: решение принимает вид
    /// последнего сбоя.
    /// </para>
    /// <para>
    /// Запуск и остановка обязаны идти из UI-потока: <see cref="AutoRetryLoop.StateChanged"/>
    /// синхронно меняет привязанное <see cref="IsAutoRetrying"/>, поэтому метод зовут только
    /// из жизненного цикла страницы.
    /// </para>
    /// </remarks>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartAutoRetry();
    }

    /// <summary>
    /// Сообщает, что экран скрыт, и останавливает автоповтор.
    /// </summary>
    /// <remarks>
    /// Идемпотентен: остановка уже стоящего цикла ничего не делает. Пауза, запрос в полёте и
    /// завершение цикла отменяются, а <see cref="IsAutoRetrying"/> сразу становится
    /// <c>false</c> — экран не показывает работающий повтор поверх ушедшей страницы.
    /// </remarks>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        StopAutoRetryLoop();
    }

    /// <summary>
    /// Завершается, когда цикл автоповтора останавливается: тесты ждут его вместо пауз.
    /// </summary>
    internal Task AutoRetryCompletion => _autoRetry.Completion;

    /// <summary>
    /// Цикл автоповтора: тестам нужен счётчик попыток, приложению — нет.
    /// </summary>
    internal AutoRetryLoop AutoRetry => _autoRetry;

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
    /// <para>
    /// Прошлый запрос отменяется: два ответа на разные даты не должны смешаться. Ответ,
    /// пришедший после смены даты, отбрасывается по номеру поколения — отмена транспорта
    /// не гарантирует, что сервер не успел ответить.
    /// </para>
    /// <para>
    /// Смена даты — действие человека: цикл автоповтора останавливается приватно (страница
    /// остаётся видимой), сетка читается сразу, а цикл запускается заново только если новый
    /// сбой снова повторяемый. Иначе тихий повтор ушёл бы со старой датой поверх выбора,
    /// который человек только что сделал.
    /// </para>
    /// </remarks>
    /// <param name="date">Выбранная дата.</param>
    /// <param name="cancellationToken">Токен отмены страницы.</param>
    /// <returns>Задача чтения сетки.</returns>
    public async Task ChangeDateAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        if (_item is null || _disposed)
        {
            return;
        }

        StopAutoRetryLoop();

        var clamped = Clamp(date);

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

        // Новая дата начинает чтение с чистого листа: снимок прошлой неудачи к ней не относится.
        _retrySlotsDate = null;

        NotifySlotsState();

        await LoadSlotsCoreAsync(DateOnly.FromDateTime(clamped), silent: false, cancellationToken);

        TryStartAutoRetry();
    }

    /// <summary>
    /// Читает сетку на заданную салонную дату — вручную или тихой попыткой цикла.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ручной режим показывает индикатор и стирает прежнюю ошибку: человек сам попросил
    /// свежую сетку. Тихий режим — попытка цикла: причина ошибки остаётся на экране до ответа,
    /// иначе каждая пауза между попытками мигала бы пустым экраном и кнопкой, которую некуда
    /// нажать. Успех очищает ошибку и снимок, неудача оставляет всё как было и возвращает вид
    /// сбоя.
    /// </para>
    /// <para>
    /// Снимок — дата этого запроса, а не <see cref="SelectedDate"/>: повтор несёт тот запрос,
    /// который упал. Вид сбоя запоминается для каждого неуспеха, а не только для повторяемого
    /// (ловушка 4): иначе «транспорт, потом 404» оставил бы в памяти старый
    /// <see cref="ApiFailureKind.Transport"/> и кнопка запустила бы замкнутый цикл.
    /// </para>
    /// </remarks>
    /// <param name="day">Салонная дата запроса.</param>
    /// <param name="silent">Тихая попытка цикла: не показывать индикатор и не чистить ошибку.</param>
    /// <param name="cancellationToken">Токен отмены страницы или цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private async Task<ApiFailureKind> LoadSlotsCoreAsync(
        DateOnly day,
        bool silent,
        CancellationToken cancellationToken)
    {
        _slotsCancellation?.Cancel();
        _slotsCancellation?.Dispose();
        _slotsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var token = _slotsCancellation.Token;
        var generation = ++_generation;
        var kind = ApiFailureKind.None;

        try
        {
            var response = await _bookingsApi.GetRescheduleSlotsAsync(_item!.Id, day, token);

            if (generation != _generation)
            {
                // Пока читали, дату сменили: ответ на старую дату сетку не подменяет.
                return ApiFailureKind.None;
            }

            IsWorkingDay = response.IsWorkingDay;
            IsDayLoaded = true;

            var zone = SalonClock.TryResolve(response.TimeZoneId);

            foreach (var slot in response.Slots)
            {
                var option = new BarberSlotOption(
                    slot.StartAt,
                    slot.EndAt,
                    SalonClock.FormatTimeRange(slot.StartAt, slot.EndAt, zone))
                {
                    SelectCommand = SelectSlotCommand,
                };

                Slots.Add(option);
            }

            SlotsError = null;
            _retrySlotsDate = null;
            _slotsFailureKind = ApiFailureKind.None;
        }
        catch (Exception exception)
        {
            if (generation == _generation)
            {
                kind = ApiFailureClassifier.Classify(exception, token);

                if (kind != ApiFailureKind.None)
                {
                    // Таймаут транспорта классификатор уже свёл к «нет связи», поэтому он больше
                    // не проглатывается как отмена и не молчит.
                    SlotsError = DescribeSlotsFailure(exception, kind);
                    _slotsFailureKind = kind;
                    _retrySlotsDate = kind.IsRetryable() ? day : null;
                }
            }
        }
        finally
        {
            if (generation == _generation)
            {
                if (!silent)
                {
                    IsLoadingSlots = false;
                }

                NotifySlotsState();
            }
        }

        return kind;
    }

    /// <summary>
    /// Одна попытка цикла: повторить сетку с датой упавшего запроса.
    /// </summary>
    /// <remarks>
    /// Условия перепроверяются на момент попытки: пока цикл ждал паузу, ошибку могли снять
    /// или человек мог сменить дату. Тогда повторять нечего, и цикл останавливается — так
    /// терминальный сбой после повторяемого не оживляет цикл.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private Task<ApiFailureKind> RetrySlotsAttemptAsync(CancellationToken cancellationToken)
    {
        if (_retrySlotsDate is not { } day
            || SlotsError is null
            || !_slotsFailureKind.IsRetryable()
            || IsLoadingSlots)
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadSlotsCoreAsync(day, silent: true, cancellationToken);
    }

    /// <summary>
    /// Запускает цикл, если экран виден, сбой повторяемый и есть снимок даты.
    /// </summary>
    /// <remarks>
    /// Второй цикл поверх первого удвоил бы запросы, поэтому <see cref="AutoRetryLoop.Start"/>
    /// при работающем цикле ничего не делает. «Видна» — сигнал страницы, а не следствие
    /// нажатия: повтор запускается и из завершения загрузки, но только пока экран виден.
    /// </remarks>
    private void TryStartAutoRetry()
    {
        if (!_isPageVisible
            || IsLoadingSlots
            || SlotsError is null
            || !_slotsFailureKind.IsRetryable()
            || _retrySlotsDate is null)
        {
            return;
        }

        _autoRetry.Start(RetrySlotsAttemptAsync);
    }

    /// <summary>
    /// Останавливает цикл, не трогая признак «экран виден».
    /// </summary>
    /// <remarks>
    /// Нужен кнопке и смене даты: они останавливают фон, но экран остаётся видимым, и после
    /// ответа цикл имеет право запуститься снова. Публичный <see cref="StopAutoRetry"/>
    /// дополнительно помечает страницу скрытой — для них это неверно.
    /// </remarks>
    private void StopAutoRetryLoop() => _autoRetry.Stop();

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Переводит сбой сетки в текст, не показывая английский <c>detail</c>.
    /// </summary>
    /// <remarks>
    /// <c>429</c> сохраняет свой текст — это просьба сбавить темп, а не «нет связи»; повтор
    /// при этом остаётся повторяемым, как решил классификатор.
    /// </remarks>
    private static string DescribeSlotsFailure(Exception exception, ApiFailureKind kind) =>
        kind == ApiFailureKind.Transport
            ? NetworkErrorText
            : exception is BarberSearchApiException
            {
                StatusCode: HttpStatusCode.TooManyRequests,
            }
                ? TooManyRequestsText
                : SlotsLoadErrorText;


    /// <summary>
    /// Выбирает слот; повторное нажатие на выбранный слот ничего не меняет.
    /// </summary>
    /// <param name="slot">Слот сетки.</param>
    [RelayCommand]
    private void SelectSlot(BarberSlotOption? slot)
    {
        if (slot is null)
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
    /// Повторяет чтение сетки после ошибки.
    /// </summary>
    /// <remarks>
    /// «Повторить» значит «повторить сейчас»: <see cref="ChangeDateAsync"/> приватно
    /// останавливает цикл, читает выбранную дату без паузы и запускает цикл заново только при
    /// новом повторяемом сбое. Публичный <see cref="StopAutoRetry"/> здесь не годится — он
    /// ещё и пометил бы страницу скрытой, и повтор не смог бы включиться снова.
    /// </remarks>
    [RelayCommand]
    private Task RetrySlotsAsync() => LoadSlotsAsync();

    /// <summary>
    /// Переносит визит на выбранный слот.
    /// </summary>
    /// <remarks>
    /// Отправляется ровно начало выбранного слота, без пересчёта в пояс устройства: сервер
    /// сравнивает абсолютные моменты и сам берёт конец. Занятое время и устаревший слот
    /// перечитывают сетку, остальные ошибки оставляют выбор на месте.
    /// </remarks>
    [RelayCommand]
    private async Task SubmitAsync()
    {
        var slot = SelectedSlot;

        if (IsSubmitting || slot is null || _item is null)
        {
            return;
        }

        IsSubmitting = true;
        ErrorMessage = null;

        try
        {
            await _bookingsApi.RescheduleBookingAsync(_item.Id, slot.StartAt);

            Rescheduled?.Invoke(this, new BarberSlotResponse(slot.StartAt, slot.EndAt));
        }
        catch (BarberSearchApiException exception)
        {
            var (message, reloadSlots) = DescribeRescheduleFailure(exception);

            ErrorMessage = message;

            if (reloadSlots)
            {
                await ChangeDateAsync(SelectedDate);
            }
        }
        catch (HttpRequestException)
        {
            ErrorMessage = NetworkErrorText;
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            ErrorMessage = NetworkErrorText;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    /// <summary>
    /// Освобождает отмену сетки слотов и останавливает автоповтор.
    /// </summary>
    /// <remarks>
    /// Цикл останавливается здесь ещё раз, а не только на странице: экран уходит из стека,
    /// и тихая попытка не должна пережить свой ViewModel.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _autoRetry.Stop();

        _slotsCancellation?.Cancel();
        _slotsCancellation?.Dispose();
        _slotsCancellation = null;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Привязывает к дате изменение выбора: <c>DatePicker</c> меняет дату, экран читает сетку.
    /// </summary>
    /// <param name="value">Новая дата из <c>DatePicker</c>.</param>
    partial void OnSelectedDateChanged(DateTime value)
    {
        if (_isApplyingDate)
        {
            return;
        }

        _ = ChangeDateAsync(value);
    }

    /// <summary>
    /// Держит дату внутри окна записи: раньше сегодня и позже девяноста дней сервер отвергнет.
    /// </summary>
    private DateTime Clamp(DateTime date)
    {
        if (date < MinimumDate)
        {
            return MinimumDate;
        }

        return date > MaximumDate ? MaximumDate : date;
    }

    /// <summary>
    /// Поднимает уведомления о вычисляемых свойствах сетки.
    /// </summary>
    private void NotifySlotsState()
    {
        OnPropertyChanged(nameof(ShowEmptyDay));
        OnPropertyChanged(nameof(EmptyDayText));
    }

    /// <summary>
    /// Переводит ошибку переноса в текст и признак «перечитать сетку».
    /// </summary>
    /// <remarks>
    /// Коды важнее статуса: <c>overlap</c> и остаточная гонка <c>update.overlap</c> значат
    /// одно — время занято; <c>slot</c> — сетка устарела. Английский <c>detail</c> не
    /// показывается никогда.
    /// </remarks>
    private static (string Message, bool ReloadSlots) DescribeRescheduleFailure(
        BarberSearchApiException exception)
    {
        if (HasCode(exception, ApiErrorCodes.Booking.RescheduleOverlap)
            || HasCode(exception, RescheduleOverlapCode))
        {
            return (Loc.T("Это время уже занято. Выберите другое."), true);
        }

        if (HasCode(exception, ApiErrorCodes.Booking.RescheduleSlot))
        {
            return (Loc.T("Это время больше не доступно. Выберите другое."), true);
        }

        if (HasCode(exception, ApiErrorCodes.Booking.RescheduleStartAtInPast))
        {
            return (Loc.T("Это время уже прошло. Выберите другое."), true);
        }

        if (HasCode(exception, ApiErrorCodes.Booking.RescheduleStartAtTooFar))
        {
            return (Loc.T("Слишком далеко в будущем."), false);
        }

        if (HasCode(exception, ApiErrorCodes.Booking.RescheduleStatus))
        {
            return (Loc.T("Запись уже нельзя перенести — обновите страницу."), false);
        }

        if (HasCode(exception, ApiErrorCodes.Booking.RescheduleLockTimeout))
        {
            return (Loc.T("Не удалось перенести: попробуйте ещё раз."), true);
        }

        if (HasCode(exception, ApiErrorCodes.Booking.UpdateConcurrencyConflict))
        {
            return (Loc.T("Запись изменилась — обновите страницу."), true);
        }

        if (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return (Loc.T("Запись не найдена."), false);
        }

        if (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            return (Loc.T("Это запись другого клиента."), false);
        }

        return (DescribeApiFailure(exception), false);
    }

    /// <summary>
    /// Общие тексты сбоев: лимит запросов, истёкшая сессия и запасной текст.
    /// </summary>
    private static string DescribeApiFailure(BarberSearchApiException exception) =>
        exception.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => TooManyRequestsText,
            HttpStatusCode.Unauthorized => SessionExpiredText,
            _ => SubmitFailedText,
        };

    /// <summary>
    /// Проверяет наличие публичного кода ошибки.
    /// </summary>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);
}
