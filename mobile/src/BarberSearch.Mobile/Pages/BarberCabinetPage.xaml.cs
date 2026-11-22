using System.ComponentModel;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Barber;
using BarberSearch.Mobile.Theming;
using BarberSearch.Mobile.Views.Barber;
using BarberBookingsSectionView = BarberSearch.Mobile.Views.Barber.BarberBookingsView;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Кабинет мастера: шапка с колокольчиком, четыре раздела и гейт сессии.
/// </summary>
/// <remarks>
/// <para>
/// Кабинет — не вкладка, а экран поверх стека «Профиля»: роль мастера редкая, и пятая
/// вкладка занимала бы место у всех остальных. Разделы переключаются внутри страницы,
/// поэтому маршрут у кабинета один, а у четырёх разделов — один общий <c>ContentView</c>
/// вместо четырёх страниц: разделу не нужен свой жизненный цикл, его сообщает страница.
/// </para>
/// <para>
/// Список разделов — единственное место, куда добавляется раздел: запись пары
/// «ключ подписи» и «фабрика <c>ContentView</c>». Сегменты навигации строятся из того же
/// списка, поэтому новый раздел появляется одной строкой. Экземпляры создаются из DI при
/// открытии гейта и живут до конца страницы: переключение — это только <c>IsVisible</c>.
/// </para>
/// <para>
/// Жизненный цикл повторяет §5 передачи среза: первый проход — <c>InitializeAsync</c>
/// гейта, возврат на уже готовый гейт — тихий <c>ReloadAsync</c>; затем
/// <c>Initialize(profileId)</c> и <c>ActivateAsync</c> активного раздела. Уход и
/// <c>Window.Stopped</c> — <c>Deactivate</c>. Shell не поднимает <c>OnDisappearing</c>
/// при push, поэтому перед переходом на дочерний экран страница гаснет явно — вместе с
/// видимостью и таймерами, — а возврат снова показывает её.
/// </para>
/// <para>
/// Таймеры живут здесь, потому что время — платформенная сущность MAUI. Тридцатисекундный
/// таймер тихо перечитывает «Записи» и только их: он молчит, пока раздел не активен, пока
/// страница не видна, пока идёт мутация и пока на экране диалог причины отмены. Минутный
/// таймер обновляет счётчик непрочитанных; его сбой тихий по построению ViewModel.
/// </para>
/// </remarks>
public partial class BarberCabinetPage : ContentPage
{
    /// <summary>
    /// Интервал тихого обновления «Записей».
    /// </summary>
    private const int BookingsAutoRefreshSeconds = 30;

    /// <summary>
    /// Интервал обновления счётчика непрочитанных.
    /// </summary>
    private const int UnreadRefreshSeconds = 60;

    /// <summary>
    /// Индекс раздела «Записи»: только он обновляется по таймеру.
    /// </summary>
    private const int BookingsSectionIndex = 0;

    /// <summary>
    /// Индекс раздела «Расписание»: из «Салонов» к нему переходит выбор места.
    /// </summary>
    private const int ScheduleSectionIndex = 1;

    private readonly BarberCabinetViewModel _viewModel;
    private readonly SectionEntry[] _sections;
    private readonly List<ContentView?> _sectionViews;
    private readonly List<Button> _segmentButtons = [];

    private int _activeIndex;
    private int? _requestedSectionIndex;
    private bool _isVisible;
    private bool _isActivating;
    private bool _isActivationPending;
    private int _deactivateVersion;
    private bool _isShellNavigatedSubscribed;
    private Window? _window;
    private IDispatcherTimer? _bookingsTimer;
    private IDispatcherTimer? _unreadTimer;

    /// <summary>
    /// Создаёт кабинет и строит сегменты навигации из списка разделов.
    /// </summary>
    /// <param name="viewModel">Гейт сессии, роли и профиля, плюс счётчик непрочитанных.</param>
    /// <param name="services">Контейнер, из которого лениво резолвятся разделы.</param>
    public BarberCabinetPage(BarberCabinetViewModel viewModel, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(services);

        InitializeComponent();

        _viewModel = viewModel;

        // Одна запись — один раздел: подпись сегмента и фабрика содержимого. Список —
        // единственное место, куда добавляется раздел: «Салоны» и «Профиль» разрешаются
        // из DI так же, как «Записи» и «Расписание», а не собираются здесь.
        // Имя раздела совпадает с Core-перечислением BarberBookingsView (вид списка),
        // поэтому тип взят через псевдоним — это разные сущности.
        _sections =
        [
            new SectionEntry("Записи", () => services.GetRequiredService<BarberBookingsSectionView>()),
            new SectionEntry("Расписание", () => services.GetRequiredService<BarberScheduleView>()),
            new SectionEntry("Салоны", () => CreateSalonsView(services)),
            new SectionEntry("Профиль", () => services.GetRequiredService<BarberProfileView>()),
        ];

        _sectionViews = new List<ContentView?>(_sections.Length);

        for (var index = 0; index < _sections.Length; index++)
        {
            _sectionViews.Add(null);
        }

        BindingContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        BuildSegments();
    }

    /// <summary>
    /// Активный раздел; <c>null</c>, пока Views не созданы.
    /// </summary>
    private IBarberCabinetSection? ActiveSection =>
        _sectionViews[_activeIndex]?.BindingContext as IBarberCabinetSection;

    /// <summary>
    /// Раздел «Записи»: его таймеру нужно знать, открыт ли диалог причины отмены.
    /// </summary>
    private BarberBookingsSectionView? BookingsSection =>
        _sectionViews[BookingsSectionIndex] as BarberBookingsSectionView;

    /// <summary>
    /// Появление страницы: гейт, затем первый раздел.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>async void</c> — единственная допустимая форма переопределения. Автоповтор гейта
    /// включается до чтения: сбой, пришедший из него, уже может его запустить.
    /// </para>
    /// <para>
    /// Shell поднимает <c>Appearing</c> и после возврата с дочернего экрана. Готовый гейт
    /// в этом случае перечитывается тихо: <see cref="BarberCabinetViewModel.InitializeAsync"/>
    /// сбросил бы <c>IsReady</c>, и открытые разделы на мгновение исчезли бы. Первый проход
    /// по-прежнему идёт через <c>InitializeAsync</c>.
    /// </para>
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _isVisible = true;

        SubscribeWindowLifecycle();
        SubscribeShellNavigated();

        StartTimers();

        _viewModel.StartAutoRetry();

        if (_viewModel.IsReady)
        {
            await _viewModel.ReloadAsync();
        }
        else
        {
            await _viewModel.InitializeAsync();
        }

        if (_viewModel.IsReady)
        {
            InitializeSections();
        }

        await ApplyRequestedSectionAsync(TakeRequestedSection());

        UpdateTimers();
    }

    /// <summary>
    /// Уход со страницы: раздел, таймеры и автоповтор гаснут, данные остаются.
    /// </summary>
    protected override void OnDisappearing()
    {
        SuspendActivity();

        UnsubscribeWindowLifecycle();
        UnsubscribeShellNavigated();

        base.OnDisappearing();
    }

    /// <summary>
    /// Строит сегменты навигации из списка разделов: подпись, 44 pt и равная доля ширины.
    /// </summary>
    private void BuildSegments()
    {
        SegmentBar.ColumnDefinitions.Clear();
        SegmentBar.Children.Clear();
        _segmentButtons.Clear();

        for (var index = 0; index < _sections.Length; index++)
        {
            SegmentBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var button = new Button
            {
                Text = Loc.T(_sections[index].TitleKey),
                CommandParameter = index,
                Style = ResolveStyle("SegmentButton"),
            };

            button.Clicked += OnSegmentClicked;

            Grid.SetColumn(button, index);

            SegmentBar.Children.Add(button);
            _segmentButtons.Add(button);
        }

        UpdateSegmentStyles();
    }

    /// <summary>
    /// Переключает раздел: гасит прежний, показывает новый и активирует его.
    /// </summary>
    /// <param name="index">Индекс раздела в списке.</param>
    /// <returns>Задача активации.</returns>
    private async Task SelectSectionAsync(int index)
    {
        if (index < 0 || index >= _sections.Length || index == _activeIndex)
        {
            return;
        }

        DeactivateActive();

        _activeIndex = index;

        UpdateSectionVisibility();
        UpdateSegmentStyles();
        UpdateTimers();

        if (!_viewModel.IsReady)
        {
            return;
        }

        InitializeSections();

        await ActivateActiveAsync();
    }

    /// <summary>
    /// Создаёт разделы из DI при первом открытии гейта и привязывает их к профилю.
    /// </summary>
    /// <remarks>
    /// Привязка выполняется только при смене профиля: контракт раздела обещает
    /// идемпотентность, но «Профиль» внутри <c>Initialize</c> очищает карточку, и
    /// повторный вызов с тем же профилем сбросил бы показанное в первый скелетон.
    /// Поэтому метод можно звать после каждого перечитывания <c>/me</c> — в том числе
    /// после онбординга, когда профиль появился.
    /// </remarks>
    private void InitializeSections()
    {
        if (_viewModel.BarberProfileId is not { } profileId)
        {
            return;
        }

        EnsureSectionViews();

        foreach (var view in _sectionViews)
        {
            // Повторная привязка к тому же профилю ничего не сбрасывает: «Профиль» при
            // каждом вызове очищает карточку и показал бы первый скелетон вместо тихого
            // обновления. Смена профиля (выход, другой аккаунт, онбординг) — новый id,
            // и раздел инициализируется заново.
            if (view?.BindingContext is IBarberCabinetSection section
                && section.BarberProfileId != profileId)
            {
                section.Initialize(profileId);
            }
        }

        UpdateSectionVisibility();
        UpdateSegmentStyles();
    }

    /// <summary>
    /// Создаёт недостающие Views и добавляет их в общую ячейку.
    /// </summary>
    private void EnsureSectionViews()
    {
        for (var index = 0; index < _sections.Length; index++)
        {
            if (_sectionViews[index] is { } existing)
            {
                existing.IsVisible = index == _activeIndex;

                continue;
            }

            var view = _sections[index].CreateView();

            view.IsVisible = index == _activeIndex;

            _sectionViews[index] = view;

            SectionsHost.Children.Add(view);
        }
    }

    /// <summary>
    /// Активирует активный раздел; вызов во время активации повторяет её после текущей.
    /// </summary>
    /// <remarks>
    /// Пока идёт чтение, человек может выбрать другой раздел, а инбокс уведомлений —
    /// попросить «Записи». Такой вызов не отбрасывается: он запоминается, и если за время
    /// чтения активным стал другой раздел или прежний был погашен, активация повторяется.
    /// Повтор того же раздела после непрерывного чтения не нужен: оно уже идёт.
    /// </remarks>
    /// <returns>Задача чтения раздела.</returns>
    private async Task ActivateActiveAsync()
    {
        if (!_viewModel.IsReady)
        {
            return;
        }

        if (_isActivating)
        {
            _isActivationPending = true;

            return;
        }

        _isActivating = true;

        try
        {
            int activatedIndex;
            int activatedVersion;

            do
            {
                _isActivationPending = false;

                activatedIndex = _activeIndex;
                activatedVersion = _deactivateVersion;

                InitializeSections();

                if (ActiveSection is { } section)
                {
                    await section.ActivateAsync();
                }
            }
            while (_isActivationPending
                && (activatedIndex != _activeIndex || activatedVersion != _deactivateVersion));
        }
        finally
        {
            _isActivating = false;
        }
    }

    /// <summary>
    /// Активирует раздел, который попросил дочерний экран, либо текущий.
    /// </summary>
    /// <param name="requestedIndex">Индекс раздела из просьбы; <c>null</c> — просьбы нет.</param>
    /// <returns>Задача чтения раздела.</returns>
    private async Task ApplyRequestedSectionAsync(int? requestedIndex)
    {
        if (requestedIndex is { } requested && requested != _activeIndex)
        {
            await SelectSectionAsync(requested);

            return;
        }

        await ActivateActiveAsync();
    }

    /// <summary>
    /// Забирает просьбу дочернего экрана: она относится к одному возврату.
    /// </summary>
    /// <returns>Индекс раздела либо <c>null</c>, если просьбы нет.</returns>
    private int? TakeRequestedSection()
    {
        var requested = _requestedSectionIndex;

        _requestedSectionIndex = null;

        return requested;
    }

    /// <summary>
    /// Гасит страницу: видимость, таймеры, активный раздел и автоповтор гейта.
    /// </summary>
    /// <remarks>
    /// Shell не поднимает <c>OnDisappearing</c> при push, поэтому те же действия нужны
    /// перед уходом на дочерний экран и по событию <c>Push</c> для переходов из разделов.
    /// </remarks>
    private void SuspendActivity()
    {
        _isVisible = false;

        StopTimers();
        DeactivateActive();

        _viewModel.StopAutoRetry();
    }

    /// <summary>
    /// Гасит активный раздел: автоповтор и таймеры раздела останавливаются.
    /// </summary>
    private void DeactivateActive()
    {
        if (ActiveSection is { } section)
        {
            _deactivateVersion++;

            section.Deactivate();
        }
    }

    /// <summary>
    /// Показывает ровно один раздел; остальные остаются в дереве, но невидимы.
    /// </summary>
    private void UpdateSectionVisibility()
    {
        for (var index = 0; index < _sectionViews.Count; index++)
        {
            if (_sectionViews[index] is { } view)
            {
                view.IsVisible = index == _activeIndex;
            }
        }
    }

    /// <summary>
    /// Красит выбранный сегмент стилем <c>SegmentButtonSelected</c>.
    /// </summary>
    private void UpdateSegmentStyles()
    {
        for (var index = 0; index < _segmentButtons.Count; index++)
        {
            SegmentedButtonStyle.Apply(_segmentButtons[index], index == _activeIndex);
        }
    }

    /// <summary>
    /// Создаёт таймеры страницы.
    /// </summary>
    private void StartTimers()
    {
        _bookingsTimer ??= CreateBookingsTimer();
        _unreadTimer ??= CreateUnreadTimer();

        UpdateTimers();
    }

    /// <summary>
    /// Останавливает оба таймера, не выбрасывая их.
    /// </summary>
    private void StopTimers()
    {
        _bookingsTimer?.Stop();
        _unreadTimer?.Stop();
    }

    /// <summary>
    /// Запускает или останавливает таймеры по состоянию страницы и раздела.
    /// </summary>
    private void UpdateTimers()
    {
        if (_bookingsTimer is { } bookingTimer)
        {
            if (ShouldAutoRefreshBookings())
            {
                bookingTimer.Start();
            }
            else
            {
                bookingTimer.Stop();
            }
        }

        if (_unreadTimer is { } unreadTimer)
        {
            if (_isVisible && _viewModel.IsReady)
            {
                unreadTimer.Start();
            }
            else
            {
                unreadTimer.Stop();
            }
        }
    }

    /// <summary>
    /// Тихое обновление «Записей» уместно: раздел активен, страница видна и нет диалога.
    /// </summary>
    /// <returns><c>true</c>, если таймер может тикать.</returns>
    private bool ShouldAutoRefreshBookings() =>
        _isVisible
        && _viewModel.IsReady
        && _activeIndex == BookingsSectionIndex
        && BookingsSection is { IsPromptOpen: false };

    /// <summary>
    /// Создаёт таймер тихого обновления «Записей».
    /// </summary>
    /// <returns>Таймер с тридцатисекундным интервалом.</returns>
    private IDispatcherTimer CreateBookingsTimer()
    {
        var timer = Dispatcher.CreateTimer();

        timer.Interval = TimeSpan.FromSeconds(BookingsAutoRefreshSeconds);
        timer.Tick += OnBookingsTimerTick;

        return timer;
    }

    /// <summary>
    /// Создаёт таймер счётчика непрочитанных.
    /// </summary>
    /// <returns>Таймер с минутным интервалом.</returns>
    private IDispatcherTimer CreateUnreadTimer()
    {
        var timer = Dispatcher.CreateTimer();

        timer.Interval = TimeSpan.FromSeconds(UnreadRefreshSeconds);
        timer.Tick += OnUnreadTimerTick;

        return timer;
    }

    /// <summary>
    /// Тихо перечитывает «Записи» — но только когда это разрешено состоянием экрана.
    /// </summary>
    /// <remarks>
    /// Время пересчитывается до запроса: «Завершить» и «Неявка» появляются после начала
    /// визита, и тик — единственный момент, когда это видно без действия человека.
    /// </remarks>
    private async void OnBookingsTimerTick(object? sender, EventArgs e)
    {
        if (!ShouldAutoRefreshBookings() || BookingsSection?.ViewModel is not { } bookings)
        {
            return;
        }

        bookings.RecomputeTimes();

        await bookings.AutoRefreshAsync();
    }

    /// <summary>
    /// Тихо обновляет счётчик непрочитанных; сбой съедает сам ViewModel.
    /// </summary>
    private async void OnUnreadTimerTick(object? sender, EventArgs e)
    {
        if (!_isVisible || !_viewModel.IsReady)
        {
            return;
        }

        await _viewModel.RefreshUnreadAsync();
    }

    /// <summary>
    /// Переключает раздел по нажатию сегмента.
    /// </summary>
    private async void OnSegmentClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: int index })
        {
            return;
        }

        await SelectSectionAsync(index);
    }

    /// <summary>
    /// Возврат на форму входа в «Профиле»: у кабинета нет собственной формы входа.
    /// </summary>
    private async void OnSignInClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//profile");

    /// <summary>
    /// Открывает онбординг, когда роль есть, а профиля мастера ещё нет.
    /// </summary>
    private async void OnCreateProfileClicked(object? sender, EventArgs e)
    {
        SuspendActivity();

        await Shell.Current.GoToAsync(BarberCabinetRoutes.Onboarding);
    }

    /// <summary>
    /// Открывает инбокс уведомлений и передаёт ему профиль мастера.
    /// </summary>
    /// <remarks>
    /// Профиль нужен инбоксу, чтобы прочитать счётчик и список: собственного <c>/me</c>
    /// у него нет. Себя страница передаёт как <c>owner</c>, чтобы инбокс мог попросить
    /// «Записи» на возврате. Страница гаснет до перехода — Shell не поднимает
    /// <c>OnDisappearing</c> при push, и таймеры иначе тикали бы под чужой страницей.
    /// </remarks>
    private async void OnBellTapped(object? sender, EventArgs e)
    {
        // Инбокс читает список по профилю мастера: до готовности гейта профиля нет,
        // и открывать страницу, которая сразу закроется, незачем.
        if (_viewModel.BarberProfileId is not { } profileId)
        {
            return;
        }

        SuspendActivity();

        // Профиль нужен инбоксу для чтения, а сама страница — чтобы попросить «Записи»
        // на возврате: строка уведомления ведёт именно туда.
        await Shell.Current.GoToAsync(
            BarberCabinetRoutes.Notifications,
            new Dictionary<string, object>
            {
                ["owner"] = this,
                ["barberProfileId"] = profileId,
            });
    }

    /// <summary>
    /// Просит кабинет открыть раздел после возврата с дочернего экрана.
    /// </summary>
    /// <remarks>
    /// Нужен инбоксу уведомлений: нажатие на строку ведёт в «Записи», но открыть их сразу
    /// нельзя — между нажатием и возвратом экран уведомлений ещё показан, а кабинет
    /// применяет просьбу на возврате, когда гейт уже прочитан. Поэтому просьба
    /// запоминается здесь, а исполняется в <see cref="OnShellNavigated"/> или
    /// <see cref="OnAppearing"/>. Индекс вне списка игнорируется: разделов ровно столько,
    /// сколько в списке.
    /// </remarks>
    /// <param name="index">Индекс раздела в списке разделов кабинета.</param>
    public void RequestSectionAfterPop(int index)
    {
        if (index >= 0 && index < _sections.Length)
        {
            _requestedSectionIndex = index;
        }
    }

    /// <summary>
    /// Гейт пересчитался: открывает или гасит разделы вместе с ним.
    /// </summary>
    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BarberCabinetViewModel.IsReady))
        {
            return;
        }

        if (!_viewModel.IsReady)
        {
            DeactivateActive();
            UpdateTimers();

            return;
        }

        InitializeSections();
        UpdateTimers();

        await ApplyRequestedSectionAsync(TakeRequestedSection());
    }

    /// <summary>
    /// Переход Shell: уход на дочерний экран гасит страницу, возврат — снова показывает.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shell не поднимает <c>OnDisappearing</c> при push, поэтому видимость и таймеры
    /// снимаются здесь — в том числе для переходов из разделов. Иначе после возврата
    /// из фона под дочерним экраном 30-секундный и минутный таймеры продолжили бы тикать.
    /// </para>
    /// <para>
    /// На <c>Pop</c> страница снова видима, а гейт и раздел перечитывает
    /// <c>OnAppearing</c>, который Shell поднимает на этом же возврате. Просьба инбокса
    /// уведомлений исполняется один раз: она относится к конкретному возврату.
    /// </para>
    /// </remarks>
    private async void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        if (e.Source == ShellNavigationSource.Push)
        {
            SuspendActivity();

            return;
        }

        if (e.Source != ShellNavigationSource.Pop)
        {
            return;
        }

        _isVisible = true;

        StartTimers();

        _viewModel.StartAutoRetry();

        if (!_viewModel.IsReady)
        {
            // Гейт ещё читается: просьбу применит OnAppearing после его готовности.
            return;
        }

        await ApplyRequestedSectionAsync(TakeRequestedSection());
    }

    /// <summary>
    /// Подписывается на переход приложения в фон и обратно.
    /// </summary>
    private void SubscribeWindowLifecycle()
    {
        if (Window is not { } window)
        {
            return;
        }

        _window = window;

        window.Stopped -= OnWindowStopped;
        window.Stopped += OnWindowStopped;
        window.Resumed -= OnWindowResumed;
        window.Resumed += OnWindowResumed;
    }

    /// <summary>
    /// Снимает подписку на жизненный цикл окна.
    /// </summary>
    private void UnsubscribeWindowLifecycle()
    {
        if (_window is not { } window)
        {
            return;
        }

        window.Stopped -= OnWindowStopped;
        window.Resumed -= OnWindowResumed;

        _window = null;
    }

    /// <summary>
    /// Подписывается на навигацию Shell: возврат с дочернего экрана.
    /// </summary>
    private void SubscribeShellNavigated()
    {
        if (_isShellNavigatedSubscribed || Shell.Current is not { } shell)
        {
            return;
        }

        shell.Navigated += OnShellNavigated;
        _isShellNavigatedSubscribed = true;
    }

    /// <summary>
    /// Снимает подписку на навигацию Shell.
    /// </summary>
    private void UnsubscribeShellNavigated()
    {
        if (!_isShellNavigatedSubscribed || Shell.Current is not { } shell)
        {
            return;
        }

        shell.Navigated -= OnShellNavigated;
        _isShellNavigatedSubscribed = false;
    }

    /// <summary>
    /// Приложение ушло в фон: раздел и таймеры гаснут, сеть не опрашивается.
    /// </summary>
    private void OnWindowStopped(object? sender, EventArgs e)
    {
        StopTimers();
        DeactivateActive();
        _viewModel.StopAutoRetry();
    }

    /// <summary>
    /// Приложение снова на экране: таймеры возвращаются, раздел активируется заново.
    /// </summary>
    private async void OnWindowResumed(object? sender, EventArgs e)
    {
        if (!_isVisible)
        {
            return;
        }

        StartTimers();

        _viewModel.StartAutoRetry();

        await _viewModel.RefreshUnreadAsync();

        await ActivateActiveAsync();
    }

    /// <summary>
    /// Создаёт раздел «Салоны» и подписывается на его переходы: к расписанию салона,
    /// к форме создания собственного места и к форме добавления из каталога.
    /// </summary>
    /// <remarks>
    /// Подписка живёт столько же, сколько раздел: он создаётся один раз и до конца
    /// страницы, поэтому отписка не нужна — событие уходит вместе с экземпляром.
    /// </remarks>
    /// <param name="services">Контейнер, из которого резолвится раздел.</param>
    /// <returns>Раздел «Салоны».</returns>
    private BarberSalonsView CreateSalonsView(IServiceProvider services)
    {
        var view = services.GetRequiredService<BarberSalonsView>();

        view.ScheduleRequested += OnSalonsScheduleRequested;
        view.CreateSalonRequested += OnSalonsCreateRequested;
        view.AddSalonRequested += OnSalonsAddRequested;

        return view;
    }

    /// <summary>
    /// Переход из «Салонов» к расписанию выбранного салона.
    /// </summary>
    /// <remarks>
    /// Сначала выбирается салон: раздел расписания запоминает выбор даже до первого
    /// чтения, поэтому неважно, открывали ли его раньше. Затем страница переключает
    /// раздел — это её работа, а не событие раздела.
    /// </remarks>
    /// <param name="sender">Раздел «Салоны».</param>
    /// <param name="salonId">Салон, который нужно показать.</param>
    private async void OnSalonsScheduleRequested(object? sender, Guid salonId)
    {
        if (_sectionViews[ScheduleSectionIndex]?.BindingContext is BarberScheduleViewModel schedule)
        {
            schedule.SelectSalon(salonId);
        }

        await SelectSectionAsync(ScheduleSectionIndex);
    }

    /// <summary>
    /// Открывает форму создания салона и передаёт ей раздел «Салоны» владельцем.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Владелец нужен, чтобы форма объявила результат в баннере раздела и закрылась:
    /// «Салон отправлен на проверку.» или «Место приёма создано.» увидит человек на
    /// возврате. Профиль мастера передаётся рядом — форма без него закрывается сама.
    /// </para>
    /// <para>
    /// <c>ActivateAsync</c> здесь не зовётся: кабинет перечитывает активный раздел на Pop
    /// (<c>OnAppearing</c> → <c>ApplyRequestedSectionAsync</c> → <c>ActivateActiveAsync</c>),
    /// и второй вызов был бы вторым запросом за одно возвращение.
    /// </para>
    /// </remarks>
    /// <param name="sender">Раздел «Салоны».</param>
    /// <param name="e">Аргументы события раздела.</param>
    private async void OnSalonsCreateRequested(object? sender, EventArgs e)
    {
        if (sender is not BarberSalonsView { ViewModel.BarberProfileId: { } profileId } view)
        {
            return;
        }

        SuspendActivity();

        await Shell.Current.GoToAsync(
            BarberCabinetRoutes.CreateSalon,
            new Dictionary<string, object>
            {
                ["owner"] = view.ViewModel,
                ["barberProfileId"] = profileId,
            });
    }

    /// <summary>
    /// Открывает форму добавления салона из каталога и передаёт ей раздел владельцем.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Владелец нужен, чтобы форма объявила результат в баннере раздела и закрылась:
    /// «Салон добавлен.» увидит человек на возврате. Профиль мастера и признак «есть
    /// салоны» форма берёт у владельца, поэтому отдельных параметров маршрута нет.
    /// </para>
    /// <para>
    /// <c>ActivateAsync</c> здесь не зовётся: кабинет перечитывает активный раздел на Pop
    /// (<c>OnAppearing</c> → <c>ApplyRequestedSectionAsync</c> → <c>ActivateActiveAsync</c>),
    /// и второй вызов был бы вторым запросом за одно возвращение.
    /// </para>
    /// </remarks>
    /// <param name="sender">Раздел «Салоны».</param>
    /// <param name="e">Аргументы события раздела.</param>
    private async void OnSalonsAddRequested(object? sender, EventArgs e)
    {
        if (sender is not BarberSalonsView { ViewModel.BarberProfileId: not null } view)
        {
            return;
        }

        SuspendActivity();

        await Shell.Current.GoToAsync(
            BarberCabinetRoutes.AddSalon,
            new Dictionary<string, object>
            {
                ["owner"] = view.ViewModel,
            });
    }

    /// <summary>
    /// Достаёт стиль сегмента из ресурсов приложения.
    /// </summary>
    /// <param name="key">Ключ стиля в <c>Styles.xaml</c>.</param>
    /// <returns>Стиль или <c>null</c>, если ресурс не найден.</returns>
    private static Style? ResolveStyle(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true
            && value is Style style
                ? style
                : null;

    /// <summary>
    /// Один раздел кабинета: подпись сегмента и фабрика содержимого.
    /// </summary>
    /// <param name="TitleKey">Русская строка — ключ каталога подписи сегмента.</param>
    /// <param name="CreateView">Фабрика раздела; вызывается один раз.</param>
    private sealed record SectionEntry(string TitleKey, Func<ContentView> CreateView);
}
