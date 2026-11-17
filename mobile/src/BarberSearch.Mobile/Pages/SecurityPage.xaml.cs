using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Localization;
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
    /// Область «страница видна»: из неё растёт автоповтор, пока страница на виду.
    /// </summary>
    private IDisposable? _visibility;

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
    /// <remarks>
    /// Область видимости создаётся до первой загрузки: сбой, пришедший из неё, уже может
    /// запустить автоповтор.
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

        _cooldownTimer ??= CreateCooldownTimer();
        _cooldownTimer.Start();

        await _viewModel.InitializeAsync();

        if (!_viewModel.IsAuthenticated)
        {
            await ReturnToProfileAsync();
        }
    }

    /// <summary>
    /// Останавливает таймер и автоповтор и снимает подписку, пока страница не на экране.
    /// </summary>
    /// <remarks>
    /// <see cref="IDisposable.Dispose"/> сам сообщает «скрыта» и снимает подписку на окно,
    /// поэтому отдельного вызова <c>StopAutoRetry</c> здесь нет.
    /// </remarks>
    protected override void OnDisappearing()
    {
        _visibility?.Dispose();
        _visibility = null;

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
            Loc.T("Пароль изменён"),
            Loc.T("Войдите снова с новым паролем."),
            Loc.T("Хорошо"));

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
