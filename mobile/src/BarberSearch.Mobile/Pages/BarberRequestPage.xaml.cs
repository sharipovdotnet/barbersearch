using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Стать мастером»: заявка клиента на роль мастера — форма, статус и ошибки.
/// </summary>
/// <remarks>
/// <para>
/// Страница открывается поверх стека «Профиля» обычным маршрутом Shell, поэтому возврат
/// оставляет вкладку и её состояние на месте. Пять состояний экрана живут во ViewModel
/// и взаимоисключающие — страница только раскладывает их по блокам.
/// </para>
/// <para>
/// <see cref="OnAppearing"/> запускает автоповтор чтения через <see cref="PageVisibility"/>:
/// пара «видна/скрыта» учитывает и уход приложения в фон, чего один
/// <see cref="Page.OnDisappearing"/> не умеет. Чтение лечится само, запись — нет: после сбоя
/// отправки человек сам решает, повторять ли её.
/// </para>
/// <para>
/// Экран живёт один визит, поэтому ViewModel освобождается при уходе со страницы: тихая
/// попытка не должна пережить свой экран. Навигация — работа страницы, а не Core: уход
/// приходит событием <see cref="BarberRequestViewModel.LeaveRequested"/>.
/// </para>
/// </remarks>
public partial class BarberRequestPage : ContentPage
{
    /// <summary>
    /// Маршрут экрана в Shell.
    /// </summary>
    public const string Route = "barber-request";

    private readonly BarberRequestViewModel _viewModel;

    /// <summary>
    /// Область «страница видна»: из неё растёт автоповтор, пока страница на виду.
    /// </summary>
    private IDisposable? _visibility;

    /// <summary>
    /// Создаёт экран и подписывается на просьбу закрыться.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    public BarberRequestPage(BarberRequestViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;

        _viewModel.LeaveRequested += OnLeaveRequested;
    }

    /// <summary>
    /// Читает заявку при каждом появлении и запускает автоповтор неудачного чтения.
    /// </summary>
    /// <remarks>
    /// Область видимости создаётся до первой загрузки: сбой, пришедший из неё, уже может
    /// запустить автоповтор. <see cref="BarberRequestViewModel.InitializeAsync"/> сам гасит
    /// отмену и ошибки, поэтому <c>async void</c> здесь безопасен и является единственной
    /// допустимой формой переопределения.
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
    }

    /// <summary>
    /// Снимает подписку на события экрана и освобождает ViewModel.
    /// </summary>
    /// <remarks>
    /// <see cref="IDisposable.Dispose"/> сам сообщает «скрыта» и останавливает цикл, поэтому
    /// отдельного вызова <c>StopAutoRetry</c> здесь нет.
    /// </remarks>
    protected override void OnDisappearing()
    {
        _visibility?.Dispose();
        _visibility = null;

        _viewModel.LeaveRequested -= OnLeaveRequested;
        _viewModel.Dispose();

        base.OnDisappearing();
    }

    /// <summary>
    /// Закрывает экран: «К входу» после истёкшей сессии и «Выйти и войти снова» после
    /// одобрения заявки приводят к возврату в «Профиль».
    /// </summary>
    private async void OnLeaveRequested(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..");
}
