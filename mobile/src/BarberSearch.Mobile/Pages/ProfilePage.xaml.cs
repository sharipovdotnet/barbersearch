using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Вкладка «Профиль»: форма входа без сессии и хаб аккаунта с сессией.
/// </summary>
/// <remarks>
/// <para>
/// Страница — singleton (см. <c>MauiProgram</c>), поэтому подписка на сессию живёт столько же,
/// сколько приложение: отписываться и бояться утечки не нужно. Обработку события держим
/// здесь, а не во ViewModel: <see cref="TokenSession.SessionEnded"/> приходит с сетевого
/// потока, а Core не знает про диспетчер MAUI — переключение на UI-поток и есть работа
/// страницы.
/// </para>
/// <para>
/// Разделы кабинета открываются обычными страницами Shell поверх стека «Профиля»: возврат
/// сохраняет вкладку и её состояние, а маршруты регистрируются в <c>AppShell</c>.
/// </para>
/// </remarks>
public partial class ProfilePage : ContentPage
{
    private readonly ProfileViewModel _viewModel;

    /// <summary>
    /// Создаёт экран и подписывается на завершение сессии и переходы в разделы.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    /// <param name="session">Сессия, чьё завершение возвращает форму входа.</param>
    public ProfilePage(ProfileViewModel viewModel, TokenSession session)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(session);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;

        session.SessionEnded += OnSessionEnded;

        _viewModel.EditProfileRequested += OnEditProfileRequested;
        _viewModel.ReviewsRequested += OnReviewsRequested;
        _viewModel.SecurityRequested += OnSecurityRequested;
    }

    /// <summary>
    /// Проверяет сессию и тихо обновляет профиль при каждом появлении вкладки.
    /// </summary>
    /// <remarks>
    /// Именно при появлении, а не в конструкторе: после выхода или сбоя сети повторный
    /// переход на вкладку — это и есть «попробовать ещё раз», отдельной кнопки не нужно.
    /// <c>InitializeAsync</c> сам гасит отмену и ошибки, поэтому <c>async void</c> здесь
    /// безопасен и является единственной допустимой формой переопределения.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.InitializeAsync();
    }

    /// <summary>
    /// Возвращает экран к форме входа, когда сессия завершилась без участия этого экрана.
    /// </summary>
    /// <remarks>
    /// Признак «это не собственный выход» снимается здесь же, до перехода на UI-поток: событие
    /// приходит во время очистки пары, а к моменту выполнения отложенного действия флаг
    /// <c>IsSigningOut</c> уже сброшен — на телефоне это дало бы «Сессия истекла» после «Выйти».
    /// </remarks>
    private void OnSessionEnded(object? sender, EventArgs e)
    {
        var isExpired = !_viewModel.IsSigningOut;

        Dispatcher.Dispatch(() => _viewModel.ApplySessionEndedState(isExpired));
    }

    /// <summary>
    /// Открывает экран личных данных.
    /// </summary>
    private async void OnEditProfileRequested(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(AccountProfilePage.Route);
    }

    /// <summary>
    /// Открывает список своих отзывов.
    /// </summary>
    private async void OnReviewsRequested(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(MyReviewsPage.Route);
    }

    /// <summary>
    /// Открывает экран безопасности.
    /// </summary>
    private async void OnSecurityRequested(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(SecurityPage.Route);
    }
}
