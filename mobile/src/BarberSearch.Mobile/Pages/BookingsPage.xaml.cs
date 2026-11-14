using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Вкладка «Записи»: предстоящие визиты и история, отмена, перенос и отзыв.
/// </summary>
/// <remarks>
/// <para>
/// Страница — singleton (см. <c>MauiProgram</c>): ViewModel создаётся ею один раз, поэтому
/// состояние вкладки переживает переключения между разделами. Подписки на сессию и события
/// ViewModel живут столько же, сколько приложение, и отписываться от них не нужно.
/// </para>
/// <para>
/// Минутный таймер считает обратный отсчёт и доступность переноса без сети: ViewModel этого
/// делать не может, потому что таймер — платформенная сущность MAUI. Навигация к экранам
/// переноса и отзыва тоже здесь: Core не знает ни про Shell, ни про маршруты.
/// </para>
/// </remarks>
public partial class BookingsPage : ContentPage
{
    private const int RecomputeIntervalSeconds = 60;

    private readonly BookingsViewModel _viewModel;

    private IDispatcherTimer? _timeTimer;

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

        BindingContext = viewModel;

        session.SessionEnded += OnSessionEnded;

        _viewModel.RescheduleRequested += OnRescheduleRequested;
        _viewModel.ReviewRequested += OnReviewRequested;
        _viewModel.SignInRequested += OnSignInRequested;
        _viewModel.SearchRequested += OnSearchRequested;
    }

    /// <summary>
    /// Запускает минутный таймер и тихо обновляет активную вкладку.
    /// </summary>
    /// <remarks>
    /// Обновление при каждом появлении — это и есть «попробовать ещё раз» после возврата с
    /// другого экрана; ViewModel сам решает, показывать индикатор или обновить молча.
    /// <c>async void</c> здесь — единственная допустимая форма переопределения.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _timeTimer ??= CreateTimeTimer();
        _timeTimer.Start();

        await _viewModel.InitializeAsync();
    }

    /// <summary>
    /// Останавливает таймер, пока вкладка не видна.
    /// </summary>
    protected override void OnDisappearing()
    {
        _timeTimer?.Stop();

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
            "Отменить запись?",
            "Отмена необратима. Вернуть запись нельзя — придётся записываться заново. "
                + "Время может занять другой клиент.",
            "Отменить запись",
            "Не отменять");

        if (!accepted)
        {
            return;
        }

        var error = await _viewModel.CancelBookingAsync(item);

        if (error is not null)
        {
            await DisplayAlertAsync("Отмена записи", error, "Понятно");
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
    /// Создаёт таймер пересчёта времени.
    /// </summary>
    private IDispatcherTimer CreateTimeTimer()
    {
        var timer = Dispatcher.CreateTimer();

        timer.Interval = TimeSpan.FromSeconds(RecomputeIntervalSeconds);
        timer.Tick += OnTimeTimerTick;

        return timer;
    }
}
