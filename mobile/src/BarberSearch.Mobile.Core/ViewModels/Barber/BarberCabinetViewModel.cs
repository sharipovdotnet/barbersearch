using System.Net;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Оболочка кабинета мастера: проверка сессии, роли и привязанного профиля.
/// </summary>
/// <remarks>
/// <para>
/// Кабинет — не отдельная вкладка, а экран, который открывается из профиля. Поэтому
/// страница сначала спрашивает <c>GET api/auth/me</c> и только после этого решает, что
/// показать: форму входа, отказ по роли, предложение создать профиль или сам кабинет.
/// Ни один раздел не делает запросов до этого гейта — иначе клиент получил бы <c>403</c>,
/// а аноним <c>401</c> вместо объяснения.
/// </para>
/// <para>
/// Имя для шапки берётся из email: <c>/me</c> отдаёт claims, а не карточку мастера, и
/// придумывать имя из ничего нельзя. Настоящее имя покажет раздел «Профиль».
/// </para>
/// <para>
/// Счётчик непрочитанных обновляется тихо: сбой бейджа не должен превращать рабочий
/// кабинет в экран ошибки. Повторяемый сбой <c>/me</c> лечится циклом автоповтора, но
/// только пока экран виден; записи здесь не повторяются вовсе.
/// </para>
/// </remarks>
public sealed partial class BarberCabinetViewModel : ObservableObject
{
    private static string LoadFailedText => Loc.T("Не удалось загрузить кабинет.");

    private readonly IAccountApi _accountApi;
    private readonly IBarberNotificationsApi _notificationsApi;
    private readonly TokenSession _session;
    private readonly AutoRetryLoop _autoRetry;

    /// <summary>
    /// Вид последнего сбоя <c>/me</c>: по нему решает цикл автоповтора.
    /// </summary>
    private ApiFailureKind _lastFailureKind;

    /// <summary>
    /// Экран сейчас виден: цикл работает только пока это так.
    /// </summary>
    private bool _isPageVisible;

    /// <summary>
    /// Создаёт оболочку кабинета.
    /// </summary>
    /// <param name="accountApi">Проверка сессии: <c>/me</c>.</param>
    /// <param name="notificationsApi">Счётчик непрочитанных для колокольчика.</param>
    /// <param name="session">Сессия: <c>401</c> завершает её.</param>
    /// <param name="autoRetry">
    /// Цикл автоповтора <c>/me</c>; <c>null</c> — собственный цикл с обычным расписанием.
    /// </param>
    public BarberCabinetViewModel(
        IAccountApi accountApi,
        IBarberNotificationsApi notificationsApi,
        TokenSession session,
        AutoRetryLoop? autoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(accountApi);
        ArgumentNullException.ThrowIfNull(notificationsApi);
        ArgumentNullException.ThrowIfNull(session);

        _accountApi = accountApi;
        _notificationsApi = notificationsApi;
        _session = session;

        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;
    }

    /// <summary>
    /// Профиль мастера из текущей сессии; <c>null</c> до проверки.
    /// </summary>
    [ObservableProperty]
    private Guid? _barberProfileId;

    /// <summary>
    /// Имя для шапки: email текущего аккаунта.
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// Идёт первая проверка сессии.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstLoading;

    /// <summary>
    /// Сессия, роль и профиль проверены: можно открывать разделы.
    /// </summary>
    [ObservableProperty]
    private bool _isReady;

    /// <summary>
    /// Сервер ответил <c>401</c>: пары токенов больше нет, нужен вход.
    /// </summary>
    [ObservableProperty]
    private bool _isSignInRequired;

    /// <summary>
    /// Роль не <c>Barber</c>: кабинет этой учётной записи не принадлежит.
    /// </summary>
    [ObservableProperty]
    private bool _isForbidden;

    /// <summary>
    /// Роль есть, профиля нет: его создают в онбординге.
    /// </summary>
    [ObservableProperty]
    private bool _isNoProfile;

    /// <summary>
    /// Причина сбоя проверки; <c>null</c>, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Цикл сам повторяет <c>/me</c>: на экране видна строка «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Число непрочитанных уведомлений для бейджа.
    /// </summary>
    [ObservableProperty]
    private int _unreadCount;

    /// <summary>
    /// Есть ли непрочитанные: бейдж показывается только тогда.
    /// </summary>
    public bool HasUnread => UnreadCount > 0;

    /// <summary>
    /// Подпись бейджа: до 99 цифрами, дальше «99+».
    /// </summary>
    public string UnreadBadgeText => BarberProfileText.BadgeCount(UnreadCount);

    /// <summary>
    /// Проверяет сессию и профиль.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача проверки.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ResetState();
        IsFirstLoading = true;

        await LoadMeAsync(silent: false, cancellationToken);

        IsFirstLoading = false;
    }

    /// <summary>
    /// Перечитывает <c>/me</c> после возврата из онбординга или дочерних экранов.
    /// </summary>
    /// <remarks>
    /// Успешное обновление токена после создания профиля меняет claim, и прежний
    /// <see cref="BarberProfileId"/> остаётся <c>null</c> до этого чтения. Ошибка не
    /// прячет уже показанный кабинет: разделы остаются на экране, а сбой лечится повтором.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await LoadMeAsync(silent: IsReady, cancellationToken);
    }

    /// <summary>
    /// Тихо обновляет счётчик непрочитанных.
    /// </summary>
    /// <remarks>
    /// Ошибка не показывается и не повторяется: бейдж — украшение шапки, а не данные,
    /// ради которых стоит тревожить человека. Следующий вызов сам попробует снова.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения счётчика.</returns>
    public async Task RefreshUnreadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsReady)
        {
            return;
        }

        try
        {
            UnreadCount = Math.Max(
                await _notificationsApi.GetUnreadCountAsync(cancellationToken),
                0);
        }
        catch (Exception)
        {
            // Сбой счётчика не должен всплывать: бейдж просто остаётся прежним.
        }
    }

    /// <summary>
    /// Сообщает, что экран виден, и запускает автоповтор упавшей проверки.
    /// </summary>
    /// <remarks>
    /// Вызывается страницей при появлении и возврате из фона. Без повторяемого сбоя или
    /// при уже загруженном кабинете цикл не запускается: повторять нечего.
    /// </remarks>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        if (!IsReady
            && _lastFailureKind.IsRetryable()
            && !IsSignInRequired
            && !IsForbidden
            && !IsNoProfile)
        {
            _autoRetry.Start(RetryMeAsync);
        }
    }

    /// <summary>
    /// Сообщает, что экран скрыт, и останавливает автоповтор.
    /// </summary>
    public void StopAutoRetry()
    {
        _isPageVisible = false;
        _autoRetry.Stop();
    }

    /// <summary>
    /// Повторяет проверку <c>/me</c> по кнопке — сразу, без паузы.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        _autoRetry.Stop();

        await LoadMeAsync(silent: IsReady, cancellationToken);
    }

    /// <summary>
    /// Пересчитывает зависимости бейджа.
    /// </summary>
    partial void OnUnreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(UnreadBadgeText));
    }

    /// <summary>
    /// Готовит состояние к новой проверке.
    /// </summary>
    private void ResetState()
    {
        IsReady = false;
        IsSignInRequired = false;
        IsForbidden = false;
        IsNoProfile = false;
        ErrorMessage = null;
        BarberProfileId = null;
        DisplayName = string.Empty;
        UnreadCount = 0;
        _lastFailureKind = ApiFailureKind.None;
    }

    /// <summary>
    /// Читает <c>/me</c> и раскладывает ответ по состояниям оболочки.
    /// </summary>
    /// <param name="silent">
    /// Тихая проверка: причина не показывается и уже открытый кабинет не прячется.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему решает цикл.</returns>
    private async Task<ApiFailureKind> LoadMeAsync(
        bool silent,
        CancellationToken cancellationToken)
    {
        if (!await _session.IsAuthenticatedAsync(cancellationToken))
        {
            IsSignInRequired = true;
            IsReady = false;

            return ApiFailureKind.None;
        }

        try
        {
            var me = await _accountApi.GetMeAsync(cancellationToken);

            ApplyMe(me);
            _lastFailureKind = ApiFailureKind.None;
            ErrorMessage = null;

            if (IsReady)
            {
                await RefreshUnreadAsync(cancellationToken);
            }

            return ApiFailureKind.None;
        }
        catch (Exception exception)
        {
            var kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind == ApiFailureKind.None)
            {
                return ApiFailureKind.None;
            }

            _lastFailureKind = kind;

            if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Unauthorized })
            {
                await _session.ClearAsync(cancellationToken);

                IsSignInRequired = true;
                IsReady = false;
                ErrorMessage = null;

                return kind;
            }

            if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Forbidden })
            {
                IsForbidden = true;
                IsReady = false;
                ErrorMessage = null;

                return kind;
            }

            if (!silent || !IsReady)
            {
                ErrorMessage = BarberProfileText.ReadError(exception, kind, LoadFailedText);
            }

            if (_isPageVisible && kind.IsRetryable())
            {
                _autoRetry.Start(RetryMeAsync);
            }

            return kind;
        }
    }

    /// <summary>
    /// Применяет ответ <c>/me</c> к состояниям гейта.
    /// </summary>
    /// <param name="me">Текущий пользователь из токена.</param>
    private void ApplyMe(MeResponse me)
    {
        ArgumentNullException.ThrowIfNull(me);

        IsSignInRequired = false;
        IsForbidden = false;
        IsNoProfile = false;
        IsReady = false;
        BarberProfileId = null;
        DisplayName = me.Email;

        if (!string.Equals(me.Role, "Barber", StringComparison.Ordinal))
        {
            IsForbidden = true;

            return;
        }

        if (me.BarberProfileId is not { } profileId || profileId == Guid.Empty)
        {
            IsNoProfile = true;

            return;
        }

        BarberProfileId = profileId;
        IsReady = true;
    }

    /// <summary>
    /// Одна попытка цикла: перечитывает <c>/me</c> и обновляет гейт.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetryMeAsync(CancellationToken cancellationToken) =>
        LoadMeAsync(silent: false, cancellationToken);

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;
}
