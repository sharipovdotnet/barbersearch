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
/// Инбокс уведомлений мастера: keyset-страницы, непрочитанные, «Прочитать все» и чтение строки.
/// </summary>
/// <remarks>
/// <para>
/// Раздел открывается колокольчиком в шапке кабинета и живёт отдельной страницей, но данных
/// у него столько же, сколько у любого раздела: первая страница, «Дальше», счётчик
/// непрочитанных и две записи (отметить одну, отметить все). Поэтому он оформлен как
/// <see cref="IBarberCabinetSection"/>: страница кабинета может показать его и как панель,
/// и как дочерний экран, не меняя контракт.
/// </para>
/// <para>
/// Список курсорный: сервер отдаёт непрозрачный курсор следующей страницы, «Дальше»
/// дописывает строки в конец, а не заменяет их. Повторное чтение первой страницы
/// сбрасывает курсор — иначе после обновления «Дальше» вернуло бы уже показанное.
/// </para>
/// <para>
/// Чтение повторяется само (<see cref="AutoRetryLoop"/>), пока раздел виден: первая
/// страница и следующая — двумя независимыми циклами, потому что упавшая пачка не должна
/// перечитывать уже показанное. Записи не повторяются: после «Прочитать все» исход
/// известен, и тихий повтор только гонялся бы с локальной отметкой, поэтому у кнопки
/// остаётся обычное «Повторить» нажатием.
/// </para>
/// <para>
/// <c>404</c> и пустая страница — не ошибка: удалённый инбокс или отсутствие строк
/// показываются пустым состоянием, а не красной карточкой. Счётчик непрочитанных
/// обновляется тихо: сбой бейджа не должен превращать рабочий экран в ошибку.
/// </para>
/// </remarks>
public sealed partial class BarberNotificationsViewModel : ObservableObject, IBarberCabinetSection
{
    /// <summary>
    /// Размер страницы: совпадает с умолчанием сервера и веба.
    /// </summary>
    public const int PageSize = 20;

    private static string LoadFailedText => Loc.T("Не удалось загрузить уведомления.");
    private static string LoadMoreFailedText => Loc.T("Не удалось загрузить ещё уведомления.");
    private static string MarkAllFailedText => Loc.T("Не удалось прочитать уведомления.");
    private static string MarkAllDoneText => Loc.T("Все уведомления прочитаны.");

    private readonly IBarberNotificationsApi _api;
    private readonly TimeProvider _timeProvider;
    private readonly AutoRetryLoop _firstPageAutoRetry;
    private readonly AutoRetryLoop _nextPageAutoRetry;

    private Guid _profileId = Guid.Empty;
    private bool _isActive;
    private bool _isActivating;
    private bool _hasLoaded;
    private bool _hasUnreadCount;
    private string? _nextCursor;
    private CancellationTokenSource? _loadCts;
    private int _generation;
    private int _nextPageGeneration;
    private ApiFailureKind _firstPageFailureKind;
    private ApiFailureKind _nextPageFailureKind;

    /// <summary>
    /// Создаёт инбокс уведомлений.
    /// </summary>
    /// <param name="api">Чтение инбокса, счётчик и отметки прочтения.</param>
    /// <param name="timeProvider">Часы: «Сегодня» и «Вчера» в поясе салона.</param>
    /// <param name="firstPageAutoRetry">
    /// Цикл повтора первой страницы; <c>null</c> — собственный цикл с обычным расписанием.
    /// </param>
    /// <param name="nextPageAutoRetry">
    /// Цикл повтора следующей страницы; <c>null</c> — собственный цикл.
    /// </param>
    public BarberNotificationsViewModel(
        IBarberNotificationsApi api,
        TimeProvider timeProvider,
        AutoRetryLoop? firstPageAutoRetry = null,
        AutoRetryLoop? nextPageAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _api = api;
        _timeProvider = timeProvider;

        _firstPageAutoRetry = firstPageAutoRetry ?? new AutoRetryLoop();
        _nextPageAutoRetry = nextPageAutoRetry ?? new AutoRetryLoop();
        _firstPageAutoRetry.StateChanged += OnFirstPageAutoRetryStateChanged;
        _nextPageAutoRetry.StateChanged += OnNextPageAutoRetryStateChanged;
    }

    /// <summary>
    /// Строка просит открыть «Записи»: deep link в конкретный визит невозможен.
    /// </summary>
    /// <remarks>
    /// <c>GET api/bookings/{id}</c> в API нет, а список записей уже содержит визит. Поэтому
    /// нажатие открывает раздел целиком; отметка прочитанным — фон и на навигацию не влияет.
    /// </remarks>
    public event EventHandler? OpenRequested;

    /// <inheritdoc />
    public Guid? BarberProfileId => _profileId == Guid.Empty ? null : _profileId;

    /// <summary>
    /// Строки инбокса в порядке сервера: свежие сверху.
    /// </summary>
    public ObservableCollection<BarberNotificationItem> Items { get; } = [];

    /// <summary>
    /// Идёт первое чтение: на экране индикатор, а не пустое состояние.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstLoading;

    /// <summary>
    /// Идёт обновление свайпом: индикатор держит <c>RefreshView</c>.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Причина неудачи первой страницы; <c>null</c>, когда читать удалось.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Цикл сам повторяет упавшую первую страницу: видна строка «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Идёт чтение следующей страницы: «Дальше» не нажимается второй раз.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingMore;

    /// <summary>
    /// Причина неудачи следующей страницы: показанные строки остаются на месте.
    /// </summary>
    [ObservableProperty]
    private string? _loadMoreError;

    /// <summary>
    /// Цикл сам повторяет упавшую следующую страницу.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadMoreAutoRetrying;

    /// <summary>
    /// Число непрочитанных для бейджа колокольчика и кнопки «Прочитать все».
    /// </summary>
    [ObservableProperty]
    private int _unreadCount;

    /// <summary>
    /// Идёт «Прочитать все»: кнопка показывает индикатор и не нажимается дважды.
    /// </summary>
    [ObservableProperty]
    private bool _isMarkingAllRead;

    /// <summary>
    /// Причина неудачи «Прочитать все»; <c>null</c>, когда отметка удалась.
    /// </summary>
    [ObservableProperty]
    private string? _markAllError;

    /// <summary>
    /// Объявление после успешной отметки; <c>null</c>, когда баннера нет.
    /// </summary>
    [ObservableProperty]
    private string? _announcement;

    /// <summary>
    /// Строки уже читались: первое чтение закончилось успешно хотя бы раз.
    /// </summary>
    public bool HasLoaded => _hasLoaded;

    /// <summary>
    /// В инбоксе есть строки.
    /// </summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>
    /// Показывать список: первое чтение закончилось и строки есть.
    /// </summary>
    public bool ShowContent => !IsFirstLoading && Items.Count > 0;

    /// <summary>
    /// Показывать пустое состояние: читать удалось, но строк нет.
    /// </summary>
    public bool ShowEmpty => !IsFirstLoading && _hasLoaded && ErrorMessage is null && Items.Count == 0;

    /// <summary>
    /// Показывать карточку ошибки: строк нет и читать не удалось.
    /// </summary>
    public bool ShowError => !IsFirstLoading && ErrorMessage is not null && Items.Count == 0;

    /// <summary>
    /// Текст пустого состояния.
    /// </summary>
    public string EmptyText => ShowEmpty ? Loc.T("Уведомлений пока нет.") : string.Empty;

    /// <summary>
    /// У сервера есть следующая страница.
    /// </summary>
    public bool HasMore => _nextCursor is not null;

    /// <summary>
    /// «Дальше» доступна: страница есть и сейчас ничего не читается.
    /// </summary>
    public bool CanLoadMore =>
        _nextCursor is not null
        && !IsLoadingMore
        && !IsFirstLoading
        && _profileId != Guid.Empty;

    /// <summary>
    /// У следующей страницы есть причина неудачи: её показывают над «Дальше».
    /// </summary>
    public bool HasLoadMoreError => LoadMoreError is not null;

    /// <summary>
    /// Есть непрочитанные: бейдж и кнопка «Прочитать все» активны.
    /// </summary>
    public bool HasUnread => UnreadCount > 0;

    /// <summary>
    /// Подпись бейджа: до 99 цифрами, дальше «99+».
    /// </summary>
    public string UnreadBadgeText => BarberProfileText.BadgeCount(UnreadCount);

    /// <summary>
    /// «Прочитать все» доступна, когда есть что отмечать.
    /// </summary>
    public bool CanMarkAllRead => UnreadCount > 0 && !IsMarkingAllRead;

    /// <summary>
    /// Есть баннер о результате.
    /// </summary>
    public bool HasAnnouncement => !string.IsNullOrEmpty(Announcement);

    /// <summary>
    /// Есть ошибка «Прочитать все».
    /// </summary>
    public bool HasMarkAllError => MarkAllError is not null;

    /// <summary>
    /// Цикл повтора первой страницы: тестам нужен счётчик попыток и завершение.
    /// </summary>
    internal AutoRetryLoop FirstPageAutoRetry => _firstPageAutoRetry;

    /// <summary>
    /// Цикл повтора следующей страницы: тестам нужен счётчик попыток и завершение.
    /// </summary>
    internal AutoRetryLoop NextPageAutoRetry => _nextPageAutoRetry;

    /// <summary>
    /// Завершается, когда цикл первой страницы останавливается.
    /// </summary>
    internal Task FirstPageRetryCompletion => _firstPageAutoRetry.Completion;

    /// <summary>
    /// Завершается, когда цикл следующей страницы останавливается.
    /// </summary>
    internal Task NextPageRetryCompletion => _nextPageAutoRetry.Completion;

    /// <inheritdoc />
    public void Initialize(Guid barberProfileId)
    {
        if (_profileId == barberProfileId)
        {
            OnPropertyChanged(nameof(BarberProfileId));

            return;
        }

        _profileId = barberProfileId;

        _firstPageAutoRetry.Stop();
        _nextPageAutoRetry.Stop();

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        _generation++;
        _nextPageGeneration++;

        Items.Clear();
        _nextCursor = null;
        _hasLoaded = false;
        _hasUnreadCount = false;

        IsFirstLoading = false;
        IsRefreshing = false;
        ErrorMessage = null;
        IsAutoRetrying = false;
        IsLoadingMore = false;
        LoadMoreError = null;
        IsLoadMoreAutoRetrying = false;
        UnreadCount = 0;
        IsMarkingAllRead = false;
        MarkAllError = null;
        Announcement = null;

        _firstPageFailureKind = ApiFailureKind.None;
        _nextPageFailureKind = ApiFailureKind.None;

        NotifyListState();
        NotifyUnreadState();
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
            await LoadFirstPageAsync(silent, cancellationToken);
        }
        finally
        {
            _isActivating = false;
            IsRefreshing = false;
        }

        TryStartFirstPageAutoRetry();
    }

    /// <inheritdoc />
    public void Deactivate()
    {
        _isActive = false;

        _firstPageAutoRetry.Stop();
        _nextPageAutoRetry.Stop();

        IsAutoRetrying = false;
        IsLoadMoreAutoRetrying = false;
    }

    /// <summary>
    /// Сообщает, что раздел виден, и запускает автоповтор упавших чтений.
    /// </summary>
    /// <remarks>
    /// Идемпотентен: без повторяемого сбоя или уже работающего цикла ничего не делает.
    /// </remarks>
    public void StartAutoRetry()
    {
        _isActive = true;

        TryStartFirstPageAutoRetry();
        TryStartNextPageAutoRetry();
    }

    /// <summary>
    /// Останавливает автоповтор, не сбрасывая показанные строки.
    /// </summary>
    public void StopAutoRetry() => Deactivate();

    /// <summary>
    /// Перечитывает первую страницу по кнопке — сразу, без паузы цикла.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        if (_profileId == Guid.Empty || IsFirstLoading)
        {
            return;
        }

        _firstPageAutoRetry.Stop();

        await LoadFirstPageAsync(Items.Count > 0, cancellationToken);

        TryStartFirstPageAutoRetry();
    }

    /// <summary>
    /// Обновляет инбокс свайпом.
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
        _firstPageAutoRetry.Stop();

        try
        {
            await LoadFirstPageAsync(silent: true, cancellationToken);
        }
        finally
        {
            IsRefreshing = false;
        }

        TryStartFirstPageAutoRetry();
    }

    /// <summary>
    /// Дописывает следующую страницу инбокса.
    /// </summary>
    /// <remarks>
    /// На ошибке курсор не сдвигается: повтор вернёт ту же страницу, а уже показанные
    /// строки останутся на месте. Повторное нажатие во время чтения игнорируется.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync(CancellationToken cancellationToken)
    {
        if (_nextCursor is not { } cursor)
        {
            return;
        }

        _nextPageAutoRetry.Stop();

        await LoadNextPageAsync(cursor, silent: false, cancellationToken);

        TryStartNextPageAutoRetry();
    }

    /// <summary>
    /// Отмечает все уведомления прочитанными и гасит точки без перечитывания страницы.
    /// </summary>
    /// <remarks>
    /// Это запись: тихого повтора нет. Повторное нажатие во время запроса игнорируется,
    /// а ошибка показывается рядом с кнопкой — человек сам решает, когда повторить.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand(CanExecute = nameof(CanMarkAllRead))]
    private async Task MarkAllReadAsync(CancellationToken cancellationToken)
    {
        if (!CanMarkAllRead)
        {
            return;
        }

        IsMarkingAllRead = true;
        MarkAllError = null;

        try
        {
            await _api.MarkAllReadAsync(cancellationToken);

            foreach (var item in Items)
            {
                item.MarkRead();
            }

            _hasUnreadCount = true;
            UnreadCount = 0;
            Announcement = MarkAllDoneText;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Экран ушёл: ошибку показывать некому, строки остаются непрочитанными.
        }
        catch (Exception exception)
        {
            MarkAllError = DescribeFailure(exception, MarkAllFailedText);
        }
        finally
        {
            IsMarkingAllRead = false;
        }
    }

    /// <summary>
    /// Открывает строку: отмечает её прочитанной и просит страницу перейти в «Записи».
    /// </summary>
    /// <remarks>
    /// Отметка прочитанным — фон: строка открывается, даже если запрос не удался.
    /// <c>404</c> значит, что уведомление уже удалено; локальная точка гасится, ошибки нет.
    /// </remarks>
    /// <param name="item">Строка инбокса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task OpenAsync(
        BarberNotificationItem? item,
        CancellationToken cancellationToken)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsUnread)
        {
            try
            {
                await _api.MarkReadAsync(item.Id, cancellationToken);

                item.MarkRead();
                RefreshUnreadAfterRead();
            }
            catch (BarberSearchApiException exception)
                when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                item.MarkRead();
                RefreshUnreadAfterRead();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Отмена — не сбой: строка всё равно откроется ниже.
            }
            catch (Exception)
            {
                // Сбой отметки не мешает открыть раздел; строка останется непрочитанной.
            }
        }

        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Убирает баннер о результате.
    /// </summary>
    [RelayCommand]
    private void DismissAnnouncement() => Announcement = null;

    /// <summary>
    /// Читает первую страницу; тихий режим не показывает индикатор первого чтения.
    /// </summary>
    /// <param name="silent">Обновление уже показанных строк.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему решает цикл повтора.</returns>
    private async Task<ApiFailureKind> LoadFirstPageAsync(
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

        // Обновление первой страницы делает недействительным ответ «Дальше»: он дописал бы
        // строки к списку, которого больше нет.
        _nextPageGeneration++;
        _nextPageAutoRetry.Stop();
        IsLoadingMore = false;
        LoadMoreError = null;

        if (!silent)
        {
            IsFirstLoading = true;
            ErrorMessage = null;
        }

        var kind = ApiFailureKind.None;
        var loaded = false;

        try
        {
            var page = await _api.GetAsync(
                cursor: null,
                PageSize,
                cancellationToken: cts.Token);

            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            Items.Clear();

            var now = _timeProvider.GetUtcNow();

            foreach (var notification in page.Items)
            {
                Items.Add(new BarberNotificationItem(notification, now));
            }

            _nextCursor = page.NextCursor;
            _hasLoaded = true;
            loaded = true;
            ErrorMessage = null;
            _firstPageFailureKind = ApiFailureKind.None;
        }
        catch (Exception exception)
        {
            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.NotFound })
            {
                // Удалённый или ещё не созданный инбокс — пустое состояние, а не ошибка.
                Items.Clear();
                _nextCursor = null;
                _hasLoaded = true;
                ErrorMessage = null;
                _firstPageFailureKind = ApiFailureKind.None;
            }
            else
            {
                kind = ApiFailureClassifier.Classify(exception, cts.Token);

                if (kind != ApiFailureKind.None)
                {
                    _firstPageFailureKind = kind;

                    // Тихий повтор не стирает уже показанные строки: причина у пустого
                    // списка, у списка со строками ошибка выглядела бы поломкой данных.
                    if (Items.Count == 0)
                    {
                        ErrorMessage = BarberProfileText.ReadError(
                            exception,
                            kind,
                            LoadFailedText);
                    }
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
            NotifyListState();
        }

        if (loaded)
        {
            // Счётчик читается после того, как список уже показан: сбой бейджа не должен
            // задерживать строки, а сам счётчик обновляется тихо.
            await RefreshUnreadAsync(cancellationToken);
        }

        if (kind.IsRetryable())
        {
            TryStartFirstPageAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Дописывает следующую страницу.
    /// </summary>
    /// <param name="cursor">Курсор страницы, которую запрашиваем.</param>
    /// <param name="silent">Попытка цикла: не показывать индикатор и не стирать причину.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему решает цикл повтора.</returns>
    private async Task<ApiFailureKind> LoadNextPageAsync(
        string cursor,
        bool silent,
        CancellationToken cancellationToken)
    {
        var generation = ++_nextPageGeneration;

        if (!silent)
        {
            IsLoadingMore = true;
            LoadMoreError = null;
        }

        var kind = ApiFailureKind.None;

        try
        {
            var page = await _api.GetAsync(
                cursor,
                PageSize,
                cancellationToken);

            if (generation != _nextPageGeneration)
            {
                return ApiFailureKind.None;
            }

            var now = _timeProvider.GetUtcNow();
            var known = Items.Select(item => item.Id).ToHashSet();

            foreach (var notification in page.Items)
            {
                // Повтор страницы не должен дописать уже показанную строку второй раз.
                if (known.Add(notification.Id))
                {
                    Items.Add(new BarberNotificationItem(notification, now));
                }
            }

            _nextCursor = page.NextCursor;
            LoadMoreError = null;
            _nextPageFailureKind = ApiFailureKind.None;
        }
        catch (Exception exception)
        {
            if (generation != _nextPageGeneration)
            {
                return ApiFailureKind.None;
            }

            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                _nextPageFailureKind = kind;
                LoadMoreError = BarberProfileText.ReadError(
                    exception,
                    kind,
                    LoadMoreFailedText);
            }
        }
        finally
        {
            if (generation == _nextPageGeneration)
            {
                IsLoadingMore = false;
            }

            NotifyListState();
        }

        if (kind.IsRetryable())
        {
            TryStartNextPageAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Тихо обновляет счётчик непрочитанных.
    /// </summary>
    /// <remarks>
    /// Ошибка счётчика не показывается и не повторяется: бейдж — украшение шапки.
    /// Если счётчик ещё ни разу не приходил, нижней оценкой служат непрочитанные строки
    /// уже загруженной страницы.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task RefreshUnreadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var count = await _api.GetUnreadCountAsync(cancellationToken);

            _hasUnreadCount = true;
            UnreadCount = Math.Max(count, UnreadItemsCount());
        }
        catch (Exception)
        {
            if (!_hasUnreadCount)
            {
                UnreadCount = UnreadItemsCount();
            }
        }
    }

    /// <summary>
    /// Уменьшает счётчик после отметки одной строки, не опускаясь ниже показанных строк.
    /// </summary>
    private void RefreshUnreadAfterRead()
    {
        _hasUnreadCount = true;
        UnreadCount = Math.Max(UnreadCount - 1, UnreadItemsCount());
    }

    /// <summary>
    /// Сколько непрочитанных в уже загруженных строках.
    /// </summary>
    /// <returns>Число непрочитанных строк.</returns>
    private int UnreadItemsCount() => Items.Count(item => item.IsUnread);

    /// <summary>
    /// Запускает цикл первой страницы, если раздел виден и сбой повторяемый.
    /// </summary>
    private void TryStartFirstPageAutoRetry()
    {
        if (!_isActive
            || _profileId == Guid.Empty
            || !_firstPageFailureKind.IsRetryable())
        {
            return;
        }

        _firstPageAutoRetry.Start(RetryFirstPageAsync);
    }

    /// <summary>
    /// Запускает цикл следующей страницы, если раздел виден и сбой повторяемый.
    /// </summary>
    private void TryStartNextPageAutoRetry()
    {
        if (!_isActive
            || _profileId == Guid.Empty
            || !_nextPageFailureKind.IsRetryable()
            || _nextCursor is null)
        {
            return;
        }

        _nextPageAutoRetry.Start(RetryNextPageAsync);
    }

    /// <summary>
    /// Одна попытка цикла: перечитывает первую страницу, не сбрасывая показанное и причину.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetryFirstPageAsync(CancellationToken cancellationToken) =>
        LoadFirstPageAsync(silent: true, cancellationToken);

    /// <summary>
    /// Одна попытка цикла: повторяет ровно ту страницу, которая упала.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetryNextPageAsync(CancellationToken cancellationToken) =>
        _nextCursor is { } cursor
            ? LoadNextPageAsync(cursor, silent: true, cancellationToken)
            : Task.FromResult(ApiFailureKind.None);

    /// <summary>
    /// Начинает новое чтение, отменяя предыдущее.
    /// </summary>
    /// <param name="cancellationToken">Токен экрана.</param>
    /// <returns>Источник с связанным токеном.</returns>
    private CancellationTokenSource StartRequest(CancellationToken cancellationToken)
    {
        _loadCts?.Cancel();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _loadCts = cts;

        return cts;
    }

    /// <summary>
    /// Переводит сбой записи в текст для человека.
    /// </summary>
    /// <param name="exception">Исключение запроса.</param>
    /// <param name="fallback">Подпись действия.</param>
    /// <returns>Текст для человека.</returns>
    private static string DescribeFailure(Exception exception, string fallback) =>
        exception is BarberSearchApiException apiException
            ? apiException.StatusCode switch
            {
                HttpStatusCode.Unauthorized => BarberProfileText.SessionExpired,
                HttpStatusCode.TooManyRequests => BarberProfileText.TooManyRequests,
                _ => fallback,
            }
            : exception is HttpRequestException or OperationCanceledException
                ? BarberProfileText.Network
                : fallback;

    /// <summary>
    /// Переключает строку «Пробуем снова…» первой страницы вслед за циклом.
    /// </summary>
    private void OnFirstPageAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _firstPageAutoRetry.IsRunning;

    /// <summary>
    /// Переключает строку «Пробуем снова…» следующей страницы вслед за циклом.
    /// </summary>
    private void OnNextPageAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsLoadMoreAutoRetrying = _nextPageAutoRetry.IsRunning;

    /// <summary>
    /// Поднимает уведомления обо всём, что зависит от строк и их состояния.
    /// </summary>
    private void NotifyListState()
    {
        OnPropertyChanged(nameof(HasLoaded));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(HasLoadMoreError));

        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Поднимает уведомления о счётчике, бейдже и доступности «Прочитать все».
    /// </summary>
    private void NotifyUnreadState()
    {
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(UnreadBadgeText));
        OnPropertyChanged(nameof(CanMarkAllRead));

        MarkAllReadCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Первое чтение закончилось: переключаются «список», «пусто» и «ошибка».
    /// </summary>
    partial void OnIsFirstLoadingChanged(bool value) => NotifyListState();

    /// <summary>
    /// Чтение следующей страницы закончилось: «Дальше» снова доступна.
    /// </summary>
    partial void OnIsLoadingMoreChanged(bool value) => NotifyListState();

    /// <summary>
    /// Причина первой страницы изменилась: переключаются «список» и «ошибка».
    /// </summary>
    partial void OnErrorMessageChanged(string? value) => NotifyListState();

    /// <summary>
    /// Причина следующей страницы изменилась: переключается строка над «Дальше».
    /// </summary>
    partial void OnLoadMoreErrorChanged(string? value) => NotifyListState();

    /// <summary>
    /// Счётчик изменился: пересчитываются бейдж и доступность «Прочитать все».
    /// </summary>
    partial void OnUnreadCountChanged(int value) => NotifyUnreadState();

    /// <summary>
    /// Отметка «все прочитаны» началась или закончилась.
    /// </summary>
    partial void OnIsMarkingAllReadChanged(bool value) => NotifyUnreadState();

    /// <summary>
    /// Ошибка «Прочитать все» появилась или исчезла.
    /// </summary>
    partial void OnMarkAllErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasMarkAllError));

    /// <summary>
    /// Баннер появился или исчез.
    /// </summary>
    partial void OnAnnouncementChanged(string? value) =>
        OnPropertyChanged(nameof(HasAnnouncement));
}
