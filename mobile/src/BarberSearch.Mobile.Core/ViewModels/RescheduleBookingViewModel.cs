using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Bookings;
using BarberSearch.Mobile.Core.Formatting;
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
/// </remarks>
public sealed partial class RescheduleBookingViewModel : ObservableObject, IDisposable
{
    private const int MaxDaysAhead = 90;

    private const string SlotsLoadErrorText = "Не удалось загрузить свободное время.";
    private const string SubmitFailedText = "Не удалось перенести запись.";
    private const string NoWorkingDayText = "В этот день барбер не работает.";
    private const string NoFreeTimeText = "Свободного времени на этот день нет.";
    private const string NetworkErrorText =
        "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string TooManyRequestsText =
        "Слишком много запросов. Подождите минуту и повторите.";
    private const string SessionExpiredText = "Сессия истекла — войдите снова.";

    private const string RescheduleOverlapCode = "booking.update.overlap";

    private readonly IBookingsApi _bookingsApi;
    private readonly TimeProvider _timeProvider;

    private BookingListItem? _item;
    private TimeZoneInfo? _timeZone;
    private CancellationTokenSource? _slotsCancellation;
    private int _generation;
    private bool _isApplyingDate;
    private bool _disposed;

    /// <summary>
    /// Создаёт экран переноса.
    /// </summary>
    /// <param name="bookingsApi">Слоты переноса и сама операция переноса.</param>
    /// <param name="timeProvider">Часы: границы дат считаются от «сегодня» салона.</param>
    public RescheduleBookingViewModel(IBookingsApi bookingsApi, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(bookingsApi);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _bookingsApi = bookingsApi;
        _timeProvider = timeProvider;
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
    /// Имя барбера из карточки: «Тимур Барберов».
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
    /// Рабочий ли день у барбера: от этого зависит текст пустого дня.
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
    public string SubmitText => IsSubmitting ? "Переносим…" : "Перенести визит";

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
    /// Прошлый запрос отменяется: два ответа на разные даты не должны смешаться. Ответ,
    /// пришедший после смены даты, отбрасывается по номеру поколения — отмена транспорта
    /// не гарантирует, что сервер не успел ответить.
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

        NotifySlotsState();

        _slotsCancellation?.Cancel();
        _slotsCancellation?.Dispose();
        _slotsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var token = _slotsCancellation.Token;
        var generation = ++_generation;
        var day = DateOnly.FromDateTime(clamped);

        try
        {
            var response = await _bookingsApi.GetRescheduleSlotsAsync(_item.Id, day, token);

            if (generation != _generation)
            {
                return;
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
        }
        catch (OperationCanceledException)
        {
            // Смена даты или закрытие экрана: сетку уже читает следующий запрос.
        }
        catch (BarberSearchApiException)
        {
            if (generation == _generation)
            {
                SlotsError = SlotsLoadErrorText;
            }
        }
        catch (HttpRequestException)
        {
            if (generation == _generation)
            {
                SlotsError = SlotsLoadErrorText;
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoadingSlots = false;
                NotifySlotsState();
            }
        }
    }

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
    /// Освобождает отмену сетки слотов.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

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
            return ("Это время уже занято. Выберите другое.", true);
        }

        if (HasCode(exception, ApiErrorCodes.Booking.RescheduleSlot))
        {
            return ("Это время больше не доступно. Выберите другое.", true);
        }

        if (HasCode(exception, ApiErrorCodes.Booking.RescheduleStartAtInPast))
        {
            return ("Это время уже прошло. Выберите другое.", true);
        }

        if (HasCode(exception, ApiErrorCodes.Booking.RescheduleStartAtTooFar))
        {
            return ("Слишком далеко в будущем.", false);
        }

        if (HasCode(exception, ApiErrorCodes.Booking.RescheduleStatus))
        {
            return ("Запись уже нельзя перенести — обновите страницу.", false);
        }

        if (HasCode(exception, ApiErrorCodes.Booking.RescheduleLockTimeout))
        {
            return ("Не удалось перенести: попробуйте ещё раз.", true);
        }

        if (HasCode(exception, ApiErrorCodes.Booking.UpdateConcurrencyConflict))
        {
            return ("Запись изменилась — обновите страницу.", true);
        }

        if (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return ("Запись не найдена.", false);
        }

        if (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            return ("Это запись другого клиента.", false);
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
