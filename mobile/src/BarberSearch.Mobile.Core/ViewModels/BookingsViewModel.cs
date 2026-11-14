using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Bookings;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран «Записи»: предстоящие визиты и история, отмена, перенос и отзыв.
/// </summary>
/// <remarks>
/// <para>
/// Два независимых списка (<c>upcoming</c> и <c>past</c>) с собственными курсорами и
/// состоянием: переключение вкладок внутри экрана ничего не перезапрашивает, а история
/// грузится лениво при первом открытии. Строки приходят без имён, поэтому имя барбера,
/// услуга, салон и пояс догружаются публичной карточкой и кэшируются на жизнь ViewModel.
/// </para>
/// <para>
/// Фонового опроса нет (веб опрашивает «Предстоящие» раз в минуту): список обновляется при
/// появлении вкладки, свайпом и после действий. Обратный отсчёт считает таймер страницы,
/// а не сеть. Это решение среза, записанное в <c>mobile/AGENT.md</c> §5.
/// </para>
/// <para>
/// Ошибки показываются по статусу и публичным кодам, никогда по английскому <c>detail</c>.
/// <c>401</c> гасит сессию и возвращает гейт входа; <c>429</c> и сеть — это разные тексты.
/// </para>
/// </remarks>
public sealed partial class BookingsViewModel : ObservableObject
{
    /// <summary>
    /// Размер страницы списка: совпадает с умолчанием сервера и веба.
    /// </summary>
    public const int PageSize = 20;

    private const int MaxParallelProfiles = 4;

    private const string LoadErrorText = "Не удалось загрузить записи.";
    private const string LoadMoreErrorText = "Не удалось загрузить ещё записи.";
    private const string NetworkErrorText =
        "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string TooManyRequestsText =
        "Слишком много запросов. Подождите минуту и повторите.";
    private const string CancelFailedText = "Не удалось отменить запись.";

    /// <summary>
    /// Сноска под списком, когда часть карточек барберов не загрузилась.
    /// </summary>
    public const string ProfilesWarningText =
        "Часть карточек барберов не загрузилась — имена и адреса могут быть неполными.";

    private const string UpcomingEmptyTitle = "Пока ни одной записи";
    private const string UpcomingEmptyDescription =
        "Выберите барбера, услугу и свободное время — запись появится здесь. "
        + "Прошлые визиты останутся в истории.";
    private const string HistoryEmptyTitle = "История пока пуста";
    private const string HistoryEmptyDescription =
        "После визита здесь останется запись и можно будет оставить отзыв.";

    private readonly IBookingsApi _bookingsApi;
    private readonly IBarberSearchApi _barberApi;
    private readonly IAccountApi _accountApi;
    private readonly TokenSession _session;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Кэш карточек барберов: <c>null</c> — карточка не загрузилась, повторять запрос не нужно.
    /// </summary>
    private readonly Dictionary<Guid, BarberProfileResponse?> _profiles = [];

    private readonly BookingListState _upcoming;
    private readonly BookingListState _history;

    private Guid? _userId;
    private bool _isActionRunning;
    private BookingsTab _tab = BookingsTab.Upcoming;

    /// <summary>
    /// Создаёт экран записей.
    /// </summary>
    /// <param name="bookingsApi">Список, отмена и перенос.</param>
    /// <param name="barberApi">Публичная карточка барбера для имён и адресов.</param>
    /// <param name="accountApi">Текущий пользователь: без него список не сузить до своих.</param>
    /// <param name="session">Сессия — источник правды о входе.</param>
    /// <param name="timeProvider">Часы: отсчёт и правила действий проверяются без ожидания.</param>
    public BookingsViewModel(
        IBookingsApi bookingsApi,
        IBarberSearchApi barberApi,
        IAccountApi accountApi,
        TokenSession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(bookingsApi);
        ArgumentNullException.ThrowIfNull(barberApi);
        ArgumentNullException.ThrowIfNull(accountApi);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _bookingsApi = bookingsApi;
        _barberApi = barberApi;
        _accountApi = accountApi;
        _session = session;
        _timeProvider = timeProvider;

        _upcoming = new BookingListState(BookingScope.Upcoming, UpcomingGroups);
        _history = new BookingListState(BookingScope.Past, HistoryGroups);
    }

    /// <summary>
    /// Запрос на перенос записи: страница открывает экран переноса.
    /// </summary>
    public event EventHandler<BookingListItem>? RescheduleRequested;

    /// <summary>
    /// Запрос на отзыв: страница открывает экран отзыва.
    /// </summary>
    public event EventHandler<BookingListItem>? ReviewRequested;

    /// <summary>
    /// Нажата «Войти» в гейте: страница уводит на вкладку «Профиль».
    /// </summary>
    public event EventHandler? SignInRequested;

    /// <summary>
    /// Нажата «Вернуться к поиску» или «Найти барбера»: страница уводит на поиск.
    /// </summary>
    public event EventHandler? SearchRequested;

    /// <summary>
    /// Записи «Предстоящие», сгруппированные по месяцам.
    /// </summary>
    public ObservableCollection<BookingMonthGroup> UpcomingGroups { get; } = [];

    /// <summary>
    /// История визитов, сгруппированная по месяцам.
    /// </summary>
    public ObservableCollection<BookingMonthGroup> HistoryGroups { get; } = [];

    /// <summary>
    /// Список активной вкладки; при переключении меняет привязку одним уведомлением.
    /// </summary>
    public IEnumerable<BookingMonthGroup> ActiveGroups =>
        _tab == BookingsTab.Upcoming ? UpcomingGroups : HistoryGroups;

    /// <summary>
    /// Пользователь вошёл: показаны списки, а не гейт.
    /// </summary>
    [ObservableProperty]
    private bool _isSignedIn;

    /// <summary>
    /// Сессия умерла во время работы: гейт объясняет, что вход нужен заново.
    /// </summary>
    [ObservableProperty]
    private bool _isSessionExpired;

    /// <summary>
    /// Идёт обновление свайпом: <c>RefreshView</c> держит индикатор.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Хотя бы одна карточка барбера не загрузилась: под списком сноска.
    /// </summary>
    [ObservableProperty]
    private bool _showProfilesWarning;

    /// <summary>
    /// Текст баннера об успешном действии; <c>null</c>, когда баннера нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnnouncementVisible))]
    private string? _announcement;

    /// <summary>
    /// Видна ли вкладка «Предстоящие».
    /// </summary>
    public bool IsUpcomingSelected => _tab == BookingsTab.Upcoming;

    /// <summary>
    /// Видна ли вкладка «История».
    /// </summary>
    public bool IsHistorySelected => !IsUpcomingSelected;

    /// <summary>
    /// Показывать гейт входа вместо списка.
    /// </summary>
    public bool ShowGate => !IsSignedIn;

    /// <summary>
    /// Показывать индикатор первой загрузки активной вкладки.
    /// </summary>
    public bool ShowLoading => IsSignedIn && ActiveState.IsFirstLoading;

    /// <summary>
    /// Показывать ошибку первой страницы с кнопкой «Повторить».
    /// </summary>
    public bool ShowError => IsSignedIn && ActiveState.Error is not null;

    /// <summary>
    /// Причина ошибки второй строкой под заголовком.
    /// </summary>
    public string? ErrorMessage => ActiveState.Error;

    /// <summary>
    /// Показывать список: вошли, ошибки нет.
    /// </summary>
    public bool ShowList => IsSignedIn && !ShowError;

    /// <summary>
    /// Показывать пустое состояние; пока идёт первая загрузка — не показывать.
    /// </summary>
    /// <remarks>
    /// Иначе на каждом открытии вкладки пустое состояние мигало бы до ответа сервера.
    /// </remarks>
    public bool ShowEmptyState =>
        IsSignedIn
        && !ActiveState.IsFirstLoading
        && ActiveState.Error is null
        && ActiveState.HasLoaded
        && ActiveState.Groups.Count == 0;

    /// <summary>
    /// Заголовок пустого состояния активной вкладки.
    /// </summary>
    public string EmptyTitle => IsUpcomingSelected ? UpcomingEmptyTitle : HistoryEmptyTitle;

    /// <summary>
    /// Объяснение пустого состояния активной вкладки.
    /// </summary>
    public string EmptyDescription =>
        IsUpcomingSelected ? UpcomingEmptyDescription : HistoryEmptyDescription;

    /// <summary>
    /// Идёт догрузка следующей страницы.
    /// </summary>
    public bool IsLoadingMore => ActiveState.IsLoadingMore;

    /// <summary>
    /// Есть ли следующая страница.
    /// </summary>
    public bool CanLoadMore => ActiveState.NextCursor is not null;

    /// <summary>
    /// Ошибка догрузки: уже загруженные строки остаются, под списком «Повторить».
    /// </summary>
    public bool ShowLoadMoreError => ActiveState.LoadMoreError is not null;

    /// <summary>
    /// Виден ли баннер о результате действия.
    /// </summary>
    public bool IsAnnouncementVisible => !string.IsNullOrEmpty(Announcement);

    private BookingListState ActiveState =>
        _tab == BookingsTab.Upcoming ? _upcoming : _history;

    /// <summary>
    /// Проверяет сессию и тихо обновляет активную вкладку.
    /// </summary>
    /// <remarks>
    /// Вызывается при каждом появлении страницы. <c>/me</c> спрашивается один раз за жизнь
    /// ViewModel: без сессии не делается ни одного запроса, а данные прошлого пользователя
    /// после выхода уже стёрты. Первая загрузка вкладки показывает индикатор, повторная —
    /// оставляет строки до ответа.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
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

        IsSignedIn = true;
        IsSessionExpired = false;

        if (_isActionRunning)
        {
            return;
        }

        if (_userId is null && !await LoadUserIdAsync(cancellationToken))
        {
            return;
        }

        var state = ActiveState;

        await LoadFirstPageAsync(
            state,
            state.HasLoaded ? FirstPageMode.Silent : FirstPageMode.Initial,
            cancellationToken);
    }

    /// <summary>
    /// Сбрасывает экран в гейт входа и стирает данные предыдущего пользователя.
    /// </summary>
    /// <remarks>
    /// Вызывается страницей по <see cref="TokenSession.SessionEnded"/> и самим ViewModel при
    /// <c>401</c>. Списки, кэш профилей и id пользователя очищаются, поколения сдвигаются:
    /// ответы, которые ещё летят, не вернут чужие записи на экран.
    /// </remarks>
    public void ApplySignedOutState()
    {
        IsSignedIn = false;
        _userId = null;
        _profiles.Clear();
        ShowProfilesWarning = false;
        Announcement = null;
        IsRefreshing = false;
        _tab = BookingsTab.Upcoming;

        ResetState(_upcoming);
        ResetState(_history);

        NotifyViewState();
    }

    /// <summary>
    /// Пересчитывает время, отсчёт и доступность переноса во всех строках.
    /// </summary>
    /// <remarks>
    /// Вызывается минутным таймером страницы: сеть при этом не трогается.
    /// </remarks>
    public void RecomputeTimes()
    {
        var now = Now();

        foreach (var item in EnumerateItems())
        {
            item.RecomputeTimes(now);
        }
    }

    /// <summary>
    /// Применяет результат переноса: новое время строки и баннер.
    /// </summary>
    /// <param name="item">Строка, которую перенесли.</param>
    /// <param name="slot">Слот, который подтвердил сервер.</param>
    public void ApplyRescheduled(BookingListItem item, BarberSlotResponse slot)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(slot);

        item.ApplyRescheduled(slot, Now());
        Announcement = BookingText.RescheduleAnnouncement(item.MomentText);

        MarkHistoryDirty();
        UpdateNextBadge();
    }

    /// <summary>
    /// Применяет результат отзыва: кнопка становится «Отзыв отправлен», история устаревает.
    /// </summary>
    /// <param name="item">Строка, по которой оставили отзыв.</param>
    public void ApplyReviewed(BookingListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.ApplyReviewed();
        Announcement = BookingText.ReviewSubmittedAnnouncement;

        MarkHistoryDirty();
    }

    /// <summary>
    /// Отменяет запись после подтверждения на странице.
    /// </summary>
    /// <remarks>
    /// Возвращает текст для нативного диалога либо <c>null</c>, когда всё получилось (или
    /// сессия истекла — тогда экран уже показал гейт). Повторное нажатие во время запроса
    /// игнорируется: строка помечена <see cref="BookingListItem.IsCancelling"/>.
    /// </remarks>
    /// <param name="item">Строка, которую отменяют.</param>
    /// <returns>Причина ошибки для показа либо <c>null</c>.</returns>
    public async Task<string?> CancelBookingAsync(BookingListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.IsCancelling || !item.CanCancel)
        {
            return null;
        }

        item.IsCancelling = true;
        _isActionRunning = true;

        try
        {
            await _bookingsApi.CancelBookingAsync(item.Id);

            RemoveItem(item);
            Announcement = BookingText.CancelAnnouncement(item.MomentText);
            MarkHistoryDirty();
            UpdateNextBadge();

            return null;
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();

            return null;
        }
        catch (BarberSearchApiException exception)
        {
            // 404 и 409 значат, что строка устарела: список перечитывается, а текст
            // остаётся тем же — пользователю нужен повтор, а не подробности.
            if (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
            {
                await RefreshActiveListAsync();
            }

            return DescribeFailure(exception, CancelFailedText);
        }
        catch (HttpRequestException)
        {
            return NetworkErrorText;
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            return NetworkErrorText;
        }
        finally
        {
            item.IsCancelling = false;
            _isActionRunning = false;
        }
    }

    /// <summary>
    /// Открывает экран переноса для строки.
    /// </summary>
    /// <param name="item">Строка списка.</param>
    [RelayCommand]
    private void RequestReschedule(BookingListItem? item)
    {
        if (item is null || !item.CanReschedule)
        {
            return;
        }

        RescheduleRequested?.Invoke(this, item);
    }

    /// <summary>
    /// Открывает экран отзыва для строки.
    /// </summary>
    /// <param name="item">Строка списка.</param>
    [RelayCommand]
    private void RequestReview(BookingListItem? item)
    {
        if (item is null || !item.CanReview)
        {
            return;
        }

        ReviewRequested?.Invoke(this, item);
    }

    /// <summary>
    /// Переключает на «Предстоящие»; грузит вкладку, только если она ещё не загружена.
    /// </summary>
    [RelayCommand]
    private async Task SelectUpcomingAsync()
    {
        if (_tab == BookingsTab.Upcoming)
        {
            return;
        }

        AbandonLoad(_history);

        _tab = BookingsTab.Upcoming;
        NotifyViewState();

        await EnsureActiveListLoadedAsync();
    }

    /// <summary>
    /// Переключает на «Историю»; первая загрузка и грязный список читаются, остальное — нет.
    /// </summary>
    [RelayCommand]
    private async Task SelectHistoryAsync()
    {
        if (_tab == BookingsTab.History)
        {
            return;
        }

        AbandonLoad(_upcoming);

        _tab = BookingsTab.History;
        NotifyViewState();

        await EnsureActiveListLoadedAsync();
    }

    /// <summary>
    /// Отбрасывает ответ, который ещё летит для скрытой вкладки.
    /// </summary>
    /// <remarks>
    /// Смена вкладки — это и есть «список больше не показывается»: поколение сдвигается, иначе
    /// запоздавший ответ уже скрытого списка всплыл бы на экране и перебил более свежие данные.
    /// Индикаторы сбрасываются здесь же: <c>finally</c> загрузки их уже не тронет — она сверяет
    /// поколение и намеренно молчит.
    /// </remarks>
    private static void AbandonLoad(BookingListState state)
    {
        state.Generation++;
        state.IsFirstLoading = false;
        state.IsLoadingMore = false;
    }

    /// <summary>
    /// Обновляет первую страницу активной вкладки свайпом.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsSignedIn || _isActionRunning)
        {
            IsRefreshing = false;

            return;
        }

        if (_userId is null && !await LoadUserIdAsync(CancellationToken.None))
        {
            IsRefreshing = false;

            return;
        }

        IsRefreshing = true;

        try
        {
            await LoadFirstPageAsync(ActiveState, FirstPageMode.Silent, CancellationToken.None);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Догружает следующую страницу активной вкладки.
    /// </summary>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        var state = ActiveState;

        if (!IsSignedIn
            || _isActionRunning
            || _userId is null
            || state.IsFirstLoading
            || state.IsLoadingMore
            || state.NextCursor is null)
        {
            return;
        }

        var userId = _userId.Value;

        state.IsLoadingMore = true;
        state.LoadMoreError = null;
        NotifyViewState();

        var generation = state.Generation;

        try
        {
            var page = await _bookingsApi.GetBookingsAsync(
                state.Scope,
                userId,
                state.NextCursor,
                PageSize);

            if (generation != state.Generation)
            {
                return;
            }

            AppendItems(state, page.Items);
            state.NextCursor = page.NextCursor;

            await ResolveProfilesAsync(page.Items.Select(booking => booking.BarberProfileId));
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();
        }
        catch (BarberSearchApiException)
        {
            state.LoadMoreError = LoadMoreErrorText;
        }
        catch (HttpRequestException)
        {
            state.LoadMoreError = LoadMoreErrorText;
        }
        catch (OperationCanceledException)
        {
            state.LoadMoreError = LoadMoreErrorText;
        }
        finally
        {
            state.IsLoadingMore = false;
            NotifyViewState();
        }
    }

    /// <summary>
    /// Повторяет первую загрузку активной вкладки после ошибки.
    /// </summary>
    [RelayCommand]
    private async Task RetryAsync()
    {
        if (!IsSignedIn)
        {
            return;
        }

        if (_userId is null && !await LoadUserIdAsync(CancellationToken.None))
        {
            return;
        }

        var state = ActiveState;
        state.Error = null;
        NotifyViewState();

        await LoadFirstPageAsync(state, FirstPageMode.Initial, CancellationToken.None);
    }

    /// <summary>
    /// Повторяет догрузку после ошибки, не трогая уже загруженные строки.
    /// </summary>
    [RelayCommand]
    private async Task RetryLoadMoreAsync()
    {
        ActiveState.LoadMoreError = null;
        NotifyViewState();

        await LoadMoreAsync();
    }

    /// <summary>
    /// Закрывает баннер о результате действия.
    /// </summary>
    [RelayCommand]
    private void DismissAnnouncement() => Announcement = null;

    /// <summary>
    /// Просит страницу открыть вход.
    /// </summary>
    [RelayCommand]
    private void SignIn() => SignInRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Просит страницу вернуться к поиску.
    /// </summary>
    [RelayCommand]
    private void Search() => SearchRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Грузит активную вкладку, если она ещё ни разу не загружалась или помечена грязной.
    /// </summary>
    private async Task EnsureActiveListLoadedAsync()
    {
        if (!IsSignedIn || _isActionRunning)
        {
            return;
        }

        var state = ActiveState;

        if (state.IsFirstLoading || (state.HasLoaded && !state.IsDirty))
        {
            return;
        }

        if (_userId is null && !await LoadUserIdAsync(CancellationToken.None))
        {
            return;
        }

        await LoadFirstPageAsync(
            state,
            state.Groups.Count == 0 ? FirstPageMode.Initial : FirstPageMode.Silent,
            CancellationToken.None);
    }

    /// <summary>
    /// Читает первую страницу списка и заменяет его содержимое.
    /// </summary>
    /// <remarks>
    /// Поколение списка сдвигается на старте: ответ, пришедший после смены вкладки или
    /// обновления, отбрасывается. Ошибка тихого обновления не стирает уже загруженные строки.
    /// </remarks>
    private async Task LoadFirstPageAsync(
        BookingListState state,
        FirstPageMode mode,
        CancellationToken cancellationToken)
    {
        if (_userId is null)
        {
            return;
        }

        var userId = _userId.Value;

        state.Generation++;
        var generation = state.Generation;

        state.Error = null;
        state.LoadMoreError = null;

        if (mode == FirstPageMode.Initial)
        {
            state.IsFirstLoading = true;
        }

        NotifyViewState();

        try
        {
            var page = await _bookingsApi.GetBookingsAsync(
                state.Scope,
                userId,
                cursor: null,
                PageSize,
                cancellationToken);

            if (generation != state.Generation)
            {
                return;
            }

            ReplaceItems(state, page.Items);
            state.NextCursor = page.NextCursor;
            state.HasLoaded = true;
            state.IsDirty = false;
            state.IsFirstLoading = false;

            NotifyViewState();

            await ResolveProfilesAsync(page.Items.Select(booking => booking.BarberProfileId));
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();
        }
        catch (BarberSearchApiException exception)
        {
            SetPageFailure(state, generation, DescribeFailure(exception, LoadErrorText));
        }
        catch (HttpRequestException)
        {
            SetPageFailure(state, generation, NetworkErrorText);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetPageFailure(state, generation, NetworkErrorText);
        }
        catch (OperationCanceledException)
        {
            // Отмена страницей — не ошибка.
        }
        finally
        {
            if (generation == state.Generation)
            {
                state.IsFirstLoading = false;
                NotifyViewState();
            }
        }
    }

    /// <summary>
    /// Показывает ошибку страницы, но не стирает уже показанные строки.
    /// </summary>
    private void SetPageFailure(BookingListState state, int generation, string message)
    {
        if (generation != state.Generation)
        {
            return;
        }

        state.IsFirstLoading = false;

        if (state.Groups.Count == 0)
        {
            state.Error = message;
        }

        NotifyViewState();
    }

    /// <summary>
    /// Заменяет строки списка страницей с сервера и пересобирает группы месяцев.
    /// </summary>
    private void ReplaceItems(BookingListState state, IReadOnlyList<BookingResponse> bookings)
    {
        state.Groups.Clear();

        foreach (var booking in bookings)
        {
            var item = new BookingListItem(booking, Now());
            ApplyCachedProfile(item);
            AppendItem(state, item);
        }

        UpdateNextBadge();
    }

    /// <summary>
    /// Дополняет список следующей страницей: существующая группа месяца не дублируется.
    /// </summary>
    private void AppendItems(BookingListState state, IReadOnlyList<BookingResponse> bookings)
    {
        foreach (var booking in bookings)
        {
            var item = new BookingListItem(booking, Now());
            ApplyCachedProfile(item);
            AppendItem(state, item);
        }

        UpdateNextBadge();
    }

    /// <summary>
    /// Кладёт строку в группу её месяца, создавая группу только при переходе на новый месяц.
    /// </summary>
    private static void AppendItem(BookingListState state, BookingListItem item)
    {
        foreach (var group in state.Groups)
        {
            if (group.Key == item.MonthKey)
            {
                group.Add(item);

                return;
            }
        }

        var created = new BookingMonthGroup(item.MonthKey, item.MonthTitle);
        created.Add(item);
        state.Groups.Add(created);
    }

    /// <summary>
    /// Убирает строку из списка и удаляет опустевшую группу.
    /// </summary>
    private void RemoveItem(BookingListItem item)
    {
        foreach (var group in _upcoming.Groups.Concat(_history.Groups).ToList())
        {
            if (!group.Remove(item))
            {
                continue;
            }

            if (group.Count == 0)
            {
                _upcoming.Groups.Remove(group);
                _history.Groups.Remove(group);
            }

            break;
        }

        NotifyViewState();
    }

    /// <summary>
    /// Переносит бейдж «Ближайшая запись» на первую строку «Предстоящих».
    /// </summary>
    private void UpdateNextBadge()
    {
        BookingListItem? first = _upcoming.Groups.Count > 0 && _upcoming.Groups[0].Count > 0
            ? _upcoming.Groups[0][0]
            : null;

        foreach (var item in _upcoming.Groups.SelectMany(group => group))
        {
            item.IsNext = ReferenceEquals(item, first);
        }
    }

    /// <summary>
    /// Помечает историю устаревшей: она перечитается при следующем открытии вкладки.
    /// </summary>
    private void MarkHistoryDirty() => _history.IsDirty = true;

    /// <summary>
    /// Перечитывает первую страницу активной вкладки, сохраняя строки до ответа.
    /// </summary>
    private async Task RefreshActiveListAsync()
    {
        if (_userId is null)
        {
            return;
        }

        await LoadFirstPageAsync(ActiveState, FirstPageMode.Silent, CancellationToken.None);
    }

    /// <summary>
    /// Спрашивает <c>/me</c> один раз: идентификатор нужен для <c>userId</c> в списке.
    /// </summary>
    /// <returns><c>true</c>, если пользователь известен.</returns>
    private async Task<bool> LoadUserIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            var me = await _accountApi.GetMeAsync(cancellationToken);

            _userId = me.Id;

            return true;
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();

            return false;
        }
        catch (BarberSearchApiException exception)
        {
            ActiveState.Error = DescribeFailure(exception, LoadErrorText);
            NotifyViewState();

            return false;
        }
        catch (HttpRequestException)
        {
            ActiveState.Error = NetworkErrorText;
            NotifyViewState();

            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ActiveState.Error = NetworkErrorText;
            NotifyViewState();

            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Загружает карточки незакэшированных барберов, не больше четырёх одновременно.
    /// </summary>
    /// <remarks>
    /// Один упавший профиль не ломает список: строка остаётся с подписями-заглушками,
    /// а под списком появляется сноска. Повторно тот же барбер не запрашивается — в том
    /// числе после ошибки, иначе один мёртвый профиль тормозил бы каждую страницу.
    /// </remarks>
    private async Task ResolveProfilesAsync(IEnumerable<Guid> barberProfileIds)
    {
        var missing = barberProfileIds
            .Distinct()
            .Where(id => !_profiles.ContainsKey(id))
            .ToList();

        foreach (var chunk in missing.Chunk(MaxParallelProfiles))
        {
            await Task.WhenAll(chunk.Select(FetchProfileAsync));
        }
    }

    /// <summary>
    /// Загружает одну карточку барбера и применяет её к строкам обеих вкладок.
    /// </summary>
    private async Task FetchProfileAsync(Guid barberProfileId)
    {
        try
        {
            var profile = await _barberApi.GetBarberProfileAsync(barberProfileId);

            _profiles[barberProfileId] = profile;
            ApplyProfile(barberProfileId, profile);
        }
        catch (BarberSearchApiException)
        {
            RecordProfileFailure(barberProfileId);
        }
        catch (HttpRequestException)
        {
            RecordProfileFailure(barberProfileId);
        }
        catch (OperationCanceledException)
        {
            RecordProfileFailure(barberProfileId);
        }
        catch (InvalidOperationException)
        {
            // Пустой ответ сервера: для экрана это та же недоступная карточка.
            RecordProfileFailure(barberProfileId);
        }
    }

    /// <summary>
    /// Запоминает, что карточку получить не удалось, и показывает сноску под списком.
    /// </summary>
    private void RecordProfileFailure(Guid barberProfileId)
    {
        _profiles[barberProfileId] = null;
        ShowProfilesWarning = true;

        ApplyProfile(barberProfileId, null);
    }

    /// <summary>
    /// Применяет карточку (или её отсутствие) ко всем строкам этого барбера.
    /// </summary>
    /// <remarks>
    /// Пояс салона приходит только с карточкой, а заголовок месяца считается в поясе салона.
    /// Поэтому после ответа проверяется, не переехала ли строка в соседний месяц: визит
    /// в 22:30 UTC 30 ноября — это уже 1 декабря в Москве. Пересобираются только группы с
    /// несовпавшим ключом, обычный ответ карточки список не трогает.
    /// </remarks>
    private void ApplyProfile(Guid barberProfileId, BarberProfileResponse? profile)
    {
        var applied = false;

        foreach (var item in EnumerateItems())
        {
            if (item.BarberProfileId == barberProfileId)
            {
                item.ApplyProfile(profile);
                applied = true;
            }
        }

        if (!applied)
        {
            return;
        }

        RegroupIfNeeded(_upcoming);
        RegroupIfNeeded(_history);
        UpdateNextBadge();
    }

    /// <summary>
    /// Пересобирает группы месяцев, если пояс салона перенёс строку в другой месяц.
    /// </summary>
    private static void RegroupIfNeeded(BookingListState state)
    {
        if (!state.Groups.Any(group => group.Any(item => group.Key != item.MonthKey)))
        {
            return;
        }

        var items = state.Groups.SelectMany(group => group).ToList();

        state.Groups.Clear();

        foreach (var item in items)
        {
            AppendItem(state, item);
        }
    }

    /// <summary>
    /// Применяет уже загруженную карточку к новой строке, не ходя в сеть.
    /// </summary>
    private void ApplyCachedProfile(BookingListItem item)
    {
        if (_profiles.TryGetValue(item.BarberProfileId, out var profile))
        {
            item.ApplyProfile(profile);
        }
    }

    /// <summary>
    /// Перебирает строки обеих вкладок.
    /// </summary>
    private IEnumerable<BookingListItem> EnumerateItems() =>
        _upcoming.Groups.SelectMany(group => group).Concat(_history.Groups.SelectMany(group => group));

    /// <summary>
    /// Гасит сессию и возвращает гейт: токен мёртв, дальше requests бессмысленны.
    /// </summary>
    private async Task EndSessionAsync()
    {
        ApplySignedOutState();

        await _session.ClearAsync(CancellationToken.None);

        IsSessionExpired = true;
    }

    /// <summary>
    /// Возвращает список в исходное состояние, сдвигая поколение.
    /// </summary>
    private static void ResetState(BookingListState state)
    {
        state.Generation++;
        state.Groups.Clear();
        state.NextCursor = null;
        state.HasLoaded = false;
        state.IsDirty = false;
        state.IsFirstLoading = false;
        state.IsLoadingMore = false;
        state.Error = null;
        state.LoadMoreError = null;
    }

    /// <summary>
    /// Переводит сбой API в текст, не показывая английский <c>detail</c>.
    /// </summary>
    private static string DescribeFailure(BarberSearchApiException exception, string fallback) =>
        exception.StatusCode == HttpStatusCode.TooManyRequests ? TooManyRequestsText : fallback;

    /// <summary>
    /// Текущий момент по часам приложения.
    /// </summary>
    private DateTimeOffset Now() => _timeProvider.GetUtcNow();

    /// <summary>
    /// Поднимает уведомления обо всех вычисляемых свойствах экрана.
    /// </summary>
    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(ActiveGroups));
        OnPropertyChanged(nameof(IsUpcomingSelected));
        OnPropertyChanged(nameof(IsHistorySelected));
        OnPropertyChanged(nameof(ShowGate));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        OnPropertyChanged(nameof(IsLoadingMore));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(ShowLoadMoreError));
    }

    /// <summary>
    /// Вкладки экрана «Записи».
    /// </summary>
    private enum BookingsTab
    {
        Upcoming,
        History,
    }

    /// <summary>
    /// Режим загрузки первой страницы: показать индикатор или обновить молча.
    /// </summary>
    private enum FirstPageMode
    {
        Initial,
        Silent,
    }

    /// <summary>
    /// Состояние одной вкладки: строки, курсор, ошибки и поколение ответов.
    /// </summary>
    private sealed class BookingListState(BookingScope scope, ObservableCollection<BookingMonthGroup> groups)
    {
        /// <summary>
        /// Скоуп запроса сервера.
        /// </summary>
        public BookingScope Scope { get; } = scope;

        /// <summary>
        /// Группы месяцев этой вкладки.
        /// </summary>
        public ObservableCollection<BookingMonthGroup> Groups { get; } = groups;

        /// <summary>
        /// Курсор следующей страницы; <c>null</c> — страниц больше нет.
        /// </summary>
        public string? NextCursor { get; set; }

        /// <summary>
        /// Первая страница уже загружалась.
        /// </summary>
        public bool HasLoaded { get; set; }

        /// <summary>
        /// Список устарел после действия и перечитается при открытии вкладки.
        /// </summary>
        public bool IsDirty { get; set; }

        /// <summary>
        /// Идёт первая загрузка: показывается индикатор, пустое состояние скрыто.
        /// </summary>
        public bool IsFirstLoading { get; set; }

        /// <summary>
        /// Идёт догрузка следующей страницы.
        /// </summary>
        public bool IsLoadingMore { get; set; }

        /// <summary>
        /// Ошибка первой страницы; <c>null</c>, когда ошибки нет.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Ошибка догрузки; <c>null</c>, когда догрузка прошла.
        /// </summary>
        public string? LoadMoreError { get; set; }

        /// <summary>
        /// Поколение ответов: старый ответ после смены вкладки или обновления отбрасывается.
        /// </summary>
        public int Generation { get; set; }
    }
}
