using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Мои отзывы»: список, правка и удаление.
/// </summary>
/// <remarks>
/// <para>
/// Страница живёт один визит: открывается поверх стека «Профиля» и закрывается возвратом.
/// Подписка на <see cref="TokenSession.SessionEnded"/> снимается при уходе со страницы,
/// чтобы singleton-сессия не держала её экземпляр.
/// </para>
/// <para>
/// Подтверждение удаления — нативный диалог: тексты повторяют веб, а иконок и модальных
/// окон в MAUI нет. Ошибку, после которой строка остаётся на месте, показывает тоже
/// страница: ViewModel возвращает текст, чтобы не заводить собственных формулировок
/// для <c>429</c> и сети.
/// </para>
/// </remarks>
public partial class MyReviewsPage : ContentPage
{
    /// <summary>
    /// Маршрут экрана в Shell.
    /// </summary>
    public const string Route = "my-reviews";

    private readonly MyReviewsViewModel _viewModel;
    private readonly TokenSession _session;

    private bool _isNavigating;

    /// <summary>
    /// Создаёт экран отзывов и подписывается на запрос правки.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    /// <param name="session">Сессия, чьё завершение возвращает на вкладку «Профиль».</param>
    public MyReviewsPage(MyReviewsViewModel viewModel, TokenSession session)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(session);

        InitializeComponent();

        _viewModel = viewModel;
        _session = session;

        BindingContext = viewModel;

        _viewModel.EditRequested += OnEditRequested;
    }

    /// <summary>
    /// Читает список при первом появлении и подписывается на завершение сессии.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _session.SessionEnded -= OnSessionEnded;
        _session.SessionEnded += OnSessionEnded;

        await _viewModel.InitializeAsync();

        if (!_viewModel.IsAuthenticated)
        {
            await ReturnToProfileAsync();
        }
    }

    /// <summary>
    /// Снимает подписку, пока страница не на экране.
    /// </summary>
    protected override void OnDisappearing()
    {
        _session.SessionEnded -= OnSessionEnded;

        base.OnDisappearing();
    }

    /// <summary>
    /// Открывает карточку барбера по строке отзыва.
    /// </summary>
    private async void OnBarberClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not MyReviewListItem item)
        {
            return;
        }

        await Shell.Current.GoToAsync(string.Concat(BarberPage.Route, "?id=", item.BarberId));
    }

    /// <summary>
    /// Открывает экран правки для выбранной строки.
    /// </summary>
    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is MyReviewListItem item)
        {
            _viewModel.EditCommand.Execute(item);
        }
    }

    /// <summary>
    /// Удаляет отзыв после нативного подтверждения.
    /// </summary>
    /// <remarks>
    /// Диалог — две строки: «Удаление необратимо.» и через пустую строку объяснение, что
    /// по этой записи можно оставить новый отзыв. Ошибка, после которой строка остаётся,
    /// показывается вторым диалогом, чтобы человек видел, что ничего не удалилось.
    /// </remarks>
    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not MyReviewListItem item)
        {
            return;
        }

        var accepted = await DisplayAlertAsync(
            "Удалить отзыв?",
            "Удаление необратимо.\n\nОтзыв исчезнет навсегда, восстановить его нельзя. "
                + "По этой записи можно будет оставить новый отзыв.",
            "Удалить отзыв",
            "Не удалять");

        if (!accepted)
        {
            return;
        }

        var error = await _viewModel.DeleteAsync(item);

        if (error is not null)
        {
            await DisplayAlertAsync("Удаление отзыва", error, "Понятно");
        }
    }

    /// <summary>
    /// Уводит к «Записям» из пустого состояния.
    /// </summary>
    private async void OnGoToBookingsClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//bookings");
    }

    /// <summary>
    /// Открывает страницу правки и передаёт ей строку и её ViewModel.
    /// </summary>
    /// <remarks>
    /// Данные передаются объектами, а не идентификатором: отдельного
    /// <c>GET api/reviews/{id}</c> нет, а строка уже содержит всё для формы.
    /// </remarks>
    private async void OnEditRequested(object? sender, MyReviewListItem item)
    {
        await Shell.Current.GoToAsync(
            EditReviewPage.Route,
            new Dictionary<string, object>
            {
                ["review"] = item,
                ["owner"] = _viewModel,
            });
    }

    /// <summary>
    /// Возвращает на вкладку «Профиль» после завершения сессии.
    /// </summary>
    private void OnSessionEnded(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(async () => await ReturnToProfileAsync());

    /// <summary>
    /// Закрывает стек кабинета и открывает вкладку «Профиль».
    /// </summary>
    private async Task ReturnToProfileAsync()
    {
        if (_isNavigating)
        {
            return;
        }

        _isNavigating = true;

        try
        {
            await Shell.Current.GoToAsync("//profile");
        }
        finally
        {
            _isNavigating = false;
        }
    }
}
