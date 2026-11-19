using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Admin;
using BarberSearch.Mobile.Theming;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Очередь заявок «Стать мастером»: фильтр, строки и переход в карточку.
/// </summary>
/// <remarks>
/// <para>
/// Страница открывается строкой хаба и живёт один визит: возврат из карточки после решения
/// перечитывает очередь в <see cref="OnAppearing"/>, поэтому решённая заявка исчезает без
/// отдельного события. Переход в карточку — здесь, а не во ViewModel: маршруты Shell
/// принадлежат приложению, а Core про Shell не знает.
/// </para>
/// <para>
/// Сегменты фильтра красятся через <see cref="SegmentedButtonStyle"/>: цвета берутся из
/// стилей темы, а не запоминаются в code-behind, поэтому смена темы перекрашивает выбранный
/// сегмент на месте.
/// </para>
/// </remarks>
public partial class AdminRequestsPage : ContentPage
{
    /// <summary>
    /// Маршрут очереди заявок в Shell.
    /// </summary>
    public const string Route = AdminRoutes.Requests;

    private readonly AdminRequestsViewModel _viewModel;

    /// <summary>
    /// Область «страница видна»: из неё растёт автоповтор чтения, пока страница на виду.
    /// </summary>
    private IDisposable? _visibility;

    /// <summary>
    /// Создаёт очередь заявок.
    /// </summary>
    /// <param name="viewModel">Состояние очереди.</param>
    public AdminRequestsPage(AdminRequestsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;

        UpdateSegmentStyles();
    }

    /// <summary>
    /// Читает очередь при появлении и включает автоповтор на время показа.
    /// </summary>
    /// <remarks>
    /// Область видимости создаётся до первой загрузки: сбой, пришедший из неё, уже может
    /// запустить автоповтор. Старая область закрывается: Shell не поднимает
    /// <c>OnDisappearing</c> у страницы, поверх которой открыли карточку.
    /// <c>async void</c> здесь — единственная допустимая форма переопределения.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _visibility?.Dispose();
        _visibility = PageVisibility.Watch(
            this,
            _viewModel.StartAutoRetry,
            _viewModel.StopAutoRetry);

        await _viewModel.InitializeAsync();

        UpdateSegmentStyles();
    }

    /// <summary>
    /// Останавливает автоповтор и снимает подписку на окно.
    /// </summary>
    protected override void OnDisappearing()
    {
        _visibility?.Dispose();
        _visibility = null;

        base.OnDisappearing();
    }

    /// <summary>
    /// Переключает фильтр на очередь.
    /// </summary>
    /// <param name="sender">Сегмент «В очереди».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnQueueFilterClicked(object? sender, EventArgs e) =>
        await SelectFilterAsync(AdminRequestListFilter.Pending);

    /// <summary>
    /// Переключает фильтр на все заявки.
    /// </summary>
    /// <param name="sender">Сегмент «Все».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnAllFilterClicked(object? sender, EventArgs e) =>
        await SelectFilterAsync(AdminRequestListFilter.All);

    /// <summary>
    /// Меняет фильтр и перекрашивает сегменты под новую выборку.
    /// </summary>
    /// <param name="filter">Выбранный список.</param>
    /// <returns>Задача чтения новой страницы.</returns>
    private async Task SelectFilterAsync(AdminRequestListFilter filter)
    {
        if (_viewModel.Filter == filter)
        {
            return;
        }

        await _viewModel.SelectFilterCommand.ExecuteAsync(filter);

        UpdateSegmentStyles();
    }

    /// <summary>
    /// Открывает карточку заявки, которую принесла нажатая строка.
    /// </summary>
    /// <param name="sender">Строка списка.</param>
    /// <param name="e">Заявка в параметре жеста.</param>
    private async void OnRequestTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not AdminRequestItem item)
        {
            return;
        }

        // Фон останавливается до перехода: Shell не поднимает OnDisappearing у страницы,
        // поверх которой открыли следующую, и цикл повтора остался бы работать в ней.
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync(
            string.Concat(AdminRequestPage.Route, "?id=", item.Id.ToString("D")));
    }

    /// <summary>
    /// Возвращает к «Профилю»: сессии больше нет, форма входа стоит там.
    /// </summary>
    /// <param name="sender">Кнопка «Войти».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync("..");
    }

    /// <summary>
    /// Подсвечивает выбранный сегмент фильтра.
    /// </summary>
    private void UpdateSegmentStyles()
    {
        SegmentedButtonStyle.Apply(QueueFilterButton, _viewModel.IsQueueFilter);
        SegmentedButtonStyle.Apply(AllFilterButton, _viewModel.IsAllFilter);
    }
}
