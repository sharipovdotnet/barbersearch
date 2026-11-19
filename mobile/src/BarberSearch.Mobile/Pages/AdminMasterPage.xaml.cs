using BarberSearch.Mobile.Core.Navigation;
using BarberSearch.Mobile.Core.ViewModels.Admin;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Карточка мастера: профиль, готовность, портфолио и решение о подтверждении.
/// </summary>
/// <remarks>
/// <para>
/// Идентификатор приходит из маршрута (<see cref="IQueryAttributable"/>) и передаётся
/// ViewModel при появлении страницы. Без идентификатора карточка остаётся состоянием
/// «профиль не найден»: ссылка без id не должна открывать пустой экран.
/// </para>
/// <para>
/// Успешное решение закрывает карточку к очереди — она перечитается в <c>OnAppearing</c>.
/// Сбой остаётся на экране текстом; нативные диалоги не используются ни для решения,
/// ни для ошибки. Перед каждым переходом автоповтор останавливается: Shell не поднимает
/// <c>OnDisappearing</c> у страницы, поверх которой открыли следующую.
/// </para>
/// </remarks>
public partial class AdminMasterPage : ContentPage, IQueryAttributable
{
    /// <summary>
    /// Маршрут карточки в Shell.
    /// </summary>
    public const string Route = AdminRoutes.Master;

    private readonly AdminMasterViewModel _viewModel;

    private IDisposable? _visibility;
    private Guid? _masterId;

    /// <summary>
    /// Создаёт карточку мастера.
    /// </summary>
    /// <param name="viewModel">Состояние карточки.</param>
    public AdminMasterPage(AdminMasterViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Принимает идентификатор профиля из параметров маршрута.
    /// </summary>
    /// <param name="query">Параметры маршрута.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        _masterId = ParseMasterId(query);
    }

    /// <summary>
    /// Читает карточку при появлении и включает автоповтор на время показа.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _visibility?.Dispose();
        _visibility = PageVisibility.Watch(this, _viewModel.StartAutoRetry, _viewModel.StopAutoRetry);

        await _viewModel.InitializeAsync(_masterId);
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
    /// Подтверждает профиль; успех закрывает карточку к очереди.
    /// </summary>
    /// <param name="sender">Кнопка «Подтвердить».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnVerifyClicked(object? sender, EventArgs e)
    {
        _viewModel.StopAutoRetry();

        var error = await _viewModel.VerifyAsync();

        if (_viewModel.IsSignInRequired)
        {
            await Shell.Current.GoToAsync("//profile");

            return;
        }

        if (error is null && _viewModel.IsVerified)
        {
            await Shell.Current.GoToAsync("..");
        }
    }

    /// <summary>
    /// Открывает панель причины снятия подтверждения.
    /// </summary>
    /// <param name="sender">Кнопка «Снять подтверждение».</param>
    /// <param name="e">Аргументы события.</param>
    private void OnUnverifyClicked(object? sender, EventArgs e) =>
        _viewModel.OpenUnverifyPanelCommand.Execute(null);

    /// <summary>
    /// Снимает подтверждение после заполнения причины; успех закрывает карточку.
    /// </summary>
    /// <param name="sender">Подтверждение в панели причины.</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnUnverifyConfirmClicked(object? sender, EventArgs e)
    {
        _viewModel.StopAutoRetry();

        var error = await _viewModel.ConfirmUnverifyAsync();

        if (_viewModel.IsSignInRequired)
        {
            await Shell.Current.GoToAsync("//profile");

            return;
        }

        if (error is null && !_viewModel.IsVerified)
        {
            await Shell.Current.GoToAsync("..");
        }
    }

    /// <summary>
    /// Закрывает панель причины, ничего не меняя.
    /// </summary>
    /// <param name="sender">Кнопка «Отмена».</param>
    /// <param name="e">Аргументы события.</param>
    private void OnUnverifyCancelClicked(object? sender, EventArgs e) =>
        _viewModel.CancelUnverifyCommand.Execute(null);

    /// <summary>
    /// Возвращает к очереди из состояния «профиль не найден».
    /// </summary>
    /// <param name="sender">Кнопка «К списку».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnBackToListClicked(object? sender, EventArgs e)
    {
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync("..");
    }

    /// <summary>
    /// Возвращает к форме входа на вкладке «Профиль»: сессии больше нет.
    /// </summary>
    /// <param name="sender">Кнопка «Войти».</param>
    /// <param name="e">Аргументы события.</param>
    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync("//profile");
    }

    /// <summary>
    /// Достаёт идентификатор профиля из параметров маршрута.
    /// </summary>
    /// <param name="query">Параметры маршрута.</param>
    /// <returns>Идентификатор либо <c>null</c>, если параметра нет или он не Guid.</returns>
    private static Guid? ParseMasterId(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("id", out var value))
        {
            return null;
        }

        return value switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out var parsed) => parsed,
            _ => null,
        };
    }
}
