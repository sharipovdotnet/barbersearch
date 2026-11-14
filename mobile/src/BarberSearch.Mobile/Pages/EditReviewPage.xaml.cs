using BarberSearch.Contracts.Reviews.UpdateReview;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Изменить отзыв»: оценка, комментарий и сохранение.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI фабрикой маршрута, а строку списка и её ViewModel получает
/// объектами маршрута: эндпоинта «получить отзыв по id» нет. Успех применяется к строке
/// владельца здесь же — только список знает, как заменить статус и показать баннер.
/// </para>
/// <para>
/// Подписка на <see cref="TokenSession.SessionEnded"/> снимается при уходе со страницы,
/// чтобы singleton-сессия не держала её экземпляр.
/// </para>
/// </remarks>
public partial class EditReviewPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут экрана в Shell.
    /// </summary>
    public const string Route = "review-edit";

    private readonly EditReviewViewModel _viewModel;
    private readonly TokenSession _session;

    private MyReviewListItem? _item;
    private MyReviewsViewModel? _owner;
    private bool _queryApplied;
    private bool _isNavigating;

    /// <summary>
    /// Создаёт экран правки отзыва.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    /// <param name="session">Сессия, чьё завершение возвращает на вкладку «Профиль».</param>
    public EditReviewPage(EditReviewViewModel viewModel, TokenSession session)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(session);

        InitializeComponent();

        _viewModel = viewModel;
        _session = session;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Принимает строку списка и её ViewModel.
    /// </summary>
    /// <remarks>
    /// Без любого из двух параметров заполнять форму нечем: страница сразу закрывается,
    /// вместо того чтобы править неизвестно какой отзыв.
    /// </remarks>
    /// <param name="query">Параметры маршрута.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (_queryApplied)
        {
            return;
        }

        if (!query.TryGetValue("review", out var reviewValue)
            || reviewValue is not MyReviewListItem item
            || !query.TryGetValue("owner", out var ownerValue)
            || ownerValue is not MyReviewsViewModel owner)
        {
            Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));

            return;
        }

        _item = item;
        _owner = owner;
        _queryApplied = true;

        _viewModel.Initialize(item);
        _viewModel.Saved += OnSaved;
    }

    /// <summary>
    /// Подписывается на завершение сессии при появлении экрана.
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        _session.SessionEnded -= OnSessionEnded;
        _session.SessionEnded += OnSessionEnded;
    }

    /// <summary>
    /// Снимает подписки, пока страница не на экране.
    /// </summary>
    protected override void OnDisappearing()
    {
        _session.SessionEnded -= OnSessionEnded;

        base.OnDisappearing();
    }

    /// <summary>
    /// Применяет ответ к строке владельца и закрывает экран.
    /// </summary>
    private void OnSaved(object? sender, UpdateReviewResponse response)
    {
        var item = _item;
        var owner = _owner;

        if (item is null || owner is null)
        {
            return;
        }

        owner.ApplyUpdated(item, response, _viewModel.Rating, _viewModel.NormalizedText);

        Dispatcher.Dispatch(async () => await Shell.Current.GoToAsync(".."));
    }

    /// <summary>
    /// Закрывает экран без сохранения.
    /// </summary>
    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
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
