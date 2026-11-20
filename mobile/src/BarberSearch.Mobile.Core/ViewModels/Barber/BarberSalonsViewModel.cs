using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Раздел «Салоны» кабинета мастера: привязанные места, основной салон и поиск по каталогу.
/// </summary>
/// <remarks>
/// <para>
/// Список — рабочий, а не витрина: сервер отдаёт все привязки профиля, включая салон на
/// проверке и отклонённый, и основной первым. Порядок и признак «основной» не
/// пересобираются на клиенте: их решает домен, а экран показывает ответ как есть.
/// </para>
/// <para>
/// «Сделать основным» — мобильное дополнение к вебу: в браузере основной салон выбирают
/// только при привязке, а с телефона это одна кнопка на карточке. Как и привязка, это
/// запись, поэтому она не повторяется сама: после сбоя исход неизвестен, и человек сам
/// решает, повторить ли действие.
/// </para>
/// <para>
/// Каталог ищется с паузой 300 мс и только с двух символов — как в вебе. Новый запрос
/// отменяет предыдущий: медленный ответ на «Мо» не должен перетереть быстрый на «Моск».
/// Чтение каталога повторяется само, но повторяет ровно упавший запрос: правка поля
/// останавливает цикл, чтобы старые результаты не появились поверх новой строки.
/// </para>
/// <para>
/// Привязка не дублируется: <c>409</c>/<c>404</c> и коды <c>addSalon.duplicate</c> /
/// <c>addSalon.salonNotFound</c> означают, что выбор устарел, поэтому причина показывается,
/// а список перечитывается — добавлять второй раз то же самое бессмысленно.
/// </para>
/// </remarks>
public sealed partial class BarberSalonsViewModel : ObservableObject, IBarberCabinetSection
{
    /// <summary>
    /// Минимум символов поиска каталога: короче сервер отвечает ошибкой.
    /// </summary>
    public const int MinimumCatalogQueryLength = 2;

    /// <summary>
    /// Пауза между последним нажатием и запросом каталога: как у веба.
    /// </summary>
    public static readonly TimeSpan CatalogDebounce = TimeSpan.FromMilliseconds(300);

    private const string DuplicateSalonCode = "barberProfile.addSalon.duplicate";
    private const string MissingSalonCode = "barberProfile.addSalon.salonNotFound";

    private static string LoadFailedText => Loc.T("Не удалось загрузить салоны.");
    private static string AddFailedText => Loc.T("Не удалось добавить салон. Попробуйте ещё раз.");
    private static string PrimaryFailedText => Loc.T("Не удалось изменить основной салон.");
    private static string CatalogFailedText => Loc.T("Не удалось загрузить каталог.");
    private static string AddedText => Loc.T("Салон добавлен.");
    private static string PrimaryChangedText => Loc.T("Основной салон изменён.");
    private static string EmptyTitleText => Loc.T("Салон не добавлен");
    private static string EmptyDescriptionText => Loc.T("Без салона расписание и запись не работают.");

    private readonly IBarberProfileApi _profileApi;
    private readonly IBarberSearchApi _searchApi;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly AutoRetryLoop _salonsAutoRetry;
    private readonly AutoRetryLoop _catalogAutoRetry;

    private Guid _profileId = Guid.Empty;
    private bool _isActive;
    private bool _isActivating;
    private bool _hasLoaded;
    private bool _catalogLoaded;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _catalogRequest;
    private Task _catalogOperation = Task.CompletedTask;
    private int _generation;
    private ApiFailureKind _salonsFailureKind;
    private ApiFailureKind _catalogFailureKind;
    private string? _catalogFailedCity;
    private string? _catalogFailedQuery;

    /// <summary>
    /// Создаёт раздел салонов.
    /// </summary>
    /// <param name="profileApi">Список привязок, привязка и назначение основного.</param>
    /// <param name="searchApi">Публичный каталог салонов.</param>
    /// <param name="salonsAutoRetry">
    /// Цикл повтора чтения салонов; <c>null</c> — собственный цикл.
    /// </param>
    /// <param name="catalogAutoRetry">
    /// Цикл повтора каталога; <c>null</c> — собственный цикл.
    /// </param>
    public BarberSalonsViewModel(
        IBarberProfileApi profileApi,
        IBarberSearchApi searchApi,
        AutoRetryLoop? salonsAutoRetry = null,
        AutoRetryLoop? catalogAutoRetry = null)
        : this(
            profileApi,
            searchApi,
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
            salonsAutoRetry,
            catalogAutoRetry)
    {
    }

    /// <summary>
    /// Создаёт раздел салонов с подменяемой паузой — для тестов без ожидания таймера.
    /// </summary>
    /// <param name="profileApi">Список привязок, привязка и назначение основного.</param>
    /// <param name="searchApi">Публичный каталог салонов.</param>
    /// <param name="delay">Пауза перед запросом каталога.</param>
    /// <param name="salonsAutoRetry">Цикл повтора чтения салонов.</param>
    /// <param name="catalogAutoRetry">Цикл повтора каталога.</param>
    internal BarberSalonsViewModel(
        IBarberProfileApi profileApi,
        IBarberSearchApi searchApi,
        Func<TimeSpan, CancellationToken, Task> delay,
        AutoRetryLoop? salonsAutoRetry = null,
        AutoRetryLoop? catalogAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(profileApi);
        ArgumentNullException.ThrowIfNull(searchApi);
        ArgumentNullException.ThrowIfNull(delay);

        _profileApi = profileApi;
        _searchApi = searchApi;
        _delay = delay;

        _salonsAutoRetry = salonsAutoRetry ?? new AutoRetryLoop();
        _catalogAutoRetry = catalogAutoRetry ?? new AutoRetryLoop();
        _salonsAutoRetry.StateChanged += OnSalonsAutoRetryStateChanged;
        _catalogAutoRetry.StateChanged += OnCatalogAutoRetryStateChanged;
    }

    /// <inheritdoc />
    public Guid? BarberProfileId => _profileId == Guid.Empty ? null : _profileId;

    /// <summary>
    /// Привязанные салоны в порядке сервера: основной первым.
    /// </summary>
    public ObservableCollection<BarberSalonItem> Salons { get; } = [];

    /// <summary>
    /// Результаты последнего поиска каталога в порядке сервера.
    /// </summary>
    public ObservableCollection<BarberSalonCatalogItem> Catalog { get; } = [];

    /// <summary>
    /// Идёт первое чтение списка: на экране индикатор.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstLoading;

    /// <summary>
    /// Идёт обновление свайпом: индикатор держит <c>RefreshView</c>.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Причина неудачи чтения списка; <c>null</c>, когда читать удалось.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Цикл сам повторяет упавшее чтение списка.
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Объявление после успешной привязки или смены основного.
    /// </summary>
    [ObservableProperty]
    private string? _announcement;

    /// <summary>
    /// Идёт назначение основного салона: кнопки не нажимаются дважды.
    /// </summary>
    [ObservableProperty]
    private bool _isSettingPrimary;

    /// <summary>
    /// Причина неудачи смены основного салона.
    /// </summary>
    [ObservableProperty]
    private string? _actionError;

    /// <summary>
    /// Город поиска каталога.
    /// </summary>
    [ObservableProperty]
    private string _catalogCity = string.Empty;

    /// <summary>
    /// Строка поиска каталога: название или адрес.
    /// </summary>
    [ObservableProperty]
    private string _catalogQuery = string.Empty;

    /// <summary>
    /// Идёт поиск каталога, включая паузу перед запросом.
    /// </summary>
    [ObservableProperty]
    private bool _isCatalogLoading;

    /// <summary>
    /// Причина неудачи поиска каталога.
    /// </summary>
    [ObservableProperty]
    private string? _catalogError;

    /// <summary>
    /// Цикл сам повторяет упавший поиск каталога.
    /// </summary>
    [ObservableProperty]
    private bool _isCatalogAutoRetrying;

    /// <summary>
    /// Выбранная строка каталога для привязки.
    /// </summary>
    [ObservableProperty]
    private BarberSalonCatalogItem? _selectedCatalogSalon;

    /// <summary>
    /// Привязать выбранный салон как основной, даже если в профиле уже есть салоны.
    /// </summary>
    [ObservableProperty]
    private bool _makePrimary;

    /// <summary>
    /// Идёт привязка выбранного салона.
    /// </summary>
    [ObservableProperty]
    private bool _isAddingSalon;

    /// <summary>
    /// Причина неудачи привязки салона: показывается под каталогом.
    /// </summary>
    [ObservableProperty]
    private string? _addSalonError;

    /// <summary>
    /// Список уже читался: первое чтение закончилось успешно хотя бы раз.
    /// </summary>
    public bool HasLoaded => _hasLoaded;

    /// <summary>
    /// В профиле есть привязанные салоны.
    /// </summary>
    public bool HasSalons => Salons.Count > 0;

    /// <summary>
    /// Показывать список салонов.
    /// </summary>
    public bool ShowContent => !IsFirstLoading && Salons.Count > 0;

    /// <summary>
    /// Показывать пустое состояние: читать удалось, но салонов нет.
    /// </summary>
    public bool ShowEmpty => !IsFirstLoading && _hasLoaded
        && ErrorMessage is null && Salons.Count == 0;

    /// <summary>
    /// Показывать карточку ошибки: салонов нет и читать не удалось.
    /// </summary>
    public bool ShowError => !IsFirstLoading && ErrorMessage is not null && Salons.Count == 0;

    /// <summary>
    /// Заголовок пустого состояния.
    /// </summary>
    public string EmptyTitle => Salons.Count == 0 ? EmptyTitleText : string.Empty;

    /// <summary>
    /// Подпись пустого состояния.
    /// </summary>
    public string EmptyDescription => Salons.Count == 0 ? EmptyDescriptionText : string.Empty;

    /// <summary>
    /// Есть баннер о результате.
    /// </summary>
    public bool HasAnnouncement => !string.IsNullOrEmpty(Announcement);

    /// <summary>
    /// Есть ошибка смены основного салона.
    /// </summary>
    public bool HasActionError => ActionError is not null;

    /// <summary>
    /// Есть ошибка привязки салона.
    /// </summary>
    public bool HasAddSalonError => AddSalonError is not null;

    /// <summary>
    /// Первый салон профиля: он станет основным независимо от выбора.
    /// </summary>
    public bool FirstSalonBecomesPrimary => Salons.Count == 0;

    /// <summary>
    /// Подпись переключателя «сделать основным»: у первого салона выбора нет.
    /// </summary>
    public string PrimaryToggleText => FirstSalonBecomesPrimary
        ? Loc.T("Первый салон станет основным")
        : Loc.T("Сделать основным");

    /// <summary>
    /// Запрос каталога достаточно длинный.
    /// </summary>
    public bool CanSearchCatalog => CatalogQuery.Trim().Length >= MinimumCatalogQueryLength;

    /// <summary>
    /// Подсказка под полем поиска: строка короче минимума.
    /// </summary>
    public string CatalogHint => CanSearchCatalog
        ? string.Empty
        : Loc.T(
            "Начните вводить название или адрес — минимум {0} символа.",
            MinimumCatalogQueryLength);

    /// <summary>
    /// В каталоге есть результаты.
    /// </summary>
    public bool ShowCatalogResults => Catalog.Count > 0;

    /// <summary>
    /// Поиск закончился, но ничего не нашёл.
    /// </summary>
    public bool ShowCatalogEmpty => CanSearchCatalog
        && _catalogLoaded
        && !IsCatalogLoading
        && CatalogError is null
        && Catalog.Count == 0;

    /// <summary>
    /// Поиск каталога упал.
    /// </summary>
    public bool ShowCatalogError => CatalogError is not null;

    /// <summary>
    /// Привязка доступна: список уже прочитан, выбрана строка каталога и профиль известен.
    /// </summary>
    /// <remarks>
    /// Без успешного чтения списка неизвестно, есть ли уже салоны: первый салон становится
    /// основным, и привязка до ответа могла бы зря сменить основной салон профиля.
    /// </remarks>
    public bool CanAddSalon => _hasLoaded
        && SelectedCatalogSalon is not null
        && !IsAddingSalon
        && _profileId != Guid.Empty;

    /// <summary>
    /// Подпись кнопки привязки.
    /// </summary>
    public string AddSalonButtonText => IsAddingSalon
        ? Loc.T("Добавляем…")
        : Loc.T("Добавить");

    /// <summary>
    /// Цикл повтора списка: тестам нужен счётчик попыток и завершение.
    /// </summary>
    internal AutoRetryLoop SalonsAutoRetry => _salonsAutoRetry;

    /// <summary>
    /// Цикл повтора каталога: тестам нужен счётчик попыток и завершение.
    /// </summary>
    internal AutoRetryLoop CatalogAutoRetry => _catalogAutoRetry;

    /// <summary>
    /// Завершается, когда цикл списка останавливается.
    /// </summary>
    internal Task SalonsRetryCompletion => _salonsAutoRetry.Completion;

    /// <summary>
    /// Завершается, когда цикл каталога останавливается.
    /// </summary>
    internal Task CatalogRetryCompletion => _catalogAutoRetry.Completion;

    /// <summary>
    /// Завершение последнего поиска каталога — тестам, чтобы не ждать таймер.
    /// </summary>
    internal Task CatalogOperation => _catalogOperation;

    /// <inheritdoc />
    public void Initialize(Guid barberProfileId)
    {
        if (_profileId == barberProfileId)
        {
            OnPropertyChanged(nameof(BarberProfileId));

            return;
        }

        _profileId = barberProfileId;

        _salonsAutoRetry.Stop();
        _catalogAutoRetry.Stop();
        CancelCatalogRequest();

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        _generation++;

        Salons.Clear();
        Catalog.Clear();

        _hasLoaded = false;
        _catalogLoaded = false;
        _salonsFailureKind = ApiFailureKind.None;
        _catalogFailureKind = ApiFailureKind.None;
        _catalogFailedCity = null;
        _catalogFailedQuery = null;

        IsFirstLoading = false;
        IsRefreshing = false;
        ErrorMessage = null;
        IsAutoRetrying = false;
        Announcement = null;
        IsSettingPrimary = false;
        ActionError = null;
        IsCatalogLoading = false;
        CatalogError = null;
        IsCatalogAutoRetrying = false;
        SelectedCatalogSalon = null;
        MakePrimary = false;
        IsAddingSalon = false;
        AddSalonError = null;

        // Пустая строка снимает прошлый запрос и подсказку; если поля уже пусты,
        // сеттер не сработает, поэтому состояние пересчитывается явно.
        CatalogQuery = string.Empty;
        CatalogCity = string.Empty;

        NotifySalonState();
        NotifyCatalogState();
        OnPropertyChanged(nameof(BarberProfileId));
    }

    /// <inheritdoc />
    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        if (_profileId == Guid.Empty)
        {
            return;
        }

        _isActive = true;

        if (_isActivating)
        {
            return;
        }

        _isActivating = true;

        var silent = _hasLoaded;

        if (silent)
        {
            IsRefreshing = true;
        }

        try
        {
            await LoadSalonsAsync(silent, cancellationToken);
        }
        finally
        {
            _isActivating = false;
            IsRefreshing = false;
        }

        if (CanSearchCatalog
            && !_catalogLoaded
            && _catalogRequest is null
            && CatalogError is null
            && Catalog.Count == 0)
        {
            // Раздел вернулся с набранным, но так и не выполненным запросом: Deactivate
            // остановил паузу, и без повторного запуска поле осталось бы без объяснения.
            BeginCatalogSearch(debounce: false);
        }

        TryStartSalonsAutoRetry();
        TryStartCatalogAutoRetry();
    }

    /// <inheritdoc />
    public void Deactivate()
    {
        _isActive = false;

        _salonsAutoRetry.Stop();
        _catalogAutoRetry.Stop();
        CancelCatalogRequest();

        IsAutoRetrying = false;
        IsCatalogAutoRetrying = false;
    }

    /// <summary>
    /// Сообщает, что раздел виден, и запускает автоповтор упавших чтений.
    /// </summary>
    /// <remarks>
    /// Если раздел вернулся с набранным, но так и не выполненным запросом каталога
    /// (уход в фон отменил паузу), поиск повторяется: иначе поле осталось бы с текстом
    /// и пустым списком без объяснения.
    /// </remarks>
    public void StartAutoRetry()
    {
        _isActive = true;

        if (CanSearchCatalog
            && !_catalogLoaded
            && _catalogRequest is null
            && CatalogError is null
            && Catalog.Count == 0)
        {
            BeginCatalogSearch(debounce: false);
        }

        TryStartSalonsAutoRetry();
        TryStartCatalogAutoRetry();
    }

    /// <summary>
    /// Останавливает автоповтор, не сбрасывая показанные данные.
    /// </summary>
    public void StopAutoRetry() => Deactivate();

    /// <summary>
    /// Перечитывает список салонов по кнопке — сразу, без паузы цикла.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        if (_profileId == Guid.Empty || IsFirstLoading)
        {
            return;
        }

        _salonsAutoRetry.Stop();

        await LoadSalonsAsync(Salons.Count > 0, cancellationToken);

        TryStartSalonsAutoRetry();
    }

    /// <summary>
    /// Обновляет список свайпом.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (IsFirstLoading || IsRefreshing || _profileId == Guid.Empty)
        {
            IsRefreshing = false;

            return;
        }

        IsRefreshing = true;
        _salonsAutoRetry.Stop();

        try
        {
            await LoadSalonsAsync(silent: true, cancellationToken);
        }
        finally
        {
            IsRefreshing = false;
        }

        TryStartSalonsAutoRetry();
    }

    /// <summary>
    /// Назначает салон основным: сервер сам снимает признак с прежнего.
    /// </summary>
    /// <param name="item">Строка салона из списка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task SetPrimaryAsync(
        BarberSalonItem? item,
        CancellationToken cancellationToken)
    {
        if (item is null
            || item.IsPrimary
            || IsSettingPrimary
            || _profileId == Guid.Empty)
        {
            return;
        }

        IsSettingPrimary = true;
        ActionError = null;

        try
        {
            await _profileApi.SetPrimarySalonAsync(
                _profileId,
                item.SalonId,
                cancellationToken);

            Announcement = PrimaryChangedText;

            await LoadSalonsAsync(silent: true, CancellationToken.None);
        }
        catch (BarberSearchApiException exception)
            when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            // Привязку убрали с другого устройства: строка устарела, список надо перечитать.
            ActionError = BarberSalonText.WriteError(exception, PrimaryFailedText);

            await LoadSalonsAsync(silent: true, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Экран ушёл: ошибку показывать некому.
        }
        catch (Exception exception)
        {
            ActionError = BarberSalonText.WriteError(exception, PrimaryFailedText);
        }
        finally
        {
            IsSettingPrimary = false;
        }
    }

    /// <summary>
    /// Привязывает выбранный салон; первый салон профиля становится основным.
    /// </summary>
    /// <remarks>
    /// Повторная привязка того же салона (<c>409</c>/<c>404</c> или код дубликата) — не
    /// ошибка ввода: причина показывается, список перечитывается, а выбранная строка
    /// остаётся, чтобы человек мог выбрать другую.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand(CanExecute = nameof(CanAddSalon))]
    private async Task AddSalonAsync(CancellationToken cancellationToken)
    {
        if (SelectedCatalogSalon is not { } salon
            || IsAddingSalon
            || _profileId == Guid.Empty)
        {
            return;
        }

        IsAddingSalon = true;
        AddSalonError = null;

        try
        {
            await _profileApi.AddSalonAsync(
                _profileId,
                salon.Id,
                FirstSalonBecomesPrimary || MakePrimary,
                cancellationToken);

            Catalog.Remove(salon);
            SelectedCatalogSalon = null;
            MakePrimary = false;

            Announcement = AddedText;

            await LoadSalonsAsync(silent: true, CancellationToken.None);
        }
        catch (BarberSearchApiException exception) when (IsStaleAttach(exception))
        {
            AddSalonError = BarberSalonText.WriteError(exception, AddFailedText);

            await LoadSalonsAsync(silent: true, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Экран ушёл: ошибку показывать некому.
        }
        catch (Exception exception)
        {
            AddSalonError = BarberSalonText.WriteError(exception, AddFailedText);
        }
        finally
        {
            IsAddingSalon = false;
        }
    }

    /// <summary>
    /// Запускает поиск каталога сразу, без паузы: кнопка «Найти» на клавиатуре.
    /// </summary>
    [RelayCommand]
    private void SearchCatalog() => BeginCatalogSearch(debounce: false);

    /// <summary>
    /// Убирает баннер о результате.
    /// </summary>
    [RelayCommand]
    private void DismissAnnouncement() => Announcement = null;

    /// <summary>
    /// Убирает ошибку смены основного салона.
    /// </summary>
    [RelayCommand]
    private void DismissActionError() => ActionError = null;

    /// <summary>
    /// Убирает ошибку привязки салона.
    /// </summary>
    [RelayCommand]
    private void DismissAddSalonError() => AddSalonError = null;

    /// <summary>
    /// Читает список привязанных салонов.
    /// </summary>
    /// <param name="silent">Обновление уже показанного списка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему решает цикл повтора.</returns>
    private async Task<ApiFailureKind> LoadSalonsAsync(
        bool silent,
        CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        var cts = StartRequest(cancellationToken);

        // Тихая попытка могла отменить ещё не закончившееся первое чтение: индикатор
        // первого чтения принадлежит отменённому запросу, и оставить его включённым значит
        // навсегда показать пустой экран со спиннером.
        if (silent)
        {
            IsFirstLoading = false;
        }

        if (!silent)
        {
            IsFirstLoading = true;
            ErrorMessage = null;
        }

        var kind = ApiFailureKind.None;

        try
        {
            var salons = await _profileApi.GetSalonsAsync(_profileId, cts.Token);

            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            Salons.Clear();

            foreach (var salon in salons)
            {
                Salons.Add(new BarberSalonItem(salon));
            }

            _hasLoaded = true;
            ErrorMessage = null;
            _salonsFailureKind = ApiFailureKind.None;
        }
        catch (Exception exception)
        {
            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            kind = ApiFailureClassifier.Classify(exception, cts.Token);

            if (kind != ApiFailureKind.None)
            {
                _salonsFailureKind = kind;

                // Тихий повтор не стирает уже показанные салоны: причина нужна только
                // пустому списку, где она объясняет отсутствие данных.
                if (Salons.Count == 0)
                {
                    ErrorMessage = BarberProfileText.ReadError(
                        exception,
                        kind,
                        LoadFailedText);
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
                IsFirstLoading = false;
            }

            cts.Dispose();
            NotifySalonState();
        }

        if (kind.IsRetryable())
        {
            TryStartSalonsAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Начинает поиск каталога: отменяет прошлый и запускает новый с паузой или сразу.
    /// </summary>
    /// <param name="debounce">Ждать паузу перед запросом.</param>
    private void BeginCatalogSearch(bool debounce)
    {
        CancelCatalogRequest();

        CatalogError = null;
        AddSalonError = null;
        _catalogFailureKind = ApiFailureKind.None;
        _catalogFailedCity = null;
        _catalogFailedQuery = null;
        _catalogLoaded = false;

        Catalog.Clear();
        SelectedCatalogSalon = null;

        if (!CanSearchCatalog)
        {
            NotifyCatalogState();

            return;
        }

        var city = CatalogCity.Trim();
        var query = CatalogQuery.Trim();
        var request = new CancellationTokenSource();

        _catalogRequest = request;
        IsCatalogLoading = true;
        NotifyCatalogState();

        _catalogOperation = debounce
            ? SearchAfterDebounceAsync(city, query, request)
            : LoadCatalogAsync(city, query, silent: false, request);
    }

    /// <summary>
    /// Ждёт паузу и читает каталог; отмена нового строкой — не ошибка.
    /// </summary>
    /// <param name="city">Город запроса.</param>
    /// <param name="query">Строка запроса.</param>
    /// <param name="request">Источник отмены этого запроса.</param>
    /// <returns>Задача поиска.</returns>
    private async Task SearchAfterDebounceAsync(
        string city,
        string query,
        CancellationTokenSource request)
    {
        var cancellationToken = request.Token;

        try
        {
            await _delay(CatalogDebounce, cancellationToken).ConfigureAwait(true);

            await LoadCatalogAsync(city, query, silent: false, request);
        }
        catch (OperationCanceledException)
        {
            // Новый запрос отменил паузу: ответ устарел, следующий обновит список.
        }
    }

    /// <summary>
    /// Читает каталог и раскладывает ответ по строкам.
    /// </summary>
    /// <param name="city">Город запроса.</param>
    /// <param name="query">Строка запроса.</param>
    /// <param name="silent">Попытка цикла: не показывать индикатор и не стирать причину.</param>
    /// <param name="request">Источник отмены этого запроса.</param>
    /// <returns>Вид сбоя: по нему решает цикл повтора.</returns>
    private async Task<ApiFailureKind> LoadCatalogAsync(
        string city,
        string query,
        bool silent,
        CancellationTokenSource request)
    {
        var cancellationToken = request.Token;
        var kind = ApiFailureKind.None;

        if (!silent)
        {
            IsCatalogLoading = true;
            CatalogError = null;
        }

        try
        {
            var results = await _searchApi.SearchSalonsAsync(city, query, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return ApiFailureKind.None;
            }

            var selectedId = SelectedCatalogSalon?.Id;

            Catalog.Clear();

            foreach (var salon in results)
            {
                Catalog.Add(new BarberSalonCatalogItem(salon));
            }

            _catalogLoaded = true;
            CatalogError = null;
            _catalogFailureKind = ApiFailureKind.None;
            _catalogFailedCity = null;
            _catalogFailedQuery = null;
            SelectedCatalogSalon = selectedId is { } id
                ? Catalog.FirstOrDefault(salon => salon.Id == id)
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Отмена новым запросом — это не ошибка каталога.
            return ApiFailureKind.None;
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ApiFailureKind.None;
            }

            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                _catalogFailureKind = kind;
                _catalogFailedCity = city;
                _catalogFailedQuery = query;
                CatalogError = BarberProfileText.ReadError(
                    exception,
                    kind,
                    CatalogFailedText);
            }
        }
        finally
        {
            if (ReferenceEquals(_catalogRequest, request))
            {
                _catalogRequest = null;
                IsCatalogLoading = false;
                request.Dispose();
            }

            NotifyCatalogState();
        }

        if (kind.IsRetryable())
        {
            TryStartCatalogAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Запускает цикл повтора списка, если раздел виден и сбой повторяемый.
    /// </summary>
    private void TryStartSalonsAutoRetry()
    {
        if (!_isActive
            || _profileId == Guid.Empty
            || !_salonsFailureKind.IsRetryable())
        {
            return;
        }

        _salonsAutoRetry.Start(RetrySalonsAsync);
    }

    /// <summary>
    /// Запускает цикл повтора каталога, если раздел виден и повторяет ровно тот же запрос.
    /// </summary>
    private void TryStartCatalogAutoRetry()
    {
        if (!_isActive
            || _catalogFailedQuery is null
            || !_catalogFailureKind.IsRetryable()
            || !string.Equals(CatalogQuery.Trim(), _catalogFailedQuery, StringComparison.Ordinal)
            || !string.Equals(
                CatalogCity.Trim(),
                _catalogFailedCity ?? string.Empty,
                StringComparison.Ordinal))
        {
            return;
        }

        _catalogAutoRetry.Start(RetryCatalogAsync);
    }

    /// <summary>
    /// Одна попытка цикла: перечитывает список, не сбрасывая показанное и причину.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetrySalonsAsync(CancellationToken cancellationToken) =>
        LoadSalonsAsync(silent: true, cancellationToken);

    /// <summary>
    /// Одна попытка цикла: повторяет ровно тот запрос каталога, который упал.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetryCatalogAsync(CancellationToken cancellationToken)
    {
        if (!_isActive || _catalogFailedQuery is not { } query)
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        var city = _catalogFailedCity ?? string.Empty;

        if (!string.Equals(CatalogQuery.Trim(), query, StringComparison.Ordinal)
            || !string.Equals(CatalogCity.Trim(), city, StringComparison.Ordinal))
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = _catalogRequest;

        _catalogRequest = request;

        previous?.Cancel();
        previous?.Dispose();

        return LoadCatalogAsync(city, query, silent: true, request);
    }

    /// <summary>
    /// Отменяет незавершённый поиск каталога: паузу или запрос.
    /// </summary>
    private void CancelCatalogRequest()
    {
        var previous = _catalogRequest;

        _catalogRequest = null;

        previous?.Cancel();
        previous?.Dispose();

        IsCatalogLoading = false;
    }

    /// <summary>
    /// Проверяет, что выбор салона устарел: его уже привязали или он исчез.
    /// </summary>
    /// <param name="exception">Отказ привязки.</param>
    /// <returns><c>true</c>, если поможет перечитывание списка.</returns>
    private static bool IsStaleAttach(BarberSearchApiException exception) =>
        exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict
        || HasCode(exception, DuplicateSalonCode)
        || HasCode(exception, MissingSalonCode);

    /// <summary>
    /// Проверяет наличие публичного кода ошибки.
    /// </summary>
    /// <param name="exception">Отказ сервера.</param>
    /// <param name="code">Публичный код.</param>
    /// <returns><c>true</c>, если код есть в ответе.</returns>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Начинает новое чтение, отменяя предыдущее.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены экрана.</param>
    /// <returns>Источник с связанным токеном.</returns>
    private CancellationTokenSource StartRequest(CancellationToken cancellationToken)
    {
        _loadCts?.Cancel();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _loadCts = cts;

        return cts;
    }

    /// <summary>
    /// Переключает строку «Пробуем снова…» списка вслед за циклом.
    /// </summary>
    private void OnSalonsAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _salonsAutoRetry.IsRunning;

    /// <summary>
    /// Переключает строку «Пробуем снова…» каталога вслед за циклом.
    /// </summary>
    private void OnCatalogAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsCatalogAutoRetrying = _catalogAutoRetry.IsRunning;

    /// <summary>
    /// Поднимает уведомления обо всём, что зависит от списка салонов.
    /// </summary>
    private void NotifySalonState()
    {
        OnPropertyChanged(nameof(HasLoaded));
        OnPropertyChanged(nameof(HasSalons));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        OnPropertyChanged(nameof(FirstSalonBecomesPrimary));
        OnPropertyChanged(nameof(PrimaryToggleText));
        OnPropertyChanged(nameof(CanAddSalon));

        AddSalonCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Поднимает уведомления обо всём, что зависит от поиска и выбора каталога.
    /// </summary>
    private void NotifyCatalogState()
    {
        OnPropertyChanged(nameof(CanSearchCatalog));
        OnPropertyChanged(nameof(CatalogHint));
        OnPropertyChanged(nameof(ShowCatalogResults));
        OnPropertyChanged(nameof(ShowCatalogEmpty));
        OnPropertyChanged(nameof(ShowCatalogError));
        OnPropertyChanged(nameof(CanAddSalon));
        OnPropertyChanged(nameof(AddSalonButtonText));

        AddSalonCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Поле города изменилось: каталог ищется заново с паузой.
    /// </summary>
    partial void OnCatalogCityChanged(string value) => BeginCatalogSearch(debounce: true);

    /// <summary>
    /// Строка поиска изменилась: каталог ищется заново с паузой.
    /// </summary>
    partial void OnCatalogQueryChanged(string value) => BeginCatalogSearch(debounce: true);

    /// <summary>
    /// Загрузка каталога началась или закончилась.
    /// </summary>
    partial void OnIsCatalogLoadingChanged(bool value) => NotifyCatalogState();

    /// <summary>
    /// Ошибка каталога появилась или исчезла.
    /// </summary>
    partial void OnCatalogErrorChanged(string? value) => NotifyCatalogState();

    /// <summary>
    /// Выбранная строка каталога изменилась: пересчитывается доступность привязки.
    /// </summary>
    partial void OnSelectedCatalogSalonChanged(BarberSalonCatalogItem? value) =>
        NotifyCatalogState();

    /// <summary>
    /// Привязка началась или закончилась: кнопка меняет подпись.
    /// </summary>
    partial void OnIsAddingSalonChanged(bool value) => NotifyCatalogState();

    /// <summary>
    /// Ошибка привязки появилась или исчезла.
    /// </summary>
    partial void OnAddSalonErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasAddSalonError));

    /// <summary>
    /// Первое чтение закончилось: переключаются «список», «пусто» и «ошибка».
    /// </summary>
    partial void OnIsFirstLoadingChanged(bool value) => NotifySalonState();

    /// <summary>
    /// Причина чтения изменилась: переключаются «список» и «ошибка».
    /// </summary>
    partial void OnErrorMessageChanged(string? value) => NotifySalonState();

    /// <summary>
    /// Баннер появился или исчез.
    /// </summary>
    partial void OnAnnouncementChanged(string? value) =>
        OnPropertyChanged(nameof(HasAnnouncement));

    /// <summary>
    /// Ошибка смены основного появилась или исчезла.
    /// </summary>
    partial void OnActionErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasActionError));
}
