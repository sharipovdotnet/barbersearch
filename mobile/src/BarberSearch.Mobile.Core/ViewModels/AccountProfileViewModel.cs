using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран «Профиль» кабинета: карточка email, личные данные и служебные данные аккаунта.
/// </summary>
/// <remarks>
/// <para>
/// Профиль читается при каждом появлении экрана: первый раз с индикатором, повторно — молча,
/// не стирая уже показанные значения и не затирая несохранённые правки формы. Ошибка тихого
/// обновления данные не убирает: из аккаунта сбой сети не выкидывает. <c>401</c> — исключение:
/// пара мертва, сессия гасится, и страница возвращает на вкладку «Профиль».
/// </para>
/// <para>
/// Аватар в этом срезе не показывается, но <c>avatarUrl</c> уходит обратно в каждом
/// сохранении: <c>PUT</c> заменяет тело целиком, и <c>null</c> стёр бы фото, загруженное
/// на вебе.
/// </para>
/// <para>
/// Смена email — заявка, а не мгновенная смена: <c>204</c> значит «письма поставлены в
/// очередь», а показанный адрес остаётся прежним до перехода по ссылке. Поэтому успех — это
/// подтверждение отправки и локальный кулдаун 60 секунд, а не догадка о новом адресе.
/// </para>
/// </remarks>
public sealed partial class AccountProfileViewModel : ObservableObject
{
    private const string LoadFailedText = "Не удалось загрузить профиль.";
    private const string SaveFailedText = "Не удалось сохранить профиль.";
    private const string SavedText = "Изменения сохранены.";
    private const string NameRequiredText = "Введите имя.";
    private const string LastNameRequiredText = "Введите фамилию.";
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string TooManyRequestsText = "Слишком много запросов. Подождите минуту и повторите.";
    private const string SessionExpiredText = "Сессия истекла — войдите снова.";

    private const string EmailRequiredText = "Введите email.";
    private const string EmailInvalidText = "Похоже, в адресе опечатка.";
    private const string PasswordRequiredText = "Введите текущий пароль.";
    private const string EmailChangeFailedText = "Не удалось изменить email. Попробуйте позже.";
    private const string EmailChangeNewEmailCodeText = "Проверьте email: нужен адрес вида name@example.com.";
    private const string EmailChangeSameEmailText = "Этот адрес уже указан в профиле.";
    private const string EmailChangeInvalidPasswordText = "Текущий пароль неверный.";
    private const string EmailChangeTakenText = "Этот адрес уже занят. Укажите другой.";
    private const string EmailCooldownText = "Письмо уже отправлено. Повторите через минуту.";
    private const string EmailUnavailableText = "Отправка писем пока не настроена.";
    private const string EmailInvalidLinkText = "Ссылка недействительна или устарела.";
    private const string EmailTooManyAttemptsText = "Слишком много попыток. Подождите минуту и повторите.";

    private const string UpdateFirstNameCode = "user.updateProfile.firstName";
    private const string UpdateLastNameCode = "user.updateProfile.lastName";
    private const string ChangeEmailNewEmailCode = "user.changeEmail.newEmail";
    private const string ChangeEmailSameEmailCode = "user.changeEmail.sameEmail";
    private const string ChangeEmailInvalidPasswordCode = "auth.emailChange.invalidCurrentPassword";
    private const string ChangeEmailTakenCode = "user.changeEmail.emailTaken";
    private const string ChangeEmailCooldownCode = "auth.emailChange.cooldown";
    private const string ChangeEmailUnavailableCode = "auth.emailChange.unavailable";
    private const string ChangeEmailInvalidCode = "auth.emailChange.invalid";

    /// <summary>
    /// Простой шаблон адреса — тот же, что в вебе: пусто и один <c>@</c> уже отсеиваются.
    /// </summary>
    private static readonly Regex EmailPattern =
        new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant);

    private readonly IAccountApi _accountApi;
    private readonly TokenSession _session;

    private MyProfileResponse? _profile;

    private string _baselineFirstName = string.Empty;
    private string _baselineLastName = string.Empty;
    private string _baselinePhone = string.Empty;
    private bool _formInitialized;

    /// <summary>
    /// Создаёт экран профиля кабинета.
    /// </summary>
    /// <param name="accountApi">Профиль и заявка на смену email.</param>
    /// <param name="session">Сессия — источник правды о входе.</param>
    public AccountProfileViewModel(IAccountApi accountApi, TokenSession session)
    {
        ArgumentNullException.ThrowIfNull(accountApi);
        ArgumentNullException.ThrowIfNull(session);

        _accountApi = accountApi;
        _session = session;
    }

    /// <summary>
    /// Максимальная длина имени и фамилии — как в вебе.
    /// </summary>
    public int MaxNameLength { get; } = 100;

    /// <summary>
    /// Максимальная длина телефона — как в вебе.
    /// </summary>
    public int MaxPhoneLength { get; } = 40;

    /// <summary>
    /// Максимальная длина адреса — из серверного валидатора.
    /// </summary>
    public int MaxEmailLength { get; } = 254;

    /// <summary>
    /// Длительность кулдауна отправки письма.
    /// </summary>
    public int CooldownSeconds { get; } = 60;

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
    /// Идёт первая загрузка профиля: карточек ещё нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    private bool _isProfileLoading;

    /// <summary>
    /// Ошибка чтения профиля; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ErrorMessage))]
    private string? _profileError;

    /// <summary>
    /// Имя из формы.
    /// </summary>
    [ObservableProperty]
    private string _firstName = string.Empty;

    /// <summary>
    /// Фамилия из формы.
    /// </summary>
    [ObservableProperty]
    private string _lastName = string.Empty;

    /// <summary>
    /// Телефон из формы; пустая строка — «поля нет», в запрос уходит <c>null</c>.
    /// </summary>
    [ObservableProperty]
    private string _phoneNumber = string.Empty;

    /// <summary>
    /// Ошибка поля «Имя».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFirstNameError))]
    private string? _firstNameError;

    /// <summary>
    /// Ошибка поля «Фамилия».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastNameError))]
    private string? _lastNameError;

    /// <summary>
    /// Общая ошибка формы личных данных.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormError))]
    private string? _formError;

    /// <summary>
    /// Сообщение об успешном сохранении.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnnouncement))]
    private string? _announcement;

    /// <summary>
    /// Идёт сохранение: кнопки заблокированы, «Сохранить» называется «Сохраняем…».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    private bool _isSaving;

    /// <summary>
    /// Раскрыта форма смены email.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartEmailChange))]
    private bool _isEmailFormOpen;

    /// <summary>
    /// Новый адрес из формы смены email.
    /// </summary>
    [ObservableProperty]
    private string _newEmail = string.Empty;

    /// <summary>
    /// Текущий пароль из формы смены email.
    /// </summary>
    [ObservableProperty]
    private string _emailCurrentPassword = string.Empty;

    /// <summary>
    /// Ошибка поля «Новый email».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewEmailError))]
    private string? _newEmailError;

    /// <summary>
    /// Ошибка поля «Текущий пароль».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmailPasswordError))]
    private string? _emailPasswordError;

    /// <summary>
    /// Общая ошибка формы смены email.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmailFormError))]
    private string? _emailFormError;

    /// <summary>
    /// Подтверждение отправки письма: «Письмо отправлено на {email}».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmailAnnouncement))]
    private string? _emailAnnouncement;

    /// <summary>
    /// Идёт отправка заявки: повторное нажатие игнорируется.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SendEmailButtonText))]
    [NotifyPropertyChangedFor(nameof(CanSendEmail))]
    [NotifyPropertyChangedFor(nameof(CanStartEmailChange))]
    private bool _isSendingEmail;

    /// <summary>
    /// Остаток кулдауна в секундах; тикает таймер страницы.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCooldownActive))]
    [NotifyPropertyChangedFor(nameof(EmailButtonText))]
    [NotifyPropertyChangedFor(nameof(CanStartEmailChange))]
    private int _cooldownRemaining;

    /// <summary>
    /// Email из профиля; после заявки не меняется.
    /// </summary>
    [ObservableProperty]
    private string? _email;

    /// <summary>
    /// Подтверждён ли адрес.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmailStatusText))]
    [NotifyPropertyChangedFor(nameof(ShowEmailWarning))]
    private bool _isEmailVerified;

    /// <summary>
    /// Русская подпись роли.
    /// </summary>
    [ObservableProperty]
    private string _roleText = string.Empty;

    /// <summary>
    /// Когда создан аккаунт.
    /// </summary>
    [ObservableProperty]
    private string _createdAtText = string.Empty;

    /// <summary>
    /// Когда был последний вход.
    /// </summary>
    [ObservableProperty]
    private string _lastLoginText = AccountText.Never;

    /// <summary>
    /// Показывать индикатор: первая загрузка профиля.
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
    /// Подпись статуса подтверждения email.
    /// </summary>
    public string EmailStatusText => IsEmailVerified ? "Адрес подтверждён." : "Адрес не подтверждён.";

    /// <summary>
    /// Показывать предупреждение о неподтверждённом адресе в карточке email.
    /// </summary>
    public bool ShowEmailWarning => _profile is not null && !IsEmailVerified;

    /// <summary>
    /// Есть ошибка поля «Имя».
    /// </summary>
    public bool HasFirstNameError => !string.IsNullOrEmpty(FirstNameError);

    /// <summary>
    /// Есть ошибка поля «Фамилия».
    /// </summary>
    public bool HasLastNameError => !string.IsNullOrEmpty(LastNameError);

    /// <summary>
    /// Есть общая ошибка формы личных данных.
    /// </summary>
    public bool HasFormError => !string.IsNullOrEmpty(FormError);

    /// <summary>
    /// Есть сообщение об успешном сохранении.
    /// </summary>
    public bool HasAnnouncement => !string.IsNullOrEmpty(Announcement);

    /// <summary>
    /// Есть ошибка поля «Новый email».
    /// </summary>
    public bool HasNewEmailError => !string.IsNullOrEmpty(NewEmailError);

    /// <summary>
    /// Есть ошибка поля «Текущий пароль».
    /// </summary>
    public bool HasEmailPasswordError => !string.IsNullOrEmpty(EmailPasswordError);

    /// <summary>
    /// Есть общая ошибка формы смены email.
    /// </summary>
    public bool HasEmailFormError => !string.IsNullOrEmpty(EmailFormError);

    /// <summary>
    /// Есть подтверждение отправки письма.
    /// </summary>
    public bool HasEmailAnnouncement => !string.IsNullOrEmpty(EmailAnnouncement);

    /// <summary>
    /// Идёт кулдаун отправки письма.
    /// </summary>
    public bool IsCooldownActive => CooldownRemaining > 0;

    /// <summary>
    /// Форма личных данных изменена относительно последнего сохранения.
    /// </summary>
    /// <remarks>
    /// Сравниваются сырые значения, как в вебе: правка одних пробелов тоже считается
    /// изменением, иначе кнопка «Сохранить» не дала бы отправить обрезанное значение.
    /// </remarks>
    public bool IsDirty => _formInitialized
        && (FirstName != _baselineFirstName
            || LastName != _baselineLastName
            || PhoneNumber != _baselinePhone);

    /// <summary>
    /// Можно ли нажать «Сохранить».
    /// </summary>
    public bool CanSave => IsDirty && !IsSaving;

    /// <summary>
    /// Можно ли нажать «Отменить».
    /// </summary>
    public bool CanCancel => IsDirty && !IsSaving;

    /// <summary>
    /// Надпись на кнопке сохранения.
    /// </summary>
    public string SaveButtonText => IsSaving ? "Сохраняем…" : "Сохранить";

    /// <summary>
    /// Надпись на кнопке смены email: во время кулдауна — обратный отсчёт.
    /// </summary>
    public string EmailButtonText => IsCooldownActive
        ? string.Concat(
            "Повторить через ",
            CooldownRemaining.ToString(CultureInfo.InvariantCulture),
            " с")
        : "Изменить email";

    /// <summary>
    /// Надпись на кнопке отправки письма.
    /// </summary>
    public string SendEmailButtonText => IsSendingEmail ? "Отправляем письмо…" : "Отправить письмо";

    /// <summary>
    /// Можно ли нажать «Отправить письмо»: во время запроса кнопка заблокирована.
    /// </summary>
    public bool CanSendEmail => !IsSendingEmail;

    /// <summary>
    /// Можно ли раскрыть форму смены email.
    /// </summary>
    public bool CanStartEmailChange => !IsSendingEmail && !IsCooldownActive;

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
    /// Разложить экран по последнему ответу сервера.
    /// </summary>
    /// <param name="profile">Профиль, если он уже есть в памяти.</param>
    public void ApplyProfile(MyProfileResponse profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _profile = profile;
        Email = profile.Email;
        IsEmailVerified = profile.IsEmailVerified;
        RoleText = AccountText.RoleText(profile.Role);
        CreatedAtText = AccountText.Moment(profile.CreatedAt, TimeZoneInfo.Local);
        LastLoginText = AccountText.MomentOrNever(profile.LastLoginAt, TimeZoneInfo.Local);

        // Несохранённые правки не затираются тихим обновлением: форма принадлежит человеку,
        // а не последнему ответу сервера.
        if (!IsDirty)
        {
            FirstName = profile.FirstName;
            LastName = profile.LastName;
            PhoneNumber = profile.PhoneNumber ?? string.Empty;

            SetBaseline(FirstName, LastName, PhoneNumber);
        }

        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowEmailWarning));
    }

    /// <summary>
    /// Сбрасывает экран после завершения сессии.
    /// </summary>
    public void ApplySignedOutState()
    {
        _profile = null;
        _formInitialized = false;
        IsAuthenticated = false;

        Email = null;
        IsEmailVerified = false;
        RoleText = string.Empty;
        CreatedAtText = string.Empty;
        LastLoginText = AccountText.Never;

        FirstName = string.Empty;
        LastName = string.Empty;
        PhoneNumber = string.Empty;
        _baselineFirstName = string.Empty;
        _baselineLastName = string.Empty;
        _baselinePhone = string.Empty;

        FirstNameError = null;
        LastNameError = null;
        FormError = null;
        Announcement = null;

        IsEmailFormOpen = false;
        NewEmail = string.Empty;
        EmailCurrentPassword = string.Empty;
        NewEmailError = null;
        EmailPasswordError = null;
        EmailFormError = null;
        EmailAnnouncement = null;
        CooldownRemaining = 0;
        ProfileError = null;

        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowError));
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
    /// Сохраняет личные данные.
    /// </summary>
    /// <remarks>
    /// Локальные проверки выполняются при нажатии и показываются под полями. Телефон
    /// обрезается и пустой уходит <c>null</c>; аватар передаётся обратно без изменений.
    /// Успех — <c>204</c>, затем локальные значения и baseline обновляются: повторный
    /// <c>GET</c> не нужен.
    /// </remarks>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanSave || _profile is null)
        {
            return;
        }

        var firstName = FirstName.Trim();
        var lastName = LastName.Trim();
        var phoneNumber = PhoneNumber.Trim();

        ClearFormErrors();

        if (firstName.Length == 0)
        {
            FirstNameError = NameRequiredText;
        }

        if (lastName.Length == 0)
        {
            LastNameError = LastNameRequiredText;
        }

        if (HasFirstNameError || HasLastNameError)
        {
            return;
        }

        IsSaving = true;

        try
        {
            await _accountApi.UpdateProfileAsync(
                _profile.Id,
                firstName,
                lastName,
                phoneNumber.Length == 0 ? null : phoneNumber,
                _profile.AvatarUrl);

            FirstName = firstName;
            LastName = lastName;
            PhoneNumber = phoneNumber;
            SetBaseline(firstName, lastName, phoneNumber);

            Announcement = SavedText;
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();
        }
        catch (BarberSearchApiException exception)
        {
            ApplySaveFailure(exception);
        }
        catch (HttpRequestException)
        {
            FormError = NetworkText;
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            FormError = NetworkText;
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Возвращает поля к последнему сохранённому значению.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        if (!CanCancel)
        {
            return;
        }

        FirstName = _baselineFirstName;
        LastName = _baselineLastName;
        PhoneNumber = _baselinePhone;

        ClearFormErrors();
        Announcement = null;
    }

    /// <summary>
    /// Раскрывает форму смены email.
    /// </summary>
    /// <remarks>
    /// Во время кулдауна кнопка — обратный отсчёт, и нажатие ничего не делает: второе письмо
    /// сервер всё равно отвергнет, а лишний запрос тратит лимит.
    /// </remarks>
    [RelayCommand]
    private void StartEmailChange()
    {
        if (!CanStartEmailChange)
        {
            return;
        }

        NewEmail = string.Empty;
        EmailCurrentPassword = string.Empty;
        NewEmailError = null;
        EmailPasswordError = null;
        EmailFormError = null;
        EmailAnnouncement = null;
        IsEmailFormOpen = true;
    }

    /// <summary>
    /// Закрывает форму смены email без запроса.
    /// </summary>
    [RelayCommand]
    private void CancelEmailChange()
    {
        IsEmailFormOpen = false;
        NewEmail = string.Empty;
        EmailCurrentPassword = string.Empty;
        NewEmailError = null;
        EmailPasswordError = null;
        EmailFormError = null;
    }

    /// <summary>
    /// Отправляет заявку на смену адреса.
    /// </summary>
    /// <remarks>
    /// Показанный email после успеха не меняется: адрес изменится только после перехода по
    /// ссылке из письма, и до этого профиль честно показывает прежний. Кулдаун включается и
    /// по успеху, и по <c>409 auth.emailChange.cooldown</c>: оба случая значат «письмо уже
    /// ушло, повторите через минуту».
    /// </remarks>
    [RelayCommand]
    private async Task SendEmailChangeAsync()
    {
        if (IsSendingEmail)
        {
            return;
        }

        var newEmail = NewEmail.Trim();
        var currentPassword = EmailCurrentPassword;

        NewEmailError = null;
        EmailPasswordError = null;
        EmailFormError = null;

        if (newEmail.Length == 0)
        {
            NewEmailError = EmailRequiredText;
        }
        else if (!EmailPattern.IsMatch(newEmail))
        {
            NewEmailError = EmailInvalidText;
        }

        if (currentPassword.Length == 0)
        {
            EmailPasswordError = PasswordRequiredText;
        }

        if (HasNewEmailError || HasEmailPasswordError)
        {
            return;
        }

        IsSendingEmail = true;

        try
        {
            await _accountApi.RequestEmailChangeAsync(newEmail, currentPassword);

            IsEmailFormOpen = false;
            NewEmail = string.Empty;
            EmailCurrentPassword = string.Empty;
            EmailAnnouncement = string.Concat("Письмо отправлено на ", newEmail);

            StartCooldown();
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();
        }
        catch (BarberSearchApiException exception)
        {
            ApplyEmailChangeFailure(exception);
        }
        catch (HttpRequestException)
        {
            EmailFormError = NetworkText;
        }
        catch (OperationCanceledException)
        {
            EmailFormError = NetworkText;
        }
        finally
        {
            IsSendingEmail = false;
        }
    }

    /// <summary>
    /// Читает профиль и раскладывает его по экрану.
    /// </summary>
    /// <remarks>
    /// Тихое обновление не показывает индикатор и не стирает данные при ошибке. Первая
    /// загрузка без данных показывает состояние ошибки с «Повторить»; <c>401</c> гасит
    /// сессию, остальные ответы сессию не трогают.
    /// </remarks>
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
            ApplyProfile(profile);
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
    /// Раскладывает отказ сохранения по полям и текстам.
    /// </summary>
    private void ApplySaveFailure(BarberSearchApiException exception)
    {
        if (exception.ErrorCodes.Contains(UpdateFirstNameCode, StringComparer.Ordinal))
        {
            FirstNameError = NameRequiredText;

            return;
        }

        if (exception.ErrorCodes.Contains(UpdateLastNameCode, StringComparer.Ordinal))
        {
            LastNameError = LastNameRequiredText;

            return;
        }

        FormError = Describe(exception, SaveFailedText);
    }

    /// <summary>
    /// Раскладывает отказ смены email по полям, текстам и кулдауну.
    /// </summary>
    /// <remarks>
    /// Порядок повторяет веб: сначала публичные коды, потом <c>429</c>, потом общий текст.
    /// Занятый адрес показывается у поля: исправлять пользователю именно его.
    /// </remarks>
    private void ApplyEmailChangeFailure(BarberSearchApiException exception)
    {
        if (HasCode(exception, ChangeEmailNewEmailCode))
        {
            NewEmailError = EmailChangeNewEmailCodeText;
        }
        else if (HasCode(exception, ChangeEmailSameEmailCode))
        {
            NewEmailError = EmailChangeSameEmailText;
        }
        else if (HasCode(exception, ChangeEmailInvalidPasswordCode))
        {
            EmailPasswordError = EmailChangeInvalidPasswordText;
        }
        else if (HasCode(exception, ChangeEmailTakenCode))
        {
            NewEmailError = EmailChangeTakenText;
        }
        else if (HasCode(exception, ChangeEmailCooldownCode))
        {
            EmailFormError = EmailCooldownText;
            StartCooldown();
        }
        else if (HasCode(exception, ChangeEmailUnavailableCode))
        {
            EmailFormError = EmailUnavailableText;
        }
        else if (HasCode(exception, ChangeEmailInvalidCode))
        {
            EmailFormError = EmailInvalidLinkText;
        }
        else if (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            EmailFormError = EmailTooManyAttemptsText;
        }
        else
        {
            EmailFormError = EmailChangeFailedText;
        }
    }

    /// <summary>
    /// Включает кулдаун на полную длину.
    /// </summary>
    private void StartCooldown() => CooldownRemaining = CooldownSeconds;

    /// <summary>
    /// Запоминает значения, к которым вернёт «Отменить».
    /// </summary>
    private void SetBaseline(string firstName, string lastName, string phoneNumber)
    {
        _baselineFirstName = firstName;
        _baselineLastName = lastName;
        _baselinePhone = phoneNumber;
        _formInitialized = true;

        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanCancel));
    }

    /// <summary>
    /// Гасит сессию: пара мертва, страница вернёт на вкладку «Профиль».
    /// </summary>
    private async Task EndSessionAsync()
    {
        ApplySignedOutState();

        await _session.ClearAsync(CancellationToken.None);
    }

    /// <summary>
    /// Стирает ошибки формы личных данных.
    /// </summary>
    private void ClearFormErrors()
    {
        FirstNameError = null;
        LastNameError = null;
        FormError = null;
    }

    /// <summary>
    /// Переводит сбой API в текст по статусу, не показывая английский <c>detail</c>.
    /// </summary>
    private static string Describe(BarberSearchApiException exception, string fallback) =>
        exception.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => TooManyRequestsText,
            HttpStatusCode.Unauthorized => SessionExpiredText,
            _ => fallback,
        };

    /// <summary>
    /// Проверяет наличие публичного кода ошибки.
    /// </summary>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Правка поля сбрасывает ошибку под ним и прошлое сообщение об успехе.
    /// </summary>
    partial void OnFirstNameChanged(string value)
    {
        _ = value;

        FirstNameError = null;
        OnFormInputChanged();
    }

    /// <summary>
    /// Правка поля сбрасывает ошибку под ним и прошлое сообщение об успехе.
    /// </summary>
    partial void OnLastNameChanged(string value)
    {
        _ = value;

        LastNameError = null;
        OnFormInputChanged();
    }

    /// <summary>
    /// Правка телефона сбрасывает прошлое сообщение об успехе.
    /// </summary>
    partial void OnPhoneNumberChanged(string value)
    {
        _ = value;

        OnFormInputChanged();
    }

    /// <summary>
    /// Общая реакция формы на правку: снять сообщения и пересчитать доступность кнопок.
    /// </summary>
    private void OnFormInputChanged()
    {
        Announcement = null;
        FormError = null;

        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanCancel));
    }

    /// <summary>
    /// Правка адреса снимает ошибку под полем.
    /// </summary>
    partial void OnNewEmailChanged(string value)
    {
        _ = value;
        NewEmailError = null;
        EmailFormError = null;
    }

    /// <summary>
    /// Правка пароля снимает ошибку под полем.
    /// </summary>
    partial void OnEmailCurrentPasswordChanged(string value)
    {
        _ = value;
        EmailPasswordError = null;
        EmailFormError = null;
    }
}
