using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileService;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfileService;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Редактор услуги в прайсе: добавление из справочника или правка цены и длительности.
/// </summary>
/// <remarks>
/// <para>
/// Справочник читается лениво — при открытии добавления, а не вместе с прайсом: он
/// большой, а нужен только в одном диалоге. Уже добавленные услуги из выдачи убираются
/// по <c>HairServiceId</c>: повторное добавление сервер отверг бы кодом дубликата.
/// Специализация выбранной услуги, которой у мастера ещё нет, добавляется автоматически:
/// отдельной кнопки «Добавить специализацию» в разделе «Профиль» больше нет.
/// </para>
/// <para>
/// Выдача показывается группами по специализациям
/// (<see cref="BarberServiceCatalogGroups.Build"/>), а не плоским списком: мастер ищет услугу
/// своего направления, и заголовок группы называет это направление один раз. Выбор строки
/// подсвечивает её саму (<see cref="BarberServiceCatalogItem.IsSelected"/>) и раскрывает карточку
/// цены и длительности вверху экрана: поля стоят рядом с выбором, а не в конце прокрученного
/// справочника.
/// </para>
/// <para>
/// Поиск по названию фильтрует этот же снимок справочника, не пересоздавая строк: справочник
/// мал и уже в памяти, поэтому запроса, задержки на ввод и повторного чтения у поиска нет.
/// Группы сворачиваются, и по умолчанию открыта одна — выбранная или первая. Раскрытие,
/// счётчики и совпадения живут здесь, чтобы правила проверялись без телефона; странице остаются
/// поворот шеврона, клавиатура и прокрутка.
/// </para>
/// <para>
/// Цена ограничена двумя знаками после запятой и суммой 99 999 999,99, длительность —
/// 1..1440 минутами. Это клиентская граница, а не источник истины: серверные коды
/// (<c>duplicate</c>, <c>updateService.notFound</c>, <c>updateDuration</c>) раскладываются
/// по тем же полям, чтобы человек видел причину, а не общий текст.
/// </para>
/// </remarks>
public sealed partial class BarberServiceEditorViewModel : ObservableObject
{
    private const decimal MaxPrice = 99_999_999.99m;
    private const int MinDurationMinutes = 1;
    private const int MaxDurationMinutes = 1440;

    private static string PriceRequiredText => Loc.T("Укажите цену.");
    private static string PriceRangeText => Loc.T("Цена — число от 0 до 99 999 999,99.");
    private static string PriceDecimalsText => Loc.T("Цена — не больше двух знаков после запятой.");
    private static string DurationRangeText => Loc.T("Длительность — от 1 до 1440 минут.");
    private static string CatalogFailedText => Loc.T("Не удалось загрузить каталог услуг.");
    private static string SaveFailedText => Loc.T("Не удалось сохранить услугу. Попробуйте ещё раз.");
    private static string ChooseServiceText => Loc.T("Выберите услугу из справочника.");

    private readonly IBarberProfileApi _profileApi;
    private readonly IBarberSearchApi _searchApi;
    private readonly HashSet<Guid> _existingHairServiceIds = [];
    private readonly HashSet<Guid> _attachedSpecializationIds = [];

    /// <summary>
    /// Раскрытие групп до поиска: возвращается, когда строку поиска очистили.
    /// </summary>
    /// <remarks>
    /// Мастер, открывший «Окрашивание», не должен после поиска и очистки получить вид по
    /// умолчанию и искать свою группу заново. Память — на одну сессию добавления услуги.
    /// </remarks>
    private readonly Dictionary<BarberServiceCatalogGroup, bool> _expansionBeforeQuery = [];

    private Task _catalogTask = Task.CompletedTask;
    private BarberServiceItem? _editedService;
    private bool _isSearchingCatalog;

    /// <summary>
    /// Создаёт редактор услуги.
    /// </summary>
    /// <param name="profileApi">Добавление и правка услуги.</param>
    /// <param name="searchApi">Публичные справочники услуг и специализаций.</param>
    public BarberServiceEditorViewModel(
        IBarberProfileApi profileApi,
        IBarberSearchApi searchApi)
    {
        ArgumentNullException.ThrowIfNull(profileApi);
        ArgumentNullException.ThrowIfNull(searchApi);

        _profileApi = profileApi;
        _searchApi = searchApi;
    }

    /// <summary>
    /// Профиль, в прайс которого пишет редактор; <c>null</c> до инициализации.
    /// </summary>
    public Guid? BarberProfileId { get; private set; }

    /// <summary>
    /// Режим добавления: услуга выбирается из справочника.
    /// </summary>
    [ObservableProperty]
    private bool _isAddMode;

    /// <summary>
    /// Режим правки: услуга фиксирована, меняются только цена и длительность.
    /// </summary>
    [ObservableProperty]
    private bool _isEditMode;

    /// <summary>
    /// Выбранная услуга справочника в режиме добавления.
    /// </summary>
    [ObservableProperty]
    private BarberServiceCatalogItem? _selectedCatalogService;

    /// <summary>
    /// Цена строкой: поле ввода не умеет хранить десятичное число.
    /// </summary>
    [ObservableProperty]
    private string _priceText = string.Empty;

    /// <summary>
    /// Длительность строкой, минуты.
    /// </summary>
    [ObservableProperty]
    private string _durationText = string.Empty;

    /// <summary>
    /// Название услуги в режиме правки: заголовок диалога.
    /// </summary>
    [ObservableProperty]
    private string _serviceName = string.Empty;

    /// <summary>
    /// Строка поиска по названию услуги в справочнике.
    /// </summary>
    /// <remarks>
    /// Фильтр локальный и мгновенный: справочник уже в памяти, каждое нажатие клавиши
    /// пересчитывает видимые строки, счётчики групп и раскрытие. Выбор услуги поиск не трогает —
    /// карточка цены над списком остаётся на месте, меняется только список под ней.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasServiceQuery))]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private string _serviceQuery = string.Empty;

    /// <summary>
    /// Справочник услуг читается.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingCatalog;

    /// <summary>
    /// Причина сбоя справочника; живёт внутри диалога добавления.
    /// </summary>
    [ObservableProperty]
    private string? _catalogError;

    /// <summary>
    /// В справочнике не осталось услуг, которых нет в прайсе.
    /// </summary>
    [ObservableProperty]
    private bool _isCatalogEmpty;

    /// <summary>
    /// Идёт сохранение.
    /// </summary>
    [ObservableProperty]
    private bool _isSaving;

    /// <summary>
    /// Ошибка цены.
    /// </summary>
    [ObservableProperty]
    private string? _priceError;

    /// <summary>
    /// Ошибка длительности.
    /// </summary>
    [ObservableProperty]
    private string? _durationError;

    /// <summary>
    /// Общая ошибка формы: выбор услуги, отказ сервера.
    /// </summary>
    [ObservableProperty]
    private string? _formError;

    /// <summary>
    /// Доступные услуги справочника, разложенные по специализациям.
    /// </summary>
    /// <remarks>
    /// Список групп, а не плоская выдача: заголовок специализации называет направление один раз,
    /// и мастер ищет услугу в своём разделе, а не в общем перечне десятков строк.
    /// </remarks>
    public ObservableCollection<BarberServiceCatalogGroup> CatalogGroups { get; } = [];

    /// <summary>
    /// Кнопка сохранения доступна.
    /// </summary>
    public bool CanSave => !IsSaving;

    /// <summary>
    /// Форма цены и длительности видна: в правке — всегда, в добавлении — после выбора услуги.
    /// </summary>
    /// <remarks>
    /// В добавлении пустая форма из двух полей стояла бы над списком и просила цену у услуги,
    /// которой ещё нет. Поля появляются вместе с выбором, в карточке выбранной услуги, — сразу под
    /// заголовком экрана, а не в конце списка, куда надо было прокручивать весь справочник.
    /// </remarks>
    public bool IsFormVisible => IsEditMode || SelectedCatalogService is not null;

    /// <summary>
    /// Надпись кнопки сохранения: «Добавить» в добавлении, «Сохранить» в правке.
    /// </summary>
    public string SaveButtonText => IsAddMode ? Loc.T("Добавить") : Loc.T("Сохранить");

    /// <summary>
    /// Ошибка цены есть и должна быть показана.
    /// </summary>
    /// <remarks>
    /// Видимость строк — отдельные флаги, а не <c>DataTrigger</c> на «значение равно null»:
    /// пустая подпись ошибки всё равно занимает строку и промежуток, и карточка цены
    /// разъезжается на три пустых поля. Флаг говорит «показывать» прямо, и его же проверяют тесты.
    /// </remarks>
    public bool HasPriceError => !string.IsNullOrEmpty(PriceError);

    /// <summary>
    /// Ошибка длительности есть и должна быть показана.
    /// </summary>
    public bool HasDurationError => !string.IsNullOrEmpty(DurationError);

    /// <summary>
    /// Общая ошибка формы есть и должна быть показана.
    /// </summary>
    public bool HasFormError => !string.IsNullOrEmpty(FormError);

    /// <summary>
    /// Справочник не прочитан, и причина известна.
    /// </summary>
    public bool HasCatalogError => !string.IsNullOrEmpty(CatalogError);

    /// <summary>
    /// В строке поиска что-то набрано: кнопка «×» видна только тогда.
    /// </summary>
    /// <remarks>
    /// Считаются и одни пробелы: мастер их набрал, и стереть их должен «×», а не выбор строки.
    /// </remarks>
    public bool HasServiceQuery => ServiceQuery.Length > 0;

    /// <summary>
    /// Поиск ничего не нашёл: каталог прочитан, но ни одна строка не подошла.
    /// </summary>
    /// <remarks>
    /// Это не пустой справочник («Все услуги справочника уже в прайсе.»): там групп нет, и
    /// подсказка остаётся своя. Здесь группы есть, просто все они спрятаны фильтром.
    /// </remarks>
    public bool HasNoMatches =>
        !BarberServiceCatalogFilter.IsEmpty(ServiceQuery)
        && CatalogGroups.Count > 0
        && !CatalogGroups.Any(group => group.HasMatches);

    /// <summary>
    /// Строка поиска над справочником видна: каталог прочитан и в нём есть строки.
    /// </summary>
    /// <remarks>
    /// Пока каталог читается, упал или уже весь в прайсе, искать не в чем — поле молчит, а
    /// состояние объясняет подсказка. При пустом результате поиска группы ещё есть (они просто
    /// спрятаны), поэтому поле и «Сбросить» из вида не уходят.
    /// </remarks>
    public bool IsServiceSearchVisible => IsAddMode && CatalogGroups.Count > 0 && !HasCatalogError;

    /// <summary>
    /// Задача текущего чтения справочника: тесты ждут её вместо таймера.
    /// </summary>
    public Task CatalogLoading => _catalogTask;

    /// <summary>
    /// Услуга сохранена: страница объявляет сообщение и перечитывает прайс.
    /// </summary>
    public event EventHandler<string>? Saved;

    /// <summary>
    /// Привязывает редактор к профилю.
    /// </summary>
    /// <param name="barberProfileId">Профиль мастера.</param>
    public void Initialize(Guid barberProfileId)
    {
        if (barberProfileId == Guid.Empty)
        {
            throw new ArgumentException(
                "Идентификатор профиля не может быть пустым.",
                nameof(barberProfileId));
        }

        BarberProfileId = barberProfileId;
    }

    /// <summary>
    /// Готовит добавление услуги: запоминает профиль, уже существующие строки прайса и
    /// специализации.
    /// </summary>
    /// <param name="barberProfileId">Профиль мастера.</param>
    /// <param name="existing">Текущий прайс: его услуги не предлагаются повторно.</param>
    /// <param name="attachedSpecializationIds">
    /// Специализации профиля: специализация выбранной услуги, которой здесь нет, добавляется
    /// вместе с услугой.
    /// </param>
    public void InitializeForAdd(
        Guid barberProfileId,
        IReadOnlyList<BarberServiceItem> existing,
        IReadOnlyCollection<Guid>? attachedSpecializationIds = null)
    {
        Initialize(barberProfileId);
        InitializeForAdd(existing, attachedSpecializationIds);
    }

    /// <summary>
    /// Готовит добавление услуги, если профиль уже привязан через <see cref="Initialize"/>.
    /// </summary>
    /// <param name="existing">Текущий прайс: его услуги не предлагаются повторно.</param>
    /// <param name="attachedSpecializationIds">
    /// Специализации профиля: специализация выбранной услуги, которой здесь нет, добавляется
    /// вместе с услугой.
    /// </param>
    public void InitializeForAdd(
        IReadOnlyList<BarberServiceItem> existing,
        IReadOnlyCollection<Guid>? attachedSpecializationIds = null)
    {
        ArgumentNullException.ThrowIfNull(existing);

        IsAddMode = true;
        IsEditMode = false;
        _editedService = null;
        ServiceName = string.Empty;
        SelectedCatalogService = null;
        PriceText = string.Empty;
        DurationText = string.Empty;
        ClearErrors();
        CatalogError = null;
        IsCatalogEmpty = false;

        // Новый диалог — новый поиск: строка, раскрытие и память о нём не переезжают из
        // предыдущего открытия. Присваивание после SelectedCatalogService, иначе очистка
        // запроса пересчитала бы ещё пустой справочник.
        _expansionBeforeQuery.Clear();
        _isSearchingCatalog = false;
        ServiceQuery = string.Empty;
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(IsServiceSearchVisible));

        _existingHairServiceIds.Clear();
        _attachedSpecializationIds.Clear();

        foreach (var item in existing)
        {
            _existingHairServiceIds.Add(item.HairServiceId);
        }

        if (attachedSpecializationIds is not null)
        {
            foreach (var specializationId in attachedSpecializationIds)
            {
                _attachedSpecializationIds.Add(specializationId);
            }
        }

        CatalogGroups.Clear();

        _catalogTask = LoadCatalogAsync(CancellationToken.None);
    }

    /// <summary>
    /// Готовит правку строки прайса.
    /// </summary>
    /// <param name="barberProfileId">Профиль мастера.</param>
    /// <param name="item">Строка прайса.</param>
    public void InitializeForEdit(Guid barberProfileId, BarberServiceItem item)
    {
        Initialize(barberProfileId);
        InitializeForEdit(item);
    }

    /// <summary>
    /// Готовит правку строки прайса, если профиль уже привязан.
    /// </summary>
    /// <param name="item">Строка прайса.</param>
    public void InitializeForEdit(BarberServiceItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        IsAddMode = false;
        IsEditMode = true;
        _editedService = item;
        ServiceName = item.Name;
        SelectedCatalogService = null;
        PriceText = item.Price.ToString("0.##", CultureInfo.InvariantCulture);
        DurationText = item.DurationMinutes.ToString(CultureInfo.InvariantCulture);
        CatalogGroups.Clear();
        IsCatalogEmpty = false;
        CatalogError = null;
        ClearErrors();
    }

    /// <summary>
    /// Сохраняет услугу: <c>POST</c> в режиме добавления, <c>PUT</c> в режиме правки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача сохранения.</returns>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (IsSaving)
        {
            return;
        }

        if (!Validate(out var price, out var duration))
        {
            return;
        }

        if (BarberProfileId is not { } profileId)
        {
            FormError = SaveFailedText;

            return;
        }

        if (IsAddMode && SelectedCatalogService is null)
        {
            FormError = ChooseServiceText;

            return;
        }

        IsSaving = true;
        FormError = null;

        try
        {
            if (IsEditMode && _editedService is { } service)
            {
                await _profileApi.UpdateServiceAsync(
                    profileId,
                    service.HairServiceId,
                    new UpdateBarberProfileServiceRequest(price, duration),
                    cancellationToken);

                Saved?.Invoke(this, Loc.T("Цена обновлена."));
            }
            else
            {
                var selectedService = SelectedCatalogService!;

                // Специализация выбранной услуги появляется в профиле вместе с услугой:
                // отдельного шага «Добавить специализацию» больше нет. Сначала пишется она,
                // чтобы услуга не оказалась в прайсе без своей специализации.
                await EnsureSpecializationAttachedAsync(
                    profileId,
                    selectedService,
                    cancellationToken);

                await _profileApi.AddServiceAsync(
                    profileId,
                    new AddBarberProfileServiceRequest(
                        selectedService.Id,
                        price,
                        duration),
                    cancellationToken);

                Saved?.Invoke(this, Loc.T("Услуга добавлена."));
            }
        }
        catch (Exception exception)
        {
            ApplySaveFailure(exception);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Повторяет чтение справочника после ошибки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RetryCatalogAsync(CancellationToken cancellationToken)
    {
        CatalogError = null;

        await LoadCatalogAsync(cancellationToken);
    }

    /// <summary>
    /// Подставляет длительность по умолчанию при выборе услуги и переносит подсветку строки.
    /// </summary>
    /// <param name="value">Выбранная услуга; <c>null</c> — выбор снят.</param>
    partial void OnSelectedCatalogServiceChanged(BarberServiceCatalogItem? value)
    {
        // Подсветка — состояние строки шаблона: редактор снимает флаг со всех услуг и ставит
        // его выбранной, иначе в списке остались бы две подсвеченные строки.
        foreach (var group in CatalogGroups)
        {
            foreach (var item in group.Services)
            {
                item.IsSelected = ReferenceEquals(item, value);
            }

            // Выбор раскрывает свою группу: подсвеченная строка не должна оказаться под
            // закрытым заголовком. Остальные группы это не трогает — сравнение услуг идёт
            // по двум направлениям сразу.
            if (value is not null && group.Services.Contains(value))
            {
                group.IsExpanded = true;
            }
        }

        if (value is not null && IsAddMode)
        {
            DurationText = value.DefaultDurationMinutes.ToString(CultureInfo.InvariantCulture);
            FormError = null;
        }

        OnPropertyChanged(nameof(IsFormVisible));
    }

    /// <summary>
    /// Показывает форму, когда режим добавления включает её без выбранной услуги.
    /// </summary>
    partial void OnIsAddModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFormVisible));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(IsServiceSearchVisible));
    }

    /// <summary>
    /// Показывает форму правки, у которой услуга фиксирована.
    /// </summary>
    partial void OnIsEditModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFormVisible));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    /// <summary>
    /// Пересчитывает видимость строки ошибки цены.
    /// </summary>
    partial void OnPriceErrorChanged(string? value) => OnPropertyChanged(nameof(HasPriceError));

    /// <summary>
    /// Пересчитывает видимость строки ошибки длительности.
    /// </summary>
    partial void OnDurationErrorChanged(string? value) => OnPropertyChanged(nameof(HasDurationError));

    /// <summary>
    /// Пересчитывает видимость общей ошибки формы.
    /// </summary>
    partial void OnFormErrorChanged(string? value) => OnPropertyChanged(nameof(HasFormError));

    /// <summary>
    /// Пересчитывает видимость причины сбоя справочника.
    /// </summary>
    partial void OnCatalogErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasCatalogError));
        OnPropertyChanged(nameof(IsServiceSearchVisible));
    }

    /// <summary>
    /// Пересчитывает доступность кнопки вместе с флагом сохранения.
    /// </summary>
    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Перефильтровывает справочник на каждое изменение строки поиска.
    /// </summary>
    /// <param name="value">Новая строка поиска.</param>
    partial void OnServiceQueryChanged(string value) => ApplyServiceQuery();

    /// <summary>
    /// Очищает строку поиска: «×» в поле и «Сбросить» в пустом результате зовут одно и то же.
    /// </summary>
    [RelayCommand]
    private void ClearServiceQuery() => ServiceQuery = string.Empty;

    /// <summary>
    /// Сворачивает или раскрывает одну группу справочника.
    /// </summary>
    /// <param name="group">Группа, по заголовку которой нажали.</param>
    /// <remarks>
    /// Это не аккордеон: соседние группы остаются как были. Мастер сравнивает услуги двух
    /// направлений, и закрывать открытое им же — не помощь.
    /// </remarks>
    [RelayCommand]
    private void ToggleGroup(BarberServiceCatalogGroup? group)
    {
        if (group is null || !CatalogGroups.Contains(group))
        {
            return;
        }

        group.IsExpanded = !group.IsExpanded;
    }

    /// <summary>
    /// Раскрывает группу выбранной услуги, а без выбора — первую.
    /// </summary>
    /// <remarks>
    /// Первая группа — первая стрижка: с каталожным порядком это самое вероятное направление
    /// мастера, и один открытый заголовок на экран из шестнадцати и есть смысл свёрнутого
    /// списка. Остальные закрыты, чтобы каталог занимал один экран.
    /// </remarks>
    private void ApplyDefaultExpansion()
    {
        var selected = SelectedCatalogService;
        BarberServiceCatalogGroup? selectedGroup = null;

        if (selected is not null)
        {
            foreach (var group in CatalogGroups)
            {
                if (group.Services.Contains(selected))
                {
                    selectedGroup = group;

                    break;
                }
            }
        }

        for (var index = 0; index < CatalogGroups.Count; index++)
        {
            var group = CatalogGroups[index];

            group.IsExpanded = selectedGroup is not null
                ? ReferenceEquals(group, selectedGroup)
                : index == 0;
        }
    }

    /// <summary>
    /// Пересчитывает видимые строки, счётчики и раскрытие под текущую строку поиска.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Группа без совпадений прячется целиком, группа с совпадениями раскрывается на каждом
    /// изменении запроса: попадание не должно остаться под свёрнутым заголовком.
    /// </para>
    /// <para>
    /// Раскрытие до поиска запоминается на переходе «пусто → непусто» и возвращается, когда
    /// строку очистили: мастер, открывший «Окрашивание», возвращается к своему виду, а не к
    /// виду по умолчанию.
    /// </para>
    /// </remarks>
    private void ApplyServiceQuery()
    {
        var isSearching = !BarberServiceCatalogFilter.IsEmpty(ServiceQuery);

        if (isSearching && !_isSearchingCatalog)
        {
            _expansionBeforeQuery.Clear();

            foreach (var group in CatalogGroups)
            {
                _expansionBeforeQuery[group] = group.IsExpanded;
            }
        }

        _isSearchingCatalog = isSearching;

        foreach (var group in CatalogGroups)
        {
            group.ApplyQuery(ServiceQuery);

            if (isSearching && group.HasMatches)
            {
                group.IsExpanded = true;
            }
        }

        if (!isSearching)
        {
            RestoreExpansion();
        }

        OnPropertyChanged(nameof(HasNoMatches));
    }

    /// <summary>
    /// Возвращает раскрытие групп, которое было до поиска.
    /// </summary>
    private void RestoreExpansion()
    {
        if (_expansionBeforeQuery.Count == 0)
        {
            return;
        }

        foreach (var group in CatalogGroups)
        {
            if (_expansionBeforeQuery.TryGetValue(group, out var isExpanded))
            {
                group.IsExpanded = isExpanded;
            }
        }

        _expansionBeforeQuery.Clear();
    }

    /// <summary>
    /// Читает справочники услуг и специализаций и раскладывает услуги по группам.
    /// </summary>
    /// <remarks>
    /// Два чтения уходят параллельно, как в вебе: плоский список услуг не несёт названия
    /// специализации, поэтому заголовки групп приходят из второго справочника. Повтор после
    /// ошибки спрашивает оба — услуги без специализаций не из чего собрать ни одной группы,
    /// в отличие от фильтров «Поиска», где каждый справочник кормит свой фильтр.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public async Task LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoadingCatalog)
        {
            return;
        }

        IsLoadingCatalog = true;
        CatalogError = null;
        IsCatalogEmpty = false;
        _expansionBeforeQuery.Clear();

        try
        {
            var servicesTask = _searchApi.GetHairServicesAsync(cancellationToken);
            var specializationsTask = _searchApi.GetSpecializationsAsync(cancellationToken);

            await Task.WhenAll(servicesTask, specializationsTask);

            var services = await servicesTask;
            var specializations = await specializationsTask;

            var available = new List<BarberServiceCatalogItem>(services.Count);

            foreach (var service in services)
            {
                if (!_existingHairServiceIds.Contains(service.Id))
                {
                    available.Add(new BarberServiceCatalogItem(service));
                }
            }

            CatalogGroups.Clear();

            foreach (var group in BarberServiceCatalogGroups.Build(
                         available,
                         specializations,
                         Loc.T("Прочие услуги")))
            {
                CatalogGroups.Add(group);
            }

            IsCatalogEmpty = CatalogGroups.Count == 0;

            // Свежий снимок справочника раскладывается по умолчанию, а не по раскрытию старого:
            // группы — новые объекты, и запомненное раскрытие к ним не относится.
            ApplyDefaultExpansion();

            if (!BarberServiceCatalogFilter.IsEmpty(ServiceQuery))
            {
                // Поиск, переживший перечитывание, — это поиск по новому снимку: запоминаем
                // раскрытие по умолчанию, чтобы очистка строки вернула именно его.
                _isSearchingCatalog = false;
                ApplyServiceQuery();
            }

            OnPropertyChanged(nameof(IsServiceSearchVisible));
            OnPropertyChanged(nameof(HasNoMatches));
        }
        catch (Exception exception)
        {
            var kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                CatalogError = BarberProfileText.ReadError(
                    exception,
                    kind,
                    CatalogFailedText);
            }
        }
        finally
        {
            IsLoadingCatalog = false;
        }
    }

    /// <summary>
    /// Добавляет специализацию выбранной услуги, если её ещё нет в профиле.
    /// </summary>
    /// <remarks>
    /// Повторное добавление сервер отверг бы кодом дубликата; он же приходит, когда
    /// специализацию успели добавить с другого устройства, и тогда услугу можно сохранять.
    /// Любой другой отказ прерывает сохранение: услуга не должна попасть в прайс без
    /// специализации, по которой её найдут в фильтре.
    /// </remarks>
    /// <param name="profileId">Профиль мастера.</param>
    /// <param name="service">Выбранная услуга справочника.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача записи.</returns>
    private async Task EnsureSpecializationAttachedAsync(
        Guid profileId,
        BarberServiceCatalogItem service,
        CancellationToken cancellationToken)
    {
        var specializationId = service.SpecializationId;

        if (specializationId == Guid.Empty
            || !_attachedSpecializationIds.Add(specializationId))
        {
            return;
        }

        try
        {
            await _profileApi.AddSpecializationAsync(
                profileId,
                specializationId,
                cancellationToken);
        }
        catch (BarberSearchApiException exception)
            when (exception.ErrorCodes.Contains("barberProfile.addSpecialization.duplicate"))
        {
            // Специализация уже есть на сервере: профиль догоняет состояние, услугу пишем.
        }
        catch
        {
            _attachedSpecializationIds.Remove(specializationId);
            throw;
        }
    }

    /// <summary>
    /// Проверяет цену и длительность.
    /// </summary>
    /// <param name="price">Разобранная цена.</param>
    /// <param name="duration">Разобранная длительность.</param>
    /// <returns><c>true</c>, если можно отправлять.</returns>
    private bool Validate(out decimal price, out int duration)
    {
        ClearErrors();

        price = 0m;
        duration = 0;

        var isValid = true;
        var priceText = PriceText.Trim();

        if (priceText.Length == 0)
        {
            PriceError = PriceRequiredText;
            isValid = false;
        }
        else if (!TryParsePrice(priceText, out price))
        {
            PriceError = PriceRangeText;
            isValid = false;
        }
        else if (CountDecimals(priceText) > 2)
        {
            PriceError = PriceDecimalsText;
            isValid = false;
        }

        if (!int.TryParse(
                DurationText.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out duration)
            || duration < MinDurationMinutes
            || duration > MaxDurationMinutes)
        {
            DurationError = DurationRangeText;
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Разбирает цену с точкой или запятой; диапазон — от нуля до верхней границы.
    /// </summary>
    private static bool TryParsePrice(string text, out decimal price)
    {
        var normalized = text.Replace(',', '.');

        return decimal.TryParse(
                normalized,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out price)
            && price >= 0m
            && price <= MaxPrice;
    }

    /// <summary>
    /// Считает знаки после запятой: больше двух сервер округлит молча.
    /// </summary>
    private static int CountDecimals(string text)
    {
        var separator = text.IndexOfAny(['.', ',']);

        return separator < 0 ? 0 : text.Length - separator - 1;
    }

    /// <summary>
    /// Разбирает отказ сервера по коду услуги.
    /// </summary>
    /// <param name="exception">Исключение API.</param>
    private void ApplySaveFailure(Exception exception)
    {
        if (exception is not BarberSearchApiException apiException)
        {
            FormError = SaveFailedText;

            return;
        }

        var code = apiException.ErrorCodes.Count > 0 ? apiException.ErrorCodes[0] : null;

        FormError = code switch
        {
            "barberProfile.addService.duplicate" => Loc.T("Эта услуга уже есть в прайсе."),
            "barberProfile.updateService.notFound" => Loc.T("Эта услуга уже скрыта. Обновите страницу."),
            "barberProfile.removeService.notFound" => Loc.T("Эта услуга уже скрыта. Обновите страницу."),
            "barberProfile.addService.hairServiceNotFound" => Loc.T("Услуга справочника не найдена."),
            "barberProfile.addService.price" => Loc.T("Цена не может быть отрицательной."),
            "barberService.updatePrice" => Loc.T("Цена не может быть отрицательной."),
            "barberProfile.addService.duration" => Loc.T("Длительность — от 1 до 1440 минут."),
            "barberService.updateDuration" => Loc.T("Длительность — от 1 до 1440 минут."),
            "barberProfile.addSpecialization.specializationNotFound" => Loc.T("Специализация не найдена."),
            _ => apiException.StatusCode switch
            {
                HttpStatusCode.Forbidden => Loc.T("Прайс доступен только владельцу профиля."),
                HttpStatusCode.NotFound => Loc.T("Услуга не найдена. Обновите прайс."),
                _ => BarberProfileText.WriteError(apiException, SaveFailedText),
            },
        };
    }

    /// <summary>
    /// Сбрасывает ошибки полей перед новой проверкой.
    /// </summary>
    private void ClearErrors()
    {
        PriceError = null;
        DurationError = null;
        FormError = null;
    }
}
