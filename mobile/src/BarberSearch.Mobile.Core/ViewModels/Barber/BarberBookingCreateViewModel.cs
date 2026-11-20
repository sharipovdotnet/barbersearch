using System.Collections.ObjectModel;
using System.Windows.Input;
using BarberSearch.Contracts.BarberClients;
using BarberSearch.Contracts.BarberProfiles.GetBarberBookingOptions;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.CreateBarberBooking;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Кто записывается: найденный в сервисе клиент или новый человек без аккаунта.
/// </summary>
/// <remarks>
/// Режим меняет только состав тела запроса — либо <c>customerId</c>, либо имя и телефон
/// гостя, — а услуга, салон, дата и время общие. Смешивать два варианта нельзя:
/// сервер отвечает <c>400 booking.create.customerAmbiguous</c>.
/// </remarks>
public enum BarberClientMode
{
    /// <summary>
    /// Зарегистрированный пользователь: найден поиском по email или телефону.
    /// </summary>
    Registered,

    /// <summary>
    /// Новый клиент: имя и телефон сохраняются в самой брони, аккаунт не создаётся.
    /// </summary>
    Guest,
}

/// <summary>
/// Созданная запись: что нужно странице, чтобы обновить список и закрыться.
/// </summary>
/// <param name="BookingId">Идентификатор брони из <c>201</c>.</param>
/// <param name="ClientName">Имя клиента для баннера «Клиент записан: …».</param>
public sealed record BarberBookingCreatedResult(Guid BookingId, string ClientName);

/// <summary>
/// Строка найденного клиента в форме записи.
/// </summary>
/// <remarks>
/// Ответ поиска — подсказка максимум из десяти строк без пагинации, поэтому строка хранит
/// только то, что видно в списке: имя, email и телефон. <see cref="IsSelected"/> наблюдается,
/// а команда выбора приходит из ViewModel: шаблон <c>BindableLayout</c> не дотягивается до
/// родительского контекста в скомпилированных привязках.
/// </remarks>
public sealed partial class BarberClientRow : ObservableObject
{
    /// <summary>
    /// Создаёт строку из ответа поиска клиентов.
    /// </summary>
    /// <param name="client">Найденный пользователь.</param>
    public BarberClientRow(BarberClientResponse client)
    {
        ArgumentNullException.ThrowIfNull(client);

        Id = client.Id;
        Name = string.Concat(client.FirstName, " ", client.LastName).Trim();
        Email = client.Email;
        PhoneNumber = client.PhoneNumber;
    }

    /// <summary>
    /// Идентификатор пользователя: он уходит в бронь как <c>customerId</c>.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Имя и фамилия одной строкой: «Иван Петров».
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Адрес электронной почты: вторая строка подсказки.
    /// </summary>
    public string Email { get; }

    /// <summary>
    /// Телефон; <c>null</c>, если он не заполнен.
    /// </summary>
    public string? PhoneNumber { get; }

    /// <summary>
    /// У клиента есть телефон.
    /// </summary>
    public bool HasPhone => !string.IsNullOrWhiteSpace(PhoneNumber);

    /// <summary>
    /// Строка выбрана: подсветка списка.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Команда выбора строки.
    /// </summary>
    public ICommand? SelectCommand { get; init; }
}

/// <summary>
/// Форма «Записать клиента» в кабинете мастера.
/// </summary>
/// <remarks>
/// <para>
/// Экран повторяет веб (<c>frontend/src/components/barber/barber-booking-dialog.tsx</c>):
/// услуга, салон, дата и свободное время общие, а блок контактов переключается между
/// зарегистрированным клиентом и новым человеком без аккаунта. Отправляется один
/// <c>POST api/barber/bookings</c>, и сервер создаёт бронь сразу подтверждённой: мастер
/// и есть подтверждающая сторона, отдельного подтверждения не требуется.
/// </para>
/// <para>
/// Поиск клиента — подсказка, а не справочник: минимум три символа, пауза 300 мс и отмена
/// прошлого запроса, потому что медленный ответ на «Ива» не должен перетереть быстрый на
/// «Иванов». В режиме нового клиента поиск не выполняется вовсе: контакты вводятся вручную.
/// </para>
/// <para>
/// Дата ограничена окном записи, посчитанным в поясе салона: «сегодня» в UTC может быть
/// вчерашним днём салона, поэтому календарь ведёт пояс из <c>booking-options</c>, а время
/// слотов приходит уже в поясе ответа. Неизвестный пояс падает в UTC.
/// </para>
/// <para>
/// Ключ идемпотентности создаётся на попытку: повтор после сетевого сбоя уходит с тем же
/// ключом и не создаёт вторую бронь, а любая правка полей, от которых зависит тело запроса,
/// начинает новую логическую попытку и новый ключ. Это чуть строже веба, который меняет
/// ключ только при смене услуги, салона и даты: на телефоне поля правят чаще, и «повтор с
/// ключом от другого тела» вернул бы уже созданную бронь с прежними контактами.
/// </para>
/// <para>
/// Чтения (услуги и салоны, сетка слотов, поиск клиентов) повторяются сами, пока экран
/// виден: <see cref="StartAutoRetry"/> и <see cref="StopAutoRetry"/> зовёт страница по
/// видимости и по уходу в фон. У каждого чтения свой цикл, и циклы не запускают друг друга:
/// упавший поиск клиентов не повод перечитывать салоны. Отправка — запись, её автоповтор
/// не касается: после сбоя исход неизвестен, и человек сам решает, повторить ли действие.
/// </para>
/// </remarks>
public sealed partial class BarberBookingCreateViewModel : ObservableObject
{
    /// <summary>
    /// Минимум символов поиска клиента: короче сервер отвечает ошибкой.
    /// </summary>
    public const int MinimumClientSearchLength = 3;

    /// <summary>
    /// Максимум символов поиска клиента: тот же предел объявлен сервером.
    /// </summary>
    public const int MaximumClientSearchLength = 100;

    /// <summary>
    /// Предел комментария к визиту: как у публичной записи и у веба.
    /// </summary>
    public const int MaxNoteLength = 500;

    /// <summary>
    /// Предел имени гостя: совпадает с серверным <c>booking.create.guestFirstName</c>.
    /// </summary>
    public const int MaxGuestFirstNameLength = 100;

    /// <summary>
    /// Предел фамилии гостя: совпадает с серверным <c>booking.create.guestLastName</c>.
    /// </summary>
    public const int MaxGuestLastNameLength = 100;

    /// <summary>
    /// Предел телефона гостя: совпадает с серверным <c>booking.create.guestPhone</c>.
    /// </summary>
    public const int MaxGuestPhoneLength = 32;

    /// <summary>
    /// Горизонт записи: тридцать дней от сегодняшнего дня салона.
    /// </summary>
    public const int BookingHorizonDays = 30;

    /// <summary>
    /// Пауза между последним нажатием и запросом поиска клиента: как у веба.
    /// </summary>
    public static readonly TimeSpan ClientSearchDebounce = TimeSpan.FromMilliseconds(300);

    private static string OptionsFailedText => Loc.T("Не удалось загрузить услуги и салоны.");
    private static string SlotsFailedText => Loc.T("Не удалось загрузить свободное время.");
    private static string ClientsFailedText => Loc.T("Не удалось найти клиентов.");
    private static string SubmitFailedText => Loc.T("Не удалось записать клиента.");
    private static string GuestNameRequiredText => Loc.T("Укажите имя гостя.");
    private static string GuestPhoneRequiredText => Loc.T("Укажите телефон гостя.");

    private readonly IBarberClientsApi _clientsApi;
    private readonly IBarberBookingsApi _bookingsApi;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly AutoRetryLoop _optionsAutoRetry;
    private readonly AutoRetryLoop _slotsAutoRetry;
    private readonly AutoRetryLoop _clientsAutoRetry;

    private bool _isPageVisible;
    private bool _optionsLoaded;
    private bool _isApplyingOptions;
    private bool _isApplyingDate;
    private bool _preferredApplied;
    private bool _clientsLoaded;
    private DateTimeOffset? _preferredStartAt;
    private TimeZoneInfo? _timeZone;
    private CancellationTokenSource? _clientsRequest;
    private CancellationTokenSource? _slotsRequest;
    private Task _clientsOperation = Task.CompletedTask;
    private Task _slotsOperation = Task.CompletedTask;
    private ApiFailureKind _optionsFailureKind;
    private ApiFailureKind _slotsFailureKind;
    private ApiFailureKind _clientsFailureKind;
    private DateOnly? _retrySlotsDate;
    private string? _retryClientQuery;
    private string _idempotencyKey = NewIdempotencyKey();

    /// <summary>
    /// Создаёт форму записи клиента.
    /// </summary>
    /// <param name="clientsApi">Услуги и салоны профиля, своя сетка слотов, поиск клиентов.</param>
    /// <param name="bookingsApi">Создание брони <c>POST api/barber/bookings</c>.</param>
    /// <param name="timeProvider">Часы: «сегодня» считается в поясе салона.</param>
    /// <param name="optionsAutoRetry">
    /// Цикл повтора чтения услуг и салонов; <c>null</c> — собственный цикл.
    /// </param>
    /// <param name="slotsAutoRetry">
    /// Цикл повтора сетки слотов; <c>null</c> — собственный цикл.
    /// </param>
    /// <param name="clientsAutoRetry">
    /// Цикл повтора поиска клиентов; <c>null</c> — собственный цикл. Параметры необязательные
    /// и стоят последними, чтобы DI и тесты не зависели от их порядка.
    /// </param>
    public BarberBookingCreateViewModel(
        IBarberClientsApi clientsApi,
        IBarberBookingsApi bookingsApi,
        TimeProvider timeProvider,
        AutoRetryLoop? optionsAutoRetry = null,
        AutoRetryLoop? slotsAutoRetry = null,
        AutoRetryLoop? clientsAutoRetry = null)
        : this(
            clientsApi,
            bookingsApi,
            timeProvider,
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
            optionsAutoRetry,
            slotsAutoRetry,
            clientsAutoRetry)
    {
    }

    /// <summary>
    /// Создаёт форму с подменяемой паузой — для тестов без ожидания таймера.
    /// </summary>
    /// <param name="clientsApi">Услуги и салоны профиля, сетка слотов, поиск клиентов.</param>
    /// <param name="bookingsApi">Создание брони.</param>
    /// <param name="timeProvider">Часы приложения.</param>
    /// <param name="delay">Пауза между последним нажатием и запросом поиска клиента.</param>
    /// <param name="optionsAutoRetry">Цикл повтора чтения услуг и салонов.</param>
    /// <param name="slotsAutoRetry">Цикл повтора сетки слотов.</param>
    /// <param name="clientsAutoRetry">Цикл повтора поиска клиентов.</param>
    internal BarberBookingCreateViewModel(
        IBarberClientsApi clientsApi,
        IBarberBookingsApi bookingsApi,
        TimeProvider timeProvider,
        Func<TimeSpan, CancellationToken, Task> delay,
        AutoRetryLoop? optionsAutoRetry = null,
        AutoRetryLoop? slotsAutoRetry = null,
        AutoRetryLoop? clientsAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(clientsApi);
        ArgumentNullException.ThrowIfNull(bookingsApi);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(delay);

        _clientsApi = clientsApi;
        _bookingsApi = bookingsApi;
        _timeProvider = timeProvider;
        _delay = delay;

        _optionsAutoRetry = optionsAutoRetry ?? new AutoRetryLoop();
        _slotsAutoRetry = slotsAutoRetry ?? new AutoRetryLoop();
        _clientsAutoRetry = clientsAutoRetry ?? new AutoRetryLoop();
        _optionsAutoRetry.StateChanged += OnOptionsAutoRetryStateChanged;
        _slotsAutoRetry.StateChanged += OnSlotsAutoRetryStateChanged;
        _clientsAutoRetry.StateChanged += OnClientsAutoRetryStateChanged;
    }

    /// <summary>
    /// Запись создана: страница обновляет список владельца и закрывается.
    /// </summary>
    /// <remarks>
    /// Событие, а не вызов навигации: Core не знает ни про Shell, ни про MAUI. Обработчик
    /// страницы вызывает <see cref="BarberBookingsViewModel.OnBookingCreated"/> и делает
    /// переход назад; повторять <c>POST</c> после этого нельзя.
    /// </remarks>
    public event EventHandler<BarberBookingCreatedResult>? BookingCreated;

    /// <summary>
    /// Активные услуги мастера в порядке сервера.
    /// </summary>
    public ObservableCollection<BarberServiceItem> Services { get; } = [];

    /// <summary>
    /// Салоны мастера в порядке сервера: основной первым.
    /// </summary>
    public ObservableCollection<BarberSalonOption> Salons { get; } = [];

    /// <summary>
    /// Результаты последнего поиска клиентов в порядке сервера.
    /// </summary>
    public ObservableCollection<BarberClientRow> Clients { get; } = [];

    /// <summary>
    /// Свободное время выбранной даты.
    /// </summary>
    public ObservableCollection<BarberSlotOption> Slots { get; } = [];

    /// <summary>
    /// Кто записывается: зарегистрированный клиент или новый человек.
    /// </summary>
    [ObservableProperty]
    private BarberClientMode _mode = BarberClientMode.Registered;

    /// <summary>
    /// Строка поиска клиента: email или телефон.
    /// </summary>
    [ObservableProperty]
    private string _clientQuery = string.Empty;

    /// <summary>
    /// Выбранный зарегистрированный клиент; <c>null</c> — ещё не выбран.
    /// </summary>
    [ObservableProperty]
    private BarberClientRow? _selectedClient;

    /// <summary>
    /// Имя нового клиента.
    /// </summary>
    [ObservableProperty]
    private string _guestFirstName = string.Empty;

    /// <summary>
    /// Фамилия нового клиента; необязательна.
    /// </summary>
    [ObservableProperty]
    private string _guestLastName = string.Empty;

    /// <summary>
    /// Телефон нового клиента.
    /// </summary>
    [ObservableProperty]
    private string _guestPhone = string.Empty;

    /// <summary>
    /// Ошибка поля «Имя» в режиме нового клиента.
    /// </summary>
    [ObservableProperty]
    private string? _guestNameError;

    /// <summary>
    /// Ошибка поля «Телефон» в режиме нового клиента.
    /// </summary>
    [ObservableProperty]
    private string? _guestPhoneError;

    /// <summary>
    /// Выбранная услуга: от её длительности строится сетка слотов.
    /// </summary>
    [ObservableProperty]
    private BarberServiceItem? _selectedService;

    /// <summary>
    /// Выбранный салон: он задаёт пояс, окно дат и саму бронь.
    /// </summary>
    [ObservableProperty]
    private BarberSalonOption? _selectedSalon;

    /// <summary>
    /// Выбранная дата визита в поясе салона.
    /// </summary>
    [ObservableProperty]
    private DateTime _selectedDate;

    /// <summary>
    /// Минимальная дата календаря: сегодня в поясе салона.
    /// </summary>
    [ObservableProperty]
    private DateTime _minimumDate;

    /// <summary>
    /// Максимальная дата календаря: сегодня плюс тридцать дней.
    /// </summary>
    [ObservableProperty]
    private DateTime _maximumDate;

    /// <summary>
    /// Выбранный слот; <c>null</c>, пока время не выбрано.
    /// </summary>
    [ObservableProperty]
    private BarberSlotOption? _selectedSlot;

    /// <summary>
    /// Комментарий к визиту; пустой уходит как <c>null</c>.
    /// </summary>
    [ObservableProperty]
    private string _note = string.Empty;

    /// <summary>
    /// Идёт чтение услуг и салонов.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingOptions;

    /// <summary>
    /// Причина неудачи чтения услуг и салонов; <c>null</c>, когда читать удалось.
    /// </summary>
    [ObservableProperty]
    private string? _optionsError;

    /// <summary>
    /// Цикл сам повторяет упавшее чтение услуг и салонов.
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetryingOptions;

    /// <summary>
    /// Идёт поиск клиентов, включая паузу перед запросом.
    /// </summary>
    [ObservableProperty]
    private bool _isSearchingClients;

    /// <summary>
    /// Причина неудачи поиска клиентов.
    /// </summary>
    [ObservableProperty]
    private string? _clientsError;

    /// <summary>
    /// Цикл сам повторяет упавший поиск клиентов.
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetryingClients;

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
    /// Цикл сам повторяет упавшее чтение сетки.
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetryingSlots;

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
    /// Идёт отправка: кнопка неактивна, повтор игнорируется.
    /// </summary>
    [ObservableProperty]
    private bool _isSubmitting;

    /// <summary>
    /// Ошибка отправки; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    private string? _submitError;

    /// <summary>
    /// Выбран режим зарегистрированного клиента.
    /// </summary>
    public bool IsRegisteredMode => Mode == BarberClientMode.Registered;

    /// <summary>
    /// Выбран режим нового клиента.
    /// </summary>
    public bool IsGuestMode => Mode == BarberClientMode.Guest;

    /// <summary>
    /// Поиск клиента достаточно длинный, чтобы сервер его принял.
    /// </summary>
    public bool CanSearchClients => ClientQuery.Trim().Length >= MinimumClientSearchLength
        && ClientQuery.Trim().Length <= MaximumClientSearchLength;

    /// <summary>
    /// Подсказка под полем поиска: строка короче минимума.
    /// </summary>
    public string ClientSearchHint => CanSearchClients
        ? string.Empty
        : Loc.T(
            "Минимум {0} символа. Нет клиента в сервисе? Выберите «Новый клиент».",
            MinimumClientSearchLength);

    /// <summary>
    /// В списке есть найденные клиенты.
    /// </summary>
    public bool ShowClientResults => Clients.Count > 0;

    /// <summary>
    /// Поиск закончился, но никого не нашёл.
    /// </summary>
    public bool ShowClientsEmpty => CanSearchClients
        && _clientsLoaded
        && !IsSearchingClients
        && ClientsError is null
        && Clients.Count == 0;

    /// <summary>
    /// Поиск клиентов упал.
    /// </summary>
    public bool ShowClientsError => ClientsError is not null;

    /// <summary>
    /// Зарегистрированный клиент выбран.
    /// </summary>
    public bool HasSelectedClient => SelectedClient is not null;

    /// <summary>
    /// Подпись выбранного клиента: «Выбран: Иван Петров».
    /// </summary>
    public string SelectedClientSummary => SelectedClient is { } client
        ? Loc.T("Выбран: {0}", client.Name)
        : string.Empty;

    /// <summary>
    /// У нового клиента заполнены обязательные имя и телефон.
    /// </summary>
    public bool HasGuestContacts => !string.IsNullOrWhiteSpace(GuestFirstName)
        && !string.IsNullOrWhiteSpace(GuestPhone);

    /// <summary>
    /// Клиент для брони известен: выбран из сервиса или заполнен вручную.
    /// </summary>
    public bool HasClient => IsRegisteredMode ? HasSelectedClient : HasGuestContacts;

    /// <summary>
    /// Форма принимает ввод: услуги и салоны прочитаны и ошибки нет.
    /// </summary>
    public bool ShowForm => _optionsLoaded && !IsLoadingOptions && OptionsError is null;

    /// <summary>
    /// Показывать причину неудачи чтения услуг и салонов.
    /// </summary>
    public bool ShowOptionsError => !IsLoadingOptions && OptionsError is not null;

    /// <summary>
    /// В прайсе есть активные услуги.
    /// </summary>
    public bool HasServices => Services.Count > 0;

    /// <summary>
    /// К профилю привязан хотя бы один салон.
    /// </summary>
    public bool HasSalons => Salons.Count > 0;

    /// <summary>
    /// Салона два и больше: только тогда есть что выбирать.
    /// </summary>
    public bool HasSeveralSalons => Salons.Count > 1;

    /// <summary>
    /// Название единственного салона: при одном месте выбора нет, но подпись нужна.
    /// </summary>
    public string OnlySalonName => Salons.Count == 1 ? Salons[0].Name : string.Empty;

    /// <summary>
    /// Подсказка о поясе выбранного салона: «Время — местное для салона «Hairo».».
    /// </summary>
    public string SalonTimeZoneText => SelectedSalon is { } salon
        ? Loc.T("Время — местное для салона «{0}».", salon.Name)
        : string.Empty;

    /// <summary>
    /// Окно дат известно: пояс салона прочитан.
    /// </summary>
    public bool HasDateWindow => MaximumDate != default;

    /// <summary>
    /// Выбранная дата попадает в окно записи.
    /// </summary>
    public bool IsDateValid => HasDateWindow
        && SelectedDate >= MinimumDate
        && SelectedDate <= MaximumDate;

    /// <summary>
    /// Что делать, пока сетка не строится: выбрать услугу или дату.
    /// </summary>
    public string SlotsPlaceholder
    {
        get
        {
            if (SelectedService is null)
            {
                return Loc.T("Сначала выберите услугу — от её длительности строится сетка.");
            }

            return IsDateValid ? string.Empty : Loc.T("Выберите дату.");
        }
    }

    /// <summary>
    /// Показывать сетку слотов.
    /// </summary>
    public bool ShowSlots => SelectedService is not null
        && IsDateValid
        && !IsLoadingSlots
        && SlotsError is null
        && Slots.Count > 0;

    /// <summary>
    /// Показывать индикатор чтения сетки.
    /// </summary>
    public bool ShowSlotsLoading => IsLoadingSlots;

    /// <summary>
    /// Показывать причину неудачи чтения сетки.
    /// </summary>
    public bool ShowSlotsError => SlotsError is not null;

    /// <summary>
    /// Показывать пустой день: чтение закончилось, ошибки нет и слотов нет.
    /// </summary>
    public bool ShowEmptySlots => IsDayLoaded
        && !IsLoadingSlots
        && SlotsError is null
        && Slots.Count == 0;

    /// <summary>
    /// Объяснение пустого дня: нерабочий день или всё занято.
    /// </summary>
    public string EmptySlotsText => IsWorkingDay
        ? Loc.T("Свободного времени на этот день нет.")
        : Loc.T("В этот день вы не работаете.");

    /// <summary>
    /// Дата и время выбранного слота: «сб, 15 нояб., 10:00–11:00».
    /// </summary>
    public string SelectedSlotTitle => SelectedSlot is { } slot && IsDateValid
        ? Loc.T("{0}, {1}", BookingText.ShortDate(DateOnly.FromDateTime(SelectedDate)), slot.TimeText)
        : string.Empty;

    /// <summary>
    /// Услуга, салон и цена выбранного слота: «Стрижка · Hairo · 2 400 ₽».
    /// </summary>
    public string SelectedSlotSubtitle => SelectedSlot is not null
        && SelectedService is { } service
        && SelectedSalon is { } salon
            ? Loc.T("{0} · {1} · {2}", service.Name, salon.Name, service.PriceText)
            : string.Empty;

    /// <summary>
    /// Счётчик комментария: «0 / 500».
    /// </summary>
    public string NoteCounterText => Loc.T("{0} / {1}", Note.Length, MaxNoteLength);

    /// <summary>
    /// Кнопка отправки доступна: клиент, услуга, салон и слот выбраны.
    /// </summary>
    public bool CanSubmit => !IsSubmitting
        && HasClient
        && SelectedService is not null
        && SelectedSalon is not null
        && SelectedSlot is not null;

    /// <summary>
    /// Подпись кнопки отправки: во время запроса — «Записываем…».
    /// </summary>
    public string SubmitText => IsSubmitting ? Loc.T("Записываем…") : Loc.T("Записать");

    /// <summary>
    /// Есть ошибка отправки.
    /// </summary>
    public bool HasSubmitError => SubmitError is not null;

    /// <summary>
    /// Есть ошибка поля «Имя».
    /// </summary>
    public bool HasGuestNameError => GuestNameError is not null;

    /// <summary>
    /// Есть ошибка поля «Телефон».
    /// </summary>
    public bool HasGuestPhoneError => GuestPhoneError is not null;

    /// <summary>
    /// Цикл повтора услуг и салонов: тестам нужен счётчик попыток.
    /// </summary>
    internal AutoRetryLoop OptionsAutoRetry => _optionsAutoRetry;

    /// <summary>
    /// Цикл повтора сетки слотов: тестам нужен счётчик попыток.
    /// </summary>
    internal AutoRetryLoop SlotsAutoRetry => _slotsAutoRetry;

    /// <summary>
    /// Цикл повтора поиска клиентов: тестам нужен счётчик попыток.
    /// </summary>
    internal AutoRetryLoop ClientsAutoRetry => _clientsAutoRetry;

    /// <summary>
    /// Завершается, когда все три цикла повтора останавливаются: тесты ждут его вместо пауз.
    /// </summary>
    internal Task AutoRetryCompletion => Task.WhenAll(
        _optionsAutoRetry.Completion,
        _slotsAutoRetry.Completion,
        _clientsAutoRetry.Completion);

    /// <summary>
    /// Завершение последнего поиска клиента — тестам, чтобы не ждать паузу.
    /// </summary>
    internal Task ClientsOperation => _clientsOperation;

    /// <summary>
    /// Завершение последнего чтения сетки — тестам, чтобы не ждать ответа.
    /// </summary>
    internal Task SlotsOperation => _slotsOperation;

    /// <summary>
    /// Текущий ключ идемпотентности: тестам нужно сравнить попытки.
    /// </summary>
    internal string IdempotencyKey => _idempotencyKey;

    /// <summary>
    /// Читает услуги и салоны и готовит форму при первом показе страницы.
    /// </summary>
    /// <remarks>
    /// Идемпотентен: после успешного чтения повторный вызов не перечитывает коллекции и не
    /// затирает правки. После неудачи вызов повторяет чтение — страница может прийти из фона
    /// и попробовать снова.
    /// </remarks>
    /// <param name="preferredStartAt">
    /// Желаемое начало визита из ленты дня: если такой слот есть в первой сетке, он выбран
    /// заранее. <c>null</c> — предвыбора нет.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public async Task InitializeAsync(
        DateTimeOffset? preferredStartAt = null,
        CancellationToken cancellationToken = default)
    {
        if (preferredStartAt is not null)
        {
            _preferredStartAt = preferredStartAt;
            _preferredApplied = false;
        }

        if (_optionsLoaded || IsLoadingOptions)
        {
            NotifyFormState();

            return;
        }

        await LoadOptionsAsync(silent: false, cancellationToken);
    }

    /// <summary>
    /// Сообщает, что страница видна, и запускает автоповтор упавших чтений.
    /// </summary>
    /// <remarks>
    /// Идемпотентен. Если страница вернулась с выбранными услугами, салоном и датой, но без
    /// сетки (уход в фон отменил запрос), сетка читается заново: иначе форма осталась бы с
    /// пустым блоком времени без объяснения.
    /// </remarks>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartOptionsAutoRetry();
        TryStartSlotsAutoRetry();
        TryStartClientsAutoRetry();

        if (CanLoadSlots && !IsDayLoaded && !IsLoadingSlots && SlotsError is null && Slots.Count == 0)
        {
            _slotsOperation = ReloadSlotsAsync();
        }

        if (CanSearchClients
            && !_clientsLoaded
            && !IsSearchingClients
            && ClientsError is null
            && Clients.Count == 0)
        {
            BeginClientSearch(debounce: false);
        }
    }

    /// <summary>
    /// Сообщает, что страница скрыта, и останавливает все фоновые чтения.
    /// </summary>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        _optionsAutoRetry.Stop();
        _slotsAutoRetry.Stop();
        _clientsAutoRetry.Stop();

        CancelClientSearch();
        CancelSlotsRequest();

        IsLoadingSlots = false;

        NotifyFormState();
    }

    /// <summary>
    /// Переключает на зарегистрированного клиента.
    /// </summary>
    [RelayCommand]
    private void SelectRegisteredMode() => ChangeMode(BarberClientMode.Registered);

    /// <summary>
    /// Переключает на нового клиента без аккаунта.
    /// </summary>
    [RelayCommand]
    private void SelectGuestMode() => ChangeMode(BarberClientMode.Guest);

    /// <summary>
    /// Выбирает найденного клиента.
    /// </summary>
    /// <param name="client">Строка подсказки.</param>
    [RelayCommand]
    private void SelectClient(BarberClientRow? client)
    {
        if (client is null || IsSubmitting)
        {
            return;
        }

        foreach (var row in Clients)
        {
            row.IsSelected = ReferenceEquals(row, client);
        }

        SelectedClient = client;
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
    /// Повторяет чтение услуг и салонов по кнопке — сразу, без паузы цикла.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryOptionsAsync(CancellationToken cancellationToken)
    {
        if (IsLoadingOptions)
        {
            return;
        }

        _optionsAutoRetry.Stop();

        await LoadOptionsAsync(silent: false, cancellationToken);

        TryStartOptionsAutoRetry();
    }

    /// <summary>
    /// Повторяет чтение сетки по кнопке — сразу, без паузы цикла.
    /// </summary>
    [RelayCommand]
    private async Task RetrySlotsAsync()
    {
        _slotsAutoRetry.Stop();

        _slotsOperation = ReloadSlotsAsync();

        await _slotsOperation;

        TryStartSlotsAutoRetry();
    }

    /// <summary>
    /// Повторяет поиск клиентов по кнопке — сразу, без паузы.
    /// </summary>
    [RelayCommand]
    private Task RetryClientsAsync()
    {
        _clientsAutoRetry.Stop();

        BeginClientSearch(debounce: false);

        return _clientsOperation;
    }

    /// <summary>
    /// Создаёт запись и сообщает странице имя клиента.
    /// </summary>
    /// <remarks>
    /// Защита от двойного нажатия — <see cref="IsSubmitting"/> и <see cref="CanSubmit"/>;
    /// ключ идемпотентности не меняется, поэтому повтор после сетевого сбоя возвращает ту же
    /// бронь. <c>409</c> и «время заняли» перечитывают сетку той же даты и оставляют форму:
    /// человеку нужно только выбрать другое время.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены страницы.</param>
    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync(CancellationToken cancellationToken)
    {
        var service = SelectedService;
        var salon = SelectedSalon;
        var slot = SelectedSlot;

        if (IsSubmitting || service is null || salon is null || slot is null)
        {
            return;
        }

        if (!TryBuildCustomer(out var customerId, out var guestFirstName, out var guestLastName, out var guestPhone))
        {
            return;
        }

        IsSubmitting = true;
        SubmitError = null;

        try
        {
            var request = new CreateBarberBookingRequest(
                customerId,
                guestFirstName,
                guestLastName,
                guestPhone,
                service.BarberServiceId,
                salon.SalonId,
                slot.StartAt,
                slot.EndAt,
                _idempotencyKey,
                string.IsNullOrWhiteSpace(Note) ? null : Note.Trim());

            var bookingId = await _bookingsApi.CreateAsync(request, cancellationToken);

            BookingCreated?.Invoke(
                this,
                new BarberBookingCreatedResult(bookingId, BuildClientName(guestFirstName, guestLastName)));
        }
        catch (BarberSearchApiException exception)
        {
            var failure = BarberCabinetText.CreateError(exception, SubmitFailedText);

            SubmitError = failure.Message;

            if (failure.ReloadSlots)
            {
                // Время заняли: перечитываем сетку той же даты и оставляем форму — мастеру
                // нужно только выбрать другое время. Пока сетка читается, отправка заблокирована.
                SelectedSlot = null;

                _slotsOperation = LoadSlotsCoreAsync(
                    DateOnly.FromDateTime(SelectedDate),
                    silent: false,
                    cancellationToken);

                await _slotsOperation;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Страница ушла: исход отправки неизвестен, повторять молча нельзя.
        }
        catch (Exception exception)
        {
            SubmitError = BarberCabinetText.ReadError(exception, SubmitFailedText);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    /// <summary>
    /// Проверяет контакты и раскладывает ошибки по полям.
    /// </summary>
    /// <param name="customerId">Клиент сервиса либо <c>null</c> для гостя.</param>
    /// <param name="guestFirstName">Имя гостя либо <c>null</c>.</param>
    /// <param name="guestLastName">Фамилия гостя либо <c>null</c>.</param>
    /// <param name="guestPhone">Телефон гостя либо <c>null</c>.</param>
    /// <returns><c>true</c>, если можно отправлять.</returns>
    private bool TryBuildCustomer(
        out Guid? customerId,
        out string? guestFirstName,
        out string? guestLastName,
        out string? guestPhone)
    {
        customerId = null;
        guestFirstName = null;
        guestLastName = null;
        guestPhone = null;

        GuestNameError = null;
        GuestPhoneError = null;

        if (IsRegisteredMode)
        {
            if (SelectedClient is not { } client)
            {
                return false;
            }

            customerId = client.Id;

            return true;
        }

        var firstName = GuestFirstName.Trim();
        var phone = GuestPhone.Trim();
        var hasError = false;

        if (firstName.Length == 0)
        {
            GuestNameError = GuestNameRequiredText;
            hasError = true;
        }

        if (phone.Length == 0)
        {
            GuestPhoneError = GuestPhoneRequiredText;
            hasError = true;
        }

        if (hasError)
        {
            return false;
        }

        var lastName = GuestLastName.Trim();

        guestFirstName = firstName;
        guestPhone = phone;
        guestLastName = lastName.Length == 0 ? null : lastName;

        return true;
    }

    /// <summary>
    /// Имя клиента для баннера «Клиент записан: …».
    /// </summary>
    /// <param name="guestFirstName">Имя гостя либо <c>null</c>.</param>
    /// <param name="guestLastName">Фамилия гостя либо <c>null</c>.</param>
    /// <returns>Имя зарегистрированного клиента или имя гостя.</returns>
    private string BuildClientName(string? guestFirstName, string? guestLastName)
    {
        if (IsRegisteredMode && SelectedClient is { } client)
        {
            return client.Name;
        }

        return string.Concat(guestFirstName ?? string.Empty, " ", guestLastName ?? string.Empty).Trim();
    }

    /// <summary>
    /// Читает услуги и салоны и раскладывает ответ по форме.
    /// </summary>
    /// <param name="silent">Тихая попытка цикла: не показывать индикатор и не стирать причину.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private async Task<ApiFailureKind> LoadOptionsAsync(
        bool silent,
        CancellationToken cancellationToken)
    {
        if (!silent)
        {
            IsLoadingOptions = true;
            OptionsError = null;
        }

        NotifyFormState();

        var kind = ApiFailureKind.None;

        try
        {
            var options = await _clientsApi.GetBookingOptionsAsync(cancellationToken);

            ApplyOptions(options);

            OptionsError = null;
            _optionsFailureKind = ApiFailureKind.None;
            _optionsLoaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Страница ушла: ошибку показывать некому.
        }
        catch (Exception exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                OptionsError = BarberCabinetText.ReadError(exception, OptionsFailedText);
                _optionsFailureKind = kind;
            }
        }
        finally
        {
            if (!silent)
            {
                IsLoadingOptions = false;
            }

            NotifyFormState();
        }

        if (kind.IsRetryable())
        {
            TryStartOptionsAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Заполняет коллекции и выбирает салон по умолчанию.
    /// </summary>
    /// <remarks>
    /// Прошлый выбор сохраняется, если он есть в новом ответе: перечитывание каталога не
    /// должно сбрасывать форму. Салон по умолчанию — основной, иначе первый: при одном
    /// салоне выбор всё равно показан строкой, но менять нечего.
    /// </remarks>
    /// <param name="options">Услуги и салоны профиля.</param>
    private void ApplyOptions(BarberBookingOptionsResponse options)
    {
        var previousServiceId = SelectedService?.BarberServiceId;
        var previousSalonId = SelectedSalon?.SalonId;

        _isApplyingOptions = true;

        try
        {
            Services.Clear();

            foreach (var service in options.Services)
            {
                Services.Add(new BarberServiceItem(service));
            }

            Salons.Clear();

            foreach (var salon in options.Salons)
            {
                Salons.Add(BarberSalonOption.From(salon));
            }

            SelectedService = previousServiceId is { } serviceId
                ? FindService(serviceId)
                : null;

            SelectedSalon = previousSalonId is { } salonId
                ? FindSalon(salonId) ?? DefaultSalon()
                : DefaultSalon();

            ApplySalonWindow();
        }
        finally
        {
            _isApplyingOptions = false;
        }
    }

    /// <summary>
    /// Ищет услугу по идентификатору строки прайса.
    /// </summary>
    /// <param name="barberServiceId">Идентификатор услуги мастера.</param>
    /// <returns>Строка прайса либо <c>null</c>.</returns>
    private BarberServiceItem? FindService(Guid barberServiceId)
    {
        for (var index = 0; index < Services.Count; index++)
        {
            if (Services[index].BarberServiceId == barberServiceId)
            {
                return Services[index];
            }
        }

        return null;
    }

    /// <summary>
    /// Ищет салон по идентификатору справочника.
    /// </summary>
    /// <param name="salonId">Идентификатор салона.</param>
    /// <returns>Пункт выбора либо <c>null</c>.</returns>
    private BarberSalonOption? FindSalon(Guid salonId)
    {
        for (var index = 0; index < Salons.Count; index++)
        {
            if (Salons[index].SalonId == salonId)
            {
                return Salons[index];
            }
        }

        return null;
    }

    /// <summary>
    /// Салон по умолчанию: основной, иначе первый.
    /// </summary>
    /// <returns>Пункт выбора либо <c>null</c>, если салонов нет.</returns>
    private BarberSalonOption? DefaultSalon()
    {
        for (var index = 0; index < Salons.Count; index++)
        {
            if (Salons[index].IsPrimary)
            {
                return Salons[index];
            }
        }

        return Salons.Count > 0 ? Salons[0] : null;
    }

    /// <summary>
    /// Пересчитывает окно дат в поясе выбранного салона и держит дату внутри него.
    /// </summary>
    /// <remarks>
    /// «Сегодня» берётся из пояса салона: у полуночи UTC-дата и дата салона различаются.
    /// Неизвестный пояс падает в UTC — так же, как на карточке мастера.
    /// </remarks>
    private void ApplySalonWindow()
    {
        _timeZone = SalonClock.TryResolve(SelectedSalon?.TimeZoneId);

        var today = SalonClock.Today(_timeZone, _timeProvider.GetUtcNow());

        _isApplyingDate = true;

        try
        {
            MinimumDate = today.ToDateTime(TimeOnly.MinValue);
            MaximumDate = MinimumDate.AddDays(BookingHorizonDays);
            SelectedDate = ClampDate(SelectedDate == default ? MinimumDate : SelectedDate);
        }
        finally
        {
            _isApplyingDate = false;
        }
    }

    /// <summary>
    /// Держит дату внутри окна записи: раньше сегодня и позже тридцати дней сервер отвергнет.
    /// </summary>
    /// <param name="date">Дата из календаря.</param>
    /// <returns>Дата внутри окна.</returns>
    private DateTime ClampDate(DateTime date)
    {
        if (date < MinimumDate)
        {
            return MinimumDate;
        }

        return date > MaximumDate ? MaximumDate : date;
    }

    /// <summary>
    /// Перечитывает сетку на текущую дату — после смены услуги, салона или даты.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    private async Task ReloadSlotsAsync()
    {
        if (!CanLoadSlots)
        {
            CancelSlotsRequest();

            Slots.Clear();
            SelectedSlot = null;
            IsDayLoaded = false;
            IsLoadingSlots = false;
            SlotsError = null;
            _retrySlotsDate = null;
            _slotsFailureKind = ApiFailureKind.None;

            NotifySlotsState();

            return;
        }

        await LoadSlotsCoreAsync(
            DateOnly.FromDateTime(SelectedDate),
            silent: false,
            CancellationToken.None);

        TryStartSlotsAutoRetry();
    }

    /// <summary>
    /// Выбранные услуга, салон и дата позволяют строить сетку.
    /// </summary>
    private bool CanLoadSlots => SelectedService is not null
        && SelectedSalon is not null
        && IsDateValid;

    /// <summary>
    /// Читает сетку на заданную салонную дату — вручную или тихой попыткой цикла.
    /// </summary>
    /// <remarks>
    /// Снимок даты уходит в цикл, поэтому повтор несёт именно упавший запрос, а не текущий
    /// выбор календаря. Новый запрос отменяет прошлый: ответ на старую дату сетку не подменяет.
    /// Тихая попытка не стирает причину и не показывает индикатор — иначе блок с «Повторить»
    /// мигал бы на каждой паузе.
    /// </remarks>
    /// <param name="day">Салонная дата запроса.</param>
    /// <param name="silent">Тихая попытка цикла.</param>
    /// <param name="cancellationToken">Токен отмены страницы или цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private async Task<ApiFailureKind> LoadSlotsCoreAsync(
        DateOnly day,
        bool silent,
        CancellationToken cancellationToken)
    {
        var service = SelectedService;
        var salon = SelectedSalon;

        if (service is null || salon is null)
        {
            return ApiFailureKind.None;
        }

        CancelSlotsRequest();

        var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _slotsRequest = request;

        var token = request.Token;
        var kind = ApiFailureKind.None;

        if (!silent)
        {
            IsLoadingSlots = true;
            SlotsError = null;
        }

        NotifySlotsState();

        try
        {
            var response = await _clientsApi.GetSlotsAsync(
                day,
                service.HairServiceId,
                salon.SalonId,
                token);

            if (!ReferenceEquals(_slotsRequest, request))
            {
                return ApiFailureKind.None;
            }

            ApplySlots(response);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return ApiFailureKind.None;
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(_slotsRequest, request))
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
            if (ReferenceEquals(_slotsRequest, request))
            {
                _slotsRequest = null;

                if (!silent)
                {
                    IsLoadingSlots = false;
                }

                request.Dispose();

                NotifySlotsState();
            }
        }

        if (kind.IsRetryable())
        {
            TryStartSlotsAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Собирает строки сетки и применяет предвыбор из ленты дня.
    /// </summary>
    /// <remarks>
    /// Предвыбор действует один раз — на первой сетке: он пришёл из окна между визитами и
    /// ничего не отправляет. Ручной выбор всегда сильнее, а повторное чтение после конфликта
    /// не должно возвращать чужое время.
    /// </remarks>
    /// <param name="response">Ответ сетки слотов.</param>
    private void ApplySlots(BarberSlotsResponse response)
    {
        var zone = SalonClock.TryResolve(response.TimeZoneId) ?? _timeZone;

        Slots.Clear();
        SelectedSlot = null;

        foreach (var slot in response.Slots)
        {
            var option = new BarberSlotOption(
                slot.StartAt,
                slot.EndAt,
                SalonClock.FormatTimeRange(slot.StartAt, slot.EndAt, zone))
            {
                SelectCommand = SelectSlotCommand,
            };

            if (!_preferredApplied && _preferredStartAt is { } preferred && option.StartAt == preferred)
            {
                option.IsSelected = true;
                SelectedSlot = option;
            }

            Slots.Add(option);
        }

        _preferredApplied = true;
        IsWorkingDay = response.IsWorkingDay;
        IsDayLoaded = true;
        SlotsError = null;
        _slotsFailureKind = ApiFailureKind.None;
        _retrySlotsDate = null;

        NotifySlotsState();
    }

    /// <summary>
    /// Начинает поиск клиентов: отменяет прошлый и запускает новый с паузой или сразу.
    /// </summary>
    /// <param name="debounce">Ждать паузу перед запросом.</param>
    private void BeginClientSearch(bool debounce)
    {
        CancelClientSearch();

        Clients.Clear();
        SelectedClient = null;
        ClientsError = null;
        _clientsFailureKind = ApiFailureKind.None;
        _retryClientQuery = null;
        _clientsLoaded = false;

        if (!CanSearchClients)
        {
            NotifyClientsState();

            return;
        }

        var query = ClientQuery.Trim();
        var request = new CancellationTokenSource();

        _clientsRequest = request;
        IsSearchingClients = true;

        NotifyClientsState();

        _clientsOperation = debounce
            ? SearchClientsAfterDebounceAsync(query, request)
            : LoadClientsAsync(query, silent: false, request);
    }

    /// <summary>
    /// Ждёт паузу и ищет клиентов; отмена нового строкой — не ошибка.
    /// </summary>
    /// <param name="query">Строка запроса.</param>
    /// <param name="request">Источник отмены этого запроса.</param>
    /// <returns>Задача поиска.</returns>
    private async Task SearchClientsAfterDebounceAsync(
        string query,
        CancellationTokenSource request)
    {
        var cancellationToken = request.Token;

        try
        {
            await _delay(ClientSearchDebounce, cancellationToken).ConfigureAwait(true);

            await LoadClientsAsync(query, silent: false, request);
        }
        catch (OperationCanceledException)
        {
            // Новый запрос отменил паузу: ответ устарел, следующий обновит список.
        }
    }

    /// <summary>
    /// Читает подсказку клиентов и раскладывает ответ по строкам.
    /// </summary>
    /// <param name="query">Строка запроса.</param>
    /// <param name="silent">Тихая попытка цикла: не показывать индикатор и не стирать причину.</param>
    /// <param name="request">Источник отмены этого запроса.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private async Task<ApiFailureKind> LoadClientsAsync(
        string query,
        bool silent,
        CancellationTokenSource request)
    {
        var token = request.Token;
        var kind = ApiFailureKind.None;

        if (!silent)
        {
            IsSearchingClients = true;
            ClientsError = null;
        }

        NotifyClientsState();

        try
        {
            var results = await _clientsApi.SearchClientsAsync(query, token);

            if (!ReferenceEquals(_clientsRequest, request))
            {
                return ApiFailureKind.None;
            }

            ApplyClients(results);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return ApiFailureKind.None;
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(_clientsRequest, request))
            {
                return ApiFailureKind.None;
            }

            kind = ApiFailureClassifier.Classify(exception, token);

            if (kind != ApiFailureKind.None)
            {
                ClientsError = BarberCabinetText.ReadError(exception, ClientsFailedText);
                _clientsFailureKind = kind;
                _retryClientQuery = kind.IsRetryable() ? query : null;
            }
        }
        finally
        {
            if (ReferenceEquals(_clientsRequest, request))
            {
                _clientsRequest = null;
                IsSearchingClients = false;
                request.Dispose();

                NotifyClientsState();
            }
        }

        if (kind.IsRetryable())
        {
            TryStartClientsAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Заменяет подсказку ответом сервера.
    /// </summary>
    /// <param name="results">Найденные пользователи.</param>
    private void ApplyClients(IReadOnlyList<BarberClientResponse> results)
    {
        var selectedId = SelectedClient?.Id;

        Clients.Clear();

        foreach (var client in results)
        {
            Clients.Add(new BarberClientRow(client) { SelectCommand = SelectClientCommand });
        }

        _clientsLoaded = true;
        ClientsError = null;
        _clientsFailureKind = ApiFailureKind.None;
        _retryClientQuery = null;

        if (selectedId is { } id)
        {
            SelectedClient = FindClient(id);
        }

        NotifyClientsState();
    }

    /// <summary>
    /// Ищет строку подсказки по идентификатору пользователя.
    /// </summary>
    /// <param name="clientId">Идентификатор пользователя.</param>
    /// <returns>Строка подсказки либо <c>null</c>.</returns>
    private BarberClientRow? FindClient(Guid clientId)
    {
        for (var index = 0; index < Clients.Count; index++)
        {
            if (Clients[index].Id == clientId)
            {
                return Clients[index];
            }
        }

        return null;
    }

    /// <summary>
    /// Запускает цикл повтора услуг и салонов, если сбой повторяемый и страница видна.
    /// </summary>
    private void TryStartOptionsAutoRetry()
    {
        if (!_isPageVisible
            || _optionsLoaded
            || IsLoadingOptions
            || OptionsError is null
            || !_optionsFailureKind.IsRetryable())
        {
            return;
        }

        _optionsAutoRetry.Start(RetryOptionsAttemptAsync);
    }

    /// <summary>
    /// Запускает цикл повтора сетки, если он может помочь.
    /// </summary>
    private void TryStartSlotsAutoRetry()
    {
        if (!_isPageVisible
            || IsLoadingSlots
            || SlotsError is null
            || _retrySlotsDate is null
            || !_slotsFailureKind.IsRetryable())
        {
            return;
        }

        _slotsAutoRetry.Start(RetrySlotsAttemptAsync);
    }

    /// <summary>
    /// Запускает цикл повтора поиска, если повторяется тот же запрос.
    /// </summary>
    private void TryStartClientsAutoRetry()
    {
        if (!_isPageVisible
            || IsSearchingClients
            || ClientsError is null
            || _retryClientQuery is null
            || !_clientsFailureKind.IsRetryable()
            || !string.Equals(ClientQuery.Trim(), _retryClientQuery, StringComparison.Ordinal))
        {
            return;
        }

        _clientsAutoRetry.Start(RetryClientsAttemptAsync);
    }

    /// <summary>
    /// Одна попытка цикла: перечитать услуги и салоны, не гася причину.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetryOptionsAttemptAsync(CancellationToken cancellationToken) =>
        OptionsError is null || _optionsLoaded
            ? Task.FromResult(ApiFailureKind.None)
            : LoadOptionsAsync(silent: true, cancellationToken);

    /// <summary>
    /// Одна попытка цикла: повторить сетку с датой упавшего запроса.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetrySlotsAttemptAsync(CancellationToken cancellationToken) =>
        _isPageVisible && _retrySlotsDate is { } day && SlotsError is not null
            ? LoadSlotsCoreAsync(day, silent: true, cancellationToken)
            : Task.FromResult(ApiFailureKind.None);

    /// <summary>
    /// Одна попытка цикла: повторить ровно тот поиск, который упал.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetryClientsAttemptAsync(CancellationToken cancellationToken)
    {
        if (!_isPageVisible
            || _retryClientQuery is not { } query
            || IsSearchingClients
            || ClientsError is null
            || !string.Equals(ClientQuery.Trim(), query, StringComparison.Ordinal))
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _clientsRequest = request;

        return LoadClientsAsync(query, silent: true, request);
    }

    /// <summary>
    /// Отменяет незавершённый поиск клиента: паузу или запрос.
    /// </summary>
    private void CancelClientSearch()
    {
        var previous = _clientsRequest;

        _clientsRequest = null;

        previous?.Cancel();
        previous?.Dispose();

        IsSearchingClients = false;
    }

    /// <summary>
    /// Отменяет незавершённое чтение сетки.
    /// </summary>
    private void CancelSlotsRequest()
    {
        var previous = _slotsRequest;

        _slotsRequest = null;

        previous?.Cancel();
        previous?.Dispose();
    }

    /// <summary>
    /// Создаёт новый ключ идемпотентности для новой логической попытки.
    /// </summary>
    /// <returns>Ключ в формате GUID.</returns>
    private static string NewIdempotencyKey() => Guid.NewGuid().ToString("D");

    /// <summary>
    /// Начинает новую логическую попытку: тело запроса изменилось.
    /// </summary>
    /// <remarks>
    /// Пока запрос в полёте, ключ не меняется — иначе повтор ушёл бы с другим ключом и
    /// потенциально создал вторую бронь.
    /// </remarks>
    private void OnAttemptInputChanged()
    {
        if (IsSubmitting)
        {
            return;
        }

        _idempotencyKey = NewIdempotencyKey();

        if (SubmitError is not null)
        {
            SubmitError = null;
        }
    }

    /// <summary>
    /// Меняет режим клиента: старые контакты и подсказки не переносятся.
    /// </summary>
    /// <param name="mode">Новый режим.</param>
    private void ChangeMode(BarberClientMode mode)
    {
        if (Mode == mode || IsSubmitting)
        {
            return;
        }

        Mode = mode;

        SelectedClient = null;
        Clients.Clear();
        _clientsLoaded = false;
        ClientsError = null;
        _retryClientQuery = null;
        _clientsFailureKind = ApiFailureKind.None;

        GuestNameError = null;
        GuestPhoneError = null;

        OnAttemptInputChanged();

        if (IsRegisteredMode)
        {
            BeginClientSearch(debounce: true);
        }
        else
        {
            CancelClientSearch();
        }

        NotifyFormState();
    }

    /// <summary>
    /// Поднимает уведомления обо всём, что зависит от формы.
    /// </summary>
    private void NotifyFormState()
    {
        OnPropertyChanged(nameof(IsRegisteredMode));
        OnPropertyChanged(nameof(IsGuestMode));
        OnPropertyChanged(nameof(CanSearchClients));
        OnPropertyChanged(nameof(ClientSearchHint));
        OnPropertyChanged(nameof(ShowClientResults));
        OnPropertyChanged(nameof(ShowClientsEmpty));
        OnPropertyChanged(nameof(ShowClientsError));
        OnPropertyChanged(nameof(HasSelectedClient));
        OnPropertyChanged(nameof(SelectedClientSummary));
        OnPropertyChanged(nameof(HasGuestContacts));
        OnPropertyChanged(nameof(HasClient));
        OnPropertyChanged(nameof(ShowForm));
        OnPropertyChanged(nameof(ShowOptionsError));
        OnPropertyChanged(nameof(HasServices));
        OnPropertyChanged(nameof(HasSalons));
        OnPropertyChanged(nameof(HasSeveralSalons));
        OnPropertyChanged(nameof(OnlySalonName));
        OnPropertyChanged(nameof(SalonTimeZoneText));
        OnPropertyChanged(nameof(HasDateWindow));
        OnPropertyChanged(nameof(IsDateValid));
        OnPropertyChanged(nameof(SlotsPlaceholder));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(HasSubmitError));
        OnPropertyChanged(nameof(HasGuestNameError));
        OnPropertyChanged(nameof(HasGuestPhoneError));

        SubmitCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Поднимает уведомления о сетке слотов.
    /// </summary>
    private void NotifySlotsState()
    {
        OnPropertyChanged(nameof(ShowSlots));
        OnPropertyChanged(nameof(ShowSlotsLoading));
        OnPropertyChanged(nameof(ShowSlotsError));
        OnPropertyChanged(nameof(ShowEmptySlots));
        OnPropertyChanged(nameof(EmptySlotsText));
        OnPropertyChanged(nameof(SlotsPlaceholder));
        OnPropertyChanged(nameof(SelectedSlotTitle));
        OnPropertyChanged(nameof(SelectedSlotSubtitle));
        OnPropertyChanged(nameof(CanSubmit));

        SubmitCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Поднимает уведомления о подсказке клиентов.
    /// </summary>
    private void NotifyClientsState()
    {
        OnPropertyChanged(nameof(CanSearchClients));
        OnPropertyChanged(nameof(ClientSearchHint));
        OnPropertyChanged(nameof(ShowClientResults));
        OnPropertyChanged(nameof(ShowClientsEmpty));
        OnPropertyChanged(nameof(ShowClientsError));
    }

    /// <summary>
    /// Режим изменился: переключаются блоки контактов.
    /// </summary>
    /// <param name="value">Новый режим.</param>
    partial void OnModeChanged(BarberClientMode value)
    {
        NotifyFormState();
        NotifyClientsState();
    }

    /// <summary>
    /// Строка поиска изменилась: подсказка ищется заново с паузой.
    /// </summary>
    /// <param name="value">Новая строка.</param>
    partial void OnClientQueryChanged(string value)
    {
        OnAttemptInputChanged();
        BeginClientSearch(debounce: true);
    }

    /// <summary>
    /// Выбранный клиент изменился.
    /// </summary>
    /// <param name="value">Строка подсказки либо <c>null</c>.</param>
    partial void OnSelectedClientChanged(BarberClientRow? value)
    {
        OnAttemptInputChanged();
        NotifyFormState();
    }

    /// <summary>
    /// Имя гостя изменилось: ошибка поля снимается.
    /// </summary>
    /// <param name="value">Новое имя.</param>
    partial void OnGuestFirstNameChanged(string value)
    {
        GuestNameError = null;
        OnAttemptInputChanged();
    }

    /// <summary>
    /// Фамилия гостя изменилась.
    /// </summary>
    /// <param name="value">Новая фамилия.</param>
    partial void OnGuestLastNameChanged(string value) => OnAttemptInputChanged();

    /// <summary>
    /// Телефон гостя изменился: ошибка поля снимается.
    /// </summary>
    /// <param name="value">Новый телефон.</param>
    partial void OnGuestPhoneChanged(string value)
    {
        GuestPhoneError = null;
        OnAttemptInputChanged();
    }

    /// <summary>
    /// Ошибка поля «Имя» появилась или исчезла.
    /// </summary>
    /// <param name="value">Текст ошибки.</param>
    partial void OnGuestNameErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasGuestNameError));

    /// <summary>
    /// Ошибка поля «Телефон» появилась или исчезла.
    /// </summary>
    /// <param name="value">Текст ошибки.</param>
    partial void OnGuestPhoneErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasGuestPhoneError));

    /// <summary>
    /// Услуга изменилась: сетка строится заново от её длительности.
    /// </summary>
    /// <param name="value">Новая услуга либо <c>null</c>.</param>
    partial void OnSelectedServiceChanged(BarberServiceItem? value)
    {
        if (_isApplyingOptions)
        {
            return;
        }

        OnAttemptInputChanged();
        SelectedSlot = null;
        _slotsOperation = ReloadSlotsAsync();
        NotifyFormState();
    }

    /// <summary>
    /// Салон изменился: пересчитываются пояс, окно дат и сетка.
    /// </summary>
    /// <param name="value">Новый салон либо <c>null</c>.</param>
    partial void OnSelectedSalonChanged(BarberSalonOption? value)
    {
        if (_isApplyingOptions)
        {
            return;
        }

        ApplySalonWindow();
        OnAttemptInputChanged();
        _slotsOperation = ReloadSlotsAsync();
        NotifyFormState();
    }

    /// <summary>
    /// Дата изменилась: сетка читается заново, выбор времени сбрасывается.
    /// </summary>
    /// <param name="value">Новая дата.</param>
    partial void OnSelectedDateChanged(DateTime value)
    {
        if (_isApplyingDate)
        {
            return;
        }

        var clamped = ClampDate(value);

        if (clamped != value)
        {
            _isApplyingDate = true;

            try
            {
                SelectedDate = clamped;
            }
            finally
            {
                _isApplyingDate = false;
            }
        }

        OnAttemptInputChanged();
        SelectedSlot = null;
        _slotsOperation = ReloadSlotsAsync();
        NotifyFormState();
    }

    /// <summary>
    /// Выбранный слот изменился.
    /// </summary>
    /// <param name="value">Слот либо <c>null</c>.</param>
    partial void OnSelectedSlotChanged(BarberSlotOption? value)
    {
        OnAttemptInputChanged();
        NotifySlotsState();
        NotifyFormState();
    }

    /// <summary>
    /// Комментарий изменился: счётчик и ключ попытки.
    /// </summary>
    /// <param name="value">Новый текст.</param>
    partial void OnNoteChanged(string value)
    {
        OnAttemptInputChanged();
        OnPropertyChanged(nameof(NoteCounterText));
    }

    /// <summary>
    /// Чтение услуг и салонов началось или закончилось.
    /// </summary>
    /// <param name="value">Идёт чтение.</param>
    partial void OnIsLoadingOptionsChanged(bool value) => NotifyFormState();

    /// <summary>
    /// Причина неудачи чтения услуг и салонов появилась или исчезла.
    /// </summary>
    /// <param name="value">Текст причины.</param>
    partial void OnOptionsErrorChanged(string? value) => NotifyFormState();

    /// <summary>
    /// Поиск клиентов начался или закончился.
    /// </summary>
    /// <param name="value">Идёт поиск.</param>
    partial void OnIsSearchingClientsChanged(bool value) => NotifyClientsState();

    /// <summary>
    /// Причина неудачи поиска клиентов появилась или исчезла.
    /// </summary>
    /// <param name="value">Текст причины.</param>
    partial void OnClientsErrorChanged(string? value) => NotifyClientsState();

    /// <summary>
    /// Чтение сетки началось или закончилось.
    /// </summary>
    /// <param name="value">Идёт чтение.</param>
    partial void OnIsLoadingSlotsChanged(bool value) => NotifySlotsState();

    /// <summary>
    /// Причина неудачи чтения сетки появилась или исчезла.
    /// </summary>
    /// <param name="value">Текст причины.</param>
    partial void OnSlotsErrorChanged(string? value) => NotifySlotsState();

    /// <summary>
    /// Признак рабочего дня изменился: меняется текст пустого дня.
    /// </summary>
    /// <param name="value">Рабочий ли день.</param>
    partial void OnIsWorkingDayChanged(bool value) => NotifySlotsState();

    /// <summary>
    /// Сетка загрузилась: переключаются пустое состояние и индикатор.
    /// </summary>
    /// <param name="value">Сетка загружена.</param>
    partial void OnIsDayLoadedChanged(bool value) => NotifySlotsState();

    /// <summary>
    /// Отправка началась или закончилась: кнопка меняет подпись.
    /// </summary>
    /// <param name="value">Идёт отправка.</param>
    partial void OnIsSubmittingChanged(bool value)
    {
        NotifyFormState();
        NotifySlotsState();
    }

    /// <summary>
    /// Ошибка отправки появилась или исчезла.
    /// </summary>
    /// <param name="value">Текст ошибки.</param>
    partial void OnSubmitErrorChanged(string? value) => OnPropertyChanged(nameof(HasSubmitError));

    /// <summary>
    /// Переключает строку «Пробуем снова…» услуг и салонов вслед за циклом.
    /// </summary>
    /// <param name="sender">Цикл повтора.</param>
    /// <param name="e">Аргументы события.</param>
    private void OnOptionsAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetryingOptions = _optionsAutoRetry.IsRunning;

    /// <summary>
    /// Переключает строку «Пробуем снова…» сетки вслед за циклом.
    /// </summary>
    /// <param name="sender">Цикл повтора.</param>
    /// <param name="e">Аргументы события.</param>
    private void OnSlotsAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetryingSlots = _slotsAutoRetry.IsRunning;

    /// <summary>
    /// Переключает строку «Пробуем снова…» поиска вслед за циклом.
    /// </summary>
    /// <param name="sender">Цикл повтора.</param>
    /// <param name="e">Аргументы события.</param>
    private void OnClientsAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetryingClients = _clientsAutoRetry.IsRunning;
}
