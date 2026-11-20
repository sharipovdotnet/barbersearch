using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Admin;

/// <summary>
/// Экран «Заявки»: очередь «Стать мастером» и все заявки одним списком.
/// </summary>
/// <remarks>
/// <para>
/// Повторяет веб (<c>admin/barber-requests-screen.tsx</c>), но без двух колонок: на телефоне
/// список — главная поверхность, а карточка — отдельная страница. Фильтр повторяет значения,
/// которые принимает API (<c>pending</c> и <c>all</c>), и начинается с очереди: именно её
/// считает бейдж хаба.
/// </para>
/// <para>
/// Список читается при каждом появлении страницы: возврат из карточки после решения — это
/// и есть обновление, отдельного события «очередь изменилась» телефону не нужно. Курсор
/// непрозрачен, счётчиков нет — сервер отдаёт только страницу, поэтому «всего» экран
/// не показывает.
/// </para>
/// <para>
/// Ошибки чтения, пока страница видна, повторяются сами: у первой страницы и у догрузки свои
/// циклы, потому что это два разных чтения с разными снимками. Снимок догрузки — её курсор
/// на момент сбоя. Решения по заявке живут в карточке: очередь только читает и не повторяет
/// записи никогда.
/// </para>
/// </remarks>
public sealed partial class AdminRequestsViewModel : ObservableObject
{
    /// <summary>
    /// Размер страницы: совпадает с умолчанием сервера и вебом.
    /// </summary>
    public const int PageSize = 20;

    private static string LoadErrorText => Loc.T("Не удалось загрузить заявки.");
    private static string LoadMoreErrorText => Loc.T("Не удалось загрузить ещё заявки.");

    private readonly IAdminRequestsApi _api;
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
    /// потом 403» оставил бы в памяти старый <see cref="ApiFailureKind.Transport"/>, и
    /// «Повторить» снова запустила бы цикл на отказе, который не изменится.
    /// </remarks>
    private ApiFailureKind _firstPageFailureKind;

    /// <summary>
    /// Вид последнего сбоя догрузки: по нему решает второй цикл.
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
    /// Создаёт экран заявок.
    /// </summary>
    /// <param name="api">Страница заявок и курсор следующей.</param>
    /// <param name="session">Сессия: <c>401</c> гасит пару и возвращает состояние входа.</param>
    /// <param name="timeProvider">Часы: «Обновлено в 14:32» считается от них.</param>
    /// <param name="autoRetry">
    /// Цикл автоповтора первой страницы; <c>null</c> — собственный цикл с обычным расписанием.
    /// </param>
    /// <param name="loadMoreAutoRetry">
    /// Цикл автоповтора догрузки, отдельный от первой страницы; <c>null</c> — собственный
    /// цикл с обычным расписанием.
    /// </param>
    public AdminRequestsViewModel(
        IAdminRequestsApi api,
        TokenSession session,
        TimeProvider? timeProvider = null,
        AutoRetryLoop? autoRetry = null,
        AutoRetryLoop? loadMoreAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(session);

        _api = api;
        _session = session;
        _timeProvider = timeProvider ?? TimeProvider.System;

        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;

        _loadMoreAutoRetry = loadMoreAutoRetry ?? new AutoRetryLoop();
        _loadMoreAutoRetry.StateChanged += OnLoadMoreAutoRetryStateChanged;
    }

    /// <summary>
    /// Строки списка в порядке ответа сервера.
    /// </summary>
    public ObservableCollection<AdminRequestItem> Items { get; } = [];

    /// <summary>
    /// Какой список показан: очередь или все заявки.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQueueFilter))]
    [NotifyPropertyChangedFor(nameof(IsAllFilter))]
    [NotifyPropertyChangedFor(nameof(EmptyStateTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateDescription))]
    private AdminRequestListFilter _filter = AdminRequestListFilter.Pending;

    /// <summary>
    /// Идёт первая загрузка: индикатор вместо списка.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isFirstLoading = true;

    /// <summary>
    /// Идёт обновление свайпом: индикатор держит <c>RefreshView</c>.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Первая страница хотя бы раз читалась: до этого пустой список не показывается.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasLoaded;

    /// <summary>
    /// Ошибка первой страницы; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private string? _errorMessage;

    /// <summary>
    /// Цикл сам повторяет первую страницу: под причиной ошибки видна строка «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Цикл сам повторяет догрузку: в футере видна строка «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isLoadMoreAutoRetrying;

    /// <summary>
    /// Ошибка догрузки: уже загруженные строки остаются.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadMoreError))]
    private string? _loadMoreError;

    /// <summary>
    /// Идёт догрузка следующей страницы.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingMore;

    /// <summary>
    /// Сервер ответил <c>403</c>: роль не администратор, данные не показываются.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowForbidden))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isForbidden;

    /// <summary>
    /// Сервер ответил <c>401</c>: пары токенов больше нет, нужен вход.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowSignInRequired))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isSignInRequired;

    /// <summary>
    /// Время снимка: «Обновлено в 14:32»; <c>null</c>, пока снимка нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdatedAt))]
    private string? _updatedAtText;

    /// <summary>
    /// Показан список очереди.
    /// </summary>
    public bool IsQueueFilter => Filter == AdminRequestListFilter.Pending;

    /// <summary>
    /// Показаны все заявки.
    /// </summary>
    public bool IsAllFilter => Filter == AdminRequestListFilter.All;

    /// <summary>
    /// Сбой первой страницы виден: причина и «Повторить».
    /// </summary>
    public bool ShowError => ErrorMessage is not null && !IsForbidden && !IsSignInRequired;

    /// <summary>
    /// Отказ в доступе виден.
    /// </summary>
    public bool ShowForbidden => IsForbidden && !IsSignInRequired;

    /// <summary>
    /// Просьба войти снова видна.
    /// </summary>
    public bool ShowSignInRequired => IsSignInRequired;

    /// <summary>
    /// Список или пустое состояние видны: загрузка закончилась без отказа.
    /// </summary>
    public bool ShowContent =>
        !IsFirstLoading && ErrorMessage is null && !IsForbidden && !IsSignInRequired;

    /// <summary>
    /// Список виден: обновление свайпом и прокрутка доступны.
    /// </summary>
    public bool ShowList => ShowContent;

    /// <summary>
    /// Пустое состояние видно: загрузка закончилась, строк нет.
    /// </summary>
    public bool ShowEmptyState => ShowContent && HasLoaded && Items.Count == 0;

    /// <summary>
    /// Ошибка догрузки видна в футере.
    /// </summary>
    public bool ShowLoadMoreError => LoadMoreError is not null;

    /// <summary>
    /// Есть следующая страница.
    /// </summary>
    public bool CanLoadMore => _nextCursor is not null;

    /// <summary>
    /// Есть время снимка.
    /// </summary>
    public bool HasUpdatedAt => UpdatedAtText is not null;

    /// <summary>
    /// Заголовок пустого состояния: у очереди и всех заявок причины разные.
    /// </summary>
    public string EmptyStateTitle => IsQueueFilter
        ? Loc.T("Заявок нет")
        : Loc.T("Заявок с таким статусом нет");

    /// <summary>
    /// Объяснение пустого состояния: очередь и все заявки объясняются по-разному.
    /// </summary>
    public string EmptyStateDescription => IsQueueFilter
        ? Loc.T("Очередь пуста: новые заявки появятся здесь, как только клиенты их подадут.")
        : Loc.T("Попробуйте очередь — она показывает неразобранные заявки.");

    /// <summary>
    /// Завершается, когда цикл автоповтора останавливается: тесты ждут его вместо пауз.
    /// </summary>
    internal Task AutoRetryCompletion => _autoRetry.Completion;

    /// <summary>
    /// Цикл автоповтора первой страницы: тестам нужен счётчик попыток.
    /// </summary>
    internal AutoRetryLoop AutoRetry => _autoRetry;

    /// <summary>
    /// Завершается, когда цикл повтора догрузки останавливается: тесты ждут его вместо пауз.
    /// </summary>
    internal Task LoadMoreAutoRetryCompletion => _loadMoreAutoRetry.Completion;

    /// <summary>
    /// Цикл повтора догрузки: тестам нужен счётчик попыток.
    /// </summary>
    internal AutoRetryLoop LoadMoreAutoRetry => _loadMoreAutoRetry;

    /// <summary>
    /// Читает первую страницу: первый раз с индикатором, при возврате на страницу — тихо.
    /// </summary>
    /// <remarks>
    /// Вызывается при каждом появлении страницы. Возврат из карточки после решения —
    /// это и есть «обновить»: решённая заявка уходит из очереди без отдельного события.
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
    /// Сбрасывает экран после завершения сессии.
    /// </summary>
    /// <remarks>
    /// Поколение сдвигается: ответ, который ещё летит, не вернёт заявки прошлого пользователя.
    /// </remarks>
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
    /// Меняет фильтр и читает его первую страницу.
    /// </summary>
    /// <remarks>
    /// Фильтр — часть выборки, поэтому строки прошлой выборки стираются сразу: держать их
    /// под новым заголовком значило бы показывать чужие заявки. Поколение сдвигается, и
    /// запоздавший ответ прошлого фильтра не попадёт в список.
    /// </remarks>
    /// <param name="filter">Очередь или все заявки.</param>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task SelectFilterAsync(AdminRequestListFilter filter)
    {
        if (Filter == filter)
        {
            return;
        }

        StopAutoRetryLoop();
        StopLoadMoreAutoRetryLoop();

        Filter = filter;

        _generation++;
        _nextCursor = null;
        _firstPageFailureKind = ApiFailureKind.None;
        _loadMoreFailureKind = ApiFailureKind.None;
        _loadMoreRetryCursor = null;

        Items.Clear();
        ErrorMessage = null;
        LoadMoreError = null;
        IsForbidden = false;
        IsSignInRequired = false;
        IsRefreshing = false;
        IsLoadingMore = false;

        NotifyViewState();

        await LoadFirstPageAsync(FirstPageMode.Initial, CancellationToken.None);
    }

    /// <summary>
    /// Обновляет первую страницу свайпом.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsFirstLoading)
        {
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
    /// Догружает следующую страницу.
    /// </summary>
    /// <remarks>
    /// Обычная догрузка: показывает индикатор и стирает старую ошибку, потому что её запустил
    /// человек прокруткой. Само чтение живёт в <see cref="LoadMorePageAsync"/>: тем же путём
    /// ходят тихая попытка цикла и кнопка «Повторить».
    /// </remarks>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        var cursor = _nextCursor;

        if (IsFirstLoading
            || IsLoadingMore
            || IsForbidden
            || IsSignInRequired
            || ErrorMessage is not null
            || cursor is null)
        {
            return;
        }

        await LoadMorePageAsync(cursor, LoadMoreMode.Normal, CancellationToken.None);
    }

    /// <summary>
    /// Повторяет первую загрузку после ошибки — сразу, по кнопке.
    /// </summary>
    /// <remarks>
    /// «Повторить» значит «повторить сейчас»: цикл останавливается, страница читается без
    /// паузы, а цикл возвращается к жизни только при новом повторяемом сбое.
    /// </remarks>
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
    /// Повторяет догрузку после ошибки, не трогая загруженные строки.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RetryLoadMoreAsync()
    {
        StopLoadMoreAutoRetryLoop();

        var cursor = _nextCursor;

        if (!IsAuthenticatedForLoadMore() || IsLoadingMore || ErrorMessage is not null || cursor is null)
        {
            return;
        }

        await LoadMorePageAsync(cursor, LoadMoreMode.Normal, CancellationToken.None);
    }

    /// <summary>
    /// Читает первую страницу и заменяет список.
    /// </summary>
    /// <remarks>
    /// Поколение сдвигается на старте: ответ, пришедший после смены фильтра или завершения
    /// сессии, отбрасывается. Ошибка тихого обновления не стирает уже загруженные строки.
    /// </remarks>
    /// <param name="mode">Показать индикатор, обновить молча или повторить в фоне.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private async Task<ApiFailureKind> LoadFirstPageAsync(
        FirstPageMode mode,
        CancellationToken cancellationToken)
    {
        _generation++;
        var generation = _generation;

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

        HasLoaded = true;

        NotifyViewState();

        var kind = ApiFailureKind.None;

        try
        {
            var page = await _api.GetRequestsAsync(Filter, cursor: null, PageSize, cancellationToken);

            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            Items.Clear();

            var now = _timeProvider.GetUtcNow();

            foreach (var request in page.Items)
            {
                Items.Add(new AdminRequestItem(request, now));
            }

            _nextCursor = page.NextCursor;
            ErrorMessage = null;
            LoadMoreError = null;
            _firstPageFailureKind = ApiFailureKind.None;
            _loadMoreFailureKind = ApiFailureKind.None;
            _loadMoreRetryCursor = null;
            IsForbidden = false;
            IsSignInRequired = false;
            UpdatedAtText = AdminText.UpdatedAt(now);

            NotifyViewState();
        }
        catch (Exception exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Unauthorized })
                {
                    await EndSessionAsync();
                }
                else if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Forbidden })
                {
                    if (generation == _generation)
                    {
                        SetForbidden();
                    }
                }
                else
                {
                    SetPageFailure(
                        generation,
                        AdminErrorText.Read(exception, kind, LoadErrorText),
                        kind);
                }
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsFirstLoading = false;
                NotifyViewState();
            }
        }

        return kind;
    }

    /// <summary>
    /// Одна попытка цикла: перечитать первую страницу, не гася ошибку.
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
    /// Показывает ошибку страницы, но не стирает уже загруженные строки.
    /// </summary>
    /// <param name="generation">Поколение ответов, к которому относится сбой.</param>
    /// <param name="message">Текст для человека.</param>
    /// <param name="kind">Вид сбоя: по нему решает цикл автоповтора.</param>
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
    /// Показывает отказ в доступе: роль не администратор, повторять нечего.
    /// </summary>
    private void SetForbidden()
    {
        Items.Clear();

        _nextCursor = null;
        ErrorMessage = null;
        LoadMoreError = null;
        UpdatedAtText = null;
        IsForbidden = true;
        IsSignInRequired = false;

        NotifyViewState();
    }

    /// <summary>
    /// Запускает цикл первой страницы, если страница видна и сбой повторяемый.
    /// </summary>
    private void TryStartAutoRetry()
    {
        if (!_isPageVisible || IsFirstLoading || IsForbidden || IsSignInRequired)
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
    /// Останавливает цикл первой страницы, не трогая признак «страница видна».
    /// </summary>
    private void StopAutoRetryLoop() => _autoRetry.Stop();

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    /// <param name="sender">Цикл.</param>
    /// <param name="e">Признак изменения.</param>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Читает следующую страницу — обычной догрузкой или тихой попыткой цикла.
    /// </summary>
    /// <remarks>
    /// Курсор приходит аргументом: тихая попытка обязана повторить упавший запрос, даже если
    /// список успели обновить. Успех очищает ошибку; неудача оставляет строки, причину и
    /// курсор как были.
    /// </remarks>
    /// <param name="cursor">Курсор упавшей страницы: повтор несёт именно его.</param>
    /// <param name="mode">Обычная догрузка или тихая попытка цикла.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему второй цикл решает, нужна ли ещё одна попытка.</returns>
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
        var filter = Filter;
        var kind = ApiFailureKind.None;

        try
        {
            var page = await _api.GetRequestsAsync(filter, cursor, PageSize, cancellationToken);

            if (generation != _generation || _nextCursor != cursor)
            {
                // Пока читали, список перечитали первой страницей или страницу уже догрузила
                // обычная догрузка: ответ устарел. Курсор, снятый на старте, и есть защита от
                // второго добавления той же страницы.
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
                if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Unauthorized })
                {
                    await EndSessionAsync();
                }
                else if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Forbidden })
                {
                    if (generation == _generation)
                    {
                        SetForbidden();
                    }
                }
                else if (generation == _generation && _nextCursor == cursor)
                {
                    SetLoadMoreFailure(
                        cursor,
                        AdminErrorText.Read(exception, kind, LoadMoreErrorText),
                        kind);
                }
            }
        }
        finally
        {
            // Флаг снимается всегда: первая страница, перечитанная поверх догрузки, поколение
            // сдвигает, но флаг не трогает — иначе он остался бы поднятым навсегда.
            IsLoadingMore = false;
            NotifyViewState();
        }

        return kind;
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
    /// Показывает ошибку догрузки и запоминает снимок упавшего запроса.
    /// </summary>
    /// <param name="cursor">Курсор упавшей страницы.</param>
    /// <param name="message">Текст для человека.</param>
    /// <param name="kind">Вид сбоя: по нему решает цикл автоповтора.</param>
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
    /// Запускает цикл догрузки, если страница видна, сбой повторяемый и есть снимок курсора.
    /// </summary>
    private void TryStartLoadMoreAutoRetry()
    {
        if (!_isPageVisible || IsForbidden || IsSignInRequired)
        {
            return;
        }

        if (_loadMoreRetryCursor is null
            || LoadMoreError is null
            || !_loadMoreFailureKind.IsRetryable()
            || IsFirstLoading)
        {
            return;
        }

        _loadMoreAutoRetry.Start(RetryLoadMoreAttemptAsync);
    }

    /// <summary>
    /// Останавливает цикл догрузки, не трогая признак «страница видна».
    /// </summary>
    private void StopLoadMoreAutoRetryLoop() => _loadMoreAutoRetry.Stop();

    /// <summary>
    /// Переключает строку «Пробуем снова…» в футере вслед за состоянием цикла догрузки.
    /// </summary>
    /// <param name="sender">Цикл.</param>
    /// <param name="e">Признак изменения.</param>
    private void OnLoadMoreAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsLoadMoreAutoRetrying = _loadMoreAutoRetry.IsRunning;

    /// <summary>
    /// Дополняет список следующей страницей.
    /// </summary>
    /// <remarks>
    /// Заявка, которая уже есть в списке, не добавляется второй раз: повтор упавшей догрузки
    /// идёт тем же курсором, и сервер может вернуть ту же строку.
    /// </remarks>
    /// <param name="requests">Строки следующей страницы.</param>
    private void AppendItems(IReadOnlyList<AdminBarberRequestResponse> requests)
    {
        var now = _timeProvider.GetUtcNow();

        foreach (var request in requests)
        {
            if (Items.Any(item => item.Id == request.Id))
            {
                continue;
            }

            Items.Add(new AdminRequestItem(request, now));
        }

        NotifyViewState();
    }

    /// <summary>
    /// Проверяет, можно ли догружать: сессия не кончилась и доступа не отняли.
    /// </summary>
    /// <returns><c>true</c>, если догрузка имеет смысл.</returns>
    private bool IsAuthenticatedForLoadMore() => !IsForbidden && !IsSignInRequired;

    /// <summary>
    /// Гасит сессию: пара мертва, страница показывает состояние входа.
    /// </summary>
    /// <returns>Задача очистки.</returns>
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
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowForbidden));
        OnPropertyChanged(nameof(ShowSignInRequired));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowLoadMoreError));
        OnPropertyChanged(nameof(CanLoadMore));
    }

    /// <summary>
    /// Режим загрузки первой страницы: показать индикатор, обновить молча или повторить в фоне.
    /// </summary>
    private enum FirstPageMode
    {
        /// <summary>
        /// Первое чтение: экран показывает индикатор вместо содержимого.
        /// </summary>
        Initial,

        /// <summary>
        /// Тихое обновление при возврате на страницу и свайпом.
        /// </summary>
        Silent,

        /// <summary>
        /// Попытка цикла автоповтора: как <see cref="Silent"/>, но ошибка не стирается до ответа.
        /// </summary>
        Retry,
    }

    /// <summary>
    /// Режим догрузки: обычная по прокрутке или тихая попытка цикла.
    /// </summary>
    private enum LoadMoreMode
    {
        /// <summary>
        /// Обычная догрузка: индикатор, старая ошибка стирается, потому что её сменил жест.
        /// </summary>
        Normal,

        /// <summary>
        /// Тихая попытка цикла: причина и строки остаются, ответ решает исход.
        /// </summary>
        Retry,
    }
}
