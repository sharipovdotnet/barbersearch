using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Domain.Bookings;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Строка списка записей кабинета мастера — запись клиента или окно между визитами.
/// </summary>
/// <remarks>
/// <para>
/// Одна строка отвечает за две сущности ленты дня: визит и свободное окно. Отдельный тип
/// окна был бы честнее, но <c>CollectionView</c> в MAUI показывает один тип элементов, и
/// два шаблона в одном списке потребовали бы селектора шаблонов. Поэтому окно — это
/// <see cref="BarberBookingItem"/> с <see cref="IsGap"/>, у которого все свойства визита
/// пусты, а <see cref="GapText"/> и <see cref="GapActionText"/> заполнены.
/// </para>
/// <para>
/// Состояние статуса, времени и цены наблюдаемое: после действия сервер отвечает
/// <c>204</c> без тела, и строка применяет результат локально, не перечитывая список.
/// Никаких оптимистичных правок нет — методы вызываются только после успешного ответа.
/// </para>
/// <para>
/// <see cref="DateText"/> показывает дату в поясе устройства, а <see cref="TimeRangeText"/>
/// и <see cref="MomentText"/> — в поясе салона. Это осознанное разделение: день ленты
/// собирается по календарю телефона (как в вебе), а время визита всегда принадлежит салону.
/// </para>
/// </remarks>
public sealed partial class BarberBookingItem : ObservableObject
{
    private readonly int _gapMinutes;
    private DateTimeOffset _now;

    /// <summary>
    /// Создаёт строку визита из ответа API.
    /// </summary>
    /// <param name="booking">Запись клиента.</param>
    /// <param name="isUpcoming">
    /// Строка пришла из списка «Ближайшие»: только там подтверждённую запись ещё можно
    /// отменить. У истории визит уже прошёл.
    /// </param>
    /// <param name="now">Текущий момент; <c>null</c> — системные часы.</param>
    public BarberBookingItem(
        BarberBookingResponse booking,
        bool isUpcoming = true,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(booking);

        Booking = booking;
        Id = booking.Id;
        IsUpcoming = isUpcoming;
        TimeZone = SalonClock.TryResolve(booking.Salon.TimeZoneId);
        _now = now ?? DateTimeOffset.UtcNow;

        _status = booking.Status;
        _startAt = booking.StartAt;
        _endAt = booking.EndAt;
        _price = booking.Price;
    }

    /// <summary>
    /// Создаёт строку окна между визитами.
    /// </summary>
    /// <param name="startAt">Конец предыдущего визита.</param>
    /// <param name="endAt">Начало следующего визита.</param>
    /// <param name="minutes">Длительность окна в минутах.</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    private BarberBookingItem(
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        int minutes,
        TimeZoneInfo? timeZone)
    {
        IsGap = true;
        _gapMinutes = minutes;
        TimeZone = timeZone;
        _startAt = startAt;
        _endAt = endAt;
        _status = BookingStatus.Pending;
        _price = 0m;
        _now = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Сырой ответ API; <c>null</c> у окна между визитами.
    /// </summary>
    public BarberBookingResponse? Booking { get; private set; }

    /// <summary>
    /// Идентификатор брони; <see cref="Guid.Empty"/> у окна.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Это окно между визитами, а не запись клиента.
    /// </summary>
    public bool IsGap { get; }

    /// <summary>
    /// Строка принадлежит списку «Ближайшие».
    /// </summary>
    /// <remarks>
    /// Признак нужен только <see cref="CanCancel"/>: в истории подтверждённый визит уже
    /// прошёл, и отменять его поздно. Меняется сеттером, если строка переехала между
    /// списками после перечитывания.
    /// </remarks>
    public bool IsUpcoming
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCancel));
        }
    }

    /// <summary>
    /// Пояс салона; <c>null</c>, если идентификатор не распознан.
    /// </summary>
    public TimeZoneInfo? TimeZone { get; }

    /// <summary>
    /// Статус записи; у окна всегда <see cref="BookingStatus.Pending"/> и не показывается.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusKind))]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(CanComplete))]
    [NotifyPropertyChangedFor(nameof(CanNoShow))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanReschedule))]
    [NotifyPropertyChangedFor(nameof(CanChangePrice))]
    [NotifyPropertyChangedFor(nameof(CanWriteNote))]
    [NotifyPropertyChangedFor(nameof(HasCancellation))]
    [NotifyPropertyChangedFor(nameof(CancellationText))]
    private BookingStatus _status;

    /// <summary>
    /// Начало визита в UTC.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DateText))]
    [NotifyPropertyChangedFor(nameof(TimeRangeText))]
    [NotifyPropertyChangedFor(nameof(MomentText))]
    [NotifyPropertyChangedFor(nameof(CanComplete))]
    [NotifyPropertyChangedFor(nameof(CanNoShow))]
    [NotifyPropertyChangedFor(nameof(CanReschedule))]
    private DateTimeOffset _startAt;

    /// <summary>
    /// Окончание визита в UTC.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeRangeText))]
    [NotifyPropertyChangedFor(nameof(CanComplete))]
    [NotifyPropertyChangedFor(nameof(CanNoShow))]
    private DateTimeOffset _endAt;

    /// <summary>
    /// Цена визита.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PriceText))]
    private decimal _price;

    /// <summary>
    /// По строке уже идёт действие: повторное нажатие игнорируется.
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Строка — следующий клиент дня: на ней стоит бейдж «Следующий клиент».
    /// </summary>
    /// <remarks>
    /// Признак приходит от раздела «Записи»: сама строка не знает, первая ли она в ленте.
    /// Свойство наблюдаемое, потому что его ждёт XAML, и ничего не вычисляет — это только
    /// поверхность привязки, а не второе правило «кто следующий».
    /// </remarks>
    [ObservableProperty]
    private bool _isNextClient;

    /// <summary>
    /// Ошибка последнего действия над этой строкой; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionError))]
    private string? _actionError;

    /// <summary>
    /// Дата строки в поясе устройства: «сб, 15 нояб.».
    /// </summary>
    public string DateText => IsGap
        ? string.Empty
        : StartAt.ToLocalTime().ToString("ddd, d MMM", Loc.Culture);

    /// <summary>
    /// Время визита в поясе салона: «10:00–11:00».
    /// </summary>
    public string TimeRangeText => IsGap
        ? string.Empty
        : BarberCabinetText.TimeRange(StartAt, EndAt, TimeZone);

    /// <summary>
    /// Момент визита в поясе салона: «сб, 15 ноября в 10:00».
    /// </summary>
    public string MomentText => IsGap
        ? string.Empty
        : BookingText.Moment(StartAt, TimeZone);

    /// <summary>
    /// Цена визита: «2 400 ₽».
    /// </summary>
    public string PriceText => IsGap ? string.Empty : BarberText.Price(Price);

    /// <summary>
    /// Подпись статуса: «Ждёт подтверждения».
    /// </summary>
    public string StatusText => IsGap ? string.Empty : BarberCabinetText.StatusText(Status);

    /// <summary>
    /// Ключ статуса для класса карточки: <c>pending</c>, <c>confirmed</c>…
    /// </summary>
    public string StatusKind => IsGap ? string.Empty : BarberCabinetText.StatusKind(Status);

    /// <summary>
    /// Имя клиента.
    /// </summary>
    public string CustomerName => IsGap ? string.Empty : Booking?.Customer.DisplayName ?? string.Empty;

    /// <summary>
    /// Инициалы клиента для заглушки аватара.
    /// </summary>
    public string Initials => IsGap ? string.Empty : BarberText.Initials(CustomerName);

    /// <summary>
    /// Телефон клиента; <c>null</c>, если он не заполнен.
    /// </summary>
    public string? PhoneNumber => IsGap ? null : Booking?.Customer.PhoneNumber;

    /// <summary>
    /// Ссылка для звонка: <c>tel:</c> и только цифры со знаком «+».
    /// </summary>
    public string PhoneUri => HasPhone
        ? string.Concat("tel:", new string(PhoneNumber!.Where(IsPhoneCharacter).ToArray()))
        : string.Empty;

    /// <summary>
    /// У клиента есть телефон.
    /// </summary>
    public bool HasPhone => !string.IsNullOrWhiteSpace(PhoneNumber);

    /// <summary>
    /// Услуга визита.
    /// </summary>
    public string ServiceName => IsGap ? string.Empty : Booking?.Service.Name ?? string.Empty;

    /// <summary>
    /// Салон визита.
    /// </summary>
    public string SalonName => IsGap ? string.Empty : Booking?.Salon.Name ?? string.Empty;

    /// <summary>
    /// Город и адрес салона: «Москва, ул. Ленина, 1».
    /// </summary>
    public string CityAddress => IsGap || Booking is null
        ? string.Empty
        : string.Concat(Booking.Salon.City, ", ", Booking.Salon.Address);

    /// <summary>
    /// Услуга и салон одной строкой: «Стрижка · Hairo».
    /// </summary>
    public string ServiceSalonText => IsGap
        ? string.Empty
        : string.Concat(ServiceName, " · ", SalonName);

    /// <summary>
    /// У визита есть комментарий клиента.
    /// </summary>
    public bool HasCustomerNote => !IsGap && !string.IsNullOrWhiteSpace(Booking?.CustomerNote);

    /// <summary>
    /// Комментарий клиента: «Комментарий клиента: Покороче сзади».
    /// </summary>
    public string CustomerNoteText => HasCustomerNote
        ? Loc.T("Комментарий клиента: {0}", Booking!.CustomerNote!.Trim())
        : string.Empty;

    /// <summary>
    /// У визита есть заметка мастера.
    /// </summary>
    public bool HasBarberNote => !IsGap && !string.IsNullOrWhiteSpace(Booking?.BarberNote);

    /// <summary>
    /// Заметка мастера: «Ваша заметка: Покороче сзади».
    /// </summary>
    public string BarberNoteText => HasBarberNote
        ? Loc.T("Ваша заметка: {0}", Booking!.BarberNote!.Trim())
        : string.Empty;

    /// <summary>
    /// Визит отменён: показывается причина отмены.
    /// </summary>
    public bool HasCancellation => !IsGap && Status == BookingStatus.Cancelled;

    /// <summary>
    /// Причина отмены или «Причина отмены не указана».
    /// </summary>
    public string CancellationText => HasCancellation
        ? BookingText.CancellationReason(Booking?.CancellationReason)
        : string.Empty;

    /// <summary>
    /// Окно между визитами: «свободно 30 мин».
    /// </summary>
    public string GapText => IsGap ? BarberCabinetText.GapText(_gapMinutes) : string.Empty;

    /// <summary>
    /// Кнопка окна: «Занять».
    /// </summary>
    public string GapActionText => IsGap ? Loc.T("Занять") : string.Empty;

    /// <summary>
    /// Подтверждение доступно только ожидающей записи.
    /// </summary>
    public bool CanConfirm => !IsGap && Status == BookingStatus.Pending;

    /// <summary>
    /// Завершение доступно подтверждённому визиту после его начала.
    /// </summary>
    public bool CanComplete => !IsGap && Status == BookingStatus.Confirmed && IsStarted(_now);

    /// <summary>
    /// Неявка доступна подтверждённому визиту после его начала.
    /// </summary>
    public bool CanNoShow => !IsGap && Status == BookingStatus.Confirmed && IsStarted(_now);

    /// <summary>
    /// Отмена доступна ожидающей записи в любом списке и подтверждённой — только в «Ближайших».
    /// </summary>
    public bool CanCancel => !IsGap
        && (Status == BookingStatus.Pending
            || (Status == BookingStatus.Confirmed && IsUpcoming));

    /// <summary>
    /// Перенос доступен активной записи, которая ещё не началась.
    /// </summary>
    public bool CanReschedule => !IsGap
        && Status is BookingStatus.Pending or BookingStatus.Confirmed
        && StartAt > _now;

    /// <summary>
    /// Цену меняют у всего, кроме отменённой записи и неявки.
    /// </summary>
    public bool CanChangePrice => !IsGap
        && Status is not (BookingStatus.Cancelled or BookingStatus.NoShow);

    /// <summary>
    /// Заметку пишут к активному визиту.
    /// </summary>
    public bool CanWriteNote => !IsGap
        && Status is BookingStatus.Pending or BookingStatus.Confirmed;

    /// <summary>
    /// Визит уже начался: «Завершить» и «Неявка» появляются с этой минуты.
    /// </summary>
    /// <param name="now">Текущий момент.</param>
    /// <returns><c>true</c>, если начало визита наступило.</returns>
    public bool IsStarted(DateTimeOffset now) => !IsGap && StartAt <= now;

    /// <summary>
    /// Есть ошибка действия, которую надо показать.
    /// </summary>
    public bool HasActionError => !string.IsNullOrEmpty(ActionError);

    /// <summary>
    /// Создаёт строку окна между визитами.
    /// </summary>
    /// <param name="startAt">Конец предыдущего визита.</param>
    /// <param name="endAt">Начало следующего визита.</param>
    /// <param name="minutes">Длительность окна в минутах.</param>
    /// <param name="timeZone">Пояс салона; <c>null</c> — UTC.</param>
    /// <returns>Строка окна для той же коллекции, что и визиты.</returns>
    public static BarberBookingItem Gap(
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        int minutes,
        TimeZoneInfo? timeZone) =>
        new(startAt, endAt, minutes, timeZone);

    /// <summary>
    /// Обновляет текущий момент строки; от него зависят доступные действия.
    /// </summary>
    /// <param name="now">Текущий момент.</param>
    public void UpdateNow(DateTimeOffset now)
    {
        if (_now == now)
        {
            return;
        }

        _now = now;

        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanNoShow));
        OnPropertyChanged(nameof(CanReschedule));
    }

    /// <summary>
    /// Очищает или ставит ошибку действия.
    /// </summary>
    /// <param name="message">Текст ошибки; <c>null</c> очищает её.</param>
    public void SetActionError(string? message) => ActionError = message;

    /// <summary>
    /// Применяет подтверждение: <c>Pending → Confirmed</c>.
    /// </summary>
    public void ApplyConfirmed() => ApplyStatus(BookingStatus.Confirmed);

    /// <summary>
    /// Применяет завершение визита: <c>Confirmed → Completed</c>.
    /// </summary>
    public void ApplyCompleted() => ApplyStatus(BookingStatus.Completed);

    /// <summary>
    /// Применяет неявку: <c>Confirmed → NoShow</c>.
    /// </summary>
    public void ApplyNoShow() => ApplyStatus(BookingStatus.NoShow);

    /// <summary>
    /// Применяет отмену и причину, которую сохранил сервер.
    /// </summary>
    /// <param name="reason">Причина отмены; <c>null</c> — без причины.</param>
    public void ApplyCancelled(string? reason)
    {
        if (IsGap)
        {
            return;
        }

        Booking = Booking! with
        {
            Status = BookingStatus.Cancelled,
            CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        };

        Status = BookingStatus.Cancelled;
    }

    /// <summary>
    /// Применяет новые границы визита после переноса.
    /// </summary>
    /// <param name="startAt">Новое начало в UTC.</param>
    /// <param name="endAt">Новое окончание в UTC.</param>
    public void ApplyRescheduled(DateTimeOffset startAt, DateTimeOffset endAt)
    {
        if (IsGap)
        {
            return;
        }

        Booking = Booking! with { StartAt = startAt, EndAt = endAt };
        StartAt = startAt;
        EndAt = endAt;
    }

    /// <summary>
    /// Применяет заметку мастера; пустая строка очищает её.
    /// </summary>
    /// <param name="note">Текст заметки.</param>
    public void ApplyNote(string? note)
    {
        if (IsGap)
        {
            return;
        }

        Booking = Booking! with
        {
            BarberNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };

        OnPropertyChanged(nameof(HasBarberNote));
        OnPropertyChanged(nameof(BarberNoteText));
    }

    /// <summary>
    /// Применяет новую цену визита.
    /// </summary>
    /// <param name="price">Цена, которую подтвердил сервер.</param>
    public void ApplyPrice(decimal price)
    {
        if (IsGap)
        {
            return;
        }

        Booking = Booking! with { Price = price };
        Price = price;
    }

    /// <summary>
    /// Проверяет, что символ допустим в <c>tel:</c>-ссылке.
    /// </summary>
    private static bool IsPhoneCharacter(char value) => char.IsDigit(value) || value == '+';

    /// <summary>
    /// Меняет статус строки и её сырой ответ.
    /// </summary>
    private void ApplyStatus(BookingStatus status)
    {
        if (IsGap)
        {
            return;
        }

        Booking = Booking! with { Status = status };
        Status = status;
    }
}
