using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Создать профиль мастера»: форма профиля и первый салон.
/// </summary>
/// <remarks>
/// <para>
/// Страница резолвится из DI фабрикой маршрута. Параметров у маршрута нет: форма сама читает
/// свою заявку (<c>GET api/barber-requests/mine</c>) и подставляет одобренные поля.
/// </para>
/// <para>
/// Чтения повторяются сами, пока страница видна: <see cref="PageVisibility.Watch"/> сообщает
/// и уход приложения в фон, где <c>OnDisappearing</c> не вызывается. Создание профиля,
/// продление пары и привязка салона — записи, и молча не повторяются.
/// </para>
/// <para>
/// «Перейти в кабинет» и «Пропустить» поднимают одно событие: кабинет остаётся на стеке
/// ниже, и возврат к нему перечитывает <c>/me</c> — пара токенов уже обновлена, поэтому
/// кабинет откроется с профилем, созданным этим экраном.
/// </para>
/// </remarks>
public partial class BarberOnboardingPage : ContentPage
{
    private readonly BarberOnboardingViewModel _viewModel;

    /// <summary>
    /// Область «страница видна»: из неё растут циклы автоповтора заявки и каталога.
    /// </summary>
    private IDisposable? _visibility;

    /// <summary>
    /// Создаёт экран онбординга.
    /// </summary>
    /// <param name="viewModel">Состояние формы и шага салона.</param>
    public BarberOnboardingPage(BarberOnboardingViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;

        _viewModel.CabinetRequested += OnCabinetRequested;
        _viewModel.SignInRequested += OnSignInRequested;
    }

    /// <summary>
    /// Включает автоповтор и читает заявку при появлении.
    /// </summary>
    /// <remarks>
    /// Область видимости создаётся до чтения: сбой, пришедший из него, уже может запустить
    /// цикл. <c>async void</c> — единственная допустимая форма переопределения, ошибки
    /// разбирает сам ViewModel.
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
    /// Останавливает автоповтор, пока экран не виден.
    /// </summary>
    /// <remarks>
    /// <see cref="IDisposable.Dispose"/> сам сообщает «скрыта» и снимает подписку на окно,
    /// поэтому отдельного вызова <c>StopAutoRetry</c> здесь нет.
    /// </remarks>
    protected override void OnDisappearing()
    {
        _visibility?.Dispose();
        _visibility = null;

        base.OnDisappearing();
    }

    /// <summary>
    /// Выбирает салон из каталога: команды выбора у строки нет, а состояние живёт в ViewModel.
    /// </summary>
    /// <param name="sender">Распознаватель нажатия строки.</param>
    /// <param name="e">Параметр строки — сам салон каталога.</param>
    private void OnCatalogSalonTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is BarberSalonCatalogItem item)
        {
            _viewModel.SelectedCatalogSalon = item;
        }
    }

    /// <summary>
    /// Возвращает в кабинет: профиль и пара токенов уже обновлены, и кабинет перечитает
    /// <c>/me</c> на возврате.
    /// </summary>
    /// <param name="sender">ViewModel онбординга.</param>
    /// <param name="e">Событие без данных.</param>
    private async void OnCabinetRequested(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..");

    /// <summary>
    /// Открывает форму входа: у кабинета нет собственной формы, она живёт в «Профиле».
    /// </summary>
    /// <param name="sender">ViewModel онбординга.</param>
    /// <param name="e">Событие без данных.</param>
    private async void OnSignInRequested(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//profile");
}
