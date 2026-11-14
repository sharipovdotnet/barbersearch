using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Карточка барбера и запись: услуга, салон, дата, слот и подтверждение.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI фабрикой маршрута (<c>PageRouteFactory</c>), поэтому ViewModel
/// приходит конструктором. Код-behind держит только то, что Core знать не может: разбор
/// параметра маршрута, события выбора, переход на вкладку «Профиль» и прокрутку к секции —
/// навигация и вёрстка живут в приложении.
/// </para>
/// <para>
/// Подписка на смену страницы отзывов живёт между <c>OnAppearing</c> и <c>OnDisappearing</c>:
/// карточка остаётся в стеке и возвращается, поэтому подписка в конструкторе пережила бы
/// первый уход, а снятие при уходе без повторной подписки сломало бы прокрутку после возврата.
/// </para>
/// </remarks>
public partial class BarberPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут карточки в Shell.
    /// </summary>
    public const string Route = "barber";

    private readonly BarberCardViewModel _viewModel;

    private Guid? _barberId;
    private bool _queryApplied;
    private bool _isAppeared;

    /// <summary>
    /// Создаёт карточку.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    public BarberPage(BarberCardViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Принимает параметр маршрута <c>id</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Битая ссылка — не ошибка сети: <c>null</c> остаётся, и экран покажет «не найден»
    /// без запроса. Значение приходит строкой, поэтому разбирается явно.
    /// </para>
    /// <para>
    /// Shell может применить параметры и до, и после первого <c>OnAppearing</c>, поэтому
    /// загрузку запускает тот из двух шагов, который случился вторым: так карточка всегда
    /// грузится ровно один раз и не показывает «не найден», пока параметр ещё в пути.
    /// </para>
    /// </remarks>
    /// <param name="query">Параметры маршрута.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        _barberId = query.TryGetValue("id", out var raw)
            && raw is string text
            && Guid.TryParse(text, out var id)
                ? id
                : null;
        _queryApplied = true;

        if (_isAppeared)
        {
            _ = _viewModel.InitializeAsync(_barberId);
        }
    }

    /// <summary>
    /// Открывает карточку при каждом появлении экрана.
    /// </summary>
    /// <remarks>
    /// Возврат с вкладки «Профиль» или из просмотра фото не перезагружает карточку: ViewModel
    /// сам решает, что уже загружено, и только перечитывает признак входа. Подписка на смену
    /// страницы отзывов восстанавливается здесь же — страница живёт в стеке и возвращается.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _isAppeared = true;

        _viewModel.Reviews.PageChanged -= OnReviewsPageChanged;
        _viewModel.Reviews.PageChanged += OnReviewsPageChanged;

        if (_queryApplied)
        {
            await _viewModel.InitializeAsync(_barberId);
        }
    }

    /// <summary>
    /// Снимает подписку на смену страницы отзывов, пока страница не на экране.
    /// </summary>
    protected override void OnDisappearing()
    {
        _viewModel.Reviews.PageChanged -= OnReviewsPageChanged;

        base.OnDisappearing();
    }

    /// <summary>
    /// Передаёт выбранную услугу во ViewModel: от неё зависит шаг сетки.
    /// </summary>
    private async void OnServiceChanged(object? sender, EventArgs e)
    {
        if (sender is Picker picker)
        {
            await _viewModel.SelectServiceAsync(picker.SelectedItem as BarberServiceOption);
        }
    }

    /// <summary>
    /// Передаёт выбранный салон: меняются пояс, «сегодня» и сетка.
    /// </summary>
    private async void OnSalonChanged(object? sender, EventArgs e)
    {
        if (sender is Picker picker)
        {
            await _viewModel.SelectSalonAsync(picker.SelectedItem as BarberSalonOption);
        }
    }

    /// <summary>
    /// Передаёт выбранную дату: сетка перезапрашивается.
    /// </summary>
    private async void OnDateSelected(object? sender, DateChangedEventArgs e)
    {
        if (e.NewDate is { } date)
        {
            await _viewModel.SelectDateAsync(date);
        }
    }

    /// <summary>
    /// Открывает полноэкранный просмотр с выбранного кадра.
    /// </summary>
    /// <remarks>
    /// Индекс считается здесь, потому что навигация — дело приложения, а не Core. Просмотр
    /// получает кадры и номер объектами маршрута: отдельного эндпоинта «фото по id» нет.
    /// </remarks>
    private async void OnPhotoTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not BarberPhotoOption photo)
        {
            return;
        }

        var index = _viewModel.Photos.IndexOf(photo);

        if (index < 0)
        {
            return;
        }

        await Shell.Current.GoToAsync(
            PortfolioPhotoPage.Route,
            new Dictionary<string, object>
            {
                ["photos"] = _viewModel.Photos,
                ["index"] = index,
            });
    }

    /// <summary>
    /// Возвращает карточку к заголовку «Отзывы» после смены страницы.
    /// </summary>
    private void OnReviewsPageChanged(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(async () =>
            await PageScroll.ScrollToAsync(ReviewsSection, ScrollToPosition.Start, true));

    /// <summary>
    /// Уводит на вкладку «Профиль» вместо отдельной формы входа.
    /// </summary>
    /// <remarks>
    /// Стек вкладки «Поиск» сохраняется при переключении вкладок, поэтому выбранный слот
    /// переживает вход и после возврата «Записаться» уже доступна.
    /// </remarks>
    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//profile");
    }
}
