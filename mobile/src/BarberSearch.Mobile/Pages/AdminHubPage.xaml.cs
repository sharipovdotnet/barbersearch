using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Admin;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Хаб администрирования: четыре очереди модерации, счётчики и переходы в разделы.
/// </summary>
/// <remarks>
/// <para>
/// Страница открывается строкой из «Профиля», а не вкладкой: администрирование — редкая
/// роль, и пятая вкладка занимала бы место у всех остальных. Возврат к «Профилю» системной
/// кнопкой ничего не теряет: хаб перечитывает сводку при каждом появлении.
/// </para>
/// <para>
/// Переход по строке — здесь, а не в ViewModel: маршруты Shell принадлежат приложению,
/// а Core про Shell не знает. Строка списка приносит свой маршрут, поэтому разметка не
/// знает ни одного адреса.
/// </para>
/// </remarks>
public partial class AdminHubPage : ContentPage
{
    /// <summary>
    /// Маршрут хаба в Shell.
    /// </summary>
    public const string Route = AdminRoutes.Hub;

    private readonly AdminHubViewModel _viewModel;

    private IDisposable? _visibility;

    /// <summary>
    /// Создаёт хаб.
    /// </summary>
    /// <param name="viewModel">Состояние хаба.</param>
    public AdminHubPage(AdminHubViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Читает сводку при появлении и включает автоповтор на время показа.
    /// </summary>
    /// <remarks>
    /// Возврат из раздела после действия — это и есть обновление: отдельного события
    /// «очередь изменилась» телефону не нужно.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _visibility = PageVisibility.Watch(this, _viewModel.StartAutoRetry, _viewModel.StopAutoRetry);

        await _viewModel.InitializeAsync();
    }

    /// <summary>
    /// Останавливает автоповтор и снимает подписку на окно.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _visibility?.Dispose();
        _visibility = null;
    }

    /// <summary>
    /// Открывает раздел, который принесла нажатая строка.
    /// </summary>
    /// <param name="sender">Строка списка.</param>
    /// <param name="e">Маршрут раздела в параметре жеста.</param>
    private async void OnSectionTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not AdminSectionItem section)
        {
            return;
        }

        // Фон останавливается до перехода: Shell не поднимает OnDisappearing у страницы,
        // поверх которой открыли следующую, и цикл повтора остался бы работать в ней.
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync(section.Route);
    }

    /// <summary>
    /// Возвращает к «Профилю»: сессии больше нет, форма входа стоит там.
    /// </summary>
    /// <param name="sender">Кнопка «Войти».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnSignInClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..");
}
