using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Pages;

/// <summary>
/// Экран «Безопасность»: смена пароля и повторная отправка письма подтверждения.
/// </summary>
/// <remarks>
/// <para>
/// Страница живёт один визит: открывается поверх стека «Профиля» и закрывается возвратом.
/// Подписка на <see cref="TokenSession.SessionEnded"/> снимается при уходе со страницы,
/// чтобы singleton-сессия не держала её экземпляр.
/// </para>
/// <para>
/// После смены пароля сервер отзывает refresh-токены и ротирует <c>SecurityStamp</c>, поэтому
/// ViewModel сразу входит с новым паролем. Если вход не удался, страница — единственное
/// место, где можно показать сообщение: диалог нативный, а Core про MAUI не знает. Затем
/// сессия гасится, и хаб просит войти снова.
/// </para>
/// </remarks>
public partial class SecurityPage : ContentPage
{
    /// <summary>
    /// Маршрут экрана в Shell.
    /// </summary>
    public const string Route = "security";

    private readonly SecurityViewModel _viewModel;
    private readonly TokenSession _session;

    private IDispatcherTimer? _cooldownTimer;
    private bool _isNavigating;

    /// <summary>
    /// Создаёт экран безопасности.
    /// </summary>
    /// <param name="viewModel">Состояние экрана.</param>
    /// <param name="session">Сессия, чьё завершение возвращает на вкладку «Профиль».</param>
    public SecurityPage(SecurityViewModel viewModel, TokenSession session)
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
    /// Меняет пароль и, если повторный вход не удался, просит войти заново.
    /// </summary>
    /// <remarks>
    /// Тексты сообщения — дословно из сценария среза: пароль уже изменён, поэтому человеку
    /// нужен не отчёт об ошибке, а следующий шаг. Сессия гасится после подтверждения
    /// прочитанного, а не до него.
    /// </remarks>
    private async void OnChangePasswordClicked(object? sender, EventArgs e)
    {
        var outcome = await _viewModel.ChangePasswordAsync();

        if (outcome != PasswordChangeOutcome.NeedsSignIn)
        {
            return;
        }

        await DisplayAlertAsync(
            "Пароль изменён",
            "Войдите снова с новым паролем.",
            "Хорошо");

        await _viewModel.SignOutAsync();
    }

    /// <summary>
    /// Возвращает на вкладку «Профиль» после завершения сессии.
    /// </summary>
    private void OnSessionEnded(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(async () => await ReturnToProfileAsync());

    /// <summary>
    /// Закрывает стек кабинета и открывает вкладку «Профиль».
    /// </summary>
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
