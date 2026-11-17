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
/// </remarks>
public sealed partial class SearchViewModel : ObservableObject
{
    /// <summary>
    /// Горизонт фильтра по дате в днях.
    /// </summary>
    /// <remarks>
    /// Веб отдаёт нативный календарь без верхней границы, а <c>Picker</c> на телефоне — список
    /// значений. Список ограничен горизонтом записи приложения (30 дней): он покрывает все дни
    /// недели и не листается бесконечно. Отклонение записано в <c>AGENT.md</c> §5.
    /// </remarks>
    private const int DateHorizonDays = 30;

    private static string AnyLanguageText => Loc.T("Любой язык");
    private static string AnySpecializationText => Loc.T("Любая специализация");
    private static string AnyServiceText => Loc.T("Любая услуга");
    private static string AnyDateText => Loc.T("Любая дата");
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
    private readonly TimeProvider _timeProvider;

    private bool _isInitialized;
    private bool _hasSearched;
    private BarberSearchFilters? _appliedFilters;
    private DateOnly _dateOptionsToday;

    // Геопозиция читается один раз на экран: после отказа система не дёргается на каждый
    // «Найти», иначе Android показывал бы диалог разрешения повторно. «Повторить» в подсказке
    // сбрасывает этот флаг и пробует снова.
    private bool _locationAttempted;
    private GeoPosition? _position;
    private LocationStatus? _locationStatus;

    private readonly AutoRetryLoop _autoRetry;

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
    /// Текущий день устройства: от него отсчитывается список дат, поэтому тест задаёт его сам.
    /// </param>
    /// <param name="citySuggestions">
    /// Подсказки города под полем; живут рядом с экраном, потому что повторяют его судьбу.
    /// </param>
    /// <param name="autoRetry">
    /// Цикл автоповтора выдачи; <c>null</c> — собственный цикл с обычным расписанием. Параметр
    /// необязательный, чтобы существующие позиционные вызовы (DI и тесты) не менялись.
    /// </param>
    public SearchViewModel(
        IBarberSearchApi api,
        ILocationService locationService,
        TimeProvider timeProvider,
        CitySuggestions citySuggestions,
        AutoRetryLoop? autoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(locationService);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(citySuggestions);

        _api = api;
        _locationService = locationService;
        _timeProvider = timeProvider;
        CitySuggestions = citySuggestions;

        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;

        FillLanguages();
        RefreshDateOptions();
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
    /// Услуги справочника для фильтра: «Любая услуга» первой, дальше порядок сервера.
    /// </summary>
    public ObservableCollection<SearchCatalogOption> HairServices { get; } = [];

    /// <summary>
    /// Языки общения для фильтра: «Любой язык» первым, дальше шесть языков API.
    /// </summary>
    public ObservableCollection<SearchLanguageOption> Languages { get; } = [];

    /// <summary>
    /// Даты фильтра: «Любая дата» первой, дальше сегодня и горизонт записи.
    /// </summary>
    public ObservableCollection<SearchDateOption> Dates { get; } = [];

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
    private SearchCatalogOption? _selectedHairService;

    /// <summary>
    /// Выбранная дата; пункт «Любая дата» отправляет <c>null</c>.
    /// </summary>
    [ObservableProperty]
    private SearchDateOption? _selectedDate;

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
    public bool ShowHairServicePicker =>
        HairServices.Count > 1 || IsCatalogsLoading || HasCatalogsError;

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
    /// Готовит экран к показу: пересобирает даты и один раз грузит справочники.
    /// </summary>
    /// <remarks>
    /// Вызывается при каждом появлении вкладки. Даты пересобираются, если сменился день —
    /// приложение могло висеть в фоне, — а справочники только при первом показе: их ошибка
    /// лечится кнопкой «Повторить», а не повторным заходом на вкладку.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        RefreshDateOptions();

        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        await LoadCatalogsAsync(cancellationToken);
    }

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
    }

    /// <summary>
    /// Сообщает, что страница скрыта, и останавливает автоповтор.
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
    /// сброса экран до перезапуска приложения продолжал бы показывать всех подряд.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryLocationAsync(CancellationToken cancellationToken)
    {
        _locationAttempted = false;
        _position = null;
        _locationStatus = null;
        LocationHint = null;

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
        if (!_locationAttempted)
        {
            IsLocating = true;

            try
            {
                var result = await _locationService.GetPositionAsync(cancellationToken);

                _position = result.Position;
                _locationStatus = result.Status;
                _locationAttempted = true;
            }
            finally
            {
                IsLocating = false;
            }
        }

        return _position is { } position
            ? filters with
            {
                Latitude = position.Latitude,
                Longitude = position.Longitude,
            }
            : filters;
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
    /// Отдельная команда, а не повторный <see cref="InitializeAsync"/>: пока идёт повтор,
    /// селекты показывают «Загрузка…», а не старую ошибку.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryCatalogsAsync(CancellationToken cancellationToken)
    {
        _isInitialized = true;

        await LoadCatalogsAsync(cancellationToken);
    }

    /// <summary>
    /// Грузит справочники специализаций и услуг одним параллельным запросом.
    /// </summary>
    /// <remarks>
    /// Ошибка справочника не блокирует поиск: город, дата и язык работают и без него, поэтому
    /// экран показывает текст ошибки с «Повторить», а не отказ всей формы. Сбои сведены
    /// классификатором; <c>429</c> в справочниках сохраняет свой текст — это просьба сбавить
    /// темп, а не «нет связи».
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя; справочники не участвуют в автоповторе выдачи.</returns>
    private async Task<ApiFailureKind> LoadCatalogsAsync(CancellationToken cancellationToken)
    {
        IsCatalogsLoading = true;
        CatalogsErrorText = null;
        ShowCatalogPlaceholders();

        var kind = ApiFailureKind.None;

        try
        {
            var specializationsTask = _api.GetSpecializationsAsync(cancellationToken);
            var hairServicesTask = _api.GetHairServicesAsync(cancellationToken);

            await Task.WhenAll(specializationsTask, hairServicesTask);

            ApplyCatalogs(await specializationsTask, await hairServicesTask);
        }
        catch (Exception exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind == ApiFailureKind.None)
            {
                // Экран закрыли до ответа: следующий показ начнёт загрузку заново.
                _isInitialized = false;
            }
            else
            {
                CatalogsErrorText = kind == ApiFailureKind.Transport
                    ? NetworkText
                    : exception is BarberSearchApiException
                    {
                        StatusCode: HttpStatusCode.TooManyRequests,
                    }
                        ? RateLimitText
                        : CatalogsLoadFailedText;
            }
        }
        finally
        {
            IsCatalogsLoading = false;

            if (HasCatalogsError)
            {
                ShowCatalogPlaceholders();
            }

            NotifyCatalogOptionsChanged();
        }

        return kind;
    }

    /// <summary>
    /// Раскладывает загруженные справочники по коллекциям, ставя «любой» пункт первым.
    /// </summary>
    private void ApplyCatalogs(
        IReadOnlyList<SpecializationResponse> specializations,
        IReadOnlyList<HairServiceResponse> hairServices)
    {
        Specializations.Clear();
        Specializations.Add(SearchCatalogOption.Any(AnySpecializationText));

        foreach (var specialization in specializations)
        {
            Specializations.Add(SearchCatalogOption.From(specialization.Id, specialization.Name));
        }

        SelectedSpecialization = Specializations[0];

        HairServices.Clear();
        HairServices.Add(SearchCatalogOption.Any(AnyServiceText));

        foreach (var service in hairServices)
        {
            HairServices.Add(SearchCatalogOption.From(service.Id, service.Name));
        }

        SelectedHairService = HairServices[0];
    }

    /// <summary>
    /// Показывает в селектах «Загрузка…» или «Любая …», пока данных справочника нет.
    /// </summary>
    private void ShowCatalogPlaceholders()
    {
        Specializations.Clear();
        Specializations.Add(SearchCatalogOption.Any(
            IsCatalogsLoading ? LoadingText : AnySpecializationText));
        SelectedSpecialization = Specializations[0];

        HairServices.Clear();
        HairServices.Add(SearchCatalogOption.Any(
            IsCatalogsLoading ? LoadingText : AnyServiceText));
        SelectedHairService = HairServices[0];
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
    /// Пересобирает список дат, если сменился день устройства.
    /// </summary>
    /// <remarks>
    /// Выбранная дата остаётся выбранной, пока попадает в горизонт; иначе — «Любая дата».
    /// </remarks>
    private void RefreshDateOptions()
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

        if (today == _dateOptionsToday && Dates.Count > 0)
        {
            return;
        }

        var selected = SelectedDate?.Date;
        var lastDate = today.AddDays(DateHorizonDays);

        Dates.Clear();
        Dates.Add(SearchDateOption.Any(AnyDateText));

        for (var offset = 0; offset <= DateHorizonDays; offset++)
        {
            var date = today.AddDays(offset);

            Dates.Add(SearchDateOption.For(date, today));
        }

        _dateOptionsToday = today;
        SelectedDate = selected is { } onlyDate && onlyDate >= today && onlyDate <= lastDate
            ? Dates.First(option => option.Date == onlyDate)
            : Dates[0];
    }

    /// <summary>
    /// Собирает фильтры из текущего выбора; пустые значения уходят как «фильтра нет».
    /// </summary>
    private BarberSearchFilters BuildFilters() => new()
    {
        City = City.Trim(),
        Date = SelectedDate?.Date,
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
    /// Каждый символ города запускает подсказки; паузу и отмену держит секция.
    /// </summary>
    /// <remarks>
    /// Задача намеренно не ожидается: ввод не должен ждать ни паузы, ни сети. Ошибки внутри
    /// секции пойманы, поэтому бросить здесь нечего.
    /// </remarks>
    partial void OnCityChanged(string value) => _ = CitySuggestions.QueryAsync(value);
}
