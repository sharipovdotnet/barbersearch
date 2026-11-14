using System.Globalization;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Domain.Bookings;
using BarberSearch.Mobile.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Строка списка записей: бронь вместе с данными карточки барбера.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BookingResponse"/> несёт только идентификаторы барбера, услуги и салона, поэтому
/// имя, адрес и пояс приходят из публичной карточки отдельным запросом. До её ответа строка
/// живёт с нейтральными подписями и временем в UTC: список не ждёт карточек, иначе одна
/// упавшая карточка держала бы весь экран.
/// </para>
/// <para>
/// Время хранится UTC-инстантами, а тексты пересобираются при каждой смене пояса или момента:
/// <see cref="ApplyProfile"/> и <see cref="RecomputeTimes"/> — единственные места, где это
/// происходит. Так «через 2 часа» и доступность переноса не расходятся с часами устройства.
/// </para>
/// </remarks>
public sealed partial class BookingListItem : ObservableObject
{
    private readonly BookingResponse _booking;

    private DateTimeOffset _startAt;
    private DateTimeOffset _endAt;
    private DateTimeOffset _now;
    private TimeZoneInfo? _timeZone;

    /// <summary>
    /// Создаёт строку из ответа API.
    /// </summary>
    /// <param name="booking">Бронь из списка.</param>
    /// <param name="now">Текущий момент для отсчёта и правил действий.</param>
    public BookingListItem(BookingResponse booking, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);

        _booking = booking;
        _startAt = booking.StartAt;
        _endAt = booking.EndAt;
        _now = now;
        _hasReview = booking.HasReview;

        StatusText = BookingText.Status(booking.Status);
        PriceText = BarberText.Price(booking.Price);
        DurationText = BarberText.Duration(DurationMinutes);
        BarberNoteText = BookingText.BarberNote(booking.BarberNote);
        CustomerNoteText = BookingText.CustomerNote(booking.CustomerNote);
        CancellationText = booking.Status == BookingStatus.Cancelled
            ? BookingText.CancellationReason(booking.CancellationReason)
            : null;

        RecomputeTimes(now);
    }

    /// <summary>
    /// Исходный ответ API; время в нём остаётся моментом создания, а не переноса.
    /// </summary>
    public BookingResponse Booking => _booking;

    /// <summary>
    /// Идентификатор брони.
    /// </summary>
    public Guid Id => _booking.Id;

    /// <summary>
    /// Статус записи.
    /// </summary>
    public BookingStatus Status => _booking.Status;

    /// <summary>
    /// Начало визита в UTC; меняется после переноса.
    /// </summary>
    public DateTimeOffset StartAt => _startAt;

    /// <summary>
    /// Окончание визита в UTC; меняется после переноса.
    /// </summary>
    public DateTimeOffset EndAt => _endAt;

    /// <summary>
    /// Цена визита; перенос её не меняет.
    /// </summary>
    public decimal Price => _booking.Price;

    /// <summary>
    /// Профиль барбера — по нему догружаются имя, услуга, салон и пояс.
    /// </summary>
    public Guid BarberProfileId => _booking.BarberProfileId;

    /// <summary>
    /// Услуга барбера, по которой считается длительность и находится название.
    /// </summary>
    public Guid BarberServiceId => _booking.BarberServiceId;

    /// <summary>
    /// Салон визита.
    /// </summary>
    public Guid SalonId => _booking.SalonId;

    /// <summary>
    /// Пояс салона; <c>null</c>, пока карточка не загрузилась или пояс неизвестен.
    /// </summary>
    public TimeZoneInfo? TimeZone => _timeZone;

    /// <summary>
    /// Локальная дата салона, в которую начинается визит.
    /// </summary>
    public DateOnly StartDate => SalonClock.Today(_timeZone, _startAt);

    /// <summary>
    /// Ключ группы месяцев (<c>yyyy-MM</c> в поясе салона).
    /// </summary>
    public string MonthKey => StartDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// Заголовок группы месяцев: «Ноябрь 2026».
    /// </summary>
    public string MonthTitle => BookingText.MonthTitle(StartDate);

    /// <summary>
    /// Время начала: «18:30».
    /// </summary>
    [ObservableProperty]
    private string _timeText = string.Empty;

    /// <summary>
    /// Короткая дата: «сб, 15 нояб.».
    /// </summary>
    [ObservableProperty]
    private string _dateText = string.Empty;

    /// <summary>
    /// Полный момент визита: «сб, 15 ноября в 18:30»; используется в баннерах и подписях.
    /// </summary>
    [ObservableProperty]
    private string _momentText = string.Empty;

    /// <summary>
    /// Цена: «2 400 ₽».
    /// </summary>
    [ObservableProperty]
    private string _priceText = string.Empty;

    /// <summary>
    /// Имя барбера из карточки; до её ответа — «Барбер».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServiceAndSalonText))]
    private string _barberName = BookingText.BarberFallback;

    /// <summary>
    /// Название услуги из карточки; до её ответа — «Услуга».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServiceAndSalonText))]
    private string _serviceName = BookingText.ServiceFallback;

    /// <summary>
    /// Название салона из карточки; до её ответа — «Салон».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServiceAndSalonText))]
    private string _salonName = BookingText.SalonFallback;

    /// <summary>
    /// Город и адрес салона: «Москва, ул. Тверская, 1». Пусто, если карточки нет.
    /// </summary>
    [ObservableProperty]
    private string _cityAddress = string.Empty;

    /// <summary>
    /// Длительность визита: из услуги, иначе из границ брони.
    /// </summary>
    [ObservableProperty]
    private string _durationText = string.Empty;

    /// <summary>
    /// Русская подпись статуса.
    /// </summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// Обратный отсчёт до визита; <c>null</c>, если он не нужен или невозможен.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCountdown))]
    private string? _countdownText;

    /// <summary>
    /// Ближайшая запись: бейдж на первой карточке «Предстоящих».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCountdown))]
    private bool _isNext;

    /// <summary>
    /// Идёт отмена этой записи: повторное нажатие игнорируется.
    /// </summary>
    [ObservableProperty]
    private bool _isCancelling;

    /// <summary>
    /// По записи уже есть отзыв — в любом статусе модерации.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReview))]
    private bool _hasReview;

    /// <summary>
    /// Перенос доступен: запись активна и ещё не началась.
    /// </summary>
    [ObservableProperty]
    private bool _canReschedule;

    /// <summary>
    /// Заметка барбера строкой карточки; <c>null</c>, если заметки нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBarberNote))]
    private string? _barberNoteText;

    /// <summary>
    /// Заметка клиента строкой карточки; <c>null</c>, если заметки нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomerNote))]
    private string? _customerNoteText;

    /// <summary>
    /// Причина отмены; <c>null</c> у неотменённых записей.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCancellationText))]
    private string? _cancellationText;

    /// <summary>
    /// Карточка барбера уже загрузилась: имя, адрес и пояс настоящие.
    /// </summary>
    [ObservableProperty]
    private bool _isProfileResolved;

    /// <summary>
    /// Показывать ли обратный отсчёт: он есть только у ближайшей записи.
    /// </summary>
    /// <remarks>
    /// Отсчёт считается для каждой строки, но показывается один раз — на первой карточке
    /// «Предстоящих», рядом с бейджем: иначе экран превратился бы в столбец таймеров.
    /// </remarks>
    public bool HasCountdown => IsNext && !string.IsNullOrEmpty(CountdownText);

    /// <summary>
    /// Услуга и салон одной строкой: «Стрижка · Барбершоп на Тверской».
    /// </summary>
    public string ServiceAndSalonText => string.Join(" · ", new[] { ServiceName, SalonName });

    /// <summary>
    /// Вид статуса для оформления бейджа: <c>pending</c>, <c>confirmed</c> и т. д.
    /// </summary>
    public string StatusKind => Status switch
    {
        BookingStatus.Pending => "pending",
        BookingStatus.Confirmed => "confirmed",
        BookingStatus.Cancelled => "cancelled",
        BookingStatus.Completed => "completed",
        BookingStatus.NoShow => "noshow",
        _ => "cancelled",
    };

    /// <summary>
    /// Отмена доступна у заявки и подтверждённого визита — ровно как в домене.
    /// </summary>
    public bool CanCancel => Status is BookingStatus.Pending or BookingStatus.Confirmed;

    /// <summary>
    /// Отзыв доступен только по завершённому визиту без отзыва.
    /// </summary>
    public bool CanReview => Status == BookingStatus.Completed && !HasReview;

    /// <summary>
    /// Показывать ли «Отзыв отправлен» вместо кнопки отзыва.
    /// </summary>
    public bool ShowReviewSent => Status == BookingStatus.Completed && HasReview;

    /// <summary>
    /// Есть ли строка заметки барбера.
    /// </summary>
    public bool HasBarberNote => !string.IsNullOrEmpty(BarberNoteText);

    /// <summary>
    /// Есть ли строка заметки клиента.
    /// </summary>
    public bool HasCustomerNote => !string.IsNullOrEmpty(CustomerNoteText);

    /// <summary>
    /// Есть ли строка причины отмены.
    /// </summary>
    public bool HasCancellationText => !string.IsNullOrEmpty(CancellationText);

    /// <summary>
    /// Применяет карточку барбера: имя, услугу, салон, адрес, пояс и длительность.
    /// </summary>
    /// <remarks>
    /// <c>null</c> означает, что карточку получить не удалось (или услуга выключена, или салон
    /// отвязан). Тогда строка остаётся с подписями-заглушками и временем в UTC: врать чужим
    /// именем хуже, чем показать «Барбер».
    /// </remarks>
    /// <param name="profile">Карточка барбера либо <c>null</c>.</param>
    public void ApplyProfile(BarberProfileResponse? profile)
    {
        var service = profile?.Services.FirstOrDefault(candidate =>
            candidate.BarberServiceId == _booking.BarberServiceId && candidate.IsActive);

        var salon = profile?.Salons.FirstOrDefault(candidate => candidate.SalonId == _booking.SalonId);

        _timeZone = SalonClock.TryResolve(salon?.TimeZoneId);

        BarberName = Fallback(profile?.DisplayName, BookingText.BarberFallback);
        ServiceName = Fallback(service?.Name, BookingText.ServiceFallback);
        SalonName = Fallback(salon?.Name, BookingText.SalonFallback);
        CityAddress = BuildCityAddress(salon?.City, salon?.Address);
        DurationText = BarberText.Duration(service?.DurationMinutes ?? DurationMinutes);
        IsProfileResolved = profile is not null;

        RecomputeTimes(_now);
    }

    /// <summary>
    /// Применяет новое время после переноса: меняются только границы визита.
    /// </summary>
    /// <param name="slot">Слот переноса, который подтвердил сервер.</param>
    public void ApplyRescheduled(BarberSlotResponse slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        _startAt = slot.StartAt;
        _endAt = slot.EndAt;

        RecomputeTimes(_now);
    }

    /// <summary>
    /// Применяет новое время после переноса и пересчитывает тексты на заданный момент.
    /// </summary>
    /// <param name="slot">Слот переноса, который подтвердил сервер.</param>
    /// <param name="now">Текущий момент для отсчёта и правил действий.</param>
    public void ApplyRescheduled(BarberSlotResponse slot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(slot);

        _startAt = slot.StartAt;
        _endAt = slot.EndAt;

        RecomputeTimes(now);
    }

    /// <summary>
    /// Отмечает, что отзыв отправлен: кнопка навсегда становится «Отзыв отправлен».
    /// </summary>
    public void ApplyReviewed() => HasReview = true;

    /// <summary>
    /// Пересчитывает время, дату, отсчёт и доступность переноса.
    /// </summary>
    /// <remarks>
    /// Вызывается минутным таймером страницы: «через 2 часа» и «перенести ещё можно» устаревают
    /// без запросов к сети.
    /// </remarks>
    /// <param name="now">Текущий момент.</param>
    public void RecomputeTimes(DateTimeOffset now)
    {
        _now = now;

        TimeText = SalonClock.FormatTime(_startAt, _timeZone);
        DateText = BookingText.ShortDate(StartDate);
        MomentText = BookingText.Moment(_startAt, _timeZone);
        CountdownText = BookingText.Countdown(_startAt, now);
        CanReschedule = CanCancel && _startAt > now;
    }

    /// <summary>
    /// Длительность визита по границам брони: запасной вариант, когда услуги нет.
    /// </summary>
    private int DurationMinutes => (int)Math.Round((_endAt - _startAt).TotalMinutes);

    /// <summary>
    /// Подпись из карточки либо заглушка, если значения нет.
    /// </summary>
    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    /// <summary>
    /// Собирает «Город, адрес», опуская отсутствующие части.
    /// </summary>
    private static string BuildCityAddress(string? city, string? address)
    {
        var parts = new[] { city, address }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim());

        return string.Join(", ", parts);
    }
}
