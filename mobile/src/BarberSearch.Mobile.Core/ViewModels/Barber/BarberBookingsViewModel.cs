using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Domain.Bookings;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Вид кабинета мастера: лента сегодняшнего дня, ближайшие записи или история.
/// </summary>
public enum BarberBookingsView
{
    /// <summary>
    /// «Сегодня»: визиты дня, собранные из первых страниц обоих списков.
    /// </summary>
    Today,

    /// <summary>
    /// «Ближайшие»: первый серверный скоуп.
    /// </summary>
    Upcoming,

    /// <summary>
    /// «История»: второй серверный скоуп.
    /// </summary>
    Past,
}

/// <summary>
/// Запрос на запись клиента в свободное окно дня.
/// </summary>
/// <param name="Date">Календарная дата дня в поясе устройства.</param>
/// <param name="StartAt">Начало окна — желаемое начало визита.</param>
public sealed record BarberGapRequest(DateOnly Date, DateTimeOffset StartAt);

/// <summary>
/// Раздел «Записи» кабинета мастера: два списка, лента дня и пять действий над визитом.
/// </summary>
/// <remarks>
/// <para>
/// Кабинет повторяет веб (<c>frontend/src/pages/barber-cabinet-page.tsx</c>): «Сегодня» —
/// не отдельный запрос, а слияние первых страниц «Ближайших» и «Истории» по календарной
/// дате устройства, с дедупликацией по идентификатору (строка из «Ближайших» свежее),
/// сортировкой по началу и окнами не короче двадцати минут. Поэтому переключение
/// «Сегодня → Ближайшие» не делает ни одного запроса.
/// </para>
/// <para>
/// Дата дня считается в поясе устройства, как в вебе: мастер смотрит свой телефон.
/// Это осознанное отличие от слота и времени визита, которые всегда в поясе салона:
/// календарь дня принадлежит человеку, а время визита — салону.
/// </para>
/// <para>
/// У каждого скоупа свой цикл автоповтора первого чтения: сбой «Ближайших» не должен
/// перечитывать историю, и наоборот. Оба цикла останавливаются в <see cref="Deactivate"/>:
/// страница кабинета уходит на дочерний экран, Shell не поднимает <c>OnDisappearing</c>,
/// и повтор без остановки ходил бы в сеть под чужой страницей.
/// </para>
/// <para>
/// Действия не оптимистичны: сервер отвечает <c>204</c> без тела, и строка применяет
/// результат только после успеха. <c>409</c> показывает причину в строке — «Запись уже
/// изменилась — обновите список.»; список перечитывается свайпом-обновлением
/// (<c>RefreshView</c>/<c>RefreshCommand</c>) или тридцатисекундным обновлением, отдельной
/// кнопки для этого нет.
/// </para>
/// </remarks>
public sealed partial class BarberBookingsViewModel : ObservableObject, IBarberCabinetSection
{
    /// <summary>
    /// Размер страницы: совпадает с умолчанием сервера и веба.
    /// </summary>
    public const int PageSize = 20;

    /// <summary>
    /// Минимальное окно, которое стоит показывать: меньше — визуальный шум.
    /// </summary>
    public const int MinVisibleGapMinutes = 20;

    private static string LoadErrorText => Loc.T("Не удалось загрузить записи.");
    private static string LoadMoreErrorText => Loc.T("Не удалось загрузить ещё записи.");
    private static string ConfirmFailedText => Loc.T("Не удалось подтвердить запись.");
    private static string CompleteFailedText => Loc.T("Не удалось завершить визит.");
    private static string CancelFailedText => Loc.T("Не удалось отменить запись.");
    private static string NoShowFailedText => Loc.T("Не удалось отметить неявку.");
    private static string NoteFailedText => Loc.T("Не удалось сохранить заметку.");
    private static string PriceFailedText => Loc.T("Не удалось изменить цену визита.");

    private readonly IBarberBookingsApi _bookingsApi;
    private readonly TimeProvider _timeProvider;
    private readonly AutoRetryLoop _upcomingAutoRetry;
    private readonly AutoRetryLoop _pastAutoRetry;
    private readonly BookingListState _upcoming;
    private readonly BookingListState _past;

    private Guid? _barberProfileId;
    private bool _isVisible;
    private bool _isActivating;
    private bool _isAutoRefreshing;
    private int _isActionRunning;

    /// <summary>
    /// Снимок ленты дня: по нему строятся подпись, сводка и строка следующего клиента.
    /// </summary>
    private DaySnapshot _day = DaySnapshot.Empty;

    /// <summary>
    /// Создаёт раздел записей кабинета.
    /// </summary>
    /// <param name="bookingsApi">Список, действия и слоты переноса.</param>
    /// <param name="timeProvider">Часы: «сегодня», доступность действий и баннеры.</param>
    /// <param name="upcomingAutoRetry">
    /// Цикл повтора «Ближайших»; <c>null</c> — собственный цикл с обычным расписанием.
    /// </param>
    /// <param name="pastAutoRetry">
    /// Цикл повтора «Истории»; <c>null</c> — собственный цикл. Параметры необязательные
    /// и стоят последними, чтобы DI и тесты не зависели от их порядка.
    /// </param>
    public BarberBookingsViewModel(
        IBarberBookingsApi bookingsApi,
        TimeProvider timeProvider,
        AutoRetryLoop? upcomingAutoRetry = null,
        AutoRetryLoop? pastAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(bookingsApi);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _bookingsApi = bookingsApi;
        _timeProvider = timeProvider;

        _upcomingAutoRetry = upcomingAutoRetry ?? new AutoRetryLoop();
        _pastAutoRetry = pastAutoRetry ?? new AutoRetryLoop();
        _upcomingAutoRetry.StateChanged += OnAutoRetryStateChanged;
        _pastAutoRetry.StateChanged += OnAutoRetryStateChanged;

        _upcoming = new BookingListState("upcoming", isUpcoming: true);
        _past = new BookingListState("past", isUpcoming: false);
    }

    /// <summary>
    /// Мастер нажал «Записать клиента» в пустом дне или в шапке.
    /// </summary>
    public event EventHandler? CreateBookingRequested;

    /// <summary>
    /// Мастер нажал «Занять» в окне между визитами.
    /// </summary>
    public event EventHandler<BarberGapRequest>? FillGapRequested;

    /// <summary>
    /// Строки текущего вида; у «Сегодня» между визитами вставлены окна.
    /// </summary>
    public ObservableCollection<BarberBookingItem> Items { get; } = [];

    /// <summary>
    /// Идентификатор профиля мастера; <c>null</c> до инициализации.
    /// </summary>
    public Guid? BarberProfileId => _barberProfileId;

    /// <summary>
    /// Текущий вид раздела.
    /// </summary>
    [ObservableProperty]
    private BarberBookingsView _view = BarberBookingsView.Today;

    /// <summary>
    /// Идёт первое чтение обоих списков.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstLoading;

    /// <summary>
    /// Идёт обновление свайпом: индикатор держит <c>RefreshView</c>.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Цикл автоповтора сам перечитывает упавший список.
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Баннер о результате действия; <c>null</c>, когда баннера нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnnouncement))]
    private string? _announcement;

    /// <summary>
    /// Когда списки последний раз пришли с сервера.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedAtText))]
    private DateTimeOffset? _updatedAt;

    /// <summary>
    /// Виден ли «Сегодня».
    /// </summary>
    public bool IsToday => View == BarberBookingsView.Today;

    /// <summary>
    /// Видны ли «Ближайшие».
    /// </summary>
    public bool IsUpcoming => View == BarberBookingsView.Upcoming;

    /// <summary>
    /// Видна ли «История».
    /// </summary>
    public bool IsPast => View == BarberBookingsView.Past;

    /// <summary>
    /// Показывать действие «Записать клиента»: в «Истории» его нет — новая запись всё равно
    /// попадёт в «Ближайшие», а прошлое читают, а не дописывают.
    /// </summary>
    public bool ShowCreateBooking => !IsPast;

    /// <summary>
    /// Заголовок дня рисуется только на «Сегодня».
    /// </summary>
    public bool ShowDayHeader => IsToday;

    /// <summary>
    /// Заголовок дня: «Сегодня».
    /// </summary>
    public string DayHeadingText => BarberCabinetText.DayHeading(DeviceToday(_timeProvider.GetUtcNow()), DeviceToday(_timeProvider.GetUtcNow()));

    /// <summary>
    /// Подпись дня: «сб, 15 ноября · 2 визита · 1 окно».
    /// </summary>
    public string DaySubtitleText => _day.Subtitle;

    /// <summary>
    /// Сводка дня: «2 визита · 4 800 ₽».
    /// </summary>
    public string TodaySummaryText => _day.Summary;

    /// <summary>
    /// Строка о ближайшем клиенте; <c>null</c>, когда визитов впереди нет.
    /// </summary>
    public string? NextClientText => _day.NextClient;

    /// <summary>
    /// Есть ли строка о ближайшем клиенте: пустая строка не должна занимать место.
    /// </summary>
    public bool HasNextClient => !string.IsNullOrEmpty(NextClientText);

    /// <summary>
    /// В дне больше двадцати записей: показаны только первые.
    /// </summary>
    public bool ShowDayTruncated => _day.IsTruncated;

    /// <summary>
    /// Подсказка об усечённом дне.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Свойство связано XAML: статическое свойство сломало бы привязку.")]
    public string TodayTruncatedText => Loc.T("Показаны первые 20 записей дня.");

    /// <summary>
    /// Виден ли баннер о результате действия.
    /// </summary>
    public bool HasAnnouncement => !string.IsNullOrEmpty(Announcement);

    /// <summary>
    /// Подпись свежести списка: «Обновлено в 18:30».
    /// </summary>
    public string? UpdatedAtText => UpdatedAt is { } moment
        ? Loc.T("Обновлено в {0}", moment.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture))
        : null;

    /// <summary>
    /// Первое чтение ещё идёт: список и пустое состояние не показываются.
    /// </summary>
    public bool ShowFirstLoading => IsFirstLoading;

    /// <summary>
    /// Причина ошибки, которую надо показать; у дня с частичным успехом её нет.
    /// </summary>
    public string? ErrorMessage
    {
        get
        {
            if (IsToday)
            {
                return Items.Count > 0 ? null : _upcoming.Error ?? _past.Error;
            }

            return ActiveState.Error;
        }
    }

    /// <summary>
    /// Есть ли ошибка, которую надо показать.
    /// </summary>
    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Показывать карточку ошибки вместо списка.
    /// </summary>
    public bool ShowError => !IsFirstLoading && Items.Count == 0 && HasError;

    /// <summary>
    /// Показывать строки.
    /// </summary>
    public bool ShowList => !IsFirstLoading && Items.Count > 0;

    /// <summary>
    /// Показывать пустое состояние: чтение закончилось, ошибки нет и строк нет.
    /// </summary>
    public bool ShowEmpty => !IsFirstLoading
        && !HasError
        && HasActiveLoaded
        && Items.Count == 0;

    /// <summary>
    /// Заголовок пустого состояния текущего вида.
    /// </summary>
    public string EmptyTitle => View switch
    {
        BarberBookingsView.Today => Loc.T("На сегодня записей нет."),
        BarberBookingsView.Upcoming => Loc.T("Ближайших записей нет."),
        _ => Loc.T("История пуста."),
    };

    /// <summary>
    /// Объяснение пустого состояния текущего вида.
    /// </summary>
    public string EmptyDescription => View switch
    {
        BarberBookingsView.Today => Loc.T("Запишите клиента, если он придёт без записи."),
        BarberBookingsView.Upcoming => _past.FirstPageItems.Count > 0
            ? Loc.T("Прошлые визиты остались в истории — их можно открыть в соседней вкладке.")
            : Loc.T("Новые заявки появятся здесь, как только клиент запишется."),
        _ => string.Empty,
    };

    /// <summary>
    /// У «Ближайших» упало первое чтение.
    /// </summary>
    public bool HasUpcomingError => _upcoming.Error is not null;

    /// <summary>
    /// У «Истории» упало первое чтение.
    /// </summary>
    public bool HasPastError => _past.Error is not null;

    /// <summary>
    /// День показывает строки, но один из списков не обновился.
    /// </summary>
    public bool HasPartialError => IsToday
        && Items.Count > 0
        && (HasUpcomingError || HasPastError);

    /// <summary>
    /// Предупреждение о частичном сбое дня.
    /// </summary>
    public string? PartialErrorText => HasPartialError
        ? Loc.T("Не удалось обновить часть записей.")
        : null;

    /// <summary>
    /// Показывать пейджер: он есть только у списков, не у дня.
    /// </summary>
    public bool ShowPager => !IsToday && Items.Count > 0 && ErrorMessage is null;

    /// <summary>
    /// Подпись пейджера для экранного диктора.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Свойство связано XAML: статическое свойство сломало бы привязку.")]
    public string PageLabel => Loc.T("Страницы записей");

    /// <summary>
    /// Номер показанной страницы с единицы.
    /// </summary>
    public int PageNumber => ActiveState.PageIndex + 1;

    /// <summary>
    /// Подпись страницы: «Страница 2».
    /// </summary>
    public string PageText => Loc.T("Страница {0}", PageNumber);

    /// <summary>
    /// Сколько строк было на предыдущих страницах.
    /// </summary>
    public int Offset => ActiveState.PageIndex * PageSize;

    /// <summary>
    /// Есть ли предыдущая страница в кэше.
    /// </summary>
    public bool HasPrev => !IsToday && ActiveState.PageIndex > 0;

    /// <summary>
    /// Есть ли следующая страница — в кэше или на сервере.
    /// </summary>
    public bool HasNext => !IsToday && ActiveState.HasNext;

    /// <summary>
    /// Курсор следующей страницы текущего списка.
    /// </summary>
    public string? NextCursor => IsToday ? null : ActiveState.NextCursor;

    /// <summary>
    /// Идёт чтение следующей страницы.
    /// </summary>
    public bool IsLoadingPage => ActiveState.IsLoadingPage;

    /// <summary>
    /// Ошибка чтения следующей страницы: показанные строки остаются.
    /// </summary>
    public string? PageError => IsToday ? null : ActiveState.PageError;

    /// <summary>
    /// Завершается, когда оба цикла автоповтора останавливаются: тесты ждут его вместо пауз.
    /// </summary>
    internal Task AutoRetryCompletion => Task.WhenAll(
        _upcomingAutoRetry.Completion,
        _pastAutoRetry.Completion);

    /// <summary>
    /// Цикл повтора «Ближайших»: тестам нужен счётчик попыток.
    /// </summary>
    internal AutoRetryLoop UpcomingAutoRetry => _upcomingAutoRetry;

    /// <summary>
    /// Цикл повтора «Истории»: тестам нужен счётчик попыток.
    /// </summary>
    internal AutoRetryLoop PastAutoRetry => _pastAutoRetry;

    /// <summary>
    /// Ошибка первой страницы «Ближайших»: тестам нужен текст, экрану — только факт.
    /// </summary>
    internal string? UpcomingError => _upcoming.Error;

    /// <summary>
    /// Ошибка первой страницы «Истории».
    /// </summary>
    internal string? PastError => _past.Error;

    private BookingListState ActiveState =>
        View == BarberBookingsView.Past ? _past : _upcoming;

    private bool HasActiveLoaded => IsToday
        ? _upcoming.HasLoaded && _past.HasLoaded
        : ActiveState.HasLoaded;

    /// <summary>
    /// Привязывает раздел к профилю мастера; данные прошлого профиля стираются.
    /// </summary>
    /// <param name="barberProfileId">Профиль из текущей сессии.</param>
    public void Initialize(Guid barberProfileId)
    {
        if (_barberProfileId == barberProfileId)
        {
            return;
        }

        Deactivate();

        _barberProfileId = barberProfileId;
        View = BarberBookingsView.Today;
        _day = DaySnapshot.Empty;
        UpdatedAt = null;
        Announcement = null;
        IsFirstLoading = false;
        IsRefreshing = false;

        ResetState(_upcoming);
        ResetState(_past);

        Items.Clear();
        RebuildItems();
    }

    /// <summary>
    /// Показывает раздел: первое чтение обоих списков или тихое обновление.
    /// </summary>
    /// <remarks>
    /// Оба списка нужны всегда: «Сегодня» собирается из их первых страниц, поэтому
    /// открытие «Ближайших» не должно оставить день без истории. Повторный вызов
    /// идемпотентен, пока идёт первое чтение.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        _isVisible = true;

        if (_barberProfileId is null || _isActivating)
        {
            NotifyViewState();

            return;
        }

        _isActivating = true;

        var isFirstLoad = !_upcoming.HasLoaded || !_past.HasLoaded;

        if (isFirstLoad)
        {
            IsFirstLoading = true;
        }

        try
        {
            await LoadFirstPageAsync(
                _upcoming,
                _upcoming.HasLoaded ? FirstPageMode.Silent : FirstPageMode.Initial,
                cancellationToken);

            await LoadFirstPageAsync(
                _past,
                _past.HasLoaded ? FirstPageMode.Silent : FirstPageMode.Initial,
                cancellationToken);
        }
        finally
        {
            _isActivating = false;
            IsFirstLoading = false;
            NotifyViewState();
        }

        TryStartAutoRetry(_upcoming);
        TryStartAutoRetry(_past);
    }

    /// <summary>
    /// Скрывает раздел: останавливает оба цикла автоповтора, данные остаются.
    /// </summary>
    public void Deactivate()
    {
        _isVisible = false;

        _upcomingAutoRetry.Stop();
        _pastAutoRetry.Stop();

        IsAutoRetrying = false;
    }

    /// <summary>
    /// Тихо перечитывает день или активный список по таймеру страницы.
    /// </summary>
    /// <remarks>
    /// Вызывается каждые тридцать секунд, пока раздел виден. История не обновляется:
    /// прошедшие визиты меняются только действием, а действие и так перечитывает список.
    /// Обновление уступает первому чтению, свайпу, действию и циклам повтора; список,
    /// долистанный человеком, не трогается — тихий ответ отбросил бы его страницу.
    /// </remarks>
    /// <returns>Задача чтения.</returns>
    public async Task AutoRefreshAsync()
    {
        if (!_isVisible
            || View == BarberBookingsView.Past
            || IsFirstLoading
            || IsRefreshing
            || IsLoadingPage
            || _isAutoRefreshing
            || _isActionRunning > 0
            || _upcomingAutoRetry.IsRunning
            || _pastAutoRetry.IsRunning)
        {
            return;
        }

        if (!IsToday && ActiveState.PageIndex > 0)
        {
            return;
        }

        _isAutoRefreshing = true;

        try
        {
            if (IsToday)
            {
                await LoadFirstPageAsync(_upcoming, FirstPageMode.Silent, CancellationToken.None);
                await LoadFirstPageAsync(_past, FirstPageMode.Silent, CancellationToken.None);
            }
            else
            {
                await LoadFirstPageAsync(ActiveState, FirstPageMode.Silent, CancellationToken.None);
            }
        }
        finally
        {
            _isAutoRefreshing = false;
        }
    }

    /// <summary>
    /// Перечитывает активный вид: день — оба списка, иначе только выбранный.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (_barberProfileId is null)
        {
            return;
        }

        StopAutoRetryLoops();

        if (IsToday)
        {
            await LoadFirstPageAsync(
                _upcoming,
                _upcoming.HasLoaded ? FirstPageMode.Silent : FirstPageMode.Initial,
                cancellationToken);

            await LoadFirstPageAsync(
                _past,
                _past.HasLoaded ? FirstPageMode.Silent : FirstPageMode.Initial,
                cancellationToken);
        }
        else
        {
            await LoadFirstPageAsync(
                ActiveState,
                ActiveState.HasLoaded ? FirstPageMode.Silent : FirstPageMode.Initial,
                cancellationToken);
        }

        TryStartAutoRetry(_upcoming);
        TryStartAutoRetry(_past);
    }

    /// <summary>
    /// Свайп-обновление активного вида.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsRefreshing || IsFirstLoading || _isActionRunning > 0)
        {
            IsRefreshing = false;

            return;
        }

        IsRefreshing = true;

        try
        {
            await ReloadAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Повторяет упавшее первое чтение — сразу, без паузы.
    /// </summary>
    [RelayCommand]
    private async Task RetryAsync()
    {
        if (_barberProfileId is null)
        {
            return;
        }

        StopAutoRetryLoops();

        var retried = false;

        if (IsToday)
        {
            if (_upcoming.Error is not null || !_upcoming.HasLoaded)
            {
                _upcoming.Error = null;
                await LoadFirstPageAsync(_upcoming, FirstPageMode.Initial, CancellationToken.None);
                retried = true;
            }

            if (_past.Error is not null || !_past.HasLoaded)
            {
                _past.Error = null;
                await LoadFirstPageAsync(_past, FirstPageMode.Initial, CancellationToken.None);
                retried = true;
            }
        }
        else
        {
            var state = ActiveState;

            if (state.Error is not null || !state.HasLoaded)
            {
                state.Error = null;
                await LoadFirstPageAsync(state, FirstPageMode.Initial, CancellationToken.None);
                retried = true;
            }
        }

        if (!retried)
        {
            await ReloadAsync();
        }

        TryStartAutoRetry(_upcoming);
        TryStartAutoRetry(_past);
    }

    /// <summary>
    /// Переключает на «Сегодня» и возвращает оба списка на первые страницы из кэша.
    /// </summary>
    [RelayCommand]
    private async Task SelectTodayAsync() =>
        await SelectViewAsync(BarberBookingsView.Today);

    /// <summary>
    /// Переключает на «Ближайшие».
    /// </summary>
    [RelayCommand]
    private async Task SelectUpcomingAsync() =>
        await SelectViewAsync(BarberBookingsView.Upcoming);

    /// <summary>
    /// Переключает на «Историю».
    /// </summary>
    [RelayCommand]
    private async Task SelectPastAsync() =>
        await SelectViewAsync(BarberBookingsView.Past);

    /// <summary>
    /// Показывает следующую страницу: из кэша или с сервера.
    /// </summary>
    [RelayCommand]
    private async Task GoNextAsync() => await GoNextCoreAsync();

    /// <summary>
    /// Возвращает предыдущую страницу из кэша — без запроса.
    /// </summary>
    [RelayCommand]
    private void GoPrev()
    {
        var state = ActiveState;

        if (IsToday || state.PageIndex == 0)
        {
            return;
        }

        state.PageIndex--;
        RebuildItems();
        NotifyViewState();
    }

    /// <summary>
    /// Открывает форму записи клиента.
    /// </summary>
    [RelayCommand]
    private void CreateBooking() => CreateBookingRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Открывает форму записи с началом выбранного окна.
    /// </summary>
    /// <param name="gap">Строка окна.</param>
    [RelayCommand]
    private void FillGap(BarberBookingItem? gap)
    {
        if (gap is null || !gap.IsGap)
        {
            return;
        }

        FillGapRequested?.Invoke(
            this,
            new BarberGapRequest(DeviceToday(_timeProvider.GetUtcNow()), gap.StartAt));
    }

    /// <summary>
    /// Закрывает баннер о результате действия.
    /// </summary>
    [RelayCommand]
    private void DismissAnnouncement() => Announcement = null;

    /// <summary>
    /// Подтверждает запись.
    /// </summary>
    /// <param name="item">Строка записи.</param>
    /// <returns><c>true</c>, если сервер подтвердил действие.</returns>
    public async Task<bool> ConfirmAsync(BarberBookingItem item)
    {
        if (item is null || !item.CanConfirm || !TryBeginAction(item))
        {
            return false;
        }

        try
        {
            await _bookingsApi.ConfirmAsync(item.Id);

            item.ApplyConfirmed();
            Announcement = Loc.T("Запись подтверждена.");
            RebuildItems();

            return true;
        }
        catch (Exception exception)
        {
            SetActionFailure(item, exception, ConfirmFailedText);

            return false;
        }
        finally
        {
            EndAction(item);
        }
    }

    /// <summary>
    /// Завершает визит; терминальная строка уходит из «Ближайших».
    /// </summary>
    /// <param name="item">Строка записи.</param>
    /// <returns><c>true</c>, если сервер подтвердил действие.</returns>
    public async Task<bool> CompleteAsync(BarberBookingItem item)
    {
        if (item is null || !item.CanComplete || !TryBeginAction(item))
        {
            return false;
        }

        try
        {
            await _bookingsApi.CompleteAsync(item.Id);

            item.ApplyCompleted();
            RemoveTerminal(item);
            Announcement = Loc.T("Визит завершён.");
            RebuildItems();

            return true;
        }
        catch (Exception exception)
        {
            SetActionFailure(item, exception, CompleteFailedText);

            return false;
        }
        finally
        {
            EndAction(item);
        }
    }

    /// <summary>
    /// Отмечает неявку клиента.
    /// </summary>
    /// <param name="item">Строка записи.</param>
    /// <returns><c>true</c>, если сервер подтвердил действие.</returns>
    public async Task<bool> MarkNoShowAsync(BarberBookingItem item)
    {
        if (item is null || !item.CanNoShow || !TryBeginAction(item))
        {
            return false;
        }

        try
        {
            await _bookingsApi.MarkNoShowAsync(item.Id);

            item.ApplyNoShow();
            RemoveTerminal(item);
            Announcement = Loc.T("Отмечена неявка.");
            RebuildItems();

            return true;
        }
        catch (Exception exception)
        {
            SetActionFailure(item, exception, NoShowFailedText);

            return false;
        }
        finally
        {
            EndAction(item);
        }
    }

    /// <summary>
    /// Отменяет запись с причиной.
    /// </summary>
    /// <param name="item">Строка записи.</param>
    /// <param name="reason">Причина отмены; сервер требует непустую.</param>
    /// <returns><c>true</c>, если сервер подтвердил действие.</returns>
    public async Task<bool> CancelAsync(BarberBookingItem item, string reason)
    {
        if (item is null || !item.CanCancel || !TryBeginAction(item))
        {
            return false;
        }

        try
        {
            await _bookingsApi.CancelAsync(item.Id, reason);

            item.ApplyCancelled(reason);
            RemoveTerminal(item);
            Announcement = Loc.T("Запись отменена.");
            RebuildItems();

            return true;
        }
        catch (Exception exception)
        {
            SetActionFailure(item, exception, CancelFailedText);

            return false;
        }
        finally
        {
            EndAction(item);
        }
    }

    /// <summary>
    /// Сохраняет заметку мастера.
    /// </summary>
    /// <param name="item">Строка записи.</param>
    /// <param name="note">Текст заметки; пустая строка очищает её.</param>
    /// <returns><c>true</c>, если сервер сохранил заметку.</returns>
    public async Task<bool> SaveNoteAsync(BarberBookingItem item, string note)
    {
        if (item is null || !item.CanWriteNote || !TryBeginAction(item))
        {
            return false;
        }

        try
        {
            await _bookingsApi.SaveNoteAsync(item.Id, note);

            item.ApplyNote(note);
            Announcement = Loc.T("Заметка сохранена.");
            RebuildItems();

            return true;
        }
        catch (Exception exception)
        {
            SetActionFailure(item, exception, NoteFailedText);

            return false;
        }
        finally
        {
            EndAction(item);
        }
    }

    /// <summary>
    /// Меняет цену визита.
    /// </summary>
    /// <param name="item">Строка записи.</param>
    /// <param name="price">Новая цена.</param>
    /// <returns><c>true</c>, если сервер сохранил цену.</returns>
    public async Task<bool> ChangePriceAsync(BarberBookingItem item, decimal price)
    {
        if (item is null || !item.CanChangePrice || !TryBeginAction(item))
        {
            return false;
        }

        try
        {
            await _bookingsApi.ChangePriceAsync(item.Id, price);

            item.ApplyPrice(price);
            Announcement = Loc.T("Цена визита обновлена.");
            RebuildItems();

            return true;
        }
        catch (Exception exception)
        {
            SetActionFailure(item, exception, PriceFailedText);

            return false;
        }
        finally
        {
            EndAction(item);
        }
    }

    /// <summary>
    /// Применяет заметку, сохранённую отдельным экраном.
    /// </summary>
    /// <param name="item">Строка записи.</param>
    /// <param name="note">Сохранённый текст заметки.</param>
    public void ApplyNote(BarberBookingItem item, string note)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.ApplyNote(note);
        Announcement = Loc.T("Заметка сохранена.");
        RebuildItems();
    }

    /// <summary>
    /// Применяет перенос, подтверждённый экраном переноса.
    /// </summary>
    /// <param name="item">Строка записи.</param>
    /// <param name="slot">Слот, который подтвердил сервер.</param>
    public void ApplyRescheduled(BarberBookingItem item, BarberSlotResponse slot)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(slot);

        item.ApplyRescheduled(slot.StartAt, slot.EndAt);
        Announcement = BookingText.RescheduleAnnouncement(item.MomentText);
        RebuildItems();
    }

    /// <summary>
    /// Применяет только что созданную запись: баннер и перечитывание списков.
    /// </summary>
    /// <param name="name">Имя клиента для баннера.</param>
    /// <returns>Задача перечитывания.</returns>
    public async Task OnBookingCreated(string name)
    {
        Announcement = Loc.T("Клиент записан: {0}.", name);

        if (View == BarberBookingsView.Past)
        {
            View = BarberBookingsView.Today;
        }

        _upcoming.PageIndex = 0;
        _past.PageIndex = 0;

        await ReloadAsync();
    }

    /// <summary>
    /// Пересчитывает время и доступные действия во всех строках без сети.
    /// </summary>
    public void RecomputeTimes()
    {
        RebuildItems();
        NotifyViewState();
    }

    /// <summary>
    /// Читает первую страницу списка — обычным чтением или тихим повтором.
    /// </summary>
    /// <param name="state">Список.</param>
    /// <param name="mode">Показывать индикатор, обновлять молча или повторять.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private async Task<ApiFailureKind> LoadFirstPageAsync(
        BookingListState state,
        FirstPageMode mode,
        CancellationToken cancellationToken)
    {
        state.Generation++;
        var generation = state.Generation;

        if (mode != FirstPageMode.Retry)
        {
            state.Error = null;
            state.PageError = null;
            state.FailureKind = ApiFailureKind.None;
        }

        if (mode == FirstPageMode.Initial)
        {
            state.IsFirstLoading = true;
        }

        NotifyViewState();

        var kind = ApiFailureKind.None;

        try
        {
            var page = await _bookingsApi.GetAsync(
                state.Scope,
                cursor: null,
                PageSize,
                cancellationToken);

            if (generation != state.Generation)
            {
                return ApiFailureKind.None;
            }

            var items = page.Items
                .Select(booking => new BarberBookingItem(
                    booking,
                    state.IsUpcoming,
                    _timeProvider.GetUtcNow()))
                .ToList();

            state.ReplaceFirstPage(items, page.NextCursor);
            state.HasLoaded = true;
            state.Error = null;
            state.PageError = null;
            state.FailureKind = ApiFailureKind.None;

            UpdatedAt = _timeProvider.GetUtcNow();
            RebuildItems();
        }
        catch (Exception exception)
        {
            if (generation == state.Generation)
            {
                kind = ApiFailureClassifier.Classify(exception, cancellationToken);

                if (kind != ApiFailureKind.None)
                {
                    var message = BarberCabinetText.ReadError(exception, LoadErrorText);

                    // Тихий повтор не стирает уже показанные строки. Причина при этом
                    // остаётся видимой только у пустого списка: у списка со строками
                    // ошибка выглядела бы как поломка данных, которых он не касается.
                    if (state.Pages.Count == 0 || mode != FirstPageMode.Silent)
                    {
                        state.Error = message;
                    }

                    state.FailureKind = kind;
                }
            }
        }
        finally
        {
            if (generation == state.Generation)
            {
                state.IsFirstLoading = false;
                NotifyViewState();
            }
        }

        return kind;
    }

    /// <summary>
    /// Читает следующую страницу активного списка.
    /// </summary>
    private async Task GoNextCoreAsync()
    {
        var state = ActiveState;

        if (IsToday
            || IsFirstLoading
            || _isActionRunning > 0
            || state.IsLoadingPage
            || state.PageIndex + 1 >= state.Pages.Count && state.Pages.Count == 0)
        {
            return;
        }

        if (state.PageIndex + 1 < state.Pages.Count)
        {
            state.PageIndex++;
            RebuildItems();
            NotifyViewState();

            return;
        }

        var cursor = state.NextCursors.Count == 0 ? null : state.NextCursors[^1];

        if (cursor is null)
        {
            return;
        }

        state.IsLoadingPage = true;
        state.PageError = null;
        NotifyViewState();

        try
        {
            var page = await _bookingsApi.GetAsync(
                state.Scope,
                cursor,
                PageSize,
                CancellationToken.None);

            var items = page.Items
                .Select(booking => new BarberBookingItem(
                    booking,
                    state.IsUpcoming,
                    _timeProvider.GetUtcNow()))
                .ToList();

            state.AppendPage(items, cursor, page.NextCursor);
            state.PageIndex = state.Pages.Count - 1;

            UpdatedAt = _timeProvider.GetUtcNow();
            RebuildItems();
        }
        catch (Exception exception)
        {
            var kind = ApiFailureClassifier.Classify(exception);

            if (kind != ApiFailureKind.None)
            {
                state.PageError = BarberCabinetText.ReadError(exception, LoadMoreErrorText);
            }
        }
        finally
        {
            state.IsLoadingPage = false;
            NotifyViewState();
        }
    }

    /// <summary>
    /// Переключает вид; «Сегодня» возвращает оба списка на первые страницы из кэша.
    /// </summary>
    private async Task SelectViewAsync(BarberBookingsView view)
    {
        if (View == view)
        {
            return;
        }

        if (view == BarberBookingsView.Today)
        {
            _upcoming.PageIndex = 0;
            _past.PageIndex = 0;
        }

        View = view;
        Announcement = null;

        RebuildItems();
        NotifyViewState();

        var state = ActiveState;

        if (view != BarberBookingsView.Today && !state.HasLoaded && !state.IsFirstLoading)
        {
            await LoadFirstPageAsync(state, FirstPageMode.Initial, CancellationToken.None);
            TryStartAutoRetry(state);
        }
    }

    /// <summary>
    /// Начинает действие над строкой и берёт её под защиту от двойного нажатия.
    /// </summary>
    private bool TryBeginAction(BarberBookingItem? item)
    {
        if (item is null || item.IsGap || item.IsBusy || _barberProfileId is null)
        {
            return false;
        }

        item.IsBusy = true;
        item.SetActionError(null);

        Interlocked.Increment(ref _isActionRunning);

        return true;
    }

    /// <summary>
    /// Освобождает строку после действия.
    /// </summary>
    private void EndAction(BarberBookingItem item)
    {
        item.IsBusy = false;

        Interlocked.Decrement(ref _isActionRunning);
    }

    /// <summary>
    /// Переводит сбой действия в текст и ставит его строке.
    /// </summary>
    private static void SetActionFailure(
        BarberBookingItem item,
        Exception exception,
        string fallback)
    {
        item.SetActionError(BarberCabinetText.ActionError(exception, fallback));
    }

    /// <summary>
    /// Убирает терминальную строку из списков.
    /// </summary>
    /// <remarks>
    /// Строка из «Ближайших» уходит целиком: завершённая, отменённая и неявка живут
    /// в истории. У строки из истории статус меняется на месте, но копия в «Ближайших»
    /// (если она там была) удаляется — иначе день показал бы устаревший статус.
    /// </remarks>
    private void RemoveTerminal(BarberBookingItem item)
    {
        _upcoming.Remove(item.Id);

        if (item.IsUpcoming)
        {
            _past.Remove(item.Id);
        }
    }

    /// <summary>
    /// Сбрасывает состояние списка.
    /// </summary>
    private static void ResetState(BookingListState state)
    {
        state.Generation++;
        state.Pages.Clear();
        state.PageCursors.Clear();
        state.NextCursors.Clear();
        state.PageIndex = 0;
        state.Error = null;
        state.PageError = null;
        state.FailureKind = ApiFailureKind.None;
        state.HasLoaded = false;
        state.IsFirstLoading = false;
        state.IsLoadingPage = false;
    }

    /// <summary>
    /// Запускает цикл повтора списка, если сбой повторяемый и раздел виден.
    /// </summary>
    private void TryStartAutoRetry(BookingListState state)
    {
        if (!_isVisible
            || state.Error is null
            || !state.FailureKind.IsRetryable()
            || state.IsFirstLoading
            || IsFirstLoading)
        {
            return;
        }

        var loop = state.IsUpcoming ? _upcomingAutoRetry : _pastAutoRetry;

        loop.Start(cancellationToken => RetryFirstPageAsync(state, cancellationToken));
    }

    /// <summary>
    /// Одна попытка цикла: повторить именно упавший список.
    /// </summary>
    private Task<ApiFailureKind> RetryFirstPageAsync(
        BookingListState state,
        CancellationToken cancellationToken)
    {
        if (!_isVisible
            || state.Error is null
            || !state.FailureKind.IsRetryable())
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadFirstPageAsync(state, FirstPageMode.Retry, cancellationToken);
    }

    /// <summary>
    /// Останавливает оба цикла, не трогая признак видимости.
    /// </summary>
    private void StopAutoRetryLoops()
    {
        _upcomingAutoRetry.Stop();
        _pastAutoRetry.Stop();
    }

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием обоих циклов.
    /// </summary>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _upcomingAutoRetry.IsRunning || _pastAutoRetry.IsRunning;

    /// <summary>
    /// Пересобирает строки текущего вида и снимок дня.
    /// </summary>
    private void RebuildItems()
    {
        var now = _timeProvider.GetUtcNow();

        _day = BuildDaySnapshot(now);

        var source = IsToday ? _day.Items : ActiveState.CurrentItems;

        Items.Clear();

        foreach (var item in source)
        {
            item.UpdateNow(now);
            Items.Add(item);
        }

        NotifyViewState();
    }

    /// <summary>
    /// Собирает день: фильтр по дате устройства, дедупликация, сортировка и окна.
    /// </summary>
    private DaySnapshot BuildDaySnapshot(DateTimeOffset now)
    {
        var today = DeviceToday(now);
        var byId = new Dictionary<Guid, BarberBookingItem>();

        foreach (var booking in _past.FirstPageItems)
        {
            if (!booking.IsGap && IsDeviceDay(booking.StartAt, today))
            {
                byId[booking.Id] = booking;
            }
        }

        foreach (var booking in _upcoming.FirstPageItems)
        {
            if (!booking.IsGap && IsDeviceDay(booking.StartAt, today))
            {
                byId[booking.Id] = booking;
            }
        }

        var ordered = byId.Values
            .OrderBy(booking => booking.StartAt)
            .ThenBy(booking => booking.Id.ToString("D", CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .ToList();

        var gapBefore = new Dictionary<Guid, BarberBookingItem>();
        var gapCount = 0;

        for (var index = 1; index < ordered.Count; index++)
        {
            var previous = ordered[index - 1];
            var following = ordered[index];
            var minutes = (int)Math.Round(
                (following.StartAt - previous.EndAt).TotalMinutes,
                MidpointRounding.AwayFromZero);

            if (minutes < MinVisibleGapMinutes)
            {
                continue;
            }

            gapBefore[following.Id] = BarberBookingItem.Gap(
                previous.EndAt,
                following.StartAt,
                minutes,
                following.TimeZone);

            gapCount++;
        }

        var visible = new List<BarberBookingItem>(PageSize);

        for (var index = 0; index < ordered.Count && index < PageSize; index++)
        {
            var booking = ordered[index];

            if (gapBefore.TryGetValue(booking.Id, out var gap))
            {
                visible.Add(gap);
            }

            visible.Add(booking);
        }

        foreach (var booking in ordered)
        {
            booking.UpdateNow(now);
        }

        // Строка дня и бейдж строки — одно правило: отменённый визит следующим клиентом
        // не считается, даже если его время ещё не прошло.
        var next = ordered.FirstOrDefault(
            booking => booking.EndAt > now
                && (booking.Status is BookingStatus.Pending or BookingStatus.Confirmed));
        var revenue = ordered
            .Where(booking => booking.Status != BookingStatus.Cancelled)
            .Sum(booking => booking.Price);

        return new DaySnapshot(
            visible,
            BarberCabinetText.TodaySubtitle(today, ordered.Count, gapCount),
            BarberCabinetText.Summary(ordered.Count, revenue),
            next is null
                ? null
                : BarberCabinetText.NextClient(
                    next.StartAt,
                    next.EndAt,
                    now,
                    next.TimeZone),
            ordered.Count > PageSize);
    }

    /// <summary>
    /// Поднимает уведомления обо всех вычисляемых свойствах вида и состояния.
    /// </summary>
    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(IsUpcoming));
        OnPropertyChanged(nameof(IsPast));
        OnPropertyChanged(nameof(ShowCreateBooking));
        OnPropertyChanged(nameof(ShowDayHeader));
        OnPropertyChanged(nameof(DayHeadingText));
        OnPropertyChanged(nameof(DaySubtitleText));
        OnPropertyChanged(nameof(TodaySummaryText));
        OnPropertyChanged(nameof(NextClientText));
        OnPropertyChanged(nameof(HasNextClient));
        OnPropertyChanged(nameof(ShowDayTruncated));
        OnPropertyChanged(nameof(ShowFirstLoading));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        OnPropertyChanged(nameof(HasUpcomingError));
        OnPropertyChanged(nameof(HasPastError));
        OnPropertyChanged(nameof(HasPartialError));
        OnPropertyChanged(nameof(PartialErrorText));
        OnPropertyChanged(nameof(ShowPager));
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(PageNumber));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(Offset));
        OnPropertyChanged(nameof(HasPrev));
        OnPropertyChanged(nameof(HasNext));
        OnPropertyChanged(nameof(NextCursor));
        OnPropertyChanged(nameof(IsLoadingPage));
        OnPropertyChanged(nameof(PageError));
    }

    /// <summary>
    /// Сегодняшняя дата в поясе устройства.
    /// </summary>
    private static DateOnly DeviceToday(DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).DateTime);

    /// <summary>
    /// Момент попадает в заданную календарную дату устройства.
    /// </summary>
    private static bool IsDeviceDay(DateTimeOffset instant, DateOnly day) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.Local).DateTime) == day;

    /// <summary>
    /// Как читать первую страницу: с индикатором, молча или тихим повтором.
    /// </summary>
    private enum FirstPageMode
    {
        /// <summary>
        /// Первое чтение: показать индикатор.
        /// </summary>
        Initial,

        /// <summary>
        /// Тихое обновление: строки остаются до ответа.
        /// </summary>
        Silent,

        /// <summary>
        /// Попытка цикла: не стирать причину ошибки до ответа.
        /// </summary>
        Retry,
    }

    /// <summary>
    /// Снимок дня для подписей и списка.
    /// </summary>
    private sealed record DaySnapshot(
        IReadOnlyList<BarberBookingItem> Items,
        string Subtitle,
        string Summary,
        string? NextClient,
        bool IsTruncated)
    {
        /// <summary>
        /// Пустой день до первого чтения.
        /// </summary>
        public static DaySnapshot Empty { get; } = new([], string.Empty, string.Empty, null, false);
    }

    /// <summary>
    /// Состояние одного серверного списка: страницы, курсоры и ошибки.
    /// </summary>
    private sealed class BookingListState(string scope, bool isUpcoming)
    {
        public string Scope { get; } = scope;

        public bool IsUpcoming { get; } = isUpcoming;

        /// <summary>
        /// Закэшированные страницы: страница 0 — первая.
        /// </summary>
        public List<List<BarberBookingItem>> Pages { get; } = [];

        /// <summary>
        /// Курсор, которым запрошена каждая страница; у первой — <c>null</c>.
        /// </summary>
        public List<string?> PageCursors { get; } = [];

        /// <summary>
        /// Курсор следующей страницы каждого ответа.
        /// </summary>
        public List<string?> NextCursors { get; } = [];

        public int PageIndex { get; set; }

        public string? Error { get; set; }

        public string? PageError { get; set; }

        public ApiFailureKind FailureKind { get; set; } = ApiFailureKind.None;

        public bool HasLoaded { get; set; }

        public bool IsFirstLoading { get; set; }

        public bool IsLoadingPage { get; set; }

        public int Generation { get; set; }

        public IReadOnlyList<BarberBookingItem> CurrentItems =>
            Pages.Count == 0 || PageIndex >= Pages.Count ? [] : Pages[PageIndex];

        public List<BarberBookingItem> FirstPageItems =>
            Pages.Count == 0 ? [] : Pages[0];

        public string? NextCursor =>
            Pages.Count == 0 || PageIndex >= NextCursors.Count ? null : NextCursors[PageIndex];

        public bool HasNext =>
            PageIndex + 1 < Pages.Count
            || (Pages.Count > 0 && NextCursors[^1] is not null);

        /// <summary>
        /// Заменяет список первой страницей; долистанные страницы больше не нужны.
        /// </summary>
        public void ReplaceFirstPage(
            IReadOnlyList<BarberBookingItem> items,
            string? nextCursor)
        {
            Pages.Clear();
            PageCursors.Clear();
            NextCursors.Clear();

            Pages.Add([.. items]);
            PageCursors.Add(null);
            NextCursors.Add(nextCursor);
            PageIndex = 0;
        }

        /// <summary>
        /// Добавляет следующую страницу.
        /// </summary>
        public void AppendPage(
            IReadOnlyList<BarberBookingItem> items,
            string requestCursor,
            string? nextCursor)
        {
            Pages.Add([.. items]);
            PageCursors.Add(requestCursor);
            NextCursors.Add(nextCursor);
        }

        /// <summary>
        /// Удаляет строку из всех закэшированных страниц.
        /// </summary>
        public void Remove(Guid id)
        {
            foreach (var page in Pages)
            {
                page.RemoveAll(booking => booking.Id == id);
            }
        }
    }
}
