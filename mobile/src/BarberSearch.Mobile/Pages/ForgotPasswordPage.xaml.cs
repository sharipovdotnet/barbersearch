using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Восстановление пароля»: запрос ссылки для сброса забытого пароля.
/// </summary>
/// <remarks>
/// Открывается поверх стека «Профиля» и не содержит формы нового пароля: ссылка из письма
/// ведёт на веб-страницу сброса, приложение её не открывает и токен не разбирает.
/// Кулдаун повторной отправки считает таймер страницы — Core не знает про диспетчер MAUI,
/// а ViewModel хранит только остаток секунд.
/// </remarks>
public partial class ForgotPasswordPage : ContentPage
{
    /// <summary>
    /// Маршрут страницы в Shell.
    /// </summary>
    public const string Route = "forgot-password";

    private readonly ForgotPasswordViewModel _viewModel;

    private IDispatcherTimer? _cooldownTimer;

    /// <summary>
    /// Создаёт экран запроса ссылки.
    /// </summary>
    /// <param name="viewModel">Состояние формы и карточки успеха.</param>
    public ForgotPasswordPage(ForgotPasswordViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        _viewModel = viewModel;

        BindingContext = viewModel;
    }

    /// <summary>
    /// Запускает таймер кулдауна при появлении экрана.
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        _cooldownTimer ??= CreateCooldownTimer();
        _cooldownTimer.Start();
    }

    /// <summary>
    /// Останавливает таймер, пока страница не на экране.
    /// </summary>
    protected override void OnDisappearing()
    {
        _cooldownTimer?.Stop();

        base.OnDisappearing();
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
