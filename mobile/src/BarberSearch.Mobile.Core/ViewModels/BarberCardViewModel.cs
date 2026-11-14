using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Bookings;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Карточка барбера и запись: шапка, услуги, свободное время и панель брони.
/// </summary>
/// <remarks>
/// <para>
/// Один ViewModel на экран: три состояния загрузки карточки (индикатор, контент, ошибка),
/// отдельное «не найден», выбор услуги/салона/даты и отправка брони. Экрана «Записи» пока
/// нет, поэтому после успеха панель остаётся на месте с подтверждением, а не уводит клиента
/// в несуществующий раздел.
/// </para>
/// <para>
/// Время в ответах — UTC, но показывается всегда в поясе салона (<see cref="SalonClock"/>):
/// часам телефона здесь не место. Тексты ошибок взяты дословно из веба
/// (<c>frontend/src/hooks/use-create-booking.ts</c>, <c>frontend/src/lib/errors.ts</c>),
/// ветвление — только по коду состояния и публичным кодам, английский <c>detail</c> не
/// показывается никогда.
/// </para>
/// <para>
/// Снимок выбранного слота (<see cref="BookingSnapshot"/>) неизменяем: сводка и запрос читают
/// только его, поэтому перезапрос сетки после успеха не подменяет подтверждение.
/// Ключ идемпотентности создаётся на попытку и переиспользуется всеми повторами.
/// </para>
/// </remarks>
public sealed partial class BarberCardViewModel : ObservableObject
{
    /// <summary>
    /// Предел комментария клиента: в базе <c>text</c>, серверного ограничения нет, поэтому
    /// граница повторяет веб.
    /// </summary>
    public const int MaxNoteLength = 500;

    /// <summary>
    /// Горизонт записи в днях: сервер разрешает 90, интерфейс повторяет веб — 30.
    /// </summary>
    private const int SlotHorizonDays = 30;

    private const string NoReviewsTextValue = "Пока нет отзывов";
    private const string VenueFallbackText = "Салон";
    private const string NotWorkingDayValue = "В этот день барбер не работает.";
    private const string NoFreeTimeValue = "Свободного времени на этот день нет.";
    private const string ProfileLoadFailedValue = "Не удалось загрузить профиль барбера.";
    private const string SlotsLoadFailedValue = "Не удалось загрузить свободное время.";
    private const string RateLimitValue = "Слишком много запросов. Подождите минуту и повторите.";
    private const string NetworkValue = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string BookingFailedValue = "Не удалось записаться.";
    private const string SessionExpiredValue = "Сессия истекла — войдите снова.";
    private const string SignInToBookValue = "Войдите, чтобы подтвердить запись";
    private const string OverlapValue = "Это время только что заняли. Выберите другое.";
    private const string LockTimeoutValue = "Не удалось занять время, попробуйте ещё раз.";
    private const string StartAtInPastValue = "Это время уже прошло. Обновите свободное время.";
    private const string StartAtTooFarValue = "Это время слишком далеко. Обновите свободное время.";
    private const string ServiceGoneValue = "Эта услуга или салон больше недоступны.";
    private const string RetryValue = "Повторить";
    private const string BookValue = "Записаться";
    private const string SendingValue = "Отправляем…";
    private const string ChangeTimeValue = "Выбрать другое время";
    private const string RefreshSlotsValue = "Обновить свободное время";
    private const string TryAgainValue = "Попробовать снова";

    // Коды создания брони живут в Application/Domain и на клиенте сравниваются строкой,
    // как auth.login.* в ProfileViewModel: в Contracts их нет.
    private const string IdempotencyKeyConflictCode = "booking.create.idempotencyKeyConflict";
    private const string StartAtInPastCode = "booking.create.startAt.inPast";
    private const string StartAtTooFarCode = "booking.create.startAt.tooFar";

    private readonly IBarberSearchApi _api;
    private readonly BarberSearchApiOptions _apiOptions;
    private readonly IBookingsApi _bookingsApi;
    private readonly TokenSession _session;
    private readonly TimeProvider _timeProvider;

    private Guid _barberProfileId;
    private Guid? _loadedProfileId;
    private BarberProfileResponse? _profile;
    private string _primaryTimeZoneId = string.Empty;
    private string _latePolicy = string.Empty;

    private CancellationTokenSource? _slotsCts;
    private int _slotsGeneration;
    private bool _hasSlotsResponse;
    private DateOnly _slotsDate;
    private string _slotsTimeZoneId = string.Empty;
    private Guid _slotsSalonId;
    private Guid? _slotsBarberServiceId;

    private BookingSnapshot? _snapshot;
    private BarberSlotOption? _selectedSlot;
    private string? _idempotencyKey;
    private BookingFailureKind _failureKind;
    private bool _sessionExpired;

    // Границы календаря в календарных датах салона; DatePicker получает их же в DateTime.
    private DateOnly _minDate;
    private DateOnly _maxDate;

    /// <summary>
    /// Создаёт карточку барбера.
    /// </summary>
    /// <param name="api">Публичные чтения: карточка, слоты и отзывы.</param>
    /// <param name="apiOptions">Адрес API: от него зависят абсолютные адреса медиа.</param>
    /// <param name="bookingsApi">Создание брони через клиент с Bearer.</param>
    /// <param name="session">Сессия — источник правды о входе.</param>
    /// <param name="timeProvider">Текущий момент: «сегодня» в поясе салона проверяется тестом.</param>
    public BarberCardViewModel(
        IBarberSearchApi api,
        BarberSearchApiOptions apiOptions,
        IBookingsApi bookingsApi,
        TokenSession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(apiOptions);
        ArgumentNullException.ThrowIfNull(bookingsApi);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _api = api;
        _apiOptions = apiOptions;
        _bookingsApi = bookingsApi;
        _session = session;
        _timeProvider = timeProvider;

        Reviews = new BarberReviewsSection(api);
    }

    /// <summary>
    /// Идёт загрузка карточки.
    /// </summary>
    /// <remarks>
    /// Стартует взведённым: до первого ответа на экране индикатор, а не пустая шапка.
    /// </remarks>
    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>
    /// Профиль скрыт, не подтверждён или ссылка устарела.
    /// </summary>
    [ObservableProperty]
    private bool _isNotFound;

    /// <summary>
    /// Текст ошибки загрузки карточки; <c>null</c>, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    private string? _loadErrorText;

    /// <summary>
    /// Идёт запрос сетки слотов.
    /// </summary>
    [ObservableProperty]
    private bool _isSlotsLoading;

    /// <summary>
    /// Текст ошибки загрузки свободного времени; <c>null</c>, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    private string? _slotsErrorText;

    /// <summary>
    /// Почему свободного времени нет: нерабочий день или всё занято.
    /// </summary>
    [ObservableProperty]
    private string? _emptySlotsText;

    /// <summary>
    /// Есть ли действующая сессия: без неё запись не отправляется.
    /// </summary>
    [ObservableProperty]
    private bool _isAuthenticated;

    /// <summary>
    /// Текст ошибки отправки брони; <c>null</c>, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    private string? _bookingErrorText;

    /// <summary>
    /// Идёт отправка брони: кнопка называется «Отправляем…», повтор игнорируется.
    /// </summary>
    [ObservableProperty]
    private bool _isBooking;

    /// <summary>
    /// Бронь создана: панель показывает подтверждение вместо формы.
    /// </summary>
    [ObservableProperty]
    private bool _isBooked;

    /// <summary>
    /// Комментарий клиента; обрезается по краям при отправке.
    /// </summary>
    [ObservableProperty]
    private string _customerNote = string.Empty;

    /// <summary>
    /// Выбранная услуга; <c>null</c> означает пункт «Любая услуга».
    /// </summary>
    [ObservableProperty]
    private BarberServiceOption? _selectedService;

    /// <summary>
    /// Выбранный салон: от него зависят пояс, «сегодня» и сетка.
    /// </summary>
    [ObservableProperty]
    private BarberSalonOption? _selectedSalon;

    /// <summary>
    /// Выбранная календарная дата салона.
    /// </summary>
    /// <remarks>
    /// Хранится как <see cref="DateTime"/>, потому что столько ждёт <c>DatePicker</c>; время
    /// суток не используется никогда — в запрос уходят только компоненты даты.
    /// </remarks>
    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    /// <summary>
    /// Минимальная дата календаря: сегодня в поясе выбранного салона.
    /// </summary>
    [ObservableProperty]
    private DateTime _minimumDate = DateTime.Today;

    /// <summary>
    /// Максимальная дата календаря: сегодня + 30 дней.
    /// </summary>
    [ObservableProperty]
    private DateTime _maximumDate = DateTime.Today.AddDays(SlotHorizonDays);

    /// <summary>
    /// Услуги в прайсе карточки (только активные).
    /// </summary>
    public ObservableCollection<BarberServiceOption> Services { get; } = [];

    /// <summary>
    /// Пункты выбора услуги: «Любая услуга» плюс активные услуги прайса.
    /// </summary>
    public ObservableCollection<BarberServiceOption> ServiceOptions { get; } = [];

    /// <summary>
    /// Салоны барбера вместе с поясами.
    /// </summary>
    public ObservableCollection<BarberSalonOption> Salons { get; } = [];

    /// <summary>
    /// Свободные слоты выбранного дня.
    /// </summary>
    public ObservableCollection<BarberSlotOption> Slots { get; } = [];

    /// <summary>
    /// Фотографии портфолио в порядке сервера: он уже отсортировал их по <c>sortOrder</c>.
    /// </summary>
    public ObservableCollection<BarberPhotoOption> Photos { get; } = [];

    /// <summary>
    /// Чипы языков общения в порядке сервера.
    /// </summary>
    public ObservableCollection<string> LanguageChips { get; } = [];

    /// <summary>
    /// Списки раздела «О мастере» в веб-порядке; пустые списки сюда не попадают.
    /// </summary>
    public ObservableCollection<BarberInfoList> AboutLists { get; } = [];

    /// <summary>
    /// Специализации из справочника в порядке сервера.
    /// </summary>
    public ObservableCollection<BarberSpecializationOption> Specializations { get; } = [];

    /// <summary>
    /// Чипы способов оплаты в порядке сервера.
    /// </summary>
    public ObservableCollection<string> PaymentChips { get; } = [];

    /// <summary>
    /// Секция отзывов: своя пагинация, свои ошибки и «Повторить».
    /// </summary>
    public BarberReviewsSection Reviews { get; }

    /// <summary>
    /// Есть ли загруженная карточка.
    /// </summary>
    public bool HasProfile => _profile is not null;

    /// <summary>
    /// Видно ли содержимое карточки: профиль загружен и не «не найден».
    /// </summary>
    public bool IsContentVisible => _profile is not null && !IsNotFound;

    /// <summary>
    /// Есть ли ошибка загрузки карточки, которую надо показать.
    /// </summary>
    public bool HasLoadError => LoadErrorText is not null;

    /// <summary>
    /// Имя барбера.
    /// </summary>
    public string DisplayName => _profile?.DisplayName ?? string.Empty;

    /// <summary>
    /// Грейд по-русски.
    /// </summary>
    public string GradeText => _profile is null ? string.Empty : BarberText.GradeText(_profile.Grade);

    /// <summary>
    /// Рейтинг и отзывы одной строкой; без рейтинга — «Пока нет отзывов».
    /// </summary>
    public string RatingText => _profile is null
        ? string.Empty
        : _profile.Rating is null
            ? NoReviewsTextValue
            : string.Concat(
                BarberText.Rating(_profile.Rating),
                " · ",
                BarberText.Reviews(_profile.ReviewsCount));

    /// <summary>
    /// Место приёма основного салона.
    /// </summary>
    public string PlaceText
    {
        get
        {
            var salon = Salons.FirstOrDefault(item => item.IsPrimary) ?? Salons.FirstOrDefault();

            return salon is null
                ? "Салон не указан"
                : BarberText.Place(salon.Kind, salon.Name, salon.City);
        }
    }

    /// <summary>
    /// Есть ли активные услуги в прайсе.
    /// </summary>
    public bool HasServices => Services.Count > 0;

    /// <summary>
    /// Нужно ли показать, что прайс пуст.
    /// </summary>
    public bool HasNoServices => Services.Count == 0;

    /// <summary>
    /// Есть ли у барбера хотя бы один салон: без места приёма записываться некуда.
    /// </summary>
    public bool HasSalons => Salons.Count > 0;

    /// <summary>
    /// Нужно ли показать, что барбер не указал место приёма.
    /// </summary>
    public bool HasNoSalons => Salons.Count == 0;

    /// <summary>
    /// Ровно один салон: вместо выбора места показывается его название.
    /// </summary>
    public bool HasSingleSalon => Salons.Count == 1;

    /// <summary>
    /// Больше одного салона: только тогда показывается выбор места.
    /// </summary>
    public bool HasMultipleSalons => Salons.Count > 1;

    /// <summary>
    /// Абсолютный адрес аватара: вариант <c>md</c> (192 px) на 64 pt при тройной плотности.
    /// </summary>
    /// <remarks>
    /// <c>null</c> — аватара нет или он скрыт администратором. Управления аватаром на
    /// карточке нет: она только читает публичное поле.
    /// </remarks>
    public string? AvatarImageUrl => string.IsNullOrWhiteSpace(_profile?.AvatarUrl)
        ? null
        : MediaUrl.Resolve(_apiOptions.BaseAddress, MediaUrl.Resize(_profile!.AvatarUrl!, "md"));

    /// <summary>
    /// Есть ли аватар для показа картинкой.
    /// </summary>
    public bool HasAvatar => AvatarImageUrl is not null;

    /// <summary>
    /// Показывать ли инициалы: заглушка вместо картинки.
    /// </summary>
    public bool ShowInitials => !HasAvatar;

    /// <summary>
    /// Инициалы мастера для заглушки аватара.
    /// </summary>
    public string Initials => BarberText.Initials(DisplayName);

    /// <summary>
    /// Подтверждён ли профиль администратором.
    /// </summary>
    public bool IsVerified => _profile?.IsVerified ?? false;

    /// <summary>
    /// Стаж в шапке: «Стаж: 8 лет».
    /// </summary>
    public string ExperienceText => _profile is null
        ? string.Empty
        : string.Concat("Стаж: ", BarberText.Experience(_profile.YearsOfExperience));

    /// <summary>
    /// Есть ли языки общения.
    /// </summary>
    public bool HasLanguages => LanguageChips.Count > 0;

    /// <summary>
    /// Показывать ли раздел «О мастере»: нужен хотя бы один из его блоков.
    /// </summary>
    public bool HasAbout => !string.IsNullOrWhiteSpace(_profile?.Bio)
        || HasLanguages
        || AboutLists.Count > 0;

    /// <summary>
    /// Описание мастера как есть, с переносами строк; пусто, если его нет.
    /// </summary>
    public string Bio => _profile?.Bio?.Trim() ?? string.Empty;

    /// <summary>
    /// Есть ли описание «о себе».
    /// </summary>
    public bool HasBio => Bio.Length > 0;

    /// <summary>
    /// Есть ли чем заполнить «Специализации».
    /// </summary>
    public bool HasSpecializations => Specializations.Count > 0;

    /// <summary>
    /// Есть ли способы оплаты.
    /// </summary>
    public bool HasPayments => PaymentChips.Count > 0;

    /// <summary>
    /// Правило опозданий: «Опоздания: 10 minutes»; пусто, если правила нет.
    /// </summary>
    public string LatePolicyText => _latePolicy.Length == 0
        ? string.Empty
        : string.Concat("Опоздания: ", _latePolicy);

    /// <summary>
    /// Есть ли правило опозданий: пустая строка и пробелы секции не создают.
    /// </summary>
    public bool HasLatePolicy => _latePolicy.Length > 0;

    /// <summary>
    /// Есть ли фотографии портфолио: без них секции «Работы» нет.
    /// </summary>
    public bool HasPhotos => Photos.Count > 0;

    /// <summary>
    /// Есть ли ошибка загрузки слотов.
    /// </summary>
    public bool HasSlotsError => SlotsErrorText is not null;

    /// <summary>
    /// Нужно ли показать причину пустой сетки.
    /// </summary>
    public bool HasEmptySlots => EmptySlotsText is not null;

    /// <summary>
    /// Видна ли подсказка «выберите услугу»: слоты показаны, но записаться нельзя.
    /// </summary>
    public bool ShowSlotsHint => _hasSlotsResponse && !CanSelectSlots;

    /// <summary>
    /// Можно ли выбирать слот: услуга выбрана и сервер вернул её <c>barberServiceId</c>.
    /// </summary>
    /// <remarks>
    /// В режиме «Любая услуга» сетка видна, но пара для брони неизвестна — сервер отдаёт
    /// <c>barberServiceId = null</c>. Поэтому выбор слота в этом режиме ничего не делает.
    /// </remarks>
    public bool CanSelectSlots => _slotsBarberServiceId is not null && !IsSlotsLoading;

    /// <summary>
    /// Календарная дата выбранного дня.
    /// </summary>
    public DateOnly SelectedDateOnly => DateOnly.FromDateTime(SelectedDate);

    /// <summary>
    /// Выбранная дата для показа: «воскресенье, 15 ноября».
    /// </summary>
    public string SelectedDateText => SalonClock.FormatDate(SelectedDateOnly);

    /// <summary>
    /// Видна ли панель записи: есть снимок слота, ошибка отправки или подтверждение.
    /// </summary>
    public bool IsBookingPanelVisible => _snapshot is not null || IsBooked || BookingErrorText is not null;

    /// <summary>
    /// Видна ли сводка выбранного слота.
    /// </summary>
    public bool IsSummaryVisible => _snapshot is not null;

    /// <summary>
    /// Сводка: «воскресенье, 15 ноября, 18:30–19:15».
    /// </summary>
    public string SummaryDateText => _snapshot is null
        ? string.Empty
        : string.Concat(SalonClock.FormatDate(_snapshot.Date), ", ", _snapshot.TimeRangeText);

    /// <summary>
    /// Сводка: услуга и салон.
    /// </summary>
    public string SummaryServiceText => _snapshot is null
        ? string.Empty
        : string.Concat(_snapshot.ServiceName, " · ", VenueName);

    /// <summary>
    /// Сводка: цена на момент выбора.
    /// </summary>
    public string SummaryPriceText => _snapshot is null
        ? string.Empty
        : BarberText.Price(_snapshot.Price);

    /// <summary>
    /// Видна ли просьба войти вместо формы записи.
    /// </summary>
    public bool IsSignInVisible => _snapshot is not null && !IsAuthenticated && !IsBooked;

    /// <summary>
    /// Текст просьбы войти: обычный или «сессия истекла» после <c>401</c>.
    /// </summary>
    public string SignInText => _sessionExpired ? SessionExpiredValue : SignInToBookValue;

    /// <summary>
    /// Видна ли форма записи: сводка уже есть, вход выполнен.
    /// </summary>
    public bool IsBookFormVisible => _snapshot is not null && IsAuthenticated && !IsBooked;

    /// <summary>
    /// Видна ли ошибка отправки брони.
    /// </summary>
    public bool IsBookingErrorVisible => BookingErrorText is not null;

    /// <summary>
    /// Видна ли основная кнопка отправки.
    /// </summary>
    public bool IsSubmitVisible => IsBookFormVisible
        && _failureKind is BookingFailureKind.None
            or BookingFailureKind.Retry
            or BookingFailureKind.KeyConflict;

    /// <summary>
    /// Можно ли нажимать основные кнопки.
    /// </summary>
    public bool IsSubmitEnabled => !IsBooking;

    /// <summary>
    /// Надпись на основной кнопке: от неё зависит, повтор это или новая попытка.
    /// </summary>
    public string SubmitButtonText => IsBooking
        ? SendingValue
        : _failureKind switch
        {
            BookingFailureKind.Retry => RetryValue,
            BookingFailureKind.KeyConflict => TryAgainValue,
            _ => BookValue,
        };

    /// <summary>
    /// Видна ли второстепенная кнопка («Выбрать другое время» / «Обновить свободное время»).
    /// </summary>
    public bool IsBookingActionsVisible => IsBookingPanelVisible && !IsBooked && !IsBooking;

    /// <summary>
    /// Надпись на второстепенной кнопке.
    /// </summary>
    public string SecondaryButtonText =>
        _failureKind == BookingFailureKind.Unavailable ? RefreshSlotsValue : ChangeTimeValue;

    /// <summary>
    /// Видно ли подтверждение созданной брони.
    /// </summary>
    public bool IsSuccessVisible => IsBooked;

    /// <summary>
    /// Можно ли начать новую запись после подтверждения.
    /// </summary>
    public bool IsBookAgainVisible => IsBooked && !IsBooking;

    /// <summary>
    /// Сколько символов комментария ещё помещается.
    /// </summary>
    public string NoteHintText => string.Concat(
        "Осталось ",
        (MaxNoteLength - CustomerNote.Length).ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Открывает карточку: проверяет сессию, грузит профиль и слоты один раз на <paramref name="barberId"/>.
    /// </summary>
    /// <remarks>
    /// Повторное появление экрана (возврат с вкладки «Профиль») только перечитывает признак
    /// входа: карточка и выбранный слот остаются на месте, сеть не трогается. Отсутствующий
    /// или невалидный идентификатор даёт состояние «не найден» без запроса.
    /// </remarks>
    /// <param name="barberId">Идентификатор профиля из маршрута; <c>null</c> — ссылка битая.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task InitializeAsync(Guid? barberId, CancellationToken cancellationToken = default)
    {
        await _session.EnsureLoadedAsync(cancellationToken);

        IsAuthenticated = _session.IsAuthenticated;

        if (IsAuthenticated)
        {
            _sessionExpired = false;
        }

        if (barberId is null)
        {
            IsNotFound = true;
            IsLoading = false;

            return;
        }

        if (_loadedProfileId == barberId)
        {
            return;
        }

        await LoadProfileAsync(barberId.Value, cancellationToken);
    }

    /// <summary>
    /// Меняет услугу и перезапрашивает сетку: у услуги своя длительность и шаг.
    /// </summary>
    /// <param name="service">Новый пункт выбора; <c>null</c> игнорируется.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task SelectServiceAsync(
        BarberServiceOption? service,
        CancellationToken cancellationToken = default)
    {
        if (service is null || _profile is null || ReferenceEquals(service, SelectedService))
        {
            return;
        }

        SelectedService = service;
        ResetBookingState();

        await LoadSlotsAsync(cancellationToken);
    }

    /// <summary>
    /// Меняет салон: пересчитывает «сегодня» в его поясе и перезапрашивает сетку.
    /// </summary>
    /// <remarks>
    /// В другом поясе выбранная дата могла оказаться в прошлом — тогда берётся сегодня
    /// нового салона, как в вебе.
    /// </remarks>
    /// <param name="salon">Новый салон; <c>null</c> игнорируется.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task SelectSalonAsync(
        BarberSalonOption? salon,
        CancellationToken cancellationToken = default)
    {
        if (salon is null || _profile is null || ReferenceEquals(salon, SelectedSalon))
        {
            return;
        }

        SelectedSalon = salon;
        UpdateDateBounds();

        if (SelectedDateOnly < _minDate || SelectedDateOnly > _maxDate)
        {
            SelectedDate = MinimumDate;
        }

        ResetBookingState();

        await LoadSlotsAsync(cancellationToken);
    }

    /// <summary>
    /// Меняет дату и перезапрашивает сетку.
    /// </summary>
    /// <param name="date">Новая дата из календаря.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task SelectDateAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var onlyDate = DateOnly.FromDateTime(date);

        if (_profile is null || onlyDate == SelectedDateOnly)
        {
            return;
        }

        SelectedDate = date.Date;

        if (onlyDate < _minDate || onlyDate > _maxDate)
        {
            // Календарь уже ограничен, но защищаемся: дату вне горизонта сервер отвергнет.
            return;
        }

        ResetBookingState();

        await LoadSlotsAsync(cancellationToken);
    }

    /// <summary>
    /// Повторяет загрузку карточки после ошибки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryLoadAsync(CancellationToken cancellationToken)
    {
        if (_barberProfileId == Guid.Empty)
        {
            return;
        }

        await LoadProfileAsync(_barberProfileId, cancellationToken);
    }

    /// <summary>
    /// Повторяет загрузку свободного времени после ошибки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetrySlotsAsync(CancellationToken cancellationToken)
    {
        SlotsErrorText = null;

        await LoadSlotsAsync(cancellationToken);
    }

    /// <summary>
    /// Выбирает слот и делает неизменяемый снимок будущей брони.
    /// </summary>
    /// <remarks>
    /// Без выбранной услуги тап ничего не делает: паре «услуга + длительность» взяться
    /// неоткуда, а отправлять бронь без <c>barberServiceId</c> сервер не станет.
    /// </remarks>
    /// <param name="slot">Слот, по которому нажали.</param>
    [RelayCommand(CanExecute = nameof(CanSelectSlot))]
    private void SelectSlot(BarberSlotOption? slot)
    {
        if (slot is null || !CanSelectSlots || SelectedService is null || ReferenceEquals(slot, _selectedSlot))
        {
            return;
        }

        if (_selectedSlot is not null)
        {
            _selectedSlot.IsSelected = false;
        }

        slot.IsSelected = true;
        _selectedSlot = slot;
        _snapshot = new BookingSnapshot(
            _slotsDate,
            _slotsTimeZoneId,
            _slotsSalonId,
            _slotsBarberServiceId!.Value,
            slot.StartAt,
            slot.EndAt,
            SelectedService.Name,
            SelectedService.Price,
            slot.TimeText);
        _idempotencyKey = NewKey();
        _failureKind = BookingFailureKind.None;
        _sessionExpired = false;
        BookingErrorText = null;
        IsBooked = false;
        CustomerNote = string.Empty;

        RaiseBookingChanged();
    }

    /// <summary>
    /// Отправляет бронь по выбранному снимку.
    /// </summary>
    /// <remarks>
    /// Пока запрос в полёте, повтор игнорируется: двойное нажатие не создаёт вторую бронь.
    /// Без сессии запрос не уходит вовсе — панель вместо формы просит войти. После
    /// <c>401</c> обработчик уже погасил пару, поэтому панель сразу переходит в то же
    /// состояние с текстом «Сессия истекла — войдите снова.».
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task BookAsync(CancellationToken cancellationToken)
    {
        if (IsBooking || _snapshot is null || !IsAuthenticated)
        {
            return;
        }

        var snapshot = _snapshot;

        if (_failureKind == BookingFailureKind.KeyConflict)
        {
            // Конфликт ключа не лечится повтором: нужен новый ключ, иначе сервер снова
            // ответит тем же конфликтом.
            _idempotencyKey = NewKey();
            _failureKind = BookingFailureKind.None;
        }

        var key = _idempotencyKey ??= NewKey();

        IsBooking = true;
        BookingErrorText = null;
        _failureKind = BookingFailureKind.None;
        RaiseBookingChanged();

        try
        {
            await _bookingsApi.CreateBookingAsync(
                new CreateBookingRequest(
                    _barberProfileId,
                    snapshot.BarberServiceId,
                    snapshot.SalonId,
                    snapshot.StartAt,
                    snapshot.EndAt,
                    key,
                    NormalizedNote()),
                cancellationToken);

            await ShowBookedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Экран закрыли — ответ уже никому не нужен.
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            ApplyFailure(NetworkValue, BookingFailureKind.Retry);
        }
        catch (BarberSearchApiException exception)
        {
            await ApplyBookingFailureAsync(exception, cancellationToken);
        }
        catch (HttpRequestException)
        {
            ApplyFailure(NetworkValue, BookingFailureKind.Retry);
        }
        catch (InvalidOperationException)
        {
            // Пустой ответ при 201: контракт нарушен, повторять вслепую нечего.
            ApplyFailure(BookingFailedValue, BookingFailureKind.Fatal);
        }
        finally
        {
            IsBooking = false;
            RaiseBookingChanged();
        }
    }

    /// <summary>
    /// Закрывает панель записи и обновляет свободное время.
    /// </summary>
    /// <remarks>
    /// Тот же путь у «Выбрать другое время» и «Обновить свободное время»: после ошибки,
    /// которая лечится только новым временем, снимок и ключ уже сброшены.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task ChangeTimeAsync(CancellationToken cancellationToken)
    {
        ResetBookingState();

        await LoadSlotsAsync(cancellationToken);
    }

    /// <summary>
    /// Начинает новую запись после подтверждения.
    /// </summary>
    /// <remarks>
    /// Сетка уже перезапрошена после успеха, поэтому здесь достаточно закрыть панель:
    /// занятого слота в ней быть не может.
    /// </remarks>
    [RelayCommand]
    private void BookAgain() => ResetBookingState();

    /// <summary>
    /// Выбирает слот, если услуга уже выбрана.
    /// </summary>
    private bool CanSelectSlot(BarberSlotOption? slot) => CanSelectSlots;

    partial void OnLoadErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasLoadError));

    partial void OnIsNotFoundChanged(bool value) => OnPropertyChanged(nameof(IsContentVisible));

    partial void OnSlotsErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasSlotsError));

    partial void OnEmptySlotsTextChanged(string? value) => OnPropertyChanged(nameof(HasEmptySlots));

    partial void OnSelectedDateChanged(DateTime value) => OnPropertyChanged(nameof(SelectedDateText));

    partial void OnCustomerNoteChanged(string value) => OnPropertyChanged(nameof(NoteHintText));

    partial void OnIsSlotsLoadingChanged(bool value) => NotifySlotsState();

    partial void OnIsBookingChanged(bool value) => RaiseBookingChanged();

    partial void OnIsBookedChanged(bool value) => RaiseBookingChanged();

    partial void OnIsAuthenticatedChanged(bool value) => RaiseBookingChanged();

    partial void OnBookingErrorTextChanged(string? value) => RaiseBookingChanged();

    /// <summary>
    /// Грузит карточку и сразу за ней сетку слотов.
    /// </summary>
    /// <remarks>
    /// Идентификатор запоминается до запроса: повторное появление экрана после ошибки не
    /// должно само повторять сетевой запрос — для этого есть «Повторить».
    /// </remarks>
    private async Task LoadProfileAsync(Guid barberProfileId, CancellationToken cancellationToken)
    {
        _barberProfileId = barberProfileId;
        _loadedProfileId = barberProfileId;
        _profile = null;
        IsLoading = true;
        IsNotFound = false;
        LoadErrorText = null;

        RaiseProfileChanged();

        try
        {
            var profile = await _api.GetBarberProfileAsync(barberProfileId, cancellationToken);

            _profile = profile;
            ApplyProfile(profile);
            RaiseProfileChanged();

            // Карточка уже видна: индикатор относится к профилю, а не к сетке слотов.
            IsLoading = false;

            // Отзывы идут параллельно слотам и не задерживают показ карточки: ошибка секции
            // не должна ломать ни профиль, ни запись.
            var reviewsTask = Reviews.InitializeAsync(
                profile.Id,
                _primaryTimeZoneId,
                profile.ReviewsCount,
                RatingText,
                cancellationToken);

            await LoadSlotsAsync(cancellationToken);
            await reviewsTask;
        }
        catch (OperationCanceledException)
        {
            // Экран закрыли — состояние не меняем.
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            IsNotFound = true;
        }
        catch (BarberSearchApiException exception)
        {
            LoadErrorText = DescribeReadFailure(exception, ProfileLoadFailedValue);
        }
        catch (HttpRequestException)
        {
            LoadErrorText = NetworkValue;
        }
        catch (InvalidOperationException)
        {
            // Успешный ответ с пустым телом: сервер нарушил контракт, но экран не падает.
            LoadErrorText = ProfileLoadFailedValue;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Раскладывает загруженный профиль по коллекциям и выбирает начальное состояние.
    /// </summary>
    /// <remarks>
    /// Начало, как в вебе: основной салон, «Любая услуга» и сегодняшняя дата в поясе салона.
    /// Выключенные услуги в прайс не попадают: сервер отдаёт их вместе с активными, но
    /// записаться на них нельзя.
    /// </remarks>
    private void ApplyProfile(BarberProfileResponse profile)
    {
        Services.Clear();
        ServiceOptions.Clear();
        Salons.Clear();
        Photos.Clear();
        LanguageChips.Clear();
        AboutLists.Clear();
        Specializations.Clear();
        PaymentChips.Clear();

        _latePolicy = profile.LatePolicy?.Trim() ?? string.Empty;

        // Пояс отзывов — пояс основного салона: отзыв относится к барберу, а не к текущему
        // выбору в «Свободном времени».
        var primarySalon = profile.Salons.FirstOrDefault(salon => salon.IsPrimary)
            ?? (profile.Salons.Count > 0 ? profile.Salons[0] : null);

        _primaryTimeZoneId = primarySalon?.TimeZoneId ?? string.Empty;

        foreach (var service in profile.Services.Where(service => service.IsActive))
        {
            var option = BarberServiceOption.From(service);

            Services.Add(option);
            ServiceOptions.Add(option);
        }

        ServiceOptions.Insert(0, BarberServiceOption.AnyService());

        foreach (var salon in profile.Salons)
        {
            Salons.Add(BarberSalonOption.From(salon));
        }

        // Номер кадра — порядок сервера: карточка не пересортировывает портфолио.
        for (var index = 0; index < profile.Photos.Count; index++)
        {
            Photos.Add(BarberPhotoOption.From(
                profile.Photos[index],
                _apiOptions.BaseAddress,
                profile.DisplayName,
                index + 1));
        }

        foreach (var language in profile.Languages)
        {
            LanguageChips.Add(BarberText.LanguageText(language));
        }

        AddInfoList("Дополнительные услуги", profile.AdditionalServices);
        AddInfoList("Сертификаты и обучение", profile.CertificatesAndTraining);
        AddInfoList("Достижения", profile.Achievements);
        AddInfoList("Особенности", profile.Goodies);
        AddInfoList("Формат общения", profile.CommunicationFormat);

        foreach (var specialization in profile.Specializations)
        {
            Specializations.Add(BarberSpecializationOption.From(specialization));
        }

        foreach (var paymentMethod in profile.PaymentMethods)
        {
            PaymentChips.Add(BarberText.PaymentText(paymentMethod));
        }

        SelectedSalon = Salons.FirstOrDefault(salon => salon.IsPrimary) ?? Salons.FirstOrDefault();
        SelectedService = ServiceOptions[0];
        UpdateDateBounds();
        SelectedDate = MinimumDate;

        RaiseProfileChanged();
    }

    /// <summary>
    /// Добавляет список «О мастере», если в нём есть хоть одна непустая строка.
    /// </summary>
    private void AddInfoList(string title, IReadOnlyList<string> items)
    {
        var list = BarberInfoList.Create(title, items);

        if (list is not null)
        {
            AboutLists.Add(list);
        }
    }

    /// <summary>
    /// Пересчитывает границы календаря в поясе выбранного салона.
    /// </summary>
    private void UpdateDateBounds()
    {
        var timeZone = SalonClock.TryResolve(SelectedSalon?.TimeZoneId);
        var today = SalonClock.Today(timeZone, _timeProvider.GetUtcNow());

        _minDate = today;
        _maxDate = today.AddDays(SlotHorizonDays);
        MinimumDate = _minDate.ToDateTime(TimeOnly.MinValue);
        MaximumDate = _maxDate.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>
    /// Грузит сетку слотов для текущего выбора.
    /// </summary>
    /// <remarks>
    /// В полёте всегда один запрос: новый отменяет предыдущий, а устаревший ответ
    /// отбрасывается по номеру поколения. Старый список не очищается до ответа, чтобы
    /// сетка не мигала; на время запроса выбор слотов выключен.
    /// </remarks>
    private async Task LoadSlotsAsync(CancellationToken cancellationToken)
    {
        if (_profile is null || SelectedSalon is null)
        {
            _hasSlotsResponse = false;
            _slotsBarberServiceId = null;
            Slots.Clear();
            EmptySlotsText = null;
            NotifySlotsState();

            return;
        }

        var generation = ++_slotsGeneration;

        _slotsCts?.Cancel();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _slotsCts = cts;

        IsSlotsLoading = true;
        SlotsErrorText = null;
        EmptySlotsText = null;
        _hasSlotsResponse = false;
        _slotsBarberServiceId = null;
        NotifySlotsState();

        try
        {
            var response = await _api.GetSlotsAsync(
                _barberProfileId,
                SelectedDateOnly,
                SelectedService?.HairServiceId,
                SelectedSalon.SalonId,
                cts.Token);

            if (generation != _slotsGeneration)
            {
                return;
            }

            ApplySlots(response);
        }
        catch (OperationCanceledException)
        {
            // Выбор сменился: ответ устарел.
        }
        catch (BarberSearchApiException exception) when (generation == _slotsGeneration)
        {
            Slots.Clear();
            SlotsErrorText = DescribeReadFailure(exception, SlotsLoadFailedValue);
        }
        catch (HttpRequestException) when (generation == _slotsGeneration)
        {
            Slots.Clear();
            SlotsErrorText = NetworkValue;
        }
        catch (InvalidOperationException) when (generation == _slotsGeneration)
        {
            // Пустое тело при 200: сетки нет, но экран обязан показать «Повторить».
            Slots.Clear();
            SlotsErrorText = SlotsLoadFailedValue;
        }
        finally
        {
            if (ReferenceEquals(_slotsCts, cts))
            {
                _slotsCts = null;
                IsSlotsLoading = false;
            }

            cts.Dispose();
        }
    }

    /// <summary>
    /// Показывает ответ сетки: чипы, пустое состояние и признак выбора.
    /// </summary>
    private void ApplySlots(BarberSlotsResponse response)
    {
        _slotsDate = response.Date;
        _slotsTimeZoneId = response.TimeZoneId;
        _slotsSalonId = response.SalonId;
        _slotsBarberServiceId = response.BarberServiceId;

        var timeZone = SalonClock.TryResolve(response.TimeZoneId);

        Slots.Clear();

        foreach (var slot in response.Slots)
        {
            Slots.Add(new BarberSlotOption(
                slot.StartAt,
                slot.EndAt,
                SalonClock.FormatTimeRange(slot.StartAt, slot.EndAt, timeZone))
            {
                SelectCommand = SelectSlotCommand,
            });
        }

        _hasSlotsResponse = true;
        EmptySlotsText = !response.IsWorkingDay
            ? NotWorkingDayValue
            : response.Slots.Count == 0
                ? NoFreeTimeValue
                : null;

        NotifySlotsState();
    }

    /// <summary>
    /// Сообщает об изменении состояния сетки.
    /// </summary>
    private void NotifySlotsState()
    {
        OnPropertyChanged(nameof(CanSelectSlots));
        OnPropertyChanged(nameof(ShowSlotsHint));
        SelectSlotCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Показывает подтверждение и перезапрашивает сетку: занятого слота в ней быть не должно.
    /// </summary>
    private async Task ShowBookedAsync(CancellationToken cancellationToken)
    {
        DropSnapshot();
        _failureKind = BookingFailureKind.None;
        _sessionExpired = false;
        BookingErrorText = null;
        CustomerNote = string.Empty;
        IsBooked = true;
        RaiseBookingChanged();

        await LoadSlotsAsync(cancellationToken);
    }

    /// <summary>
    /// Переводит отказ сервера при создании брони в текст и следующий шаг.
    /// </summary>
    /// <remarks>
    /// Порядок, как в вебе: <c>401</c> → коды <c>409</c> → коды <c>400</c> → <c>404</c> →
    /// <c>429</c> и остальное. Ветки, которые лечатся только новым временем, сразу
    /// перезапрашивают сетку и сбрасывают снимок.
    /// </remarks>
    private async Task ApplyBookingFailureAsync(
        BarberSearchApiException exception,
        CancellationToken cancellationToken)
    {
        if (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Обработчик уже очистил пару; второй просьбы войти быть не должно.
            _sessionExpired = true;
            IsAuthenticated = _session.IsAuthenticated;
            _failureKind = BookingFailureKind.None;
            RaiseBookingChanged();

            return;
        }

        if (exception.StatusCode == HttpStatusCode.Conflict)
        {
            if (HasCode(exception, ApiErrorCodes.Booking.AddOverlap))
            {
                await DropSnapshotAndReloadAsync(OverlapValue, BookingFailureKind.StaleTime, cancellationToken);

                return;
            }

            if (HasCode(exception, ApiErrorCodes.Booking.AddLockTimeout))
            {
                ApplyFailure(LockTimeoutValue, BookingFailureKind.Retry);

                return;
            }

            if (HasCode(exception, IdempotencyKeyConflictCode))
            {
                ApplyFailure(BookingFailedValue, BookingFailureKind.KeyConflict);

                return;
            }

            ApplyFailure(BookingFailedValue, BookingFailureKind.Retry);

            return;
        }

        if (exception.StatusCode == HttpStatusCode.BadRequest)
        {
            if (HasCode(exception, StartAtInPastCode))
            {
                await DropSnapshotAndReloadAsync(StartAtInPastValue, BookingFailureKind.StaleTime, cancellationToken);

                return;
            }

            if (HasCode(exception, StartAtTooFarCode))
            {
                await DropSnapshotAndReloadAsync(StartAtTooFarValue, BookingFailureKind.StaleTime, cancellationToken);

                return;
            }

            ApplyFailure(BookingFailedValue, BookingFailureKind.Fatal);

            return;
        }

        if (exception.StatusCode == HttpStatusCode.NotFound)
        {
            await DropSnapshotAndReloadAsync(ServiceGoneValue, BookingFailureKind.Unavailable, cancellationToken);

            return;
        }

        ApplyFailure(
            exception.StatusCode == HttpStatusCode.TooManyRequests ? RateLimitValue : BookingFailedValue,
            BookingFailureKind.Retry);
    }

    /// <summary>
    /// Запоминает ошибку, которая лечится повтором с тем же ключом.
    /// </summary>
    private void ApplyFailure(string message, BookingFailureKind kind)
    {
        BookingErrorText = message;
        _failureKind = kind;

        RaiseBookingChanged();
    }

    /// <summary>
    /// Сбрасывает снимок и перезапрашивает сетку: старое время больше не предлагается.
    /// </summary>
    private async Task DropSnapshotAndReloadAsync(
        string message,
        BookingFailureKind kind,
        CancellationToken cancellationToken)
    {
        BookingErrorText = message;
        _failureKind = kind;
        DropSnapshot();

        RaiseBookingChanged();

        await LoadSlotsAsync(cancellationToken);
    }

    /// <summary>
    /// Забывает выбранный слот, снимок и ключ идемпотентности.
    /// </summary>
    private void DropSnapshot()
    {
        if (_selectedSlot is not null)
        {
            _selectedSlot.IsSelected = false;
            _selectedSlot = null;
        }

        _snapshot = null;
        _idempotencyKey = null;
    }

    /// <summary>
    /// Приводит панель записи в исходное состояние.
    /// </summary>
    private void ResetBookingState()
    {
        DropSnapshot();
        _failureKind = BookingFailureKind.None;
        _sessionExpired = false;
        BookingErrorText = null;
        IsBooked = false;

        RaiseBookingChanged();
    }

    /// <summary>
    /// Комментарий для сервера: обрезан по краям, пустой — <c>null</c>.
    /// </summary>
    private string? NormalizedNote()
    {
        var note = CustomerNote.Trim();

        if (note.Length == 0)
        {
            return null;
        }

        return note.Length <= MaxNoteLength ? note : note[..MaxNoteLength];
    }

    /// <summary>
    /// Новый ключ идемпотентности: в логи и на экран он не попадает.
    /// </summary>
    private static string NewKey() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Есть ли в ответе заданный публичный код ошибки.
    /// </summary>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Текст ошибки чтения: <c>429</c> — про лимит, остальное — заголовок раздела.
    /// </summary>
    private static string DescribeReadFailure(BarberSearchApiException exception, string fallback) =>
        exception.StatusCode == HttpStatusCode.TooManyRequests ? RateLimitValue : fallback;

    /// <summary>
    /// Сообщает об изменении шапки и прайса.
    /// </summary>
    private void RaiseProfileChanged()
    {
        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(IsContentVisible));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(GradeText));
        OnPropertyChanged(nameof(RatingText));
        OnPropertyChanged(nameof(PlaceText));
        OnPropertyChanged(nameof(HasServices));
        OnPropertyChanged(nameof(HasNoServices));
        OnPropertyChanged(nameof(HasSalons));
        OnPropertyChanged(nameof(HasNoSalons));
        OnPropertyChanged(nameof(HasSingleSalon));
        OnPropertyChanged(nameof(HasMultipleSalons));
        OnPropertyChanged(nameof(AvatarImageUrl));
        OnPropertyChanged(nameof(HasAvatar));
        OnPropertyChanged(nameof(ShowInitials));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(IsVerified));
        OnPropertyChanged(nameof(ExperienceText));
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(HasLanguages));
        OnPropertyChanged(nameof(HasAbout));
        OnPropertyChanged(nameof(Bio));
        OnPropertyChanged(nameof(HasBio));
        OnPropertyChanged(nameof(HasSpecializations));
        OnPropertyChanged(nameof(HasPayments));
        OnPropertyChanged(nameof(LatePolicyText));
        OnPropertyChanged(nameof(HasLatePolicy));
    }

    /// <summary>
    /// Сообщает об изменении панели записи.
    /// </summary>
    /// <remarks>
    /// Панель зависит от снимка, ключа, признака входа и вида ошибки — это обычные поля,
    /// а не наблюдаемые свойства, поэтому уведомления рассылаются одним списком.
    /// </remarks>
    private void RaiseBookingChanged()
    {
        OnPropertyChanged(nameof(IsBookingPanelVisible));
        OnPropertyChanged(nameof(IsSummaryVisible));
        OnPropertyChanged(nameof(SummaryDateText));
        OnPropertyChanged(nameof(SummaryServiceText));
        OnPropertyChanged(nameof(SummaryPriceText));
        OnPropertyChanged(nameof(IsSignInVisible));
        OnPropertyChanged(nameof(SignInText));
        OnPropertyChanged(nameof(IsBookFormVisible));
        OnPropertyChanged(nameof(IsBookingErrorVisible));
        OnPropertyChanged(nameof(IsSubmitVisible));
        OnPropertyChanged(nameof(IsSubmitEnabled));
        OnPropertyChanged(nameof(SubmitButtonText));
        OnPropertyChanged(nameof(IsBookingActionsVisible));
        OnPropertyChanged(nameof(SecondaryButtonText));
        OnPropertyChanged(nameof(IsSuccessVisible));
        OnPropertyChanged(nameof(IsBookAgainVisible));
    }

    /// <summary>
    /// Название салона из снимка: сводка показывает место, а не его идентификатор.
    /// </summary>
    private string VenueName =>
        _snapshot is null
            ? VenueFallbackText
            : Salons.FirstOrDefault(salon => salon.SalonId == _snapshot.SalonId)?.Name
                ?? VenueFallbackText;

    /// <summary>
    /// Вид ошибки отправки: от него зависит набор кнопок панели.
    /// </summary>
    private enum BookingFailureKind
    {
        /// <summary>Ошибки нет: обычная форма записи.</summary>
        None,

        /// <summary>Временная ошибка: повтор с тем же ключом.</summary>
        Retry,

        /// <summary>Конфликт ключа идемпотентности: повтор с новым ключом.</summary>
        KeyConflict,

        /// <summary>Время ушло: сетка перезапрошена, нужен другой слот.</summary>
        StaleTime,

        /// <summary>Услуга или салон исчезли: сетка перезапрошена.</summary>
        Unavailable,

        /// <summary>Повторять ту же попытку бессмысленно.</summary>
        Fatal,
    }
}
