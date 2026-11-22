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
/// Экран «Добавить салон»: поиск по публичному каталогу и привязка к профилю мастера.
/// </summary>
/// <remarks>
/// <para>
/// Это отдельная задача раздела «Салоны»: список привязанных мест остаётся в разделе, а
/// поиск и выбор нового места живут страницей поверх кабинета. Так раздел не несёт двух
/// работ сразу, а у формы свой возврат: системный жест и есть отмена.
/// </para>
/// <para>
/// Профиль и признак «в профиле уже есть салон» приходят из раздела в
/// <see cref="Initialize"/>: первый салон становится основным сам, поэтому форма должна
/// знать это до привязки, а не читать список заново.
/// </para>
/// <para>
/// Каталог ищется с паузой 300 мс и только с двух символов — как в вебе. Новый запрос
/// отменяет предыдущий: медленный ответ на «Мо» не должен перетереть быстрый на «Моск».
/// Чтение каталога повторяется само, но повторяет ровно упавший запрос: правка поля
/// останавливает цикл, чтобы старые результаты не появились поверх новой строки.
/// </para>
/// <para>
/// Привязка не дублируется: <c>409</c>/<c>404</c> и коды <c>addSalon.duplicate</c> /
/// <c>addSalon.salonNotFound</c> означают, что выбор устарел. Причина показывается, а
/// страница остаётся открытой: скрытый список раздел перечитает сам на Pop, когда
/// страница закроется, — отдельное чтение здесь было бы запросом под невидимым экраном.
/// </para>
/// </remarks>
public sealed partial class BarberSalonAddViewModel : ObservableObject
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

    private static string AddFailedText => Loc.T("Не удалось добавить салон. Попробуйте ещё раз.");
    private static string CatalogFailedText => Loc.T("Не удалось загрузить каталог.");
    private static string AddedText => Loc.T("Салон добавлен.");

    private readonly IBarberProfileApi _profileApi;
    private readonly IBarberSearchApi _searchApi;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly AutoRetryLoop _catalogAutoRetry;

    private Guid _profileId = Guid.Empty;
    private bool _hasSalons;
    private bool _isActive;
    private bool _catalogLoaded;
    private CancellationTokenSource? _catalogRequest;
    private Task _catalogOperation = Task.CompletedTask;
    private ApiFailureKind _catalogFailureKind;
    private string? _catalogFailedCity;
    private string? _catalogFailedQuery;

    /// <summary>
    /// Создаёт форму добавления салона.
    /// </summary>
    /// <param name="profileApi">Привязка салона к профилю.</param>
    /// <param name="searchApi">Публичный каталог салонов.</param>
    /// <param name="catalogAutoRetry">
    /// Цикл повтора каталога; <c>null</c> — собственный цикл.
    /// </param>
    public BarberSalonAddViewModel(
        IBarberProfileApi profileApi,
        IBarberSearchApi searchApi,
        AutoRetryLoop? catalogAutoRetry = null)
        : this(
            profileApi,
            searchApi,
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
            catalogAutoRetry)
    {
    }

    /// <summary>
    /// Создаёт форму с подменяемой паузой — для тестов без ожидания таймера.
    /// </summary>
    /// <param name="profileApi">Привязка салона к профилю.</param>
    /// <param name="searchApi">Публичный каталог салонов.</param>
    /// <param name="delay">Пауза перед запросом каталога.</param>
    /// <param name="catalogAutoRetry">Цикл повтора каталога.</param>
    internal BarberSalonAddViewModel(
        IBarberProfileApi profileApi,
        IBarberSearchApi searchApi,
        Func<TimeSpan, CancellationToken, Task> delay,
        AutoRetryLoop? catalogAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(profileApi);
        ArgumentNullException.ThrowIfNull(searchApi);
        ArgumentNullException.ThrowIfNull(delay);

        _profileApi = profileApi;
        _searchApi = searchApi;
        _delay = delay;

        _catalogAutoRetry = catalogAutoRetry ?? new AutoRetryLoop();
        _catalogAutoRetry.StateChanged += OnCatalogAutoRetryStateChanged;
    }

    /// <summary>
    /// Привязка прошла: страница объявляет результат в разделе и закрывается.
    /// </summary>
    /// <remarks>
    /// Аргумент — «Салон добавлен.». Список раздела здесь не перечитывается: он скрыт
    /// страницей и обновится на Pop, как и после создания салона.
    /// </remarks>
    public event EventHandler<string>? Attached;

    /// <summary>
    /// Результаты последнего поиска каталога в порядке сервера.
    /// </summary>
    public ObservableCollection<BarberSalonCatalogItem> Catalog { get; } = [];

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
    /// Первый салон профиля: он станет основным независимо от выбора.
    /// </summary>
    public bool FirstSalonBecomesPrimary => !_hasSalons;

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
    /// Привязка доступна: профиль известен, выбрана строка каталога и привязки нет.
    /// </summary>
    public bool CanAddSalon => _profileId != Guid.Empty
        && SelectedCatalogSalon is not null
        && !IsAddingSalon;

    /// <summary>
    /// Подпись кнопки привязки.
    /// </summary>
    public string AddSalonButtonText => IsAddingSalon
        ? Loc.T("Добавляем…")
        : Loc.T("Добавить");

    /// <summary>
    /// Есть ошибка привязки салона.
    /// </summary>
    public bool HasAddSalonError => AddSalonError is not null;

    /// <summary>
    /// Цикл повтора каталога: тестам нужен счётчик попыток и завершение.
    /// </summary>
    internal AutoRetryLoop CatalogAutoRetry => _catalogAutoRetry;

    /// <summary>
    /// Завершается, когда цикл каталога останавливается.
    /// </summary>
    internal Task CatalogRetryCompletion => _catalogAutoRetry.Completion;

    /// <summary>
    /// Завершение последнего поиска каталога — тестам, чтобы не ждать таймер.
    /// </summary>
    internal Task CatalogOperation => _catalogOperation;

    /// <summary>
    /// Привязывает форму к профилю мастера и состоянию раздела.
    /// </summary>
    /// <param name="barberProfileId">Профиль мастера из текущей сессии.</param>
    /// <param name="hasSalons">В профиле уже есть хотя бы один привязанный салон.</param>
    public void Initialize(Guid barberProfileId, bool hasSalons)
    {
        _profileId = barberProfileId;
        _hasSalons = hasSalons;

        _catalogAutoRetry.Stop();
        CancelCatalogRequest();

        _catalogLoaded = false;
        _catalogFailureKind = ApiFailureKind.None;
        _catalogFailedCity = null;
        _catalogFailedQuery = null;

        Catalog.Clear();

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

        NotifyCatalogState();
    }

    /// <summary>
    /// Сообщает, что страница видна, и запускает автоповтор упавшего поиска.
    /// </summary>
    /// <remarks>
    /// Если страница вернулась с набранным, но так и не выполненным запросом каталога
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

        TryStartCatalogAutoRetry();
    }

    /// <summary>
    /// Останавливает автоповтор, не сбрасывая показанные данные.
    /// </summary>
    public void StopAutoRetry()
    {
        _isActive = false;

        _catalogAutoRetry.Stop();
        CancelCatalogRequest();

        IsCatalogAutoRetrying = false;
    }

    /// <summary>
    /// Привязывает выбранный салон; первый салон профиля становится основным.
    /// </summary>
    /// <remarks>
    /// Устаревшая привязка (<c>409</c>/<c>404</c> или код дубликата) — не ошибка ввода:
    /// причина показывается, страница остаётся открытой, а выбранная строка остаётся,
    /// чтобы человек мог выбрать другую. Скрытый список раздел перечитает на Pop.
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

            Attached?.Invoke(this, AddedText);
        }
        catch (BarberSearchApiException exception) when (IsStaleAttach(exception))
        {
            AddSalonError = BarberSalonText.WriteError(exception, AddFailedText);
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
    /// Убирает ошибку привязки салона.
    /// </summary>
    [RelayCommand]
    private void DismissAddSalonError() => AddSalonError = null;

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
    /// Запускает цикл повтора каталога, если страница видна и повторяет ровно тот же запрос.
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
    /// <returns><c>true</c>, если страница должна остаться открытой.</returns>
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
    /// Переключает строку «Пробуем снова…» каталога вслед за циклом.
    /// </summary>
    private void OnCatalogAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsCatalogAutoRetrying = _catalogAutoRetry.IsRunning;

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
        OnPropertyChanged(nameof(FirstSalonBecomesPrimary));
        OnPropertyChanged(nameof(PrimaryToggleText));
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
    /// Выбранная строка каталога изменилась: пересчитывается доступность привязки, а
    /// кружок прежней строки гаснет.
    /// </summary>
    /// <remarks>
    /// <c>RadioButton</c> на Android не передаёт своему <c>Content</c> контекст строки,
    /// поэтому кружок и подписи стоят рядом, а выбранность строки живёт здесь: смена
    /// <see cref="SelectedCatalogSalon"/> — единственное место, где она меняется.
    /// </remarks>
    partial void OnSelectedCatalogSalonChanged(
        BarberSalonCatalogItem? oldValue,
        BarberSalonCatalogItem? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }

        NotifyCatalogState();
    }

    /// <summary>
    /// Привязка началась или закончилась: кнопка меняет подпись.
    /// </summary>
    partial void OnIsAddingSalonChanged(bool value) => NotifyCatalogState();

    /// <summary>
    /// Ошибка привязки появилась или исчезла.
    /// </summary>
    partial void OnAddSalonErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasAddSalonError));
}
