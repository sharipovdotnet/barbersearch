using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Admin;

/// <summary>
/// Очередь мастеров: профили, ждущие подтверждения, и два соседних фильтра.
/// </summary>
/// <remarks>
/// <para>
/// Список читается страницами по курсору: первая страница — при каждом появлении экрана,
/// следующая — прокруткой. Возврат из карточки после решения и есть обновление очереди:
/// решённая строка исчезает из «На проверке» без отдельного события. Число на бейдже хаба
/// считает сервер теми же предикатами, поэтому очередь и бейдж не расходятся.
/// </para>
/// <para>
/// Фильтры взяты у веба (<c>ADMIN_BARBER_STATUS_LABELS</c>) и поддержаны API: «На проверке»,
/// «Все», «Скрытые». По умолчанию открыта очередь проверки — та, которую считает хаб.
/// </para>
/// <para>
/// Состояния: первая загрузка, сбой с повтором, отказ в доступе и конец сессии. Ошибка
/// следующей страницы не стирает уже показанные строки: причина и «Повторить» живут в футере.
/// Пока страница видна, повторяемый сбой чтения лечится сам; записи не повторяются никогда.
/// </para>
/// </remarks>
public sealed partial class AdminMastersViewModel : ObservableObject
{
    /// <summary>
    /// Размер страницы — тот же, что у веба.
    /// </summary>
    public const int PageSize = AdminMastersApi.DefaultPageSize;

    private static string LoadErrorText => Loc.T("Не удалось загрузить мастеров.");
    private static string LoadMoreErrorText => Loc.T("Не удалось загрузить ещё мастеров.");

    private readonly IAdminMastersApi _mastersApi;
    private readonly BarberSearchApiOptions _apiOptions;
    private readonly TokenSession _session;
    private readonly TimeProvider _timeProvider;
    private readonly AutoRetryLoop _autoRetry;
    private readonly AutoRetryLoop _loadMoreAutoRetry;

    private string? _nextCursor;
    private int _generation;

    /// <summary>
    /// Вид последнего сбоя первой страницы: по нему решает цикл автоповтора.
    /// </summary>
    /// <remarks>
    /// Запоминается для каждого неуспеха, а не только для повторяемого: иначе «транспорт,
    /// потом <c>403</c>» оставил бы в памяти старый <see cref="ApiFailureKind.Transport"/>,
    /// и кнопка снова запустила бы цикл на отказе, который не изменится.
    /// </remarks>
    private ApiFailureKind _firstPageFailureKind;

    /// <summary>
    /// Вид последнего сбоя догрузки: у второго чтения свой снимок и свой цикл.
    /// </summary>
    private ApiFailureKind _loadMoreFailureKind;

    /// <summary>
    /// Курсор упавшей догрузки: повтор несёт его, а не текущий курсор списка.
    /// </summary>
    private string? _loadMoreRetryCursor;

    /// <summary>
    /// Страница сейчас видна: циклы автоповтора работают только пока это так.
    /// </summary>
    private bool _isPageVisible;

    /// <summary>
    /// Создаёт очередь мастеров.
    /// </summary>
    /// <param name="mastersApi">Список и карточка мастеров.</param>
    /// <param name="apiOptions">Адрес API: от него зависят абсолютные адреса аватаров.</param>
    /// <param name="session">Сессия: конец сессии при <c>401</c>.</param>
    /// <param name="timeProvider">Часы: время последнего чтения показывает экран.</param>
    /// <param name="autoRetry">
    /// Цикл автоповтора первой страницы; <c>null</c> — собственный цикл с обычным расписанием.
    /// </param>
    /// <param name="loadMoreAutoRetry">
    /// Цикл автоповтора догрузки, отдельный от первой страницы; <c>null</c> — обычное расписание.
    /// </param>
    public AdminMastersViewModel(
        IAdminMastersApi mastersApi,
        BarberSearchApiOptions apiOptions,
        TokenSession session,
        TimeProvider timeProvider,
        AutoRetryLoop? autoRetry = null,
        AutoRetryLoop? loadMoreAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(mastersApi);
        ArgumentNullException.ThrowIfNull(apiOptions);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _mastersApi = mastersApi;
        _apiOptions = apiOptions;
        _session = session;
        _timeProvider = timeProvider;

        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;

        _loadMoreAutoRetry = loadMoreAutoRetry ?? new AutoRetryLoop();
        _loadMoreAutoRetry.StateChanged += OnLoadMoreAutoRetryStateChanged;
    }

    /// <summary>
    /// Строки очереди в порядке ответа сервера.
    /// </summary>
    public ObservableCollection<AdminMasterItem> Items { get; } = [];

    /// <summary>
    /// Выбранный фильтр: с него начинается каждая выборка.
    /// </summary>
    [ObservableProperty]
    private AdminMasterFilter _selectedFilter = AdminMasterFilter.Pending;

    /// <summary>
    /// Идёт первая загрузка: индикатор вместо списка.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstLoading;

    /// <summary>
    /// Свайп вниз: спиннер обновления, строки остаются на месте.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Идёт догрузка следующей страницы.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingMore;

    /// <summary>
    /// Цикл сам повторяет первую страницу: под причиной видна строка «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Цикл сам повторяет догрузку: строка «Пробуем снова…» живёт в футере.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadMoreAutoRetrying;

    /// <summary>
    /// Ошибка первой страницы; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Ошибка догрузки: уже загруженные строки остаются.
    /// </summary>
    [ObservableProperty]
    private string? _loadMoreError;

    /// <summary>
    /// Сервер ответил <c>403</c>: роль не администратор, данные не показываются.
    /// </summary>
    [ObservableProperty]
    private bool _isForbidden;

    /// <summary>
    /// Сервер ответил <c>401</c>: пары токенов больше нет, нужен вход.
    /// </summary>
    [ObservableProperty]
    private bool _isSignInRequired;

    /// <summary>
    /// Первая страница уже загружалась: пустое состояние не показывается до ответа.
    /// </summary>
    [ObservableProperty]
    private bool _hasLoaded;

    /// <summary>
    /// Время последнего чтения: «Обновлено в 14:32».
    /// </summary>
    [ObservableProperty]
    private string? _updatedAtText;

    /// <summary>
    /// Выбран фильтр «На проверке».
    /// </summary>
    public bool IsPendingSelected => SelectedFilter == AdminMasterFilter.Pending;

    /// <summary>
    /// Выбран фильтр «Все».
    /// </summary>
    public bool IsAllSelected => SelectedFilter == AdminMasterFilter.All;

    /// <summary>
    /// Выбран фильтр «Скрытые».
    /// </summary>
    public bool IsHiddenSelected => SelectedFilter == AdminMasterFilter.Hidden;

    /// <summary>
    /// Заголовок пустого состояния: у каждого фильтра свой.
    /// </summary>
    public string EmptyStateTitle => SelectedFilter switch
    {
        AdminMasterFilter.Hidden => Loc.T("Скрытых профилей нет"),
        AdminMasterFilter.All => Loc.T("Мастеров пока нет"),
        _ => Loc.T("Очередь проверки пуста"),
    };

    /// <summary>
    /// Объяснение пустого состояния: слова веба, кроме «барбер» → «мастер».
    /// </summary>
    public string EmptyStateDescription => SelectedFilter switch
    {
        AdminMasterFilter.Hidden => Loc.T(
            "Никто не убран из поиска: все профили либо подтверждены, либо ждут проверки."),
        AdminMasterFilter.All => Loc.T(
            "Профили появятся здесь, как только мастера их создадут."),
        _ => Loc.T(
            "Все профили подтверждены. Новые появятся здесь, когда мастера зарегистрируются."),
    };

    /// <summary>
    /// Список или пустое состояние видны.
    /// </summary>
    public bool ShowContent =>
        !IsFirstLoading && !ShowError && !ShowForbidden && !ShowSignInRequired;

    /// <summary>
    /// Сбой чтения виден: причина и «Повторить».
    /// </summary>
    public bool ShowError => ErrorMessage is not null && !ShowForbidden && !IsSignInRequired;

    /// <summary>
    /// Отказ в доступе виден.
    /// </summary>
    public bool ShowForbidden => IsForbidden && !IsSignInRequired;

    /// <summary>
    /// Просьба войти снова видна.
    /// </summary>
    public bool ShowSignInRequired => IsSignInRequired;

    /// <summary>
    /// Пустое состояние видно: чтение закончилось, строк нет и ошибки нет.
    /// </summary>
    public bool ShowEmptyState =>
        ShowContent && HasLoaded && Items.Count == 0 && ErrorMessage is null;

    /// <summary>
    /// Список виден: содержимое есть и пустого состояния нет.
    /// </summary>
    public bool ShowList => ShowContent && !ShowEmptyState;

    /// <summary>
    /// Есть следующая страница.
    /// </summary>
    public bool CanLoadMore => _nextCursor is not null;

    /// <summary>
    /// Ошибка догрузки видна в футере.
    /// </summary>
    public bool ShowLoadMoreError => LoadMoreError is not null;

    /// <summary>
    /// Время последнего чтения известно.
    /// </summary>
    public bool HasUpdatedAt => UpdatedAtText is not null;

    /// <summary>
    /// Цикл автоповтора первой страницы: тестам нужен счётчик попыток.
    /// </summary>
    internal AutoRetryLoop AutoRetry => _autoRetry;

    /// <summary>
    /// Завершается, когда цикл первой страницы останавливается.
    /// </summary>
    internal Task AutoRetryCompletion => _autoRetry.Completion;

    /// <summary>
    /// Цикл автоповтора догрузки: тестам нужен счётчик попыток.
    /// </summary>
    internal AutoRetryLoop LoadMoreAutoRetry => _loadMoreAutoRetry;

    /// <summary>
    /// Завершается, когда цикл догрузки останавливается.
    /// </summary>
    internal Task LoadMoreAutoRetryCompletion => _loadMoreAutoRetry.Completion;

    /// <summary>
    /// Читает первую страницу: первый раз с индикатором, при возврате на экран тихо.
    /// </summary>
    /// <remarks>
    /// Возврат из карточки после подтверждения — это и есть обновление: решённая строка
    /// исчезает из очереди без отдельного события «очередь изменилась».
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        LoadFirstPageAsync(
            HasLoaded ? FirstPageMode.Silent : FirstPageMode.Initial,
            cancellationToken);

    /// <summary>
    /// Включает автоповтор: страница видна.
    /// </summary>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartAutoRetry();
        TryStartLoadMoreAutoRetry();
    }

    /// <summary>
    /// Выключает автоповтор: страница скрыта или открыта карточка.
    /// </summary>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        StopAutoRetryLoop();
        StopLoadMoreAutoRetryLoop();
    }

    /// <summary>
    /// Гасит экран после завершения сессии: строки прошлого пользователя не остаются.
    /// </summary>
    public void ApplySignedOutState()
    {
        StopAutoRetryLoop();
        StopLoadMoreAutoRetryLoop();

        _generation++;
        _nextCursor = null;
        _firstPageFailureKind = ApiFailureKind.None;
        _loadMoreFailureKind = ApiFailureKind.None;
        _loadMoreRetryCursor = null;

        Items.Clear();

        IsFirstLoading = false;
        IsRefreshing = false;
        IsLoadingMore = false;
        HasLoaded = false;
        ErrorMessage = null;
        LoadMoreError = null;
        UpdatedAtText = null;
        IsForbidden = false;
        IsSignInRequired = true;

        NotifyViewState();
    }

    /// <summary>
    /// Перечитывает первую страницу свайпом, оставляя строки на месте.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!ShowContent || IsFirstLoading)
        {
            IsRefreshing = false;

            return;
        }

        IsRefreshing = true;

        try
        {
            await LoadFirstPageAsync(FirstPageMode.Silent, CancellationToken.None);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// «Повторить» после сбоя: остановить фон и прочитать снова с индикатором.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RetryAsync()
    {
        StopAutoRetryLoop();

        ErrorMessage = null;
        NotifyViewState();

        await LoadFirstPageAsync(FirstPageMode.Initial, CancellationToken.None);

        TryStartAutoRetry();
    }

    /// <summary>
    /// Догружает следующую страницу.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        var cursor = _nextCursor;

        if (IsFirstLoading || IsLoadingMore || cursor is null)
        {
            return;
        }

        await LoadMorePageAsync(cursor, LoadMoreMode.Normal, CancellationToken.None);
    }

    /// <summary>
    /// Повторяет упавшую догрузку, не трогая уже загруженные строки.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RetryLoadMoreAsync()
    {
        StopLoadMoreAutoRetryLoop();

        var cursor = _nextCursor;

        if (IsFirstLoading || cursor is null)
        {
            return;
        }

        await LoadMorePageAsync(cursor, LoadMoreMode.Normal, CancellationToken.None);
    }

    /// <summary>
    /// Переключает фильтр на очередь проверки.
    /// </summary>
    /// <returns>Задача чтения новой выборки.</returns>
    [RelayCommand]
    private Task ShowPendingAsync() => SelectFilterAsync(AdminMasterFilter.Pending);

    /// <summary>
    /// Переключает фильтр на все профили.
    /// </summary>
    /// <returns>Задача чтения новой выборки.</returns>
    [RelayCommand]
    private Task ShowAllAsync() => SelectFilterAsync(AdminMasterFilter.All);

    /// <summary>
    /// Переключает фильтр на скрытые профили.
    /// </summary>
    /// <returns>Задача чтения новой выборки.</returns>
    [RelayCommand]
    private Task ShowHiddenAsync() => SelectFilterAsync(AdminMasterFilter.Hidden);

    /// <summary>
    /// Меняет фильтр и начинает выборку с первой страницы.
    /// </summary>
    /// <remarks>
    /// Строки прошлой выборки снимаются сразу: иначе под новым фильтром остались бы чужие
    /// профили, пока идёт ответ. Курсор тоже сбрасывается — он принадлежит прошлому запросу.
    /// </remarks>
    /// <param name="filter">Новый фильтр.</param>
    /// <returns>Задача чтения.</returns>
    private async Task SelectFilterAsync(AdminMasterFilter filter)
    {
        if (SelectedFilter == filter)
        {
            return;
        }

        SelectedFilter = filter;

        StopAutoRetryLoop();
        StopLoadMoreAutoRetryLoop();

        _generation++;
        _nextCursor = null;
        _firstPageFailureKind = ApiFailureKind.None;
        _loadMoreFailureKind = ApiFailureKind.None;
        _loadMoreRetryCursor = null;

        Items.Clear();

        ErrorMessage = null;
        LoadMoreError = null;
        UpdatedAtText = null;
        IsForbidden = false;
        IsSignInRequired = false;

        NotifyViewState();

        await LoadFirstPageAsync(FirstPageMode.Initial, CancellationToken.None);
    }

    /// <summary>
    /// Читает первую страницу выбранного фильтра и заменяет строки.
    /// </summary>
    /// <param name="mode">Показать индикатор, обновить тихо или повторить в фоне.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему решает цикл автоповтора.</returns>
    private async Task<ApiFailureKind> LoadFirstPageAsync(
        FirstPageMode mode,
        CancellationToken cancellationToken)
    {
        _generation++;

        var generation = _generation;
        var kind = ApiFailureKind.None;

        if (mode != FirstPageMode.Retry)
        {
            ErrorMessage = null;
            LoadMoreError = null;
            _firstPageFailureKind = ApiFailureKind.None;
        }

        if (mode == FirstPageMode.Initial)
        {
            IsFirstLoading = true;
        }

        NotifyViewState();

        try
        {
            var page = await _mastersApi.GetMastersAsync(
                SelectedFilter,
                cursor: null,
                PageSize,
                cancellationToken);

            // Поздний ответ прошлой выборки не применяется: иначе он вернул бы строки
            // фильтра, который человек уже сменил.
            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            Items.Clear();

            foreach (var master in page.Items)
            {
                Items.Add(new AdminMasterItem(master, _apiOptions));
            }

            _nextCursor = page.NextCursor;
            _firstPageFailureKind = ApiFailureKind.None;
            _loadMoreFailureKind = ApiFailureKind.None;
            _loadMoreRetryCursor = null;
            ErrorMessage = null;
            LoadMoreError = null;
            IsForbidden = false;
            IsSignInRequired = false;
            HasLoaded = true;
            UpdatedAtText = AdminText.UpdatedAt(_timeProvider.GetUtcNow());

            NotifyViewState();
        }
        catch (Exception exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                if (exception is BarberSearchApiException
                    {
                        StatusCode: HttpStatusCode.Unauthorized,
                    })
                {
                    await EndSessionAsync();
                }
                else if (exception is BarberSearchApiException
                {
                    StatusCode: HttpStatusCode.Forbidden,
                })
                {
                    ApplyForbiddenState();
                }
                else
                {
                    SetPageFailure(generation, AdminErrorText.Read(exception, kind, LoadErrorText), kind);
                }
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsFirstLoading = false;

                NotifyViewState();
                TryStartAutoRetry();
            }
        }

        return kind;
    }

    /// <summary>
    /// Читает следующую страницу обычной догрузкой или тихой попыткой цикла.
    /// </summary>
    /// <param name="cursor">Курсор упавшей страницы: повтор несёт именно его.</param>
    /// <param name="mode">Обычная догрузка или тихая попытка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему решает второй цикл.</returns>
    private async Task<ApiFailureKind> LoadMorePageAsync(
        string cursor,
        LoadMoreMode mode,
        CancellationToken cancellationToken)
    {
        IsLoadingMore = true;

        if (mode == LoadMoreMode.Normal)
        {
            LoadMoreError = null;
            _loadMoreFailureKind = ApiFailureKind.None;
        }

        NotifyViewState();

        var generation = _generation;
        var kind = ApiFailureKind.None;

        try
        {
            var page = await _mastersApi.GetMastersAsync(
                SelectedFilter,
                cursor,
                PageSize,
                cancellationToken);

            if (generation != _generation || _nextCursor != cursor)
            {
                // Пока читали, список перечитали первой страницей или сменили фильтр:
                // запоздавшая страница не имеет права попасть в чужую выборку.
                return ApiFailureKind.None;
            }

            AppendItems(page.Items);

            _nextCursor = page.NextCursor;
            LoadMoreError = null;
            _loadMoreFailureKind = ApiFailureKind.None;
            _loadMoreRetryCursor = null;

            NotifyViewState();
        }
        catch (Exception exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                if (exception is BarberSearchApiException
                    {
                        StatusCode: HttpStatusCode.Unauthorized,
                    })
                {
                    await EndSessionAsync();
                }
                else if (exception is BarberSearchApiException
                {
                    StatusCode: HttpStatusCode.Forbidden,
                })
                {
                    ApplyForbiddenState();
                }
                else if (generation == _generation && _nextCursor == cursor)
                {
                    SetLoadMoreFailure(cursor, AdminErrorText.Read(exception, kind, LoadMoreErrorText), kind);
                }
            }
        }
        finally
        {
            IsLoadingMore = false;
            NotifyViewState();
        }

        return kind;
    }

    /// <summary>
    /// Показывает ошибку первой страницы, не стирая уже загруженные строки.
    /// </summary>
    /// <param name="generation">Поколение ответов, к которому относится сбой.</param>
    /// <param name="message">Текст для человека.</param>
    /// <param name="kind">Вид сбоя: по нему решает автоповтор.</param>
    private void SetPageFailure(int generation, string message, ApiFailureKind kind)
    {
        if (generation != _generation)
        {
            return;
        }

        IsFirstLoading = false;
        _firstPageFailureKind = kind;

        if (Items.Count == 0)
        {
            ErrorMessage = message;
        }

        NotifyViewState();

        TryStartAutoRetry();
    }

    /// <summary>
    /// Показывает ошибку догрузки и запоминает снимок упавшего запроса.
    /// </summary>
    /// <param name="cursor">Курсор упавшей страницы.</param>
    /// <param name="message">Текст для человека.</param>
    /// <param name="kind">Вид сбоя: по нему решает второй цикл.</param>
    private void SetLoadMoreFailure(string cursor, string message, ApiFailureKind kind)
    {
        IsLoadingMore = false;
        LoadMoreError = message;
        _loadMoreFailureKind = kind;
        _loadMoreRetryCursor = cursor;

        NotifyViewState();

        TryStartLoadMoreAutoRetry();
    }

    /// <summary>
    /// Включает отказ в доступе: роль не появится от повтора, данные скрываются.
    /// </summary>
    private void ApplyForbiddenState()
    {
        StopAutoRetryLoop();
        StopLoadMoreAutoRetryLoop();

        _generation++;
        _nextCursor = null;
        _firstPageFailureKind = ApiFailureKind.None;
        _loadMoreFailureKind = ApiFailureKind.None;
        _loadMoreRetryCursor = null;

        Items.Clear();

        ErrorMessage = null;
        LoadMoreError = null;
        UpdatedAtText = null;
        IsForbidden = true;
        IsSignInRequired = false;

        NotifyViewState();
    }

    /// <summary>
    /// Одна попытка цикла: перечитать первую страницу, не гася причину до ответа.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private Task<ApiFailureKind> RetryFirstPageAsync(CancellationToken cancellationToken)
    {
        if (ErrorMessage is null || !_firstPageFailureKind.IsRetryable())
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadFirstPageAsync(FirstPageMode.Retry, cancellationToken);
    }

    /// <summary>
    /// Одна попытка цикла: повторить упавшую догрузку тем же курсором.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private Task<ApiFailureKind> RetryLoadMoreAttemptAsync(CancellationToken cancellationToken)
    {
        if (_loadMoreRetryCursor is not { } cursor
            || LoadMoreError is null
            || !_loadMoreFailureKind.IsRetryable()
            || _nextCursor != cursor
            || IsLoadingMore)
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadMorePageAsync(cursor, LoadMoreMode.Retry, cancellationToken);
    }

    /// <summary>
    /// Запускает цикл первой страницы, если страница видна и сбой повторяемый.
    /// </summary>
    private void TryStartAutoRetry()
    {
        if (!_isPageVisible || IsFirstLoading || IsSignInRequired || IsForbidden)
        {
            return;
        }

        if (ErrorMessage is null || !_firstPageFailureKind.IsRetryable())
        {
            return;
        }

        _autoRetry.Start(RetryFirstPageAsync);
    }

    /// <summary>
    /// Запускает цикл догрузки, если страница видна и есть снимок упавшего курсора.
    /// </summary>
    private void TryStartLoadMoreAutoRetry()
    {
        if (!_isPageVisible || IsSignInRequired || IsForbidden || IsFirstLoading)
        {
            return;
        }

        if (_loadMoreRetryCursor is null
            || LoadMoreError is null
            || !_loadMoreFailureKind.IsRetryable())
        {
            return;
        }

        _loadMoreAutoRetry.Start(RetryLoadMoreAttemptAsync);
    }

    /// <summary>
    /// Останавливает цикл первой страницы, не трогая признак «страница видна».
    /// </summary>
    private void StopAutoRetryLoop() => _autoRetry.Stop();

    /// <summary>
    /// Останавливает цикл догрузки, не трогая признак «страница видна».
    /// </summary>
    private void StopLoadMoreAutoRetryLoop() => _loadMoreAutoRetry.Stop();

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла первой страницы.
    /// </summary>
    /// <param name="sender">Цикл.</param>
    /// <param name="e">Признак изменения.</param>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Переключает строку «Пробуем снова…» в футере вслед за состоянием цикла догрузки.
    /// </summary>
    /// <param name="sender">Цикл.</param>
    /// <param name="e">Признак изменения.</param>
    private void OnLoadMoreAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsLoadMoreAutoRetrying = _loadMoreAutoRetry.IsRunning;

    /// <summary>
    /// Дополняет список следующей страницей без дублей.
    /// </summary>
    /// <remarks>
    /// Повтор упавшей догрузки идёт тем же курсором, и сервер может вернуть уже показанную
    /// строку: без проверки повтор добавил бы дубль и сломал порядок.
    /// </remarks>
    /// <param name="masters">Строки следующей страницы.</param>
    private void AppendItems(IReadOnlyList<AdminBarberResponse> masters)
    {
        foreach (var master in masters)
        {
            if (Items.Any(item => item.Id == master.Id))
            {
                continue;
            }

            Items.Add(new AdminMasterItem(master, _apiOptions));
        }

        NotifyViewState();
    }

    /// <summary>
    /// Гасит сессию: пара мертва, экран вернёт к форме входа.
    /// </summary>
    /// <returns>Задача очистки сессии.</returns>
    private async Task EndSessionAsync()
    {
        ApplySignedOutState();

        await _session.ClearAsync(CancellationToken.None);
    }

    /// <summary>
    /// Поднимает уведомления обо всех вычисляемых свойствах экрана.
    /// </summary>
    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(IsPendingSelected));
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(IsHiddenSelected));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowForbidden));
        OnPropertyChanged(nameof(ShowSignInRequired));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(ShowLoadMoreError));
        OnPropertyChanged(nameof(HasUpdatedAt));
    }

    /// <summary>
    /// Режим чтения первой страницы: показать индикатор, обновить тихо или повторить в фоне.
    /// </summary>
    private enum FirstPageMode
    {
        /// <summary>
        /// Первое чтение: индикатор вместо содержимого.
        /// </summary>
        Initial,

        /// <summary>
        /// Свайп и возврат на страницу: строки остаются, причина сбоя стирается.
        /// </summary>
        Silent,

        /// <summary>
        /// Попытка цикла: причина остаётся на экране, пока не придёт ответ.
        /// </summary>
        Retry,
    }

    /// <summary>
    /// Режим догрузки: обычная прокрутка или тихая попытка цикла.
    /// </summary>
    private enum LoadMoreMode
    {
        /// <summary>
        /// Обычная догрузка: старая ошибка стирается, потому что её сменил жест.
        /// </summary>
        Normal,

        /// <summary>
        /// Тихая попытка: причина и строки остаются, ответ решает исход.
        /// </summary>
        Retry,
    }
}
