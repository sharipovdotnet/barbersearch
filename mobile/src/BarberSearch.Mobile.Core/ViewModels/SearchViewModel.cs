using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Location;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран поиска: фильтры витрины, справочники, список найденных мастеров и состояние загрузки.
/// </summary>
/// <remarks>
/// <para>
/// Набор фильтров повторяет веб (<c>frontend/src/components/home/search-form.tsx</c>): город,
/// дата, язык, специализация, услуга справочника и дополнительная услуга. Справочники
/// специализаций и услуг грузятся из API один раз; их ошибка не блокирует город, дату и язык —
/// как в вебе, где справочник лишь скрывает свой селект.
/// </para>
/// <para>
/// ViewModel не знает про MAUI: ни про страницы, ни про <c>Dispatcher</c>. Это позволяет
/// проверять его обычным xunit без эмулятора. Состояний ровно три — грузим, показали список,
/// показали ошибку; пустой результат — это список без элементов и подсказка из веба.
/// </para>
/// <para>
/// Поиск уходит только по нажатию «Найти»: правка фильтра ничего не перезапрашивает.
/// Исключение — подсказки города: они читаются по паузе в наборе, но выдачу не трогают
/// (<see cref="CitySuggestions"/>).
/// </para>
/// <para>
/// Пока экран виден, повторяемый сбой выдачи лечится сам: сеть, <c>5xx</c> и таймаут клиента
/// повторяются с нарастающей паузой (<see cref="Api.AutoRetryLoop"/>), а под причиной ошибки
/// видна строка «Пробуем снова…». «Повторить» остаётся и означает «повторить сейчас»:
/// останавливает цикл, читает сразу и запускает его снова, только если сбой снова повторяемый.
/// Окончательный отказ, отмена и уход со страницы повторов не запускают.
/// </para>
/// <para>
/// Тихий повтор повторяет ровно тот запрос, который упал, а не свежий из формы: пока видна
/// ошибка, правка полей остаётся в форме, и в сеть она уйдёт только по «Найти».
/// </para>
/// <para>
/// Справочники фильтров — отдельное чтение со своей ошибкой, поэтому у них свой, второй цикл,
/// поднимаемый тем же сигналом видимости. Сбои не связаны: падение справочников не запускает
/// повтор выдачи, а сбой выдачи не трогает справочники. Повтор несёт снимок того, какие из
/// двух запросов упали: частичный успех сохраняется, и заново запрашивается только упавшая
/// часть. <c>429</c> в справочниках окончателен — лимитер не лечится повтором, и его текст
/// «Слишком много запросов…» остаётся на экране.
/// </para>
/// </remarks>
public sealed partial class SearchViewModel : ObservableObject
{
    /// <summary>
    /// Горизонт записи приложения в днях: верхняя граница календаря.
    /// </summary>
    /// <remarks>
    /// Веб не ограничивает дату сверху, а календарь на телефоне останавливается на горизонте:
    /// дальше него у салонов нет слотов, и день, на который записаться некуда, только сбивал бы
    /// с толку. Отклонение записано в <c>AGENT.md</c> §5.
    /// </remarks>
    private const int DateHorizonDays = 30;

    /// <summary>
    /// Город, который фильтр получает, когда геопозицию определить не удалось.
    /// </summary>
    /// <remarks>
    /// «Москва» — город по умолчанию продукта, а не перевод: сервер сравнивает
    /// <c>Salon.City</c> точно, поэтому название должно совпадать с данными салонов даже
    /// в английском интерфейсе и не проходит через <c>Loc.T</c>.
    /// </remarks>
    public const string DefaultCity = "Москва";

    private static string AnyLanguageText => Loc.T("Любой язык");
    private static string AnySpecializationText => Loc.T("Любая специализация");
    private static string AnyServiceText => Loc.T("Любая услуга");
    private static string LoadingText => Loc.T("Загрузка…");
    private static string CatalogsLoadFailedText => Loc.T("Не удалось загрузить фильтры.");
    private static string SearchFailedText => Loc.T("Не удалось загрузить мастеров.");
    private static string NetworkText => Loc.T("Нет связи с сервером. Проверьте подключение и повторите попытку.");
    private static string RateLimitText => Loc.T("Слишком много запросов. Подождите минуту и повторите.");
    private static string NoSearchHintText => Loc.T("Введите город и нажмите «Найти»");
    private static string EmptyCityTitleText => Loc.T("В этом городе никого не нашли");
    private static string EmptyCityText => Loc.T("Проверьте написание — город учитывается точно, например «Москва».");
    private static string EmptyDateTitleText => Loc.T("На выбранную дату никого не нашли");
    private static string EmptyDateText => Loc.T("Фильтр оставляет мастеров с рабочим днём в этот день недели. Попробуйте другую дату.");
    private static string EmptyFiltersTitleText => Loc.T("С такими фильтрами никого не нашли");
    private static string EmptyFiltersText => Loc.T("Попробуйте другой язык, специализацию, услугу или дополнительную услугу.");
    private static string EmptyNoBarbersTitleText => Loc.T("Мастеров пока нет");
    private static string EmptyNoBarbersText => Loc.T("Пока никто не подтверждён. Загляните позже.");
    private static string LocationDeniedText => Loc.T(
        "Геопозиция недоступна — показываем всех мастеров. Разрешите доступ в настройках телефона.");
    private static string LocationUnavailableText => Loc.T(
        "Не удалось определить геопозицию — показываем всех мастеров.");

    private readonly IBarberSearchApi _api;
    private readonly ILocationService _locationService;
    private readonly ICityLocator _cityLocator;
    private readonly TimeProvider _timeProvider;

    private bool _isInitialized;
    private bool _hasSearched;
    private BarberSearchFilters? _appliedFilters;
    private DateOnly _dateBoundsToday;

    // Геопозиция читается один раз на экран: после отказа система не дёргается на каждый
    // «Найти», иначе Android показывал бы диалог разрешения повторно. «Повторить» в подсказке
    // сбрасывает этот флаг и пробует снова.
    private bool _locationAttempted;
    private GeoPosition? _position;
    private LocationStatus? _locationStatus;

    /// <summary>
    /// Чтение геопозиции в полёте: его делят автоподстановка города и «Найти».
    /// </summary>
    /// <remarks>
    /// Без общего чтения два потребителя, стартовавшие одновременно, спросили бы разрешение
    /// и GPS дважды: Android не любит одновременные запросы разрешения, а человек увидел бы
    /// второй диалог поверх первого.
    /// </remarks>
    private Task<LocationResult>? _locationRead;

    /// <summary>
    /// Автоподстановка города уже запускалась: экран читает геопозицию один раз за жизнь.
    /// </summary>
    private bool _cityFillStarted;

    /// <summary>
    /// Город, который экран подставил сам: его можно заменить новым ответом геокодера,
    /// а введённый человеком — нельзя.
    /// </summary>
    private string? _automaticCity;

    /// <summary>
    /// Программная подстановка города не должна открывать подсказки под полем.
    /// </summary>
    private bool _suppressCitySuggestions;

    private readonly AutoRetryLoop _autoRetry;
    private readonly AutoRetryLoop _catalogsAutoRetry;

    /// <summary>
    /// Последний загруженный справочник услуг: из него пересобираются группы при смене
    /// специализации.
    /// </summary>
    /// <remarks>
    /// Хранится ответ API, а не пункты <see cref="SearchHairServiceGroup"/>: сужение по
    /// специализации — это повторная раскладка одного и того же ответа, а не новый запрос.
    /// </remarks>
    private IReadOnlyList<HairServiceResponse> _hairServices = [];

    /// <summary>
    /// Последний загруженный справочник специализаций: он же задаёт заголовки и порядок групп.
    /// </summary>
    /// <remarks>
    /// Оба справочника грузятся параллельно, и порядок ответов не определён: группы собираются
    /// из того, что уже пришло, и пересобираются после каждого ответа.
    /// </remarks>
    private IReadOnlyList<SpecializationResponse> _specializations = [];

    /// <summary>
    /// Пункт «Любая услуга» / «Загрузка…»: к нему возвращается выбор, когда услуга не выбрана.
    /// </summary>
    /// <remarks>
    /// Живёт вне <see cref="HairServiceGroups"/>: «Любая услуга» — отсутствие выбора, а не
    /// услуга какой-то специализации, и в сгруппированном списке ей не место.
    /// </remarks>
    private SearchCatalogOption _anyHairServiceOption = SearchCatalogOption.Any(AnyServiceText);

    /// <summary>
    /// Фильтры последней неудачной попытки: цикл повторов обязан повторить именно этот запрос.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Пока на экране ошибка, человек может править город, дату, язык и услуги. Если бы тихая
    /// попытка собирала фильтры из формы заново, она отправила бы эту правку, хотя по
    /// <c>AGENT.md</c> §5 фильтры уходят в запрос только по «Найти». Поэтому повтор берёт
    /// снимок отправленного запроса целиком: и значения полей, и координаты, которые к нему
    /// приложила геопозиция.
    /// </para>
    /// <para>
    /// Снимок живёт ровно пока есть повторяемый сбой: успех и окончательный отказ его забывают,
    /// а новый ручной поиск начинает с чистого листа и запоминает свои фильтры, если упадёт
    /// повторяемо.
    /// </para>
    /// </remarks>
    private BarberSearchFilters? _retryFilters;

    /// <summary>
    /// Вид последнего сбоя выдачи: по нему <see cref="StartAutoRetry"/> решает, повторять ли
    /// запрос сам.
    /// </summary>
    private ApiFailureKind _lastFailureKind;

    /// <summary>
    /// Какие запросы справочников упали и должны быть повторены.
    /// </summary>
    /// <remarks>
    /// Снимок для второго цикла: загрузка запрашивает оба справочника, а повтор — только
    /// упавший. Частичный успех сохраняется: удавшаяся часть остаётся на экране и из снимка
    /// уходит, иначе каждый повтор заново дёргал бы оба запроса и терял уже полученные данные.
    /// </remarks>
    private bool _catalogsNeedsSpecializations = true;

    /// <summary>
    /// Упавший справочник услуг — вторая половина того же снимка.
    /// </summary>
    private bool _catalogsNeedsHairServices = true;

    /// <summary>
    /// Вид последнего сбоя справочников: по нему второй цикл решает, повторять ли запрос.
    /// </summary>
    /// <remarks>
    /// Запоминается для каждого неуспеха, а не только для повторяемого: иначе «транспорт,
    /// потом 404» оставил бы в памяти старый <see cref="ApiFailureKind.Transport"/>, и кнопка
    /// запустила бы цикл на отказе, который не изменится.
    /// </remarks>
    private ApiFailureKind _catalogsFailureKind;

    /// <summary>
    /// Страница сейчас видна: без этого сигнала цикл повторов не запускается.
    /// </summary>
    private bool _isPageVisible;

    /// <summary>
    /// Создаёт ViewModel поиска.
    /// </summary>
    /// <param name="api">Чтения API: поиск и справочники фильтров.</param>
    /// <param name="locationService">
    /// Геопозиция для сортировки «сначала ближайшие»; отказ и недоступность не блокируют поиск.
    /// </param>
    /// <param name="timeProvider">
    /// Текущий день устройства: от него отсчитываются границы календаря, поэтому тест задаёт его сам.
    /// </param>
    /// <param name="citySuggestions">
    /// Подсказки города под полем; живут рядом с экраном, потому что повторяют его судьбу.
    /// </param>
    /// <param name="cityLocator">
    /// Название города по координатам: им заполняется поле «Город» при первом показе
    /// вкладки; <c>null</c> от него означает «город по умолчанию».
    /// </param>
    /// <param name="autoRetry">
    /// Цикл автоповтора выдачи; <c>null</c> — собственный цикл с обычным расписанием. Параметр
    /// необязательный, чтобы существующие позиционные вызовы (DI и тесты) не менялись.
    /// </param>
    /// <param name="catalogsAutoRetry">
    /// Второй цикл — для справочников фильтров; <c>null</c> — собственный цикл с обычным
    /// расписанием. Он стоит последним и необязателен по той же причине, что и первый: выдача
    /// и справочники повторяются независимо, и общий цикл связал бы их сбои.
    /// </param>
    public SearchViewModel(
        IBarberSearchApi api,
        ILocationService locationService,
        TimeProvider timeProvider,
        CitySuggestions citySuggestions,
        ICityLocator cityLocator,
        AutoRetryLoop? autoRetry = null,
        AutoRetryLoop? catalogsAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(locationService);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(citySuggestions);
        ArgumentNullException.ThrowIfNull(cityLocator);

        _api = api;
        _locationService = locationService;
        _timeProvider = timeProvider;
        _cityLocator = cityLocator;
        CitySuggestions = citySuggestions;

        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;

        _catalogsAutoRetry = catalogsAutoRetry ?? new AutoRetryLoop();
        _catalogsAutoRetry.StateChanged += OnCatalogsAutoRetryStateChanged;

        FillLanguages();
        RefreshDateBounds();
        ShowCatalogPlaceholders();
    }

    /// <summary>
    /// Поиск завершён — успехом или ошибкой: страница прокручивает к результатам.
    /// </summary>
    /// <remarks>
    /// Событие нужно вёрстке: сама прокрутка — платформенная операция, а Core про неё не знает.
    /// </remarks>
    public event EventHandler? SearchCompleted;

    /// <summary>
    /// Подсказки города под полем «Город»: список, пустой ответ и тихий отказ.
    /// </summary>
    /// <remarks>
    /// Отдельная секция, а не поля экрана: у подсказок своя жизнь — дебаунс, отмена запроса и
    /// закрытие по выбору, — и держать её рядом с фильтрами и выдачей значило бы смешать два
    /// состояния в одном файле. Экран по-прежнему один, <see cref="CitySuggestions"/> не второй
    /// <c>BindingContext</c>: страница биндится к <see cref="SearchViewModel"/>, а к подсказкам
    /// обращается через это свойство.
    /// </remarks>
    public CitySuggestions CitySuggestions { get; }

    /// <summary>
    /// Найденные мастера в порядке выдачи сервера.
    /// </summary>
    public ObservableCollection<BarberSearchItemResponse> Barbers { get; } = [];

    /// <summary>
    /// Специализации для фильтра: «Любая специализация» первой, дальше порядок сервера.
    /// </summary>
    public ObservableCollection<SearchCatalogOption> Specializations { get; } = [];

    /// <summary>
    /// Услуги справочника для фильтра, сгруппированные по специализациям.
    /// </summary>
    /// <remarks>
    /// Группы собирает <see cref="SearchServiceGroups.Build"/>: выбранная специализация сужает
    /// список до её услуг, а услуги с неизвестной специализацией остаются в последней группе
    /// «Прочие услуги». Пункта «Любая услуга» в списке нет — это отсутствие выбора
    /// (<see cref="SelectedHairService"/>).
    /// </remarks>
    public ObservableCollection<SearchHairServiceGroup> HairServiceGroups { get; } = [];

    /// <summary>
    /// Языки общения для фильтра: «Любой язык» первым, дальше шесть языков API.
    /// </summary>
    public ObservableCollection<SearchLanguageOption> Languages { get; } = [];

    /// <summary>
    /// Город поиска; пусто — сервер не фильтрует.
    /// </summary>
    [ObservableProperty]
    private string _city = string.Empty;

    /// <summary>
    /// Подстрока дополнительной услуги; пусто — сервер не фильтрует.
    /// </summary>
    [ObservableProperty]
    private string _additionalService = string.Empty;

    /// <summary>
    /// Выбранный язык; пункт «Любой язык» отправляет <c>null</c>.
    /// </summary>
    [ObservableProperty]
    private SearchLanguageOption? _selectedLanguage;

    /// <summary>
    /// Выбранная специализация; пункт «Любая специализация» отправляет <c>null</c>.
    /// </summary>
    [ObservableProperty]
    private SearchCatalogOption? _selectedSpecialization;

    /// <summary>
    /// Выбранная услуга справочника; пункт «Любая услуга» отправляет <c>null</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HairServiceFieldText))]
    private SearchCatalogOption? _selectedHairService;

    /// <summary>
    /// Подпись поля «Услуга»: название выбранной услуги или «Любая услуга».
    /// </summary>
    /// <remarks>
    /// Отдельное свойство нужно потому, что «Любая услуга» больше не строка
    /// <see cref="HairServiceGroups"/>: поле показывает текст выбора, а не пункт сгруппированного
    /// списка.
    /// </remarks>
    public string HairServiceFieldText => SelectedHairService?.DisplayText ?? AnyServiceText;

    /// <summary>
    /// Ставит выбранную услугу, пришедшую с экрана выбора услуги.
    /// </summary>
    /// <remarks>
    /// Экран выбора показывает сгруппированный список и возвращает выбранную строку; отдельного
    /// <c>Picker</c> здесь нет, поэтому выбор приходит из code-behind страницы, а не командой.
    /// <c>null</c> означает «Любая услуга» — отсутствие фильтра.
    /// </remarks>
    /// <param name="service">Выбранная услуга; <c>null</c> — «Любая услуга».</param>
    public void SelectHairService(SearchCatalogOption? service) =>
        SelectedHairService = service ?? _anyHairServiceOption;

    /// <summary>
    /// Выбранная дата в календаре; <c>null</c> — «Любая дата», фильтр не задан.
    /// </summary>
    /// <remarks>
    /// Нативный <c>DatePicker</c> в MAUI 10 принимает пустое значение, поэтому отдельного
    /// пункта «Любая дата» больше не нужно: пустое поле и есть отсутствие фильтра.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDate))]
    [NotifyPropertyChangedFor(nameof(IsAnyDate))]
    private DateTime? _selectedDate;

    /// <summary>
    /// Выбрана ли дата: от этого зависит видимость кнопки «×».
    /// </summary>
    public bool HasSelectedDate => SelectedDate is not null;

    /// <summary>
    /// Пора ли показать «Любая дата»: нативный <c>DatePicker</c> не рисует подпись для пустого
    /// значения, поэтому её показывает отдельный <c>Label</c> на месте поля.
    /// </summary>
    public bool IsAnyDate => SelectedDate is null;

    /// <summary>
    /// Нижняя граница календаря — сегодняшний день устройства.
    /// </summary>
    /// <remarks>
    /// Прошедшие дни недоступны: записаться на них нельзя, и хотя веб ввод не ограничивает,
    /// листать назад на телефоне незачем.
    /// </remarks>
    public DateTime MinimumDate { get; private set; }

    /// <summary>
    /// Верхняя граница календаря — горизонт записи приложения: сегодня + 30 дней.
    /// </summary>
    /// <remarks>
    /// Веб не ограничивает дату сверху, но дальше горизонта у салонов нет слотов, поэтому
    /// календарь останавливается здесь, а не предлагает день, на который записаться некуда.
    /// </remarks>
    public DateTime MaximumDate { get; private set; }

    /// <summary>
    /// Возвращает фильтр к «Любая дата»: «×» в поле снимает выбранный день.
    /// </summary>
    [RelayCommand]
    private void ClearDate() => SelectedDate = null;

    /// <summary>
    /// Идёт запрос выдачи: кнопка блокируется, в результатах виден индикатор.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isLoading;

    /// <summary>
    /// Идёт чтение геопозиции: экран объясняет задержку перед запросом выдачи.
    /// </summary>
    [ObservableProperty]
    private bool _isLocating;

    /// <summary>
    /// Идёт автоподстановка города: под полем «Город» видно, почему он ещё не заполнен.
    /// </summary>
    /// <remarks>
    /// Отдельный признак, а не <see cref="IsLocating"/>: тот объясняет задержку «Найти»,
    /// а этот — пустое поле при первом показе вкладки.
    /// </remarks>
    [ObservableProperty]
    private bool _isCityLocating;

    /// <summary>
    /// Подсказка о том, что геопозиции нет; <c>null</c>, когда сортировка по расстоянию работает
    /// или поиск ещё не запускался.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocationHint))]
    private string? _locationHint;

    /// <summary>
    /// Текст ошибки поиска для пользователя; <c>null</c>, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private string? _errorMessage;

    /// <summary>
    /// Цикл сам повторяет выдачу: под причиной ошибки видна строка «Пробуем снова…».
    /// </summary>
    /// <remarks>
    /// Состояние цикла, а не экрана: свойство переключается подпиской на
    /// <see cref="AutoRetryLoop.StateChanged"/> и ничего не решает само.
    /// </remarks>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Идёт загрузка справочников фильтров.
    /// </summary>
    [ObservableProperty]
    private bool _isCatalogsLoading = true;

    /// <summary>
    /// Второй цикл повторяет справочники: под их ошибкой видна строка «Пробуем снова…».
    /// </summary>
    /// <remarks>
    /// Отдельно от <see cref="IsAutoRetrying"/>: это второй цикл, и его строка живёт в блоке
    /// справочников, а не в блоке выдачи. Свойство переключается подпиской на
    /// <see cref="AutoRetryLoop.StateChanged"/> и ничего не решает само.
    /// </remarks>
    [ObservableProperty]
    private bool _isCatalogsAutoRetrying;

    /// <summary>
    /// Текст ошибки загрузки справочников; <c>null</c>, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCatalogsError))]
    [NotifyPropertyChangedFor(nameof(IsCatalogsEnabled))]
    [NotifyPropertyChangedFor(nameof(ShowSpecializationPicker))]
    [NotifyPropertyChangedFor(nameof(ShowHairServicePicker))]
    private string? _catalogsErrorText;

    /// <summary>
    /// Есть ли ошибка поиска, которую надо показать.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Есть ли ошибка загрузки справочников, которую надо показать.
    /// </summary>
    public bool HasCatalogsError => CatalogsErrorText is not null;

    /// <summary>
    /// Можно ли выбирать значения справочников: пока они не загрузились или упали — нет.
    /// </summary>
    public bool IsCatalogsEnabled => !IsCatalogsLoading && !HasCatalogsError;

    /// <summary>
    /// Виден ли селект специализации: пока справочник не готов — да, пустой — нет.
    /// </summary>
    /// <remarks>
    /// Пустой справочник скрывает свой селект, как в вебе, чтобы не висел мёртвый список.
    /// </remarks>
    public bool ShowSpecializationPicker =>
        Specializations.Count > 1 || IsCatalogsLoading || HasCatalogsError;

    /// <summary>
    /// Виден ли селект услуги: пока справочник не готов — да, пустой — нет.
    /// </summary>
    /// <remarks>
    /// Проверяется весь загруженный справочник, а не собранные группы, как в вебе
    /// (<c>showHairServices</c>): выбранная специализация может сузить список до нуля услуг,
    /// но поле «Услуга» остаётся на месте и показывает «Любая услуга» — форма не прыгает,
    /// а состояние выбора не становится недоступным.
    /// </remarks>
    public bool ShowHairServicePicker =>
        _hairServices.Count > 0 || IsCatalogsLoading || HasCatalogsError;

    /// <summary>
    /// Нужно ли показать пустое состояние: не грузим, ошибки нет, список пуст.
    /// </summary>
    /// <remarks>
    /// До первого поиска это тоже оно: подсказка «Введите город и нажмите «Найти»».
    /// </remarks>
    public bool ShowEmptyState => !IsLoading && !HasError && Barbers.Count == 0;

    /// <summary>
    /// Есть ли заголовок пустой выдачи: до первого поиска его нет, есть только подсказка.
    /// </summary>
    public bool HasEmptyTitle => EmptyTitle.Length > 0;

    /// <summary>
    /// Заголовок пустой выдачи по последним отправленным фильтрам.
    /// </summary>
    /// <remarks>
    /// Порядок подсказок — как в вебе: город и дата понятнее пользователю, поэтому их
    /// подсказки приоритетнее общего «с такими фильтрами».
    /// </remarks>
    public string EmptyTitle
    {
        get
        {
            if (!_hasSearched)
            {
                return string.Empty;
            }

            var filters = _appliedFilters ?? BarberSearchFilters.Empty;

            if (!string.IsNullOrWhiteSpace(filters.City))
            {
                return EmptyCityTitleText;
            }

            if (filters.Date is not null)
            {
                return EmptyDateTitleText;
            }

            return HasChoiceFilters(filters) ? EmptyFiltersTitleText : EmptyNoBarbersTitleText;
        }
    }

    /// <summary>
    /// Пояснение пустой выдачи по последним отправленным фильтрам.
    /// </summary>
    public string EmptyDescription
    {
        get
        {
            if (!_hasSearched)
            {
                return NoSearchHintText;
            }

            var filters = _appliedFilters ?? BarberSearchFilters.Empty;

            if (!string.IsNullOrWhiteSpace(filters.City))
            {
                return EmptyCityText;
            }

            if (filters.Date is not null)
            {
                return EmptyDateText;
            }

            return HasChoiceFilters(filters) ? EmptyFiltersText : EmptyNoBarbersText;
        }
    }

    /// <summary>
    /// Виден ли счётчик найденных.
    /// </summary>
    public bool ShowResultsCount => Barbers.Count > 0;

    /// <summary>
    /// Есть ли подсказка о недоступной геопозиции.
    /// </summary>
    public bool HasLocationHint => LocationHint is not null;

    /// <summary>
    /// Показан ли список в порядке «сначала ближайшие».
    /// </summary>
    /// <remarks>
    /// Подпись появляется только у непустой выдачи: пустой экран уже объясняет себя
    /// подсказкой, и «сначала ближайшие» над ним ничего не добавляет. Кроме того, нужен хотя бы
    /// один мастер с расстоянием: сервер, который ещё не знает координат, вернул бы прежний
    /// порядок по рейтингу, и подпись обманывала бы.
    /// </remarks>
    public bool ShowNearbySort => _position is not null
        && Barbers.Any(barber => barber.DistanceKm is not null);

    /// <summary>
    /// Счётчик найденных: «Показаны 2 мастера».
    /// </summary>
    public string ResultsCountText => BarberText.ShownBarbers(Barbers.Count);

    /// <summary>
    /// Готовит экран к показу: пересчитывает границы календаря и один раз грузит справочники.
    /// </summary>
    /// <remarks>
    /// Вызывается при каждом появлении вкладки. Границы календаря пересчитываются, если
    /// сменился день — приложение могло висеть в фоне, — а справочники только при первом
    /// показе: их ошибка лечится кнопкой «Повторить» и вторым циклом автоповтора, а не
    /// повторным заходом на вкладку. После неудачной загрузки цикл справочников поднимается
    /// здесь же: страница сообщила «видна» до неё, и сбой обязан включить повтор сразу.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        RefreshDateBounds();

        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        // Город подставляется параллельно справочникам: диалог разрешения не должен
        // задерживать загрузку фильтров, а поле заполнится, как только придёт ответ.
        CityFillCompletion = FillCityFromLocationAsync(cancellationToken);

        await LoadCatalogsAsync(silent: false, cancellationToken);

        TryStartCatalogsAutoRetry();
    }

    /// <summary>
    /// Завершается, когда автоподстановка города закончилась: тесты ждут его вместо пауз.
    /// </summary>
    /// <remarks>
    /// Приложение его не читает: поле заполняется само, а поиск не обязан ждать геокодер.
    /// </remarks>
    internal Task CityFillCompletion { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Сообщает, что страница видна, и запускает автоповтор, если показывать есть что.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Зовётся страницей при каждом появлении и каждом возврате из фона. Метод идемпотентен:
    /// повторный вызов при работающем цикле ничего не делает, а без повторяемого сбоя лишь
    /// запоминает, что страница видна. Иначе говоря, «видна» — это сигнал, а не команда:
    /// решение о повторе принимает вид последнего сбоя.
    /// </para>
    /// <para>
    /// Запуск и остановка обязаны идти из UI-потока: <see cref="AutoRetryLoop.StateChanged"/>
    /// синхронно меняет привязанное <see cref="IsAutoRetrying"/>. Поэтому метод зовут только
    /// из жизненного цикла страницы, а не из фонового <c>catch</c>.
    /// </para>
    /// </remarks>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartAutoRetry();
        TryStartCatalogsAutoRetry();
    }

    /// <summary>
    /// Сообщает, что страница скрыта, и останавливает автоповтор.
    /// </summary>
    /// <remarks>
    /// Идемпотентен: остановка уже стоящего цикла ничего не делает. Пауза, запрос в полёте и
    /// завершение цикла отменяются, а <see cref="IsAutoRetrying"/> сразу становится
    /// <c>false</c> — экран не показывает работающий повтор поверх ушедшей страницы.
    /// Останавливаются оба цикла: сигнал видимости у них общий, а чтения независимы.
    /// </remarks>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        StopAutoRetryLoop();
        StopCatalogsAutoRetryLoop();
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
    /// Завершается, когда останавливается второй цикл — повтора справочников.
    /// </summary>
    internal Task CatalogsAutoRetryCompletion => _catalogsAutoRetry.Completion;

    /// <summary>
    /// Второй цикл: тестам нужен счётчик попыток, приложению — нет.
    /// </summary>
    internal AutoRetryLoop CatalogsAutoRetry => _catalogsAutoRetry;

    /// <summary>
    /// Подставляет выбранную подсказку в фильтр города.
    /// </summary>
    /// <remarks>
    /// В поле идёт голое имя города (<see cref="CitySuggestionResponse.City"/>), а не подпись из
    /// списка: сервер сравнивает город точно, поэтому «Екатеринбург» из подсказки находит салоны,
    /// а «г. Екатеринбург» — уже нет. Список закрывается здесь, клавиатуру убирает страница:
    /// это платформенная операция, и Core про неё не знает.
    /// </remarks>
    /// <param name="suggestion">Подсказка, по которой нажали.</param>
    [RelayCommand]
    private void UseCitySuggestion(CitySuggestionResponse suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        CitySuggestions.Select(suggestion);

        if (!string.Equals(City, suggestion.City, StringComparison.Ordinal))
        {
            City = suggestion.City;
        }
    }

    /// <summary>
    /// Загружает первую страницу выдачи по текущим фильтрам — вручную, по кнопке «Найти».
    /// </summary>
    /// <remarks>
    /// <para>
    /// Перед запросом список очищается: старые карточки под новым фильтром — ложь, так же
    /// поступает веб. Ошибки API и сети сведены классификатором к одному виду: отмена — тихий
    /// выход, «нет связи» и отказ сервера — свои тексты для человека.
    /// </para>
    /// <para>
    /// Ручной поиск останавливает автоповтор: человек сам попросил свежий результат, и два
    /// запроса одновременно ему не нужны. После ответа цикл запускается заново, только если
    /// сбой повторяемый и страница всё ещё видна.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        // Список подсказок не переживает отправку формы: он относится к вводу, а не к выдаче.
        CitySuggestions.Close();

        StopAutoRetryLoop();

        // Новый ручной поиск забывает снимок прошлой неудачи: повторять можно только то, что
        // уйдёт в этом запросе, а не смесь новой формы со старым сбоем.
        _retryFilters = null;

        // Город приходит из той же геопозиции, что и координаты: «Найти», нажатое во время
        // обратного геокодирования, иначе ушло бы без города, который появится в поле
        // через секунду, и выдача не совпала бы с фильтром на экране.
        await CityFillCompletion;

        await LoadResultsAsync(BuildFilters(), silent: false, cancellationToken);

        TryStartAutoRetry();
    }

    /// <summary>
    /// Читает выдачу и раскладывает её по экрану; в тихом режиме — без мигания.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ручной режим (<paramref name="silent"/> = <c>false</c>) показывает индикатор и очищает
    /// старые карточки до ответа. Тихий режим — попытка цикла автоповтора: строки и текст
    /// ошибки остаются на экране, пока не придёт ответ, иначе каждая пауза между попытками
    /// мигала бы пустым экраном.
    /// </para>
    /// <para>
    /// Геопозиция читается до ручного запроса и только один раз на экран. Отказ или отсутствие
    /// GPS не блокируют поиск: запрос уходит без координат, а экран показывает подсказку
    /// с «Повторить». Тихий повтор не читает геопозицию заново: он несёт снимок уже
    /// отправленного запроса — с координатами, если они были, и без них, если их не было.
    /// </para>
    /// <para>
    /// Повторяемый сбой запоминает отправленные фильтры целиком, а успех и окончательный отказ
    /// этот снимок забывают: только он и имеет право уйти в следующую тихую попытку.
    /// </para>
    /// </remarks>
    /// <param name="filters">
    /// Фильтры запроса: ручной режим собирает их из формы, тихий приносит снимок прошлой неудачи.
    /// </param>
    /// <param name="silent">Тихая попытка цикла: не показывать индикатор и не чистить экран.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private async Task<ApiFailureKind> LoadResultsAsync(
        BarberSearchFilters filters,
        bool silent,
        CancellationToken cancellationToken)
    {
        if (!silent)
        {
            IsLoading = true;
            ErrorMessage = null;
            Barbers.Clear();
            NotifyResultsChanged();
        }

        // Снимок того, что реально ушло в сеть: координаты прикладываются уже после сборки
        // фильтров, и повтор обязан помнить их вместе с полями формы.
        var sentFilters = filters;
        var kind = ApiFailureKind.None;

        try
        {
            if (!silent)
            {
                filters = await ApplyLocationAsync(filters, cancellationToken);
                sentFilters = filters;
            }

            var page = await _api.SearchBarbersAsync(filters, cancellationToken: cancellationToken);

            Barbers.Clear();

            foreach (var barber in page.Items)
            {
                Barbers.Add(barber);
            }

            _appliedFilters = filters;
            _hasSearched = true;
            ErrorMessage = null;
            _retryFilters = null;
        }
        catch (Exception exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                // Английский detail сервера не показывается: текст для человека живёт на клиенте.
                // Таймаут транспорта классификатор уже свёл к «нет связи», поэтому он больше не
                // проглатывается как отмена.
                ErrorMessage = kind == ApiFailureKind.Transport ? NetworkText : SearchFailedText;
            }

            // Окончательный отказ повторять нечем, поэтому и снимок ему не нужен.
            _retryFilters = kind.IsRetryable() ? sentFilters : null;
        }
        finally
        {
            if (!silent)
            {
                IsLoading = false;
                UpdateLocationHint();
                SearchCompleted?.Invoke(this, EventArgs.Empty);
            }

            NotifyResultsChanged();
        }

        _lastFailureKind = kind;

        return kind;
    }

    /// <summary>
    /// Запускает цикл, если страница видна, сбой повторяемый и ничего не грузится.
    /// </summary>
    /// <remarks>
    /// Второй цикл поверх первого удвоил бы запросы, поэтому <see cref="AutoRetryLoop.Start"/>
    /// при работающем цикле ничего не делает. Отдельный метод нужен потому, что «видна» — это
    /// сигнал страницы, а не следствие нажатия: повтор после сбоя запускается и из фонового
    /// завершения загрузки, но только пока страница видна.
    /// <para>
    /// Делегат замыкает снимок упавшего запроса, а не собирает фильтры заново: правка формы,
    /// пока видна ошибка, не должна попасть в сеть без «Найти».
    /// </para>
    /// </remarks>
    private void TryStartAutoRetry()
    {
        if (!_isPageVisible
            || IsLoading
            || !HasError
            || !_lastFailureKind.IsRetryable()
            || _retryFilters is not { } filters)
        {
            return;
        }

        _autoRetry.Start(cancellationToken =>
            LoadResultsAsync(filters, silent: true, cancellationToken));
    }

    /// <summary>
    /// Останавливает цикл, не трогая признак «страница видна».
    /// </summary>
    /// <remarks>
    /// Нужен ручному поиску: он останавливает фон, но страница остаётся видимой, и после
    /// ответа цикл имеет право запуститься снова. Публичный <see cref="StopAutoRetry"/>
    /// дополнительно помечает страницу скрытой — для него это неверно.
    /// </remarks>
    private void StopAutoRetryLoop() => _autoRetry.Stop();

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Повторяет чтение геопозиции и поиск: подсказка под списком объясняет, что расстояния нет.
    /// </summary>
    /// <remarks>
    /// Сбрасывает запомненный отказ: человек мог разрешить доступ в настройках телефона, и без
    /// сброса экран до перезапуска приложения продолжал бы показывать всех подряд. Заодно
    /// заново определяется город: после разрешения «Москва» по умолчанию уступает настоящему.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryLocationAsync(CancellationToken cancellationToken)
    {
        _locationAttempted = false;
        _position = null;
        _locationStatus = null;
        LocationHint = null;

        await ResolveCityAsync(cancellationToken);

        await SearchAsync(cancellationToken);
    }

    /// <summary>
    /// Дочитывает геопозицию и добавляет её в фильтры, если координаты есть.
    /// </summary>
    /// <remarks>
    /// Повторный заход не переспрашивает систему, пока попытка уже была: иначе каждый «Найти»
    /// после отказа заново открывал бы диалог разрешения. Успешные координаты остаются на всё
    /// время жизни экрана — за минуты поиска человек не переезжает.
    /// </remarks>
    /// <param name="filters">Фильтры без координат.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Фильтры с координатами, если они получены, иначе исходные.</returns>
    private async Task<BarberSearchFilters> ApplyLocationAsync(
        BarberSearchFilters filters,
        CancellationToken cancellationToken)
    {
        IsLocating = true;

        try
        {
            var position = await LocateOnceAsync(cancellationToken);

            return position is { } located
                ? filters with
                {
                    Latitude = located.Latitude,
                    Longitude = located.Longitude,
                }
                : filters;
        }
        finally
        {
            IsLocating = false;
        }
    }

    /// <summary>
    /// Читает геопозицию один раз на экран и возвращает координаты, если они есть.
    /// </summary>
    /// <remarks>
    /// Общий шаг сортировки «сначала ближайшие» и автоподстановки города: диалог разрешения
    /// и чтение GPS выполняются один раз, а второй потребитель берёт уже готовый результат.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Координаты либо <c>null</c>.</returns>
    private async Task<GeoPosition?> LocateOnceAsync(CancellationToken cancellationToken)
    {
        if (_locationAttempted)
        {
            return _position;
        }

        // Чтение в полёте одно на экран: второй потребитель ждёт то же самое, а не открывает
        // второе разрешение. После завершения поле очищается, чтобы отменённая попытка не
        // стала вечным отказом для следующей.
        var read = _locationRead ??= _locationService.GetPositionAsync(cancellationToken);

        try
        {
            var result = await read;

            _position = result.Position;
            _locationStatus = result.Status;
            _locationAttempted = true;
        }
        finally
        {
            _locationRead = null;
        }

        return _position;
    }

    /// <summary>
    /// Запускает автоподстановку города один раз за жизнь экрана.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task FillCityFromLocationAsync(CancellationToken cancellationToken)
    {
        if (_cityFillStarted)
        {
            return;
        }

        _cityFillStarted = true;

        await ResolveCityAsync(cancellationToken);
    }

    /// <summary>
    /// Определяет город по геопозиции, а при любом отказе подставляет город по умолчанию.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Отказ разрешения, отсутствие GPS, таймаут геокодера и пустой ответ платформы — всё
    /// это один исход для человека: поле получает «Москву». Поэтому причины не различаются,
    /// в отличие от подсказки о сортировке, где отказ и недоступность объясняются по-разному.
    /// </para>
    /// <para>
    /// Подстановка не трогает город, который человек уже ввёл или выбрал из подсказок:
    /// она заменяет только пустое поле и собственный прошлый результат.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task ResolveCityAsync(CancellationToken cancellationToken)
    {
        IsCityLocating = true;

        try
        {
            var position = await LocateOnceAsync(cancellationToken);
            var city = position is { } located
                ? CityName.Normalize(await _cityLocator.GetCityAsync(located, cancellationToken))
                : null;

            ApplyAutomaticCity(city ?? DefaultCity);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Экран ушёл до ответа: подставлять город в ушедшую форму не нужно.
        }
        catch (Exception)
        {
            // Геокодер — подсказка поля, а не ответ API: любой сбой даёт город по умолчанию.
            ApplyAutomaticCity(DefaultCity);
        }
        finally
        {
            IsCityLocating = false;
        }
    }

    /// <summary>
    /// Подставляет автоматический город, не перетирая введённый человеком.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Собственный прошлый результат запоминается в <c>_automaticCity</c>: повтор чтения
    /// геопозиции после «Повторить» имеет право заменить «Москву» по умолчанию настоящим
    /// городом, но не набранный вручную текст.
    /// </para>
    /// <para>
    /// Подсказки на программную подстановку не запрашиваются: <c>OnCityChanged</c> увидел бы
    /// новый текст как ввод и открыл бы список прямо при запуске вкладки.
    /// </para>
    /// </remarks>
    /// <param name="city">Город от геокодера или город по умолчанию.</param>
    private void ApplyAutomaticCity(string city)
    {
        if (!string.IsNullOrWhiteSpace(City)
            && !string.Equals(City, _automaticCity, StringComparison.Ordinal))
        {
            return;
        }

        _suppressCitySuggestions = true;

        try
        {
            City = city;
        }
        finally
        {
            _suppressCitySuggestions = false;
        }

        _automaticCity = city;
    }

    /// <summary>
    /// Показывает или скрывает подсказку о недоступной геопозиции.
    /// </summary>
    private void UpdateLocationHint()
    {
        LocationHint = !_locationAttempted || _position is not null
            ? null
            : _locationStatus == LocationStatus.Denied
                ? LocationDeniedText
                : LocationUnavailableText;
    }

    /// <summary>
    /// Повторяет загрузку справочников после ошибки.
    /// </summary>
    /// <remarks>
    /// «Повторить» значит «повторить сейчас»: цикл справочников останавливается приватно
    /// (публичный <see cref="StopAutoRetry"/> ещё и пометил бы страницу скрытой), оба
    /// справочника или только упавший читаются без паузы, а цикл запускается заново только
    /// если новый сбой снова повторяемый. Отдельная команда, а не повторный
    /// <see cref="InitializeAsync"/>: пока идёт повтор, селекты показывают «Загрузка…», а не
    /// старую ошибку.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryCatalogsAsync(CancellationToken cancellationToken)
    {
        _isInitialized = true;

        StopCatalogsAutoRetryLoop();

        await LoadCatalogsAsync(silent: false, cancellationToken);

        TryStartCatalogsAutoRetry();
    }

    /// <summary>
    /// Грузит справочники — оба или только ту часть, которая упала в прошлый раз.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ошибка справочника не блокирует поиск: город, дата и язык работают и без него, поэтому
    /// экран показывает текст ошибки с «Повторить», а не отказ всей формы. Каждая часть
    /// сводится классификатором отдельно: частичный успех сохраняется, и повтор запрашивает
    /// только упавшую часть, а не оба справочника заново.
    /// </para>
    /// <para>
    /// Тихий режим — попытка второго цикла: причина ошибки и уже загруженные справочники
    /// остаются на экране до ответа, иначе каждая пауза между попытками мигала бы формой.
    /// Успех очищает ошибку и снимок, неудача оставляет всё как было и возвращает вид сбоя.
    /// </para>
    /// <para>
    /// <c>429</c> в справочниках окончателен: лимитер просит сбавить темп, и повтор лишь
    /// повторил бы отказ, поэтому для этого чтения он не повторяется, а его текст остаётся.
    /// </para>
    /// </remarks>
    /// <param name="silent">Тихая попытка цикла: не очищать ошибку и загруженное до ответа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему второй цикл решает, нужна ли ещё одна попытка.</returns>
    private async Task<ApiFailureKind> LoadCatalogsAsync(
        bool silent,
        CancellationToken cancellationToken)
    {
        var needsSpecializations = _catalogsNeedsSpecializations;
        var needsHairServices = _catalogsNeedsHairServices;

        if (!needsSpecializations && !needsHairServices)
        {
            return ApiFailureKind.None;
        }

        if (!silent)
        {
            IsCatalogsLoading = true;
            CatalogsErrorText = null;

            if (needsSpecializations && needsHairServices)
            {
                ShowCatalogPlaceholders();
            }
            else if (needsSpecializations)
            {
                ShowSpecializationPlaceholder();
            }
            else
            {
                ShowHairServicePlaceholder();
            }
        }

        // Оба запроса уходят параллельно, как и раньше; ждём их по очереди, чтобы сбой одной
        // части не отменял уже полученный ответ другой.
        var specializationsTask = needsSpecializations
            ? _api.GetSpecializationsAsync(cancellationToken)
            : null;
        var hairServicesTask = needsHairServices
            ? _api.GetHairServicesAsync(cancellationToken)
            : null;

        IReadOnlyList<SpecializationResponse>? specializations = null;
        IReadOnlyList<HairServiceResponse>? hairServices = null;
        var specializationsFailure = ApiFailureKind.None;
        var hairServicesFailure = ApiFailureKind.None;
        var rateLimited = false;

        if (specializationsTask is not null)
        {
            try
            {
                specializations = await specializationsTask;
            }
            catch (Exception exception)
            {
                specializationsFailure = ClassifyCatalogsFailure(exception, cancellationToken);
                rateLimited |= IsRateLimited(exception);
            }
        }

        if (hairServicesTask is not null)
        {
            try
            {
                hairServices = await hairServicesTask;
            }
            catch (Exception exception)
            {
                hairServicesFailure = ClassifyCatalogsFailure(exception, cancellationToken);
                rateLimited |= IsRateLimited(exception);
            }
        }

        if (specializations is not null)
        {
            ApplySpecializations(specializations);
            _catalogsNeedsSpecializations = false;
        }

        if (hairServices is not null)
        {
            ApplyHairServices(hairServices);
            _catalogsNeedsHairServices = false;
        }

        var kind = WorstCatalogsFailure(specializationsFailure, hairServicesFailure);

        if (kind != ApiFailureKind.None)
        {
            CatalogsErrorText = rateLimited
                ? RateLimitText
                : kind == ApiFailureKind.Transport
                    ? NetworkText
                    : CatalogsLoadFailedText;
            _catalogsFailureKind = kind;
        }
        else if (!_catalogsNeedsSpecializations && !_catalogsNeedsHairServices)
        {
            CatalogsErrorText = null;
            _catalogsFailureKind = ApiFailureKind.None;
        }

        if (!silent)
        {
            IsCatalogsLoading = false;

            // У упавшей части вместо «Загрузка…» возвращается «Любая …»: селект не должен
            // обещать загрузку, которой уже не будет. Удавшаяся часть свои значения сохранила.
            if (_catalogsNeedsSpecializations)
            {
                ShowSpecializationPlaceholder();
            }

            if (_catalogsNeedsHairServices)
            {
                ShowHairServicePlaceholder();
            }

            if (kind == ApiFailureKind.None
                && (_catalogsNeedsSpecializations || _catalogsNeedsHairServices))
            {
                // Экран закрыли до ответа: следующий показ начнёт загрузку заново.
                _isInitialized = false;
            }
        }

        NotifyCatalogOptionsChanged();

        return kind;
    }

    /// <summary>
    /// Запускает второй цикл, если страница видна, сбой повторяемый и есть что повторять.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Снимок — какие из двух справочников упали: удавшаяся часть не запрашивается заново.
    /// «Видна» — сигнал страницы, а не следствие нажатия: повтор запускается и из завершения
    /// загрузки, но только пока страница видна.
    /// </para>
    /// <para>
    /// <c>429</c> сюда не попадает: <see cref="ClassifyCatalogsFailure"/> сделал его
    /// окончательным, поэтому <see cref="_catalogsFailureKind"/> не повторяемый и цикл не
    /// стартует, а текст «Слишком много запросов…» остаётся на экране.
    /// </para>
    /// </remarks>
    private void TryStartCatalogsAutoRetry()
    {
        if (!_isPageVisible
            || IsCatalogsLoading
            || !HasCatalogsError
            || !_catalogsFailureKind.IsRetryable()
            || (!_catalogsNeedsSpecializations && !_catalogsNeedsHairServices))
        {
            return;
        }

        _catalogsAutoRetry.Start(cancellationToken =>
            LoadCatalogsAsync(silent: true, cancellationToken));
    }

    /// <summary>
    /// Останавливает второй цикл, не трогая признак «страница видна».
    /// </summary>
    /// <remarks>
    /// Нужен кнопке «Повторить» в блоке справочников: она останавливает фон, но страница
    /// остаётся видимой, и после ответа цикл имеет право запуститься снова. Публичный
    /// <see cref="StopAutoRetry"/> дополнительно пометил бы страницу скрытой.
    /// </remarks>
    private void StopCatalogsAutoRetryLoop() => _catalogsAutoRetry.Stop();

    /// <summary>
    /// Переключает строку «Пробуем снова…» справочников вслед за состоянием второго цикла.
    /// </summary>
    private void OnCatalogsAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsCatalogsAutoRetrying = _catalogsAutoRetry.IsRunning;

    /// <summary>
    /// Сводит сбой одного справочника к виду, по которому решает второй цикл.
    /// </summary>
    /// <remarks>
    /// <c>429</c> — единственное отклонение от классификатора: лимитер просит сбавить темп, а
    /// не сообщает о временном сбое, и повтор лишь повторил бы отказ. Для справочников он
    /// поэтому окончателен, а его текст («Слишком много запросов…») остаётся на экране.
    /// </remarks>
    private static ApiFailureKind ClassifyCatalogsFailure(
        Exception exception,
        CancellationToken cancellationToken) =>
        IsRateLimited(exception)
            ? ApiFailureKind.Terminal
            : ApiFailureClassifier.Classify(exception, cancellationToken);

    /// <summary>
    /// Ответил ли сервер просьбой сбавить темп.
    /// </summary>
    private static bool IsRateLimited(Exception exception) =>
        exception is BarberSearchApiException { StatusCode: HttpStatusCode.TooManyRequests };

    /// <summary>
    /// Вид сбоя попытки по двум справочникам: худший из двух.
    /// </summary>
    /// <remarks>
    /// Окончательный отказ важнее остальных: повторять его бессмысленно, и цикл должен
    /// остановиться, даже если вторая часть упала повторяемо. Иначе «транспорт» одной части
    /// прятал бы 404 другой, и цикл крутился бы на отказе, который не изменится.
    /// </remarks>
    private static ApiFailureKind WorstCatalogsFailure(
        ApiFailureKind specializations,
        ApiFailureKind hairServices)
    {
        if (specializations == ApiFailureKind.Terminal || hairServices == ApiFailureKind.Terminal)
        {
            return ApiFailureKind.Terminal;
        }

        if (specializations == ApiFailureKind.Transport || hairServices == ApiFailureKind.Transport)
        {
            return ApiFailureKind.Transport;
        }

        return specializations == ApiFailureKind.None && hairServices == ApiFailureKind.None
            ? ApiFailureKind.None
            : ApiFailureKind.RetryableServer;
    }

    /// <summary>
    /// Раскладывает загруженные специализации, ставя «любой» пункт первым.
    /// </summary>
    /// <remarks>
    /// Справочник сохраняется целиком: он задаёт заголовки и порядок групп услуг. Группы
    /// пересобираются здесь же, потому что специализации могли прийти после услуг — порядок
    /// ответов у параллельных запросов не определён.
    /// </remarks>
    private void ApplySpecializations(IReadOnlyList<SpecializationResponse> specializations)
    {
        _specializations = specializations;

        Specializations.Clear();
        Specializations.Add(SearchCatalogOption.Any(AnySpecializationText));

        foreach (var specialization in specializations)
        {
            Specializations.Add(SearchCatalogOption.From(specialization.Id, specialization.Name));
        }

        SelectedSpecialization = Specializations[0];

        RebuildHairServiceGroups();
    }

    /// <summary>
    /// Раскладывает загруженные услуги по специализациям и сбрасывает выбор на «Любую услугу».
    /// </summary>
    /// <remarks>
    /// Загрузка справочника — это новый каталог: выбранная услуга могла в нём исчезнуть, как и
    /// её специализация, поэтому выбор возвращается к «Любой услуге». Пункт «Любая услуга»
    /// создаётся заново уже с настоящим текстом: до ответа на его месте стояло «Загрузка…».
    /// </remarks>
    private void ApplyHairServices(IReadOnlyList<HairServiceResponse> hairServices)
    {
        _hairServices = hairServices;

        RebuildHairServiceGroups();

        _anyHairServiceOption = SearchCatalogOption.Any(AnyServiceText);
        SelectedHairService = _anyHairServiceOption;
    }

    /// <summary>
    /// Пересобирает группы услуг под текущую специализацию.
    /// </summary>
    /// <remarks>
    /// Раскладку делает чистая <see cref="SearchServiceGroups.Build"/>: она не меняет
    /// загруженные справочники, поэтому смена специализации — это повторная раскладка того же
    /// ответа API, без запроса в сеть.
    /// </remarks>
    private void RebuildHairServiceGroups()
    {
        var groups = SearchServiceGroups.Build(
            _hairServices,
            _specializations,
            SelectedSpecialization?.Id,
            Loc.T("Прочие услуги"));

        HairServiceGroups.Clear();

        foreach (var group in groups)
        {
            HairServiceGroups.Add(group);
        }

        OnPropertyChanged(nameof(ShowHairServicePicker));
    }

    /// <summary>
    /// Показывает в обоих селектах «Загрузка…» или «Любая …», пока данных справочников нет.
    /// </summary>
    private void ShowCatalogPlaceholders()
    {
        ShowSpecializationPlaceholder();
        ShowHairServicePlaceholder();
    }

    /// <summary>
    /// Показывает заглушку селекта специализаций.
    /// </summary>
    private void ShowSpecializationPlaceholder()
    {
        Specializations.Clear();
        Specializations.Add(SearchCatalogOption.Any(
            IsCatalogsLoading ? LoadingText : AnySpecializationText));
        SelectedSpecialization = Specializations[0];
    }

    /// <summary>
    /// Показывает заглушку селекта услуг.
    /// </summary>
    /// <remarks>
    /// Группы очищаются: до ответа показывать нечего, а «Любая услуга» остаётся выбранной —
    /// она вне групп и не даёт фильтру услуги уйти в запрос.
    /// </remarks>
    private void ShowHairServicePlaceholder()
    {
        HairServiceGroups.Clear();
        _anyHairServiceOption = SearchCatalogOption.Any(
            IsCatalogsLoading ? LoadingText : AnyServiceText);
        SelectedHairService = _anyHairServiceOption;

        OnPropertyChanged(nameof(ShowHairServicePicker));
    }

    /// <summary>
    /// Заполняет список языков: перечисление API в порядке объявления, как в вебе.
    /// </summary>
    private void FillLanguages()
    {
        Languages.Add(SearchLanguageOption.Any(AnyLanguageText));

        foreach (var language in Enum.GetValues<Language>())
        {
            Languages.Add(SearchLanguageOption.From(language));
        }

        SelectedLanguage = Languages[0];
    }

    /// <summary>
    /// Пересчитывает границы календаря, если сменился день устройства.
    /// </summary>
    /// <remarks>
    /// Выбранная дата остаётся выбранной, пока попадает в горизонт; иначе — «Любая дата».
    /// Смена дня сдвигает обе границы, поэтому приложение, провисевшее в фоне, не предложит
    /// вчерашний день и не отрежет новый.
    /// </remarks>
    private void RefreshDateBounds()
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

        // Сначала снимаем фильтр, выпавший из горизонта: иначе смена MinimumDate
        // заставит DatePicker самому подставить новую границу и фильтр включится снова.
        // Проверка стоит до выхода по тому же дню: InitializeAsync зовут при каждом показе
        // вкладки, и дата, выставленная в обход календаря, не должна пережить этот показ.
        if (SelectedDate is { } selected
            && (DateOnly.FromDateTime(selected) < today
                || DateOnly.FromDateTime(selected) > today.AddDays(DateHorizonDays)))
        {
            SelectedDate = null;
        }

        if (today == _dateBoundsToday)
        {
            return;
        }

        _dateBoundsToday = today;

        MinimumDate = today.ToDateTime(TimeOnly.MinValue);
        MaximumDate = today.AddDays(DateHorizonDays).ToDateTime(TimeOnly.MinValue);

        OnPropertyChanged(nameof(MinimumDate));
        OnPropertyChanged(nameof(MaximumDate));
    }

    /// <summary>
    /// Собирает фильтры из текущего выбора; пустые значения уходят как «фильтра нет».
    /// </summary>
    private BarberSearchFilters BuildFilters() => new()
    {
        City = City.Trim(),
        Date = SelectedDate is { } date ? DateOnly.FromDateTime(date) : null,
        SpecializationId = SelectedSpecialization?.Id,
        HairServiceId = SelectedHairService?.Id,
        Language = SelectedLanguage?.Value,
        AdditionalService = AdditionalService.Trim(),
    };

    /// <summary>
    /// Заданы ли фильтры, кроме города и даты: язык, специализация, услуга, доп. услуга.
    /// </summary>
    private static bool HasChoiceFilters(BarberSearchFilters filters) =>
        filters.Language is not null
        || filters.SpecializationId is not null
        || filters.HairServiceId is not null
        || !string.IsNullOrWhiteSpace(filters.AdditionalService);

    /// <summary>
    /// Сообщает об изменении списка выдачи и зависящих от него подсказок.
    /// </summary>
    private void NotifyResultsChanged()
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasEmptyTitle));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        OnPropertyChanged(nameof(ShowResultsCount));
        OnPropertyChanged(nameof(ResultsCountText));
        OnPropertyChanged(nameof(ShowNearbySort));
    }

    /// <summary>
    /// Сообщает об изменении видимости и доступности селектов справочников.
    /// </summary>
    private void NotifyCatalogOptionsChanged()
    {
        OnPropertyChanged(nameof(IsCatalogsEnabled));
        OnPropertyChanged(nameof(ShowSpecializationPicker));
        OnPropertyChanged(nameof(ShowHairServicePicker));
    }

    partial void OnIsCatalogsLoadingChanged(bool value) => NotifyCatalogOptionsChanged();

    /// <summary>
    /// Смена специализации сужает группы услуг и сбрасывает ставшую чужой услугу.
    /// </summary>
    /// <remarks>
    /// Поведение веба (<c>handleSpecializationChange</c> в
    /// <c>frontend/src/components/home/search-form.tsx</c>): у услуги ровно одна специализация,
    /// поэтому после смены специализации выбранная услуга может из списка выпасть. Такой выбор
    /// сбрасывается на «Любую услугу», иначе на сервер ушла бы противоречивая пара фильтров.
    /// Возврат к «Любой специализации» ничего не сбрасывает: прежний выбор снова допустим.
    /// </remarks>
    /// <param name="value">Новый пункт фильтра специализаций; <c>null</c> — «любая».</param>
    partial void OnSelectedSpecializationChanged(SearchCatalogOption? value)
    {
        RebuildHairServiceGroups();

        if (value?.Id is { } specializationId
            && SelectedHairService?.SpecializationId is { } serviceSpecializationId
            && serviceSpecializationId != specializationId)
        {
            SelectedHairService = _anyHairServiceOption;
        }
    }

    /// <summary>
    /// Каждый символ города запускает подсказки; паузу и отмену держит секция.
    /// </summary>
    /// <remarks>
    /// Задача намеренно не ожидается: ввод не должен ждать ни паузы, ни сети. Ошибки внутри
    /// секции пойманы, поэтому бросить здесь нечего.
    /// </remarks>
    partial void OnCityChanged(string value)
    {
        if (_suppressCitySuggestions)
        {
            return;
        }

        _ = CitySuggestions.QueryAsync(value);
    }
}
