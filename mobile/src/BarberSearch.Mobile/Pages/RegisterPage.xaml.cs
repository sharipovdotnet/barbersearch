using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Регистрация»: самостоятельное создание аккаунта клиента.
/// </summary>
/// <remarks>
/// Открывается поверх стека «Профиля» обычным маршрутом Shell, поэтому возврат оставляет
/// вкладку на месте, а после успешной регистрации страница закрывается сама — сессия уже
/// создана, и хаб аккаунта читает профиль при появлении.
/// </remarks>
public partial class RegisterPage : ContentPage
{
    /// <summary>
    /// Маршрут страницы в Shell.
    /// </summary>
    public const string Route = "register";

    private readonly RegisterViewModel _viewModel;

    /// <summary>
    /// Создаёт экран и подписывается на завершение регистрации.
    /// </summary>
    /// <param name="viewModel">Состояние формы.</param>
    public RegisterPage(RegisterViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;

        _viewModel.Registered += OnRegistered;
        _viewModel.SignInRequested += OnSignInRequested;
    }

    /// <summary>
    /// Закрывает экран после успешной регистрации: пара уже сохранена.
    /// </summary>
    /// <remarks>
    /// <c>async void</c> — единственная допустимая форма обработчика события здесь, как и
    /// в <see cref="ProfilePage"/>: отмена и сбой навигации гасятся Shell.
    /// </remarks>
    private async void OnRegistered(object? sender, EventArgs e) => await CloseAsync();

    /// <summary>
    /// Возвращает к форме входа по кнопке карточки «Аккаунт создан».
    /// </summary>
    private async void OnSignInRequested(object? sender, EventArgs e) => await CloseAsync();

    /// <summary>
    /// Открывает Политику в браузере: это документ Оператора, а не экран приложения.
    /// </summary>
    private async void OnPrivacyPolicyClicked(object? sender, EventArgs e) =>
        await Launcher.OpenAsync(_viewModel.PrivacyPolicyUrl);

    /// <summary>
    /// Возвращает к форме входа по ссылке под формой.
    /// </summary>
    private async void OnSignInClicked(object? sender, EventArgs e) => await CloseAsync();

    /// <summary>
    /// Закрывает страницу возвратом на вкладку «Профиль».
    /// </summary>
    private static Task CloseAsync() => Shell.Current.GoToAsync("..");
}
