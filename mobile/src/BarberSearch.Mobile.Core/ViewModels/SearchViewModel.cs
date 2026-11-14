using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран поиска: фильтры витрины, справочники, список найденных барберов и состояние загрузки.
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
/// Запрос уходит только по нажатию «Найти»: правка фильтра ничего не перезапрашивает.
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

    private const string AnyLanguageText = "Любой язык";
    private const string AnySpecializationText = "Любая специализация";
    private const string AnyServiceText = "Любая услуга";
    private const string AnyDateText = "Любая дата";
    private const string LoadingText = "Загрузка…";
    private const string CatalogsLoadFailedText = "Не удалось загрузить фильтры.";
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string RateLimitText = "Слишком много запросов. Подождите минуту и повторите.";
    private const string NoSearchHintText = "Введите город и нажмите «Найти»";
    private const string EmptyCityTitleText = "В этом городе никого не нашли";
    private const string EmptyCityText = "Проверьте написание — город учитывается точно, например «Москва».";
    private const string EmptyDateTitleText = "На выбранную дату никого не нашли";
    private const string EmptyDateText = "Фильтр оставляет барберов с рабочим днём в этот день недели. Попробуйте другую дату.";
    private const string EmptyFiltersTitleText = "С такими фильтрами никого не нашли";
    private const string EmptyFiltersText = "Попробуйте другой язык, специализацию, услугу или дополнительную услугу.";
    private const string EmptyNoBarbersTitleText = "Барберов пока нет";
    private const string EmptyNoBarbersText = "Пока никто не подтверждён. Загляните позже.";

    private readonly IBarberSearchApi _api;
    private readonly TimeProvider _timeProvider;

    private bool _isInitialized;
    private bool _hasSearched;
    private BarberSearchFilters? _appliedFilters;
    private DateOnly _dateOptionsToday;

    /// <summary>
    /// Создаёт ViewModel поиска.
    /// </summary>
    /// <param name="api">Чтения API: поиск и справочники фильтров.</param>
    /// <param name="timeProvider">
    /// Текущий день устройства: от него отсчитывается список дат, поэтому тест задаёт его сам.
    /// </param>
    public SearchViewModel(IBarberSearchApi api, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _api = api;
        _timeProvider = timeProvider;

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
    /// Найденные барберы в порядке выдачи сервера.
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
    /// Текст ошибки поиска для пользователя; <c>null</c>, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private string? _errorMessage;

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
    /// Счётчик найденных: «Показаны 2 барбера».
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
    /// Загружает первую страницу выдачи по текущим фильтрам.
    /// </summary>
    /// <remarks>
    /// Перед запросом список очищается: старые карточки под новым фильтром — ложь, так же
    /// поступает веб. Ошибки API и сети превращаются в текст для пользователя, отмена —
    /// штатный выход без сообщения: пользователь сам закрыл экран.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        var filters = BuildFilters();

        IsLoading = true;
        ErrorMessage = null;
        Barbers.Clear();
        NotifyResultsChanged();

        try
        {
            var page = await _api.SearchBarbersAsync(filters, cancellationToken: cancellationToken);

            Barbers.Clear();

            foreach (var barber in page.Items)
            {
                Barbers.Add(barber);
            }

            _appliedFilters = filters;
            _hasSearched = true;
        }
        catch (OperationCanceledException)
        {
            // Отмена — не ошибка: пользователь сам остановил поиск.
        }
        catch (BarberSearchApiException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = NetworkText;
        }
        finally
        {
            IsLoading = false;
            NotifyResultsChanged();
            SearchCompleted?.Invoke(this, EventArgs.Empty);
        }
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
    /// экран показывает текст ошибки с «Повторить», а не отказ всей формы.
    /// </remarks>
    private async Task LoadCatalogsAsync(CancellationToken cancellationToken)
    {
        IsCatalogsLoading = true;
        CatalogsErrorText = null;
        ShowCatalogPlaceholders();

        try
        {
            var specializationsTask = _api.GetSpecializationsAsync(cancellationToken);
            var hairServicesTask = _api.GetHairServicesAsync(cancellationToken);

            await Task.WhenAll(specializationsTask, hairServicesTask);

            ApplyCatalogs(await specializationsTask, await hairServicesTask);
        }
        catch (OperationCanceledException)
        {
            // Экран закрыли до ответа: следующий показ начнёт загрузку заново.
            _isInitialized = false;
        }
        catch (BarberSearchApiException exception)
        {
            CatalogsErrorText = exception.StatusCode == HttpStatusCode.TooManyRequests
                ? RateLimitText
                : CatalogsLoadFailedText;
        }
        catch (HttpRequestException)
        {
            CatalogsErrorText = NetworkText;
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
}
