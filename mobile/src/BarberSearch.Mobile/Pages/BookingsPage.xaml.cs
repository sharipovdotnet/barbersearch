using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Вкладка «Записи»: предстоящие визиты и история, отмена, перенос и отзыв.
/// </summary>
/// <remarks>
/// <para>
/// Страница создаётся заново вместе с Shell (смена языка пересобирает интерфейс), поэтому
/// подписка на singleton-сессию снимается при уходе со страницы. События ViewModel живут
/// столько же, сколько сама страница, — их источник ей и принадлежит.
/// </para>
/// <para>
/// Два таймера страницы: раз в минуту пересчитывается обратный отсчёт и доступность переноса
/// без сети, раз в 30 секунд первая страница активного списка перечитывается молча. ViewModel
/// этого делать не может, потому что таймер — платформенная сущность MAUI. Отдельной кнопки
/// «Обновить» нет: список обновляют таймер, свайп, появление вкладки и действия. Таймеры
/// останавливаются, когда вкладка уходит с экрана и когда приложение уходит в фон: сеть
/// в невидимом окне не опрашивается. Навигация к экранам переноса и отзыва тоже здесь: Core
/// не знает ни про Shell, ни про маршруты.
/// </para>
/// </remarks>
public partial class BookingsPage : ContentPage
{
    private const int RecomputeIntervalSeconds = 60;
    private const int AutoRefreshIntervalSeconds = 30;

    private readonly BookingsViewModel _viewModel;
    private readonly TokenSession _session;

    private IDispatcherTimer? _timeTimer;
    private IDispatcherTimer? _refreshTimer;
    private bool _isVisible;

    /// <summary>
    /// Создаёт вкладку и подписывается на сессию и события ViewModel.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    /// <param name="session">Сессия, чьё завершение возвращает гейт входа.</param>
    public BookingsPage(BookingsViewModel viewModel, TokenSession session)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(session);

        InitializeComponent();

        _viewModel = viewModel;
        _session = session;

        BindingContext = viewModel;

        _viewModel.RescheduleRequested += OnRescheduleRequested;
        _viewModel.ReviewRequested += OnReviewRequested;
        _viewModel.SignInRequested += OnSignInRequested;
        _viewModel.SearchRequested += OnSearchRequested;
    }

    /// <summary>
    /// Запускает таймеры отсчёта и тихого обновления и тихо обновляет активную вкладку.
    /// </summary>
    /// <remarks>
    /// Обновление при каждом появлении — это и есть «попробовать ещё раз» после возврата с
    /// другого экрана; ViewModel сам решает, показывать индикатор или обновить молча.
    /// <c>async void</c> здесь — единственная допустимая форма переопределения.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _isVisible = true;

        _session.SessionEnded -= OnSessionEnded;
        _session.SessionEnded += OnSessionEnded;

        SubscribeWindowLifecycle();
        StartTimers();

        await _viewModel.InitializeAsync();
    }

    /// <summary>
    /// Останавливает таймеры, пока вкладка не видна.
    /// </summary>
    protected override void OnDisappearing()
    {
        _isVisible = false;

        StopTimers();
        UnsubscribeWindowLifecycle();

        _session.SessionEnded -= OnSessionEnded;

        base.OnDisappearing();
    }

    /// <summary>
    /// Возвращает гейт входа, когда сессия завершилась без участия этого экрана.
    /// </summary>
    /// <remarks>
    /// <see cref="TokenSession.SessionEnded"/> приходит с сетевого потока, а ViewModel меняет
    /// привязанные свойства — переключение на UI-поток и есть работа страницы.
    /// </remarks>
    private void OnSessionEnded(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(_viewModel.ApplySignedOutState);

    /// <summary>
    /// Открывает экран переноса для выбранной строки.
    /// </summary>
    /// <remarks>
    /// Данные передаются объектами, а не идентификатором: <c>GET api/bookings/{id}</c> нет,
    /// а строка уже содержит и бронь, и загруженную карточку барбера.
    /// </remarks>
    private async void OnRescheduleRequested(object? sender, BookingListItem item)
    {
        await Shell.Current.GoToAsync(
            RescheduleBookingPage.Route,
            new Dictionary<string, object>
            {
                ["booking"] = item,
                ["owner"] = _viewModel,
            });
    }

    /// <summary>
    /// Открывает экран отзыва для выбранной строки.
    /// </summary>
    private async void OnReviewRequested(object? sender, BookingListItem item)
    {
        await Shell.Current.GoToAsync(
            ReviewBookingPage.Route,
            new Dictionary<string, object>
            {
                ["booking"] = item,
                ["owner"] = _viewModel,
            });
    }

    /// <summary>
    /// Уводит на вкладку «Профиль» вместо отдельной формы входа.
    /// </summary>
    private async void OnSignInRequested(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//profile");
    }

    /// <summary>
    /// Возвращает к поиску барбера.
    /// </summary>
    private async void OnSearchRequested(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//search");
    }

    /// <summary>
    /// Пересчитывает время, отсчёт и доступность переноса раз в минуту.
    /// </summary>
    private void OnTimeTimerTick(object? sender, EventArgs e) => _viewModel.RecomputeTimes();

    /// <summary>
    /// Тихо перечитывает активный список раз в 30 секунд.
    /// </summary>
    /// <remarks>
    /// ViewModel сам пропускает тик, когда список занят: идёт действие, свайп или загрузка.
    /// Ошибку обновления он тоже показывает сам, поэтому обработчику остаётся только вызов.
    /// </remarks>
    private async void OnRefreshTimerTick(object? sender, EventArgs e) =>
        await _viewModel.AutoRefreshAsync();

    /// <summary>
    /// Отменяет запись после нативного подтверждения.
    /// </summary>
    /// <remarks>
    /// Текст диалога повторяет веб: отмена необратима. Ошибку показывает ViewModel текстом,
    /// а страница лишь выводит её в том же нативном диалоге: так экран не придумывает своих
    /// формулировок для <c>429</c>, сети и остальных сбоев.
    /// </remarks>
    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not BookingListItem item)
        {
            return;
        }

        var accepted = await DisplayAlertAsync(
            Loc.T("Отменить запись?"),
            Loc.T(
                "Отмена необратима. Вернуть запись нельзя — придётся записываться заново. Время может занять другой клиент."),
            Loc.T("Отменить запись"),
            Loc.T("Не отменять"));

        if (!accepted)
        {
            return;
        }

        var error = await _viewModel.CancelBookingAsync(item);

        if (error is not null)
        {
            await DisplayAlertAsync(Loc.T("Отмена записи"), error, Loc.T("Понятно"));
        }
    }

    /// <summary>
    /// Просит ViewModel открыть экран переноса для строки карточки.
    /// </summary>
    private void OnRescheduleClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is BookingListItem item)
        {
            _viewModel.RequestRescheduleCommand.Execute(item);
        }
    }

    /// <summary>
    /// Просит ViewModel открыть экран отзыва для строки карточки.
    /// </summary>
    private void OnReviewClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is BookingListItem item)
        {
            _viewModel.RequestReviewCommand.Execute(item);
        }
    }

    /// <summary>
    /// Запускает таймеры отсчёта и тихого обновления.
    /// </summary>
    private void StartTimers()
    {
        _timeTimer ??= CreateTimeTimer();
        _timeTimer.Start();

        _refreshTimer ??= CreateRefreshTimer();
        _refreshTimer.Start();
    }

    /// <summary>
    /// Останавливает оба таймера, не выбрасывая их.
    /// </summary>
    private void StopTimers()
    {
        _timeTimer?.Stop();
        _refreshTimer?.Stop();
    }

    /// <summary>
    /// Подписывается на переход приложения в фон и обратно.
    /// </summary>
    /// <remarks>
    /// Уход приложения в фон не вызывает <see cref="OnDisappearing"/>: вкладка остаётся
    /// «появившейся», и без этой подписки таймер продолжал бы ходить в сеть из невидимого окна.
    /// </remarks>
    private void SubscribeWindowLifecycle()
    {
        if (Window is null)
        {
            return;
        }

        Window.Stopped -= OnWindowStopped;
        Window.Stopped += OnWindowStopped;
        Window.Resumed -= OnWindowResumed;
        Window.Resumed += OnWindowResumed;
    }

    /// <summary>
    /// Снимает подписку на жизненный цикл окна при уходе со вкладки.
    /// </summary>
    private void UnsubscribeWindowLifecycle()
    {
        if (Window is null)
        {
            return;
        }

        Window.Stopped -= OnWindowStopped;
        Window.Resumed -= OnWindowResumed;
    }

    /// <summary>
    /// Останавливает таймеры, когда приложение ушло в фон.
    /// </summary>
    private void OnWindowStopped(object? sender, EventArgs e) => StopTimers();

    /// <summary>
    /// Возвращает таймеры, когда приложение снова на экране, и пересчитывает время.
    /// </summary>
    /// <remarks>
    /// Проверка <see cref="_isVisible"/> закрывает случай, когда окно ожило уже после ухода
    /// с вкладки: подписка снимается в <see cref="OnDisappearing"/>, но событие могло быть
    /// поставлено в очередь раньше. Время пересчитывается сразу: пока приложение было в фоне,
    /// минутный таймер молчал, и отсчёт в строках иначе остался бы старым до следующего тика.
    /// </remarks>
    private void OnWindowResumed(object? sender, EventArgs e)
    {
        if (!_isVisible)
        {
            return;
        }

        StartTimers();
        _viewModel.RecomputeTimes();
    }

    /// <summary>
    /// Создаёт таймер пересчёта времени.
    /// </summary>
    private IDispatcherTimer CreateTimeTimer()
    {
        var timer = Dispatcher.CreateTimer();

        timer.Interval = TimeSpan.FromSeconds(RecomputeIntervalSeconds);
        timer.Tick += OnTimeTimerTick;

        return timer;
    }

    /// <summary>
    /// Создаёт таймер тихого обновления списка.
    /// </summary>
    private IDispatcherTimer CreateRefreshTimer()
    {
        var timer = Dispatcher.CreateTimer();

        timer.Interval = TimeSpan.FromSeconds(AutoRefreshIntervalSeconds);
        timer.Tick += OnRefreshTimerTick;

        return timer;
    }
}
