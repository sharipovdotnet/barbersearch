using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.Admin;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Admin;

/// <summary>
/// Очередь отзывов админки: два сегмента, курсорная догрузка и обновление при появлении.
/// </summary>
/// <remarks>
/// <para>
/// Экран повторяет веб-очередь <c>/admin/reviews</c>: те же две вкладки, но переключателем-
/// сегментом, потому что вкладок на телефоне нет. Смена сегмента — это другой список на
/// сервере, поэтому она читает первую страницу заново, а не фильтрует уже загруженное.
/// </para>
/// <para>
/// Очередь перечитывается при каждом появлении страницы: возврат из карточки после решения —
/// это и есть обновление. Поэтому разрешённая строка исчезает без отдельного события
/// «очередь изменилась», а список не кэшируется между визитами.
/// </para>
/// <para>
/// Чтения повторяются сами, пока страница видна: у первой страницы и у догрузки свои циклы,
/// потому что это два разных снимка с разными курсорами. Записей у экрана нет — решения
/// принимаются в карточке, и повторов у них не бывает.
/// </para>
/// <para>
/// Ошибка, роли и сессии показывает одно состояние за раз: отказ в доступе не повторяется,
/// а <c>401</c> гасит сессию и возвращает на «Профиль».
/// </para>
/// </remarks>
public sealed partial class AdminReviewsViewModel : ObservableObject
{
    /// <summary>
    /// Размер страницы: серверный размер по умолчанию, как у веба.
    /// </summary>
    public const int PageSize = 20;

    private static string LoadErrorText => Loc.T("Не удалось загрузить отзывы");
    private static string LoadMoreErrorText => Loc.T("Не удалось загрузить ещё отзывы.");

    private readonly IAdminReviewsApi _reviewsApi;
    private readonly TokenSession _session;
    private readonly AutoRetryLoop _autoRetry;
    private readonly AutoRetryLoop _loadMoreAutoRetry;
    private readonly TimeProvider _timeProvider;

    private string? _nextCursor;
    private int _generation;
    private bool _isPageVisible;
    private bool _hasLoaded;

    /// <summary>
    /// Вид последнего сбоя первой страницы: по нему решает цикл автоповтора.
    /// </summary>
    private ApiFailureKind _failureKind;

    /// <summary>
    /// Вид последнего сбоя догрузки: у второго чтения свой цикл.
    /// </summary>
    private ApiFailureKind _loadMoreFailureKind;

    /// <summary>
    /// Курсор упавшей догрузки: тихая попытка несёт его, а не текущий курсор списка.
    /// </summary>
    private string? _loadMoreRetryCursor;

    /// <summary>
    /// Создаёт экран очереди.
    /// </summary>
    /// <param name="reviewsApi">Список отзывов выбранного статуса.</param>
    /// <param name="session">Сессия: конец сессии при <c>401</c>.</param>
    /// <param name="timeProvider">Часы: ожидание в очереди считается от них.</param>
    /// <param name="autoRetry">Цикл повтора первой страницы; в тестах — расписание без ожидания.</param>
    /// <param name="loadMoreAutoRetry">Цикл повтора догрузки, отдельный от первого.</param>
    public AdminReviewsViewModel(
        IAdminReviewsApi reviewsApi,
        TokenSession session,
        TimeProvider timeProvider,
        AutoRetryLoop? autoRetry = null,
        AutoRetryLoop? loadMoreAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(reviewsApi);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _reviewsApi = reviewsApi;
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
    public ObservableCollection<AdminReviewItem> Items { get; } = [];

    /// <summary>
    /// Первая загрузка: индикатор вместо списка.
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
    /// Цикл сам повторяет первую страницу: под ошибкой видна строка «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Цикл сам повторяет догрузку: строка «Пробуем снова…» видна в футере.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadMoreAutoRetrying;

    /// <summary>
    /// Ошибка первой страницы; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Ошибка догрузки: уже показанные строки остаются.
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
    /// Время последнего удачного чтения: «Обновлено в 14:32».
    /// </summary>
    [ObservableProperty]
    private string? _updatedAtText;

    /// <summary>
    /// Какой сегмент выбран: очередь модерации или опубликованные.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPendingSelected))]
    [NotifyPropertyChangedFor(nameof(IsPublishedSelected))]
    [NotifyPropertyChangedFor(nameof(EmptyStateTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateDescription))]
    private AdminReviewStatusFilter _selectedStatus = AdminReviewStatusFilter.Pending;

    /// <summary>
    /// Выбран сегмент «На модерации».
    /// </summary>
    public bool IsPendingSelected => SelectedStatus == AdminReviewStatusFilter.Pending;

    /// <summary>
    /// Выбран сегмент «Скрытые».
    /// </summary>
    public bool IsPublishedSelected => SelectedStatus == AdminReviewStatusFilter.Published;

    /// <summary>
    /// Заголовок пустого состояния: у двух очередей он разный.
    /// </summary>
    public string EmptyStateTitle => IsPendingSelected
        ? Loc.T("Очередь пуста")
        : Loc.T("Опубликованных отзывов пока нет");

    /// <summary>
    /// Пояснение пустого состояния: у двух очередей оно разное.
    /// </summary>
    public string EmptyStateDescription => IsPendingSelected
        ? Loc.T("Все отзывы проверены. Новые появятся здесь, как только клиенты их оставят.")
        : Loc.T("Когда отзыв опубликуют, он появится здесь — и его можно будет скрыть.");

    /// <summary>
    /// Список виден: загрузка кончилась и ни одно состояние не перекрывает его.
    /// </summary>
    public bool ShowContent =>
        !IsFirstLoading && !ShowError && !ShowForbidden && !ShowSignInRequired;

    /// <summary>
    /// Сбой чтения виден: причина и «Повторить».
    /// </summary>
    public bool ShowError =>
        ErrorMessage is not null && !ShowForbidden && !ShowSignInRequired;

    /// <summary>
    /// Отказ в доступе виден.
    /// </summary>
    public bool ShowForbidden => IsForbidden && !IsSignInRequired;

    /// <summary>
    /// Просьба войти снова видна.
    /// </summary>
    public bool ShowSignInRequired => IsSignInRequired;

    /// <summary>
    /// Пора показать пустое состояние: загрузка прошла, строк нет.
    /// </summary>
    public bool ShowEmptyState => ShowContent && _hasLoaded && Items.Count == 0;

    /// <summary>
    /// Есть следующая страница.
    /// </summary>
    public bool CanLoadMore => _nextCursor is not null;

    /// <summary>
    /// Ошибка догрузки видна в футере.
    /// </summary>
    public bool ShowLoadMoreError => LoadMoreError is not null;

    /// <summary>
    /// Есть ли время последнего чтения.
    /// </summary>
    public bool HasUpdatedAt => UpdatedAtText is not null;

    /// <summary>
    /// Читает первую страницу: первый раз с индикатором, дальше тихо.
    /// </summary>
    /// <remarks>
    /// Вызывается при каждом появлении страницы, поэтому возврат из карточки после решения
    /// перечитывает очередь. Повторный вызов без ухода со страницы тоже допустим: тихое
    /// чтение не мигает индикатором и не теряет строки до ответа.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _session.EnsureLoadedAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_session.IsAuthenticated)
        {
            ApplySignedOutState();
            return;
        }

        IsSignInRequired = false;

        await LoadAsync(_hasLoaded ? LoadMode.Silent : LoadMode.Initial, cancellationToken);
    }

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
    /// Выключает автоповторы: страница скрыта или открыта карточка.
    /// </summary>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        StopAutoRetryLoop();
        StopLoadMoreAutoRetryLoop();
    }

    /// <summary>
    /// Сбрасывает экран после конца сессии.
    /// </summary>
    /// <remarks>
    /// Поколение сдвигается: ответ, который ещё летит, не вернёт отзывы прошлого пользователя.
    /// </remarks>
    public void ApplySignedOutState()
    {
        StopAutoRetryLoop();
        StopLoadMoreAutoRetryLoop();

        _generation++;
        _nextCursor = null;
        _hasLoaded = false;
        _failureKind = ApiFailureKind.None;
        _loadMoreFailureKind = ApiFailureKind.None;
        _loadMoreRetryCursor = null;

        IsSignInRequired = true;
        IsFirstLoading = false;
        IsRefreshing = false;
        IsLoadingMore = false;
        IsForbidden = false;
        ErrorMessage = null;
        LoadMoreError = null;
        UpdatedAtText = null;

        Items.Clear();

        NotifyViewState();
    }

    /// <summary>
    /// Цикл повтора первой страницы для тестов.
    /// </summary>
    internal AutoRetryLoop AutoRetry => _autoRetry;

    /// <summary>
    /// Завершение первого цикла для тестов.
    /// </summary>
    internal Task AutoRetryCompletion => _autoRetry.Completion;

    /// <summary>
    /// Цикл повтора догрузки для тестов.
    /// </summary>
    internal AutoRetryLoop LoadMoreAutoRetry => _loadMoreAutoRetry;

    /// <summary>
    /// Завершение цикла догрузки для тестов.
    /// </summary>
    internal Task LoadMoreAutoRetryCompletion => _loadMoreAutoRetry.Completion;

    /// <summary>
    /// Свайп вниз: перечитать первую страницу, оставив строки на месте.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync(LoadMode.Refresh, CancellationToken.None);

    /// <summary>
    /// «Повторить» после сбоя: остановить фон и прочитать снова с индикатором.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RetryAsync()
    {
        StopAutoRetryLoop();

        await LoadAsync(LoadMode.Initial, CancellationToken.None);
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
    /// Повторяет догрузку после сбоя, не трогая показанные строки.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RetryLoadMoreAsync()
    {
        StopLoadMoreAutoRetryLoop();

        var cursor = _nextCursor;

        if (IsFirstLoading || IsLoadingMore || cursor is null)
        {
            return;
        }

        await LoadMorePageAsync(cursor, LoadMoreMode.Normal, CancellationToken.None);
    }

    /// <summary>
    /// Переключает сегмент на «На модерации» и читает его первую страницу.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private Task ShowPendingAsync() => SelectStatusAsync(AdminReviewStatusFilter.Pending);

    /// <summary>
    /// Переключает сегмент на «Скрытые» и читает его первую страницу.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private Task ShowPublishedAsync() => SelectStatusAsync(AdminReviewStatusFilter.Published);

    /// <summary>
    /// Переключает очередь и читает её с начала.
    /// </summary>
    /// <remarks>
    /// Вкладки веба — два разных серверных списка: фильтровать загруженное на клиенте нельзя,
    /// иначе сегмент показывал бы только то, что успело приехать в первой очереди. Повторный
    /// выбор того же сегмента ничего не делает — это не жест «обновить».
    /// </remarks>
    /// <param name="status">Новый сегмент.</param>
    /// <returns>Задача чтения.</returns>
    private async Task SelectStatusAsync(AdminReviewStatusFilter status)
    {
        if (SelectedStatus == status)
        {
            return;
        }

        StopAutoRetryLoop();
        StopLoadMoreAutoRetryLoop();

        SelectedStatus = status;
        _nextCursor = null;
        _loadMoreRetryCursor = null;
        LoadMoreError = null;
        _loadMoreFailureKind = ApiFailureKind.None;

        NotifyViewState();

        await LoadAsync(LoadMode.Initial, CancellationToken.None);
    }

    /// <summary>
    /// Читает первую страницу выбранной очереди.
    /// </summary>
    /// <param name="mode">Показать индикатор, обновить молча или повторить в фоне.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private async Task<ApiFailureKind> LoadAsync(LoadMode mode, CancellationToken cancellationToken)
    {
        _generation++;

        var generation = _generation;
        var kind = ApiFailureKind.None;

        if (mode != LoadMode.Retry)
        {
            ErrorMessage = null;
            LoadMoreError = null;
            _failureKind = ApiFailureKind.None;
            _loadMoreFailureKind = ApiFailureKind.None;
            _loadMoreRetryCursor = null;
        }

        if (mode == LoadMode.Initial)
        {
            IsFirstLoading = true;
        }

        if (mode == LoadMode.Refresh)
        {
            IsRefreshing = true;
        }

        NotifyViewState();

        try
        {
            var page = await _reviewsApi.GetReviewsAsync(
                SelectedStatus,
                cursor: null,
                PageSize,
                cancellationToken);

            // Устаревший ответ не применяется: смену сегмента или обновление уже перечитали.
            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            var now = _timeProvider.GetUtcNow();

            Items.Clear();

            foreach (var review in page.Items)
            {
                Items.Add(new AdminReviewItem(review, SelectedStatus, now));
            }

            _nextCursor = page.NextCursor;
            _hasLoaded = true;
            _failureKind = ApiFailureKind.None;
            _loadMoreFailureKind = ApiFailureKind.None;
            _loadMoreRetryCursor = null;
            ErrorMessage = null;
            LoadMoreError = null;
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
                await ApplyFirstPageFailureAsync(exception, kind, generation);
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsFirstLoading = false;
                IsRefreshing = false;

                NotifyViewState();
                TryStartAutoRetry();
            }
        }

        return kind;
    }

    /// <summary>
    /// Раскладывает сбой первой страницы по состояниям экрана.
    /// </summary>
    /// <param name="exception">Исключение из API.</param>
    /// <param name="kind">Вид сбоя.</param>
    /// <param name="generation">Поколение чтения.</param>
    /// <returns>Задача обработки.</returns>
    private async Task ApplyFirstPageFailureAsync(
        Exception exception,
        ApiFailureKind kind,
        int generation)
    {
        if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Unauthorized })
        {
            ApplySignedOutState();

            await _session.ClearAsync(CancellationToken.None);

            return;
        }

        if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Forbidden })
        {
            if (generation != _generation)
            {
                return;
            }

            IsForbidden = true;
            ErrorMessage = null;

            NotifyViewState();

            return;
        }

        if (generation != _generation)
        {
            return;
        }

        IsFirstLoading = false;
        _failureKind = kind;

        // Тихая ошибка с уже показанными строками не должна стирать список и мигать
        // полноэкранным сбоем: строки остаются, а причину человек увидит при первой загрузке.
        if (Items.Count == 0)
        {
            ErrorMessage = AdminErrorText.Read(exception, kind, LoadErrorText);
        }

        NotifyViewState();
    }

    /// <summary>
    /// Одна попытка цикла: перечитать первую страницу, не гася ошибку до ответа.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetryFirstPageAsync(CancellationToken cancellationToken)
    {
        if (ErrorMessage is null || !_failureKind.IsRetryable())
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadAsync(LoadMode.Retry, cancellationToken);
    }

    /// <summary>
    /// Читает следующую страницу — обычной догрузкой или тихой попыткой цикла.
    /// </summary>
    /// <param name="cursor">Курсор упавшей страницы: повтор несёт именно его.</param>
    /// <param name="mode">Обычная догрузка или тихая попытка цикла.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему второй цикл решает, нужна ли ещё попытка.</returns>
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
            var page = await _reviewsApi.GetReviewsAsync(
                SelectedStatus,
                cursor,
                PageSize,
                cancellationToken);

            // За время чтения могли переключить сегмент или перечитать первую страницу:
            // запоздавшая страница не имеет права попасть в чужой список.
            if (generation != _generation || _nextCursor != cursor)
            {
                return ApiFailureKind.None;
            }

            var now = _timeProvider.GetUtcNow();

            AppendItems(page.Items, now);

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
                    ApplySignedOutState();

                    await _session.ClearAsync(CancellationToken.None);
                }
                else if (generation == _generation && _nextCursor == cursor)
                {
                    LoadMoreError = AdminErrorText.Read(exception, kind, LoadMoreErrorText);
                    _loadMoreFailureKind = kind;
                    _loadMoreRetryCursor = cursor;
                }
            }
        }
        finally
        {
            IsLoadingMore = false;

            NotifyViewState();
            TryStartLoadMoreAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Одна попытка цикла: повторить упавшую догрузку тем же курсором.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
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

        if (ErrorMessage is null || !_failureKind.IsRetryable())
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
        if (!_isPageVisible || IsSignInRequired || IsForbidden)
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
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Переключает строку «Пробуем снова…» в футере вслед за вторым циклом.
    /// </summary>
    private void OnLoadMoreAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsLoadMoreAutoRetrying = _loadMoreAutoRetry.IsRunning;

    /// <summary>
    /// Дополняет список следующей страницей.
    /// </summary>
    /// <remarks>
    /// Строка, которая уже есть, не добавляется второй раз: повтор упавшей догрузки идёт тем
    /// же курсором, и сервер может вернуть ту же страницу.
    /// </remarks>
    private void AppendItems(IReadOnlyList<AdminReviewResponse> reviews, DateTimeOffset now)
    {
        foreach (var review in reviews)
        {
            if (Items.Any(item => item.Id == review.Id))
            {
                continue;
            }

            Items.Add(new AdminReviewItem(review, SelectedStatus, now));
        }
    }

    /// <summary>
    /// Поднимает уведомления обо всех вычисляемых свойствах экрана.
    /// </summary>
    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowForbidden));
        OnPropertyChanged(nameof(ShowSignInRequired));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(ShowLoadMoreError));
        OnPropertyChanged(nameof(HasUpdatedAt));
    }

    /// <summary>
    /// Как показывается чтение первой страницы.
    /// </summary>
    private enum LoadMode
    {
        /// <summary>
        /// Первое чтение: индикатор вместо списка.
        /// </summary>
        Initial,

        /// <summary>
        /// Тихое чтение при возврате на страницу: список остаётся.
        /// </summary>
        Silent,

        /// <summary>
        /// Свайп вниз: свой индикатор обновления.
        /// </summary>
        Refresh,

        /// <summary>
        /// Попытка цикла: ошибка не стирается до ответа, иначе экран мигал бы.
        /// </summary>
        Retry,
    }

    /// <summary>
    /// Как показывается догрузка.
    /// </summary>
    private enum LoadMoreMode
    {
        /// <summary>
        /// Обычная догрузка: старая ошибка стирается, потому что её сменил жест.
        /// </summary>
        Normal,

        /// <summary>
        /// Тихая попытка цикла: причина и строки остаются, ответ решает исход.
        /// </summary>
        Retry,
    }
}
