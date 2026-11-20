using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;
using BarberSearch.Mobile.Theming;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Вкладка «Профиль»: форма входа без сессии и хаб аккаунта с сессией.
/// </summary>
/// <remarks>
/// <para>
/// Страница создаётся заново вместе с Shell (смена языка пересобирает интерфейс), поэтому
/// подписка на singleton-сессию снимается при уходе со страницы. Обработку события держим
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
    private readonly TokenSession _session;

    /// <summary>
    /// Область «страница видна»: из неё растёт автоповтор, пока вкладка на виду.
    /// </summary>
    private IDisposable? _visibility;

    /// <summary>
    /// Создаёт экран и подписывается на завершение сессии и переходы в разделы.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    /// <param name="session">Сессия, чьё завершение возвращает форму входа.</param>
    /// <param name="themes">Сервис темы для переключателя «Системная / Светлая / Тёмная».</param>
    public ProfilePage(ProfileViewModel viewModel, TokenSession session, ThemeService themes)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(themes);

        InitializeComponent();

        _viewModel = viewModel;
        _session = session;

        ThemeSwitch.Initialize(themes);

        BindingContext = viewModel;

        _viewModel.EditProfileRequested += OnEditProfileRequested;
        _viewModel.ReviewsRequested += OnReviewsRequested;
        _viewModel.SecurityRequested += OnSecurityRequested;
        _viewModel.RegisterRequested += OnRegisterRequested;
        _viewModel.ForgotPasswordRequested += OnForgotPasswordRequested;
        _viewModel.BarberRequestRequested += OnBarberRequestRequested;
        _viewModel.AdminRequested += OnAdminRequested;
    }

    /// <summary>
    /// Проверяет сессию и тихо обновляет профиль при каждом появлении вкладки.
    /// </summary>
    /// <remarks>
    /// Именно при появлении, а не в конструкторе: после выхода или сбоя сети повторный
    /// переход на вкладку — это и есть «попробовать ещё раз», отдельной кнопки не нужно.
    /// Область видимости создаётся до первой загрузки: сбой, пришедший из неё, уже может
    /// запустить автоповтор. <c>InitializeAsync</c> сам гасит отмену и ошибки, поэтому
    /// <c>async void</c> здесь безопасен и является единственной допустимой формой
    /// переопределения.
    /// </remarks>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _session.SessionEnded -= OnSessionEnded;
        _session.SessionEnded += OnSessionEnded;

        _visibility?.Dispose();
        _visibility = PageVisibility.Watch(
            this,
            _viewModel.StartAutoRetry,
            _viewModel.StopAutoRetry);

        await _viewModel.InitializeAsync();
    }

    /// <summary>
    /// Снимает подписку на singleton-сессию и останавливает автоповтор, пока страница не на экране.
    /// </summary>
    /// <remarks>
    /// <see cref="IDisposable.Dispose"/> сам сообщает «скрыта» и снимает подписку на окно,
    /// поэтому отдельного вызова <c>StopAutoRetry</c> здесь нет.
    /// </remarks>
    protected override void OnDisappearing()
    {
        _visibility?.Dispose();
        _visibility = null;

        _session.SessionEnded -= OnSessionEnded;

        base.OnDisappearing();
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
    /// <remarks>
    /// Цикл автоповтора останавливается до перехода: push-навигация Shell может не вызвать
    /// <see cref="OnDisappearing"/>, и тогда повтор продолжал бы ходить в сеть под уже
    /// открытым дочерним экраном — без человека, который его видит.
    /// </remarks>
    private async void OnEditProfileRequested(object? sender, EventArgs e)
    {
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync(AccountProfilePage.Route);
    }

    /// <summary>
    /// Открывает список своих отзывов.
    /// </summary>
    private async void OnReviewsRequested(object? sender, EventArgs e)
    {
        // Та же причина, что и у «Личных данных»: push может не вызвать OnDisappearing.
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync(MyReviewsPage.Route);
    }

    /// <summary>
    /// Открывает экран безопасности.
    /// </summary>
    private async void OnSecurityRequested(object? sender, EventArgs e)
    {
        // Та же причина, что и у «Личных данных»: push может не вызвать OnDisappearing.
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync(SecurityPage.Route);
    }

    /// <summary>
    /// Открывает экран регистрации: он живёт поверх стека «Профиля».
    /// </summary>
    private async void OnRegisterRequested(object? sender, EventArgs e)
    {
        // Та же причина, что и у «Личных данных»: push может не вызвать OnDisappearing.
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync(RegisterPage.Route);
    }

    /// <summary>
    /// Открывает запрос ссылки для сброса забытого пароля.
    /// </summary>
    /// <remarks>
    /// Экран живёт поверх стека «Профиля»: возврат с него оставляет форму входа вместе с
    /// введённым адресом, а ссылка из письма открывается уже в браузере.
    /// </remarks>
    private async void OnForgotPasswordRequested(object? sender, EventArgs e)
    {
        // Та же причина, что и у «Личных данных»: push может не вызвать OnDisappearing.
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync(ForgotPasswordPage.Route);
    }

    /// <summary>
    /// Открывает экран заявки на роль мастера.
    /// </summary>
    /// <remarks>
    /// Перед переходом останавливаются оба цикла хаба — профиля и заявки: push-навигация Shell
    /// может не вызвать <see cref="OnDisappearing"/>, и тогда тихие чтения продолжали бы ходить
    /// в сеть под открытой страницей заявки.
    /// </remarks>
    private async void OnBarberRequestRequested(object? sender, EventArgs e)
    {
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync(BarberRequestPage.Route);
    }

    /// <summary>
    /// Открывает хаб администрирования.
    /// </summary>
    /// <remarks>
    /// Хаб — не вкладка, а страница поверх стека «Профиля»: администрирование нужно редко,
    /// и пятая вкладка занимала бы место у всех остальных ролей. Возврат оставляет вкладку
    /// с её сессией и профилем на месте.
    /// </remarks>
    private async void OnAdminRequested(object? sender, EventArgs e)
    {
        // Та же причина, что и у «Личных данных»: push может не вызвать OnDisappearing.
        _viewModel.StopAutoRetry();

        await Shell.Current.GoToAsync(AdminHubPage.Route);
    }

    /// <summary>
    /// Передаёт переключатель «Присылать напоминания» команде ViewModel.
    /// </summary>
    /// <remarks>
    /// Привязка <c>IsToggled</c> односторонняя: значение приходит от ViewModel, а команда
    /// вызывается только тогда, когда положение изменил человек. Событие, поднятое самой
    /// привязкой, отличается тем, что новое положение уже совпадает с состоянием ViewModel, —
    /// его и пропускаем, иначе первое появление вкладки регистрировало бы устройство дважды.
    /// </remarks>
    private async void OnNotificationsToggled(object? sender, ToggledEventArgs e)
    {
        if (_viewModel.NotificationsEnabled == e.Value)
        {
            return;
        }

        await _viewModel.ToggleNotificationsCommand.ExecuteAsync(e.Value);
    }
}
