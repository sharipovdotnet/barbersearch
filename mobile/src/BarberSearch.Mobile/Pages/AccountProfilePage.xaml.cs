using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Профиль» кабинета: карточка email, личные данные и служебные данные аккаунта.
/// </summary>
/// <remarks>
/// <para>
/// Страница живёт один визит: открывается поверх стека «Профиля» и закрывается возвратом.
/// Поэтому подписка на <see cref="TokenSession.SessionEnded"/> снимается при уходе со
/// страницы — singleton-сессия не должна держать её экземпляр.
/// </para>
/// <para>
/// Секундный таймер здесь, а не во ViewModel: кулдаун отправки письма — это «часы экрана»,
/// а платформенный таймер Core не знает. Тик только уменьшает счётчик — сеть не трогается.
/// </para>
/// </remarks>
public partial class AccountProfilePage : ContentPage
{
    /// <summary>
    /// Маршрут экрана в Shell.
    /// </summary>
    public const string Route = "account-profile";

    private readonly AccountProfileViewModel _viewModel;
    private readonly TokenSession _session;

    private IDispatcherTimer? _cooldownTimer;
    private bool _isNavigating;

    /// <summary>
    /// Создаёт экран профиля кабинета.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    /// <param name="session">Сессия, чьё завершение возвращает на вкладку «Профиль».</param>
    public AccountProfilePage(AccountProfileViewModel viewModel, TokenSession session)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(session);

        InitializeComponent();

        _viewModel = viewModel;
        _session = session;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Читает профиль, запускает таймер кулдауна и подписывается на завершение сессии.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _session.SessionEnded -= OnSessionEnded;
        _session.SessionEnded += OnSessionEnded;

        _cooldownTimer ??= CreateCooldownTimer();
        _cooldownTimer.Start();

        await _viewModel.InitializeAsync();

        if (!_viewModel.IsAuthenticated)
        {
            await ReturnToProfileAsync();
        }
    }

    /// <summary>
    /// Останавливает таймер и снимает подписку, пока страница не на экране.
    /// </summary>
    protected override void OnDisappearing()
    {
        _cooldownTimer?.Stop();

        _session.SessionEnded -= OnSessionEnded;

        base.OnDisappearing();
    }

    /// <summary>
    /// Возвращает на вкладку «Профиль» после завершения сессии.
    /// </summary>
    /// <remarks>
    /// Событие приходит с сетевого потока: навигация и привязки — работа UI-потока.
    /// </remarks>
    private void OnSessionEnded(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(async () => await ReturnToProfileAsync());

    /// <summary>
    /// Закрывает стек кабинета и открывает вкладку «Профиль».
    /// </summary>
    /// <remarks>
    /// Флаг не даёт двум путям (событию сессии и проверке при появлении) запустить
    /// навигацию дважды — повторный <c>GoToAsync</c> на тот же маршрут ломает стек.
    /// </remarks>
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

    /// <summary>
    /// Уменьшает кулдаун раз в секунду.
    /// </summary>
    private void OnCooldownTimerTick(object? sender, EventArgs e) => _viewModel.TickCooldown();

    /// <summary>
    /// Создаёт секундный таймер кулдауна.
    /// </summary>
    private IDispatcherTimer CreateCooldownTimer()
    {
        var timer = Dispatcher.CreateTimer();

        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += OnCooldownTimerTick;

        return timer;
    }
}
