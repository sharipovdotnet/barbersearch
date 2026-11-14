using System.Globalization;
using System.Net;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран «Безопасность»: смена пароля и подтверждение адреса электронной почты.
/// </summary>
/// <remarks>
/// <para>
/// Смена пароля — единственное место, где после <c>204</c> нужен повторный вход: сервер
/// отзывает все refresh-токены и ротирует <c>SecurityStamp</c>, поэтому текущая пара
/// перестаёт работать сразу, а не через пятнадцать минут. Пара меняется здесь же, через
/// <see cref="TokenSession.ApplyAsync"/>, и человек остаётся в аккаунте.
/// </para>
/// <para>
/// Подтверждение email — письмо со ссылкой: приложение её не открывает и не разбирает,
/// ссылка ведёт на веб. Для подтверждённого адреса кнопки отправки нет: сервер ответил бы
/// <c>204</c> и письма не послал.
/// </para>
/// <para>
/// Профиль читается при каждом появлении экрана — первый раз с индикатором, повторно молча.
/// Ошибка тихого обновления данные не убирает; <c>401</c> гасит сессию и возвращает на
/// вкладку «Профиль».
/// </para>
/// </remarks>
public sealed partial class SecurityViewModel : ObservableObject
{
    private const string LoadFailedText = "Не удалось загрузить профиль.";
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string TooManyRequestsText = "Слишком много запросов. Подождите минуту и повторите.";

    private const string CurrentPasswordRequiredText = "Введите текущий пароль.";
    private const string NewPasswordRequiredText = "Введите новый пароль.";
    private const string PasswordTooShortText = "Минимум 12 символов.";
    private const string PasswordTooLongText = "Максимум 128 символов.";
    private const string PasswordSameAsCurrentText = "Новый пароль должен отличаться от текущего.";
    private const string PasswordMismatchText = "Пароли не совпадают.";
    private const string InvalidCurrentPasswordText = "Текущий пароль неверный.";
    private const string PasswordCommonText = "Этот пароль слишком распространён.";
    private const string PasswordEqualsEmailText = "Пароль не должен совпадать с email.";
    private const string PasswordEqualsNameText = "Пароль не должен совпадать с именем или фамилией.";
    private const string ChangePasswordFailedText = "Не удалось сменить пароль.";
    private const string PasswordChangedText =
        "Пароль изменён. Остальные сессии входа завершены — здесь вы остались.";

    private const string EmailCooldownText = "Письмо уже отправлено. Повторите через минуту.";
    private const string EmailUnavailableText = "Отправка писем пока не настроена.";
    private const string EmailSendFailedText = "Не удалось отправить письмо. Попробуйте позже.";
    private const string EmailInvalidLinkText = "Ссылка недействительна или устарела.";

    private const string InvalidCurrentPasswordCode = "auth.changePassword.invalidCurrentPassword";
    private const string PasswordRequiredCode = "auth.password.required";
    private const string PasswordTooShortCode = "auth.password.tooShort";
    private const string PasswordTooLongCode = "auth.password.tooLong";
    private const string PasswordCommonCode = "auth.password.common";
    private const string PasswordEqualsEmailCode = "auth.password.equalsEmail";
    private const string PasswordEqualsNameCode = "auth.password.equalsName";

    private const string VerificationCooldownCode = "auth.emailVerification.cooldown";
    private const string VerificationUnavailableCode = "auth.emailVerification.unavailable";
    private const string VerificationInvalidCode = "auth.emailVerification.invalid";

    /// <summary>
    /// Минимальная длина пароля — та же, что проверяет сервер.
    /// </summary>
    public const int MinPasswordLength = 12;

    /// <summary>
    /// Максимальная длина пароля — та же, что проверяет сервер.
    /// </summary>
    public const int MaxPasswordLength = 128;

    /// <summary>
    /// Длительность кулдауна отправки письма.
    /// </summary>
    private const int ResendCooldownSeconds = 60;

    private readonly IAccountApi _accountApi;
    private readonly IAuthApi _authApi;
    private readonly TokenSession _session;

    private MyProfileResponse? _profile;

    /// <summary>
    /// Создаёт экран безопасности.
    /// </summary>
    /// <param name="accountApi">Профиль, смена пароля и письмо подтверждения.</param>
    /// <param name="authApi">Повторный вход после смены пароля.</param>
    /// <param name="session">Сессия — источник правды о входе и владелец пары.</param>
    public SecurityViewModel(IAccountApi accountApi, IAuthApi authApi, TokenSession session)
    {
        ArgumentNullException.ThrowIfNull(accountApi);
        ArgumentNullException.ThrowIfNull(authApi);
        ArgumentNullException.ThrowIfNull(session);

        _accountApi = accountApi;
        _authApi = authApi;
        _session = session;
    }

    /// <summary>
    /// Идёт первая проверка сессии и профиля.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    private bool _isLoading = true;

    /// <summary>
    /// Пара токенов есть и принята сервером.
    /// </summary>
    /// <remarks>
    /// Экран открывается только из хаба, но пара могла исчезнуть между открытием и
    /// появлением: страница по этому признаку возвращает на вкладку «Профиль».
    /// </remarks>
    [ObservableProperty]
    private bool _isAuthenticated;

    /// <summary>
    /// Идёт первая загрузка профиля.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowResendButton))]
    [NotifyPropertyChangedFor(nameof(CanResend))]
    private bool _isProfileLoading;

    /// <summary>
    /// Ошибка чтения профиля; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ErrorMessage))]
    private string? _profileError;

    /// <summary>
    /// Адрес из профиля.
    /// </summary>
    [ObservableProperty]
    private string? _email;

    /// <summary>
    /// Подтверждён ли адрес.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmailVerifiedText))]
    [NotifyPropertyChangedFor(nameof(VerificationDescription))]
    [NotifyPropertyChangedFor(nameof(ShowResendButton))]
    [NotifyPropertyChangedFor(nameof(CanResend))]
    private bool _isEmailVerified;

    /// <summary>
    /// Раскрыта форма смены пароля.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOpenPasswordButton))]
    private bool _isPasswordFormOpen;

    /// <summary>
    /// Текущий пароль из формы.
    /// </summary>
    [ObservableProperty]
    private string _currentPassword = string.Empty;

    /// <summary>
    /// Новый пароль из формы.
    /// </summary>
    [ObservableProperty]
    private string _newPassword = string.Empty;

    /// <summary>
    /// Повтор нового пароля.
    /// </summary>
    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    /// <summary>
    /// Ошибка поля «Текущий пароль».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentPasswordError))]
    private string? _currentPasswordError;

    /// <summary>
    /// Ошибка поля «Новый пароль».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewPasswordError))]
    private string? _newPasswordError;

    /// <summary>
    /// Ошибка поля «Повторите пароль».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfirmPasswordError))]
    private string? _confirmPasswordError;

    /// <summary>
    /// Общая ошибка формы пароля.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasswordFormError))]
    private string? _passwordFormError;

    /// <summary>
    /// Идёт смена пароля: повторное нажатие игнорируется.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangePassword))]
    [NotifyPropertyChangedFor(nameof(ChangePasswordButtonText))]
    private bool _isChangingPassword;

    /// <summary>
    /// Сообщение об успешной смене пароля.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasswordAnnouncement))]
    private string? _passwordAnnouncement;

    /// <summary>
    /// Идёт отправка письма подтверждения.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowResendButton))]
    [NotifyPropertyChangedFor(nameof(ResendButtonText))]
    [NotifyPropertyChangedFor(nameof(CanResend))]
    private bool _isSendingVerification;

    /// <summary>
    /// Остаток кулдауна отправки письма; тикает таймер страницы.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCooldownActive))]
    [NotifyPropertyChangedFor(nameof(ResendButtonText))]
    [NotifyPropertyChangedFor(nameof(CanResend))]
    private int _cooldownRemaining;

    /// <summary>
    /// Подтверждение отправки письма: «Письмо отправлено на {email}.».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVerificationNotice))]
    private string? _verificationNotice;

    /// <summary>
    /// Ошибка отправки письма.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVerificationError))]
    private string? _verificationError;

    /// <summary>
    /// Показывать индикатор первой загрузки.
    /// </summary>
    public bool ShowLoading => IsLoading || (IsProfileLoading && _profile is null);

    /// <summary>
    /// Показывать ошибку загрузки вместо карточек.
    /// </summary>
    public bool ShowError => !ShowLoading && _profile is null && ProfileError is not null;

    /// <summary>
    /// Показывать карточки.
    /// </summary>
    public bool ShowContent => _profile is not null;

    /// <summary>
    /// Причина ошибки второй строкой.
    /// </summary>
    public string? ErrorMessage => ProfileError;

    /// <summary>
    /// Подпись статуса: «Адрес подтверждён.» / «Адрес не подтверждён.».
    /// </summary>
    public string IsEmailVerifiedText => IsEmailVerified ? "Адрес подтверждён." : "Адрес не подтверждён.";

    /// <summary>
    /// Объяснение под заголовком карточки подтверждения.
    /// </summary>
    public string VerificationDescription => IsEmailVerified
        ? "Адрес подтверждён: на него можно получать письма о записях."
        : "Адрес пока не подтверждён. Подтвердите его по ссылке из письма — пароль и вход это не ограничивает.";

    /// <summary>
    /// Показывать кнопку повторной отправки: только для неподтверждённого и загруженного профиля.
    /// </summary>
    /// <remarks>
    /// Пока профиль грузится, кнопки нет: статус может оказаться «подтверждён», а лишний
    /// запрос тратит лимит и сбивает человека с толку.
    /// </remarks>
    public bool ShowResendButton =>
        _profile is not null
        && !IsEmailVerified
        && !IsProfileLoading;

    /// <summary>
    /// Можно ли отправить письмо ещё раз.
    /// </summary>
    public bool CanResend => ShowResendButton && !IsCooldownActive && !IsSendingVerification;

    /// <summary>
    /// Надпись на кнопке отправки письма.
    /// </summary>
    public string ResendButtonText => IsSendingVerification
        ? "Отправляем письмо…"
        : IsCooldownActive
            ? string.Concat(
                "Повторить через ",
                CooldownRemaining.ToString(CultureInfo.InvariantCulture),
                " с")
            : "Отправить письмо ещё раз";

    /// <summary>
    /// Идёт кулдаун отправки письма.
    /// </summary>
    public bool IsCooldownActive => CooldownRemaining > 0;

    /// <summary>
    /// Есть ошибка поля «Текущий пароль».
    /// </summary>
    public bool HasCurrentPasswordError => !string.IsNullOrEmpty(CurrentPasswordError);

    /// <summary>
    /// Есть ошибка поля «Новый пароль».
    /// </summary>
    public bool HasNewPasswordError => !string.IsNullOrEmpty(NewPasswordError);

    /// <summary>
    /// Есть ошибка поля «Повторите пароль».
    /// </summary>
    public bool HasConfirmPasswordError => !string.IsNullOrEmpty(ConfirmPasswordError);

    /// <summary>
    /// Есть общая ошибка формы пароля.
    /// </summary>
    public bool HasPasswordFormError => !string.IsNullOrEmpty(PasswordFormError);

    /// <summary>
    /// Есть сообщение об успешной смене пароля.
    /// </summary>
    public bool HasPasswordAnnouncement => !string.IsNullOrEmpty(PasswordAnnouncement);

    /// <summary>
    /// Есть подтверждение отправки письма.
    /// </summary>
    public bool HasVerificationNotice => !string.IsNullOrEmpty(VerificationNotice);

    /// <summary>
    /// Есть ошибка отправки письма.
    /// </summary>
    public bool HasVerificationError => !string.IsNullOrEmpty(VerificationError);

    /// <summary>
    /// Можно ли нажать «Сменить пароль».
    /// </summary>
    public bool CanChangePassword => !IsChangingPassword;

    /// <summary>
    /// Показывать кнопку, раскрывающую форму: сама форма уже открыта.
    /// </summary>
    public bool ShowOpenPasswordButton => !IsPasswordFormOpen;

    /// <summary>
    /// Надпись на кнопке смены пароля.
    /// </summary>
    public string ChangePasswordButtonText => IsChangingPassword ? "Сохраняем…" : "Сменить пароль";

    /// <summary>
    /// Проверяет сессию и читает профиль при появлении экрана.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_profile is null)
        {
            IsLoading = true;
        }

        try
        {
            await _session.EnsureLoadedAsync(cancellationToken);

            if (!_session.IsAuthenticated)
            {
                ApplySignedOutState();
                return;
            }

            IsAuthenticated = true;

            await LoadProfileAsync(_profile is not null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Отмена — не ошибка: экран просто закрыли.
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Сбрасывает экран после завершения сессии.
    /// </summary>
    public void ApplySignedOutState()
    {
        _profile = null;
        IsAuthenticated = false;

        Email = null;
        IsEmailVerified = false;

        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        CurrentPasswordError = null;
        NewPasswordError = null;
        ConfirmPasswordError = null;
        PasswordFormError = null;
        PasswordAnnouncement = null;
        IsPasswordFormOpen = false;

        CooldownRemaining = 0;
        VerificationNotice = null;
        VerificationError = null;
        IsSendingVerification = false;
        ProfileError = null;

        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowContent));
    }

    /// <summary>
    /// Уменьшает кулдаун на секунду; вызывается таймером страницы.
    /// </summary>
    public void TickCooldown()
    {
        if (CooldownRemaining > 0)
        {
            CooldownRemaining--;
        }
    }

    /// <summary>
    /// Повторяет чтение профиля после ошибки.
    /// </summary>
    [RelayCommand]
    private async Task RetryAsync()
    {
        ProfileError = null;

        await LoadProfileAsync(silent: false, CancellationToken.None);
    }

    /// <summary>
    /// Раскрывает форму смены пароля с чистыми полями.
    /// </summary>
    [RelayCommand]
    private void OpenPasswordForm()
    {
        ClearPasswordFields();

        PasswordFormError = null;
        PasswordAnnouncement = null;
        IsPasswordFormOpen = true;
    }

    /// <summary>
    /// Закрывает форму смены пароля без запроса.
    /// </summary>
    [RelayCommand]
    private void CancelPasswordChange()
    {
        ClearPasswordFields();
        PasswordFormError = null;
        IsPasswordFormOpen = false;
    }

    /// <summary>
    /// Меняет пароль и, если сервер принял, сразу входит с новым паролем.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Локальные проверки выполняются при нажатии: все три поля заполнены, новый пароль
    /// 12..128 символов, отличается от текущего, подтверждение совпадает.
    /// </para>
    /// <para>
    /// Повторный вход обязателен: <c>204</c> отозвал refresh-токены и ротировал
    /// <c>SecurityStamp</c>, поэтому старая пара мертва. Возвращаемое значение говорит
    /// странице, что делать дальше; при <see cref="PasswordChangeOutcome.NeedsSignIn"/> она
    /// показывает сообщение и просит ViewModel завершить сессию.
    /// </para>
    /// </remarks>
    /// <returns>Итог смены пароля.</returns>
    public async Task<PasswordChangeOutcome> ChangePasswordAsync()
    {
        if (IsChangingPassword || _profile is null)
        {
            return PasswordChangeOutcome.Failed;
        }

        CurrentPasswordError = null;
        NewPasswordError = null;
        ConfirmPasswordError = null;
        PasswordFormError = null;
        PasswordAnnouncement = null;

        if (CurrentPassword.Length == 0)
        {
            CurrentPasswordError = CurrentPasswordRequiredText;
        }

        if (NewPassword.Length == 0)
        {
            NewPasswordError = NewPasswordRequiredText;
        }
        else if (NewPassword.Length < MinPasswordLength)
        {
            NewPasswordError = PasswordTooShortText;
        }
        else if (NewPassword.Length > MaxPasswordLength)
        {
            NewPasswordError = PasswordTooLongText;
        }
        else if (NewPassword == CurrentPassword)
        {
            NewPasswordError = PasswordSameAsCurrentText;
        }

        if (ConfirmPassword != NewPassword)
        {
            ConfirmPasswordError = PasswordMismatchText;
        }

        if (HasCurrentPasswordError || HasNewPasswordError || HasConfirmPasswordError)
        {
            return PasswordChangeOutcome.Failed;
        }

        IsChangingPassword = true;

        var passwordChanged = false;

        try
        {
            await _accountApi.ChangePasswordAsync(CurrentPassword, NewPassword);

            passwordChanged = true;
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();

            return PasswordChangeOutcome.Failed;
        }
        catch (BarberSearchApiException exception)
        {
            ApplyPasswordFailure(exception);

            return PasswordChangeOutcome.Failed;
        }
        catch (HttpRequestException)
        {
            PasswordFormError = NetworkText;

            return PasswordChangeOutcome.Failed;
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            PasswordFormError = NetworkText;

            return PasswordChangeOutcome.Failed;
        }
        finally
        {
            // Пока пароль не изменён, повторное нажатие игнорируется. После успеха флаг
            // держится до конца повторного входа: вторая смена пароля начаться не должна.
            IsChangingPassword = passwordChanged;
        }

        try
        {
            var outcome = await SignInWithNewPasswordAsync();

            if (outcome == PasswordChangeOutcome.Changed)
            {
                ClearPasswordFields();
                IsPasswordFormOpen = false;
                PasswordAnnouncement = PasswordChangedText;
            }

            return outcome;
        }
        finally
        {
            IsChangingPassword = false;
        }
    }

    /// <summary>
    /// Завершает сессию по просьбе страницы: пара уже мертва, вход нужен заново.
    /// </summary>
    /// <remarks>
    /// Вызывается после <see cref="PasswordChangeOutcome.NeedsSignIn"/>, когда страница
    /// показала сообщение. Отмена не передаётся: выход должен состояться в любом случае.
    /// </remarks>
    public async Task SignOutAsync()
    {
        ApplySignedOutState();

        await _session.ClearAsync(CancellationToken.None);
    }

    /// <summary>
    /// Отправляет письмо подтверждения на текущий адрес.
    /// </summary>
    /// <remarks>
    /// <c>204</c> — письмо поставлено в отправку; <c>429</c> и код кулдауна включают тот же
    /// локальный отсчёт, потому что оба значат «письмо уже ушло»
    /// </remarks>
    [RelayCommand]
    private async Task ResendVerificationAsync()
    {
        if (!CanResend || _profile is null)
        {
            return;
        }

        VerificationError = null;
        VerificationNotice = null;
        IsSendingVerification = true;

        try
        {
            await _accountApi.RequestEmailVerificationAsync();

            VerificationNotice = string.Concat("Письмо отправлено на ", Email, ".");
            StartCooldown();
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();
        }
        catch (BarberSearchApiException exception)
        {
            ApplyVerificationFailure(exception);
        }
        catch (HttpRequestException)
        {
            VerificationError = NetworkText;
        }
        catch (OperationCanceledException)
        {
            VerificationError = NetworkText;
        }
        finally
        {
            IsSendingVerification = false;
        }
    }

    /// <summary>
    /// Читает профиль и раскладывает его по экрану.
    /// </summary>
    private async Task LoadProfileAsync(bool silent, CancellationToken cancellationToken)
    {
        if (!silent)
        {
            IsProfileLoading = true;
        }

        try
        {
            var profile = await _accountApi.GetMyProfileAsync(cancellationToken);

            ProfileError = null;
            _profile = profile;

            Email = profile.Email;
            IsEmailVerified = profile.IsEmailVerified;

            OnPropertyChanged(nameof(ShowContent));
            OnPropertyChanged(nameof(ShowLoading));
            OnPropertyChanged(nameof(ShowError));
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();
        }
        catch (BarberSearchApiException exception)
        {
            ProfileError = exception.StatusCode == HttpStatusCode.TooManyRequests
                ? TooManyRequestsText
                : LoadFailedText;
        }
        catch (HttpRequestException)
        {
            ProfileError = NetworkText;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ProfileError = NetworkText;
        }
        catch (InvalidOperationException)
        {
            ProfileError = LoadFailedText;
        }
        finally
        {
            IsProfileLoading = false;
        }
    }

    /// <summary>
    /// Входит с новым паролем и заменяет пару.
    /// </summary>
    /// <remarks>
    /// Ошибка здесь не означает, что пароль не сменился: он сменился, но новой пары нет,
    /// и продолжать работу нельзя. Страница объяснит это человеку и завершит сессию.
    /// </remarks>
    private async Task<PasswordChangeOutcome> SignInWithNewPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            return PasswordChangeOutcome.NeedsSignIn;
        }

        try
        {
            TokenResponse tokens = await _authApi.LoginAsync(Email, NewPassword);

            await _session.ApplyAsync(tokens, CancellationToken.None);

            return PasswordChangeOutcome.Changed;
        }
        catch (BarberSearchApiException)
        {
            return PasswordChangeOutcome.NeedsSignIn;
        }
        catch (HttpRequestException)
        {
            return PasswordChangeOutcome.NeedsSignIn;
        }
        catch (OperationCanceledException)
        {
            return PasswordChangeOutcome.NeedsSignIn;
        }
        catch (InvalidOperationException)
        {
            // Пара без refresh-токена: продолжать с половиной пары нельзя.
            return PasswordChangeOutcome.NeedsSignIn;
        }
    }

    /// <summary>
    /// Раскладывает отказ смены пароля по полям и текстам.
    /// </summary>
    /// <remarks>
    /// Порядок повторяет веб: публичные коды, потом статус. Код <c>equalsName</c> в вебе
    /// не переведён — текст добавлен здесь, чтобы серверный отказ не превращался в общий.
    /// </remarks>
    private void ApplyPasswordFailure(BarberSearchApiException exception)
    {
        if (HasCode(exception, InvalidCurrentPasswordCode))
        {
            CurrentPasswordError = InvalidCurrentPasswordText;
        }
        else if (HasCode(exception, PasswordRequiredCode))
        {
            NewPasswordError = NewPasswordRequiredText;
        }
        else if (HasCode(exception, PasswordTooShortCode))
        {
            NewPasswordError = PasswordTooShortText;
        }
        else if (HasCode(exception, PasswordTooLongCode))
        {
            NewPasswordError = PasswordTooLongText;
        }
        else if (HasCode(exception, PasswordCommonCode))
        {
            NewPasswordError = PasswordCommonText;
        }
        else if (HasCode(exception, PasswordEqualsEmailCode))
        {
            NewPasswordError = PasswordEqualsEmailText;
        }
        else if (HasCode(exception, PasswordEqualsNameCode))
        {
            NewPasswordError = PasswordEqualsNameText;
        }
        else
        {
            PasswordFormError = exception.StatusCode == HttpStatusCode.TooManyRequests
                ? TooManyRequestsText
                : ChangePasswordFailedText;
        }
    }

    /// <summary>
    /// Раскладывает отказ отправки письма по статусам и кодам.
    /// </summary>
    /// <remarks>
    /// <c>429</c> проверяется раньше кодов: лимитер и код кулдауна значат одно и то же.
    /// Сбой отправки — <c>5xx</c> или <c>sendFailed</c>: токен уже сохранён, повторить
    /// можно после кулдауна.
    /// </remarks>
    private void ApplyVerificationFailure(BarberSearchApiException exception)
    {
        if (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            VerificationError = EmailCooldownText;
            StartCooldown();

            return;
        }

        if (HasCode(exception, VerificationCooldownCode))
        {
            VerificationError = EmailCooldownText;
            StartCooldown();
        }
        else if (HasCode(exception, VerificationUnavailableCode))
        {
            VerificationError = EmailUnavailableText;
        }
        else if (HasCode(exception, VerificationInvalidCode))
        {
            VerificationError = EmailInvalidLinkText;
        }
        else
        {
            // sendFailed и 5xx: сервер принял запрос, но письмо не ушло. Для человека это
            // одна ситуация — «попробуйте позже», лишний запрос ничего не изменит.
            VerificationError = EmailSendFailedText;
        }
    }

    /// <summary>
    /// Включает кулдаун на полную длину.
    /// </summary>
    private void StartCooldown() => CooldownRemaining = ResendCooldownSeconds;

    /// <summary>
    /// Гасит сессию: пара мертва, страница вернёт на вкладку «Профиль».
    /// </summary>
    private async Task EndSessionAsync()
    {
        ApplySignedOutState();

        await _session.ClearAsync(CancellationToken.None);
    }

    /// <summary>
    /// Очищает поля формы пароля.
    /// </summary>
    private void ClearPasswordFields()
    {
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        CurrentPasswordError = null;
        NewPasswordError = null;
        ConfirmPasswordError = null;
    }

    /// <summary>
    /// Проверяет наличие публичного кода ошибки.
    /// </summary>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Правка поля снимает ошибку под ним и общую ошибку формы.
    /// </summary>
    partial void OnCurrentPasswordChanged(string value)
    {
        _ = value;
        CurrentPasswordError = null;
        PasswordFormError = null;
    }

    /// <summary>
    /// Правка поля снимает ошибку под ним и общую ошибку формы.
    /// </summary>
    partial void OnNewPasswordChanged(string value)
    {
        _ = value;
        NewPasswordError = null;
        PasswordFormError = null;
    }

    /// <summary>
    /// Правка поля снимает ошибку под ним и общую ошибку формы.
    /// </summary>
    partial void OnConfirmPasswordChanged(string value)
    {
        _ = value;
        ConfirmPasswordError = null;
        PasswordFormError = null;
    }
}

/// <summary>
/// Итог смены пароля: страница решает по нему, показывать успех или просить войти заново.
/// </summary>
public enum PasswordChangeOutcome
{
    /// <summary>
    /// Пароль изменён, пара заменена — сессия жива.
    /// </summary>
    Changed,

    /// <summary>
    /// Пароль изменён, но повторный вход не удался: нужна форма входа.
    /// </summary>
    NeedsSignIn,

    /// <summary>
    /// Пароль не изменён: причина уже разложена по полям или строке формы.
    /// </summary>
    Failed,
}
