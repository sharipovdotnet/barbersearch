using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using BarberSearch.Contracts.Auth.Register;
using BarberSearch.Contracts.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Legal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран «Регистрация»: форма самостоятельного создания аккаунта клиента.
/// </summary>
/// <remarks>
/// <para>
/// Повторяет форму веба (<c>frontend/src/components/register-form.tsx</c>): порядок полей,
/// клиентские проверки, тексты и автовход после <c>201</c>. Регистрация не выдаёт пару,
/// поэтому после успешного создания сразу выполняется обычный вход тем же паролем — человек
/// не должен вводить его дважды.
/// </para>
/// <para>
/// Состояний два и они не пересекаются: форма и карточка «Аккаунт создан». Вторая нужна,
/// когда аккаунт создан, а автовход не удался: повторная отправка получила бы <c>409</c>,
/// поэтому форма заменяется объяснением и кнопкой «Войти».
/// </para>
/// <para>
/// Пароль не срезается: пробелы внутри и по краям для сервера значимы. Email и имена
/// срезаются только для проверки и отправки. Английский <c>detail</c> не показывается
/// никогда — ветвление идёт по статусу и публичным кодам, как в <see cref="ProfileViewModel"/>.
/// </para>
/// </remarks>
public sealed partial class RegisterViewModel : ObservableObject
{
    /// <summary>
    /// Минимальная длина пароля — та же, что проверяет сервер.
    /// </summary>
    public const int MinPasswordLength = 12;

    /// <summary>
    /// Максимальная длина пароля — та же, что проверяет сервер.
    /// </summary>
    public const int MaxPasswordLength = 128;

    /// <summary>
    /// Верхняя граница адреса — из серверного валидатора (3..254).
    /// </summary>
    private const int MaxEmailLength = 254;

    // Сообщения клиентских проверок и серверных кодов совпадают дословно: человек не должен
    // видеть два разных текста об одном и том же правиле.
    private const string FirstNameRequiredText = "Введите имя.";
    private const string LastNameRequiredText = "Введите фамилию.";
    private const string EmailRequiredText = "Введите email.";
    private const string EmailInvalidText = "Проверьте email: нужен адрес вида name@example.com.";
    private const string PasswordRequiredText = "Введите пароль.";
    private const string PasswordTooShortText = "Пароль должен быть не короче 12 символов.";
    private const string PasswordTooLongText = "Пароль должен быть не длиннее 128 символов.";
    private const string PasswordCommonText = "Этот пароль слишком распространён. Выберите другой.";
    private const string PasswordEqualsEmailText = "Пароль не должен совпадать с email.";
    private const string PasswordEqualsNameText = "Пароль не должен совпадать с именем или фамилией.";
    private const string PasswordRepeatText = "Пароли не совпадают.";
    private const string ConsentRequiredText = "Отметьте согласие на обработку персональных данных.";
    private const string ConsentVersionText =
        "Политика обработки персональных данных обновилась. Обновите приложение и подтвердите согласие снова.";
    private const string EmailTakenText = "Аккаунт с таким email уже есть. Войдите или укажите другой адрес.";
    private const string RateLimitText = "Слишком много попыток регистрации. Подождите минуту и повторите.";
    private const string FallbackText = "Не удалось создать аккаунт. Попробуйте ещё раз.";
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";

    /// <summary>
    /// Коды, которые сервер отдаёт на регистрацию; в Contracts объявлены не все.
    /// </summary>
    private const string EmailTakenCode = "user.create.emailTaken";
    private const string EmailCode = "user.email";
    private const string FirstNameCode = "user.firstName";
    private const string LastNameCode = "user.lastName";
    private const string PasswordRequiredCode = "auth.password.required";
    private const string PasswordTooShortCode = "auth.password.tooShort";
    private const string PasswordTooLongCode = "auth.password.tooLong";
    private const string PasswordCommonCode = "auth.password.common";
    private const string PasswordEqualsEmailCode = "auth.password.equalsEmail";
    private const string PasswordEqualsNameCode = "auth.password.equalsName";

    /// <summary>
    /// Культура подписи редакции Политики: та же, что у русского текста интерфейса.
    /// </summary>
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly IAuthApi _authApi;
    private readonly TokenSession _session;
    private readonly LegalOptions _legal;
    private readonly string _privacyPolicyLinkText;

    /// <summary>
    /// Аккаунт уже создан этим экраном: вторую регистрацию отправлять нельзя.
    /// </summary>
    private bool _isRegistered;

    /// <summary>
    /// Создаёт форму регистрации.
    /// </summary>
    /// <param name="authApi">Создание аккаунта и автовход.</param>
    /// <param name="session">Сессия, в которую ложится пара после автовхода.</param>
    /// <param name="legal">Адрес публичного сайта с Политикой.</param>
    public RegisterViewModel(IAuthApi authApi, TokenSession session, LegalOptions legal)
    {
        ArgumentNullException.ThrowIfNull(authApi);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(legal);

        _authApi = authApi;
        _session = session;
        _legal = legal;
        _privacyPolicyLinkText = string.Concat(
            "Политика обработки персональных данных (редакция от ",
            PrivacyPolicy.EditionDate.ToString("d MMMM yyyy 'г.'", RussianCulture),
            ").");
    }

    /// <summary>
    /// Аккаунт создан и пара сохранена: страница закрывает форму.
    /// </summary>
    public event EventHandler? Registered;

    /// <summary>
    /// Нажата «Войти» на карточке «Аккаунт создан»: страница возвращает форму входа.
    /// </summary>
    public event EventHandler? SignInRequested;

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
    /// Email из формы.
    /// </summary>
    [ObservableProperty]
    private string _email = string.Empty;

    /// <summary>
    /// Пароль из формы; стирается после создания аккаунта.
    /// </summary>
    [ObservableProperty]
    private string _password = string.Empty;

    /// <summary>
    /// Повтор пароля из формы.
    /// </summary>
    [ObservableProperty]
    private string _passwordRepeat = string.Empty;

    /// <summary>
    /// Отметка согласия на обработку персональных данных.
    /// </summary>
    [ObservableProperty]
    private bool _isConsentGiven;

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
    /// Ошибка поля «Email».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmailError))]
    private string? _emailError;

    /// <summary>
    /// Ошибка поля «Пароль».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasswordError))]
    private string? _passwordError;

    /// <summary>
    /// Ошибка поля «Повтор пароля».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasswordRepeatError))]
    private string? _passwordRepeatError;

    /// <summary>
    /// Ошибка отметки согласия.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConsentError))]
    private string? _consentError;

    /// <summary>
    /// Общая ошибка формы; <c>null</c>, когда всё разложено по полям.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormError))]
    private string? _formError;

    /// <summary>
    /// Идёт отправка: кнопка называется «Создаём аккаунт…» и заблокирована.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormEnabled))]
    [NotifyPropertyChangedFor(nameof(RegisterButtonText))]
    private bool _isSubmitting;

    /// <summary>
    /// Аккаунт создан, но автовход не удался: показана карточка успеха вместо формы.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowForm))]
    [NotifyPropertyChangedFor(nameof(ShowCreatedCard))]
    private bool _isAccountCreated;

    /// <summary>
    /// Видна ли форма регистрации.
    /// </summary>
    public bool ShowForm => !IsAccountCreated;

    /// <summary>
    /// Видна ли карточка «Аккаунт создан».
    /// </summary>
    public bool ShowCreatedCard => IsAccountCreated;

    /// <summary>
    /// Можно ли править поля и нажимать «Зарегистрироваться».
    /// </summary>
    public bool IsFormEnabled => !IsSubmitting;

    /// <summary>
    /// Надпись на кнопке регистрации: во время запроса — «Создаём аккаунт…».
    /// </summary>
    public string RegisterButtonText => IsSubmitting ? "Создаём аккаунт…" : "Зарегистрироваться";

    /// <summary>
    /// Есть ошибка поля «Имя».
    /// </summary>
    public bool HasFirstNameError => !string.IsNullOrEmpty(FirstNameError);

    /// <summary>
    /// Есть ошибка поля «Фамилия».
    /// </summary>
    public bool HasLastNameError => !string.IsNullOrEmpty(LastNameError);

    /// <summary>
    /// Есть ошибка поля «Email».
    /// </summary>
    public bool HasEmailError => !string.IsNullOrEmpty(EmailError);

    /// <summary>
    /// Есть ошибка поля «Пароль».
    /// </summary>
    public bool HasPasswordError => !string.IsNullOrEmpty(PasswordError);

    /// <summary>
    /// Есть ошибка поля «Повтор пароля».
    /// </summary>
    public bool HasPasswordRepeatError => !string.IsNullOrEmpty(PasswordRepeatError);

    /// <summary>
    /// Есть ошибка отметки согласия.
    /// </summary>
    public bool HasConsentError => !string.IsNullOrEmpty(ConsentError);

    /// <summary>
    /// Есть общая ошибка формы.
    /// </summary>
    public bool HasFormError => !string.IsNullOrEmpty(FormError);

    /// <summary>
    /// Подпись ссылки на Политику с датой редакции.
    /// </summary>
    /// <remarks>
    /// Формат — длинная дата русской культуры: она даёт ту же подпись, что
    /// <c>Intl.DateTimeFormat</c> веба, включая «г.». Локаль приложения одна, поэтому
    /// культура здесь константа, а не выбор пользователя. Строка собирается один раз
    /// в конструкторе: редакция — константа сборки, меняться на лету ей нечем.
    /// </remarks>
    public string PrivacyPolicyLinkText => _privacyPolicyLinkText;

    /// <summary>
    /// Адрес страницы Политики: страница открывает его в браузере.
    /// </summary>
    public Uri PrivacyPolicyUrl => _legal.PrivacyPolicyUrl;

    /// <summary>
    /// Проверяет поля, создаёт аккаунт и входит тем же паролем.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Повторное нажатие и нажатие после <c>201</c> игнорируются: второй запрос получил бы
    /// <c>409</c>, а аккаунт уже создан.
    /// </para>
    /// <para>
    /// Таймаут транспорта приходит отменой без отмены жизненного цикла — для человека это
    /// отсутствие связи, а не отказ.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        if (IsSubmitting || _isRegistered)
        {
            return;
        }

        FormError = null;

        if (!Validate())
        {
            return;
        }

        var email = Email.Trim();
        var password = Password;

        var request = new RegisterRequest(
            email,
            password,
            FirstName.Trim(),
            LastName.Trim(),
            PrivacyPolicy.Version);

        IsSubmitting = true;

        var isCreated = false;

        try
        {
            await _authApi.RegisterAsync(request, cancellationToken);

            isCreated = true;
            _isRegistered = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Экран закрыли — не ошибка.
        }
        catch (OperationCanceledException)
        {
            FormError = NetworkText;
        }
        catch (BarberSearchApiException exception)
        {
            ApplyFailure(exception);
        }
        catch (HttpRequestException)
        {
            FormError = NetworkText;
        }
        catch (InvalidOperationException)
        {
            // Пустой ответ: сервер нарушил контракт, но экран падать не должен.
            FormError = FallbackText;
        }
        finally
        {
            if (!isCreated)
            {
                IsSubmitting = false;
            }
        }

        if (!isCreated)
        {
            return;
        }

        ClearSecrets();

        try
        {
            var tokens = await _authApi.LoginAsync(email, password, cancellationToken);

            await _session.ApplyAsync(tokens, cancellationToken);

            Registered?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Пара не получена, но аккаунт есть: повторять регистрацию нельзя.
            IsAccountCreated = true;
        }
        catch (OperationCanceledException)
        {
            IsAccountCreated = true;
        }
        catch (BarberSearchApiException)
        {
            IsAccountCreated = true;
        }
        catch (HttpRequestException)
        {
            IsAccountCreated = true;
        }
        catch (InvalidOperationException)
        {
            // Пара без refresh: войти можно только заново.
            IsAccountCreated = true;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    /// <summary>
    /// Просит страницу вернуться к форме входа.
    /// </summary>
    [RelayCommand]
    private void SignIn() => SignInRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Проверяет поля формы и раскладывает ошибки по полям.
    /// </summary>
    /// <returns><c>true</c>, если запрос можно отправлять.</returns>
    private bool Validate()
    {
        var email = Email.Trim();

        FirstNameError = string.IsNullOrWhiteSpace(FirstName) ? FirstNameRequiredText : null;
        LastNameError = string.IsNullOrWhiteSpace(LastName) ? LastNameRequiredText : null;
        EmailError = string.IsNullOrEmpty(email)
            ? EmailRequiredText
            : email.Length > MaxEmailLength || !EmailPattern().IsMatch(email)
                ? EmailInvalidText
                : null;
        PasswordError = DescribePassword(Password, email);
        PasswordRepeatError = PasswordRepeat == Password ? null : PasswordRepeatText;
        ConsentError = IsConsentGiven ? null : ConsentRequiredText;

        return !HasFirstNameError
            && !HasLastNameError
            && !HasEmailError
            && !HasPasswordError
            && !HasPasswordRepeatError
            && !HasConsentError;
    }

    /// <summary>
    /// Проверяет пароль по правилам сервера, доступным без словаря частых паролей.
    /// </summary>
    /// <remarks>
    /// Порядок проверок повторяет веб: пустой, короткий, длинный, совпадает с email (или его
    /// local-part), совпадает с именем или фамилией. Словарь частых паролей на клиенте не
    /// воспроизводится — <c>auth.password.common</c> приходит только от сервера.
    /// </remarks>
    private string? DescribePassword(string password, string email)
    {
        if (password.Length == 0)
        {
            return PasswordRequiredText;
        }

        if (password.Length < MinPasswordLength)
        {
            return PasswordTooShortText;
        }

        if (password.Length > MaxPasswordLength)
        {
            return PasswordTooLongText;
        }

        if (string.Equals(password, email, StringComparison.Ordinal)
            || string.Equals(password, EmailLocalPart(email), StringComparison.Ordinal))
        {
            return PasswordEqualsEmailText;
        }

        if (string.Equals(password, FirstName.Trim(), StringComparison.Ordinal)
            || string.Equals(password, LastName.Trim(), StringComparison.Ordinal))
        {
            return PasswordEqualsNameText;
        }

        return null;
    }

    /// <summary>
    /// Раскладывает отказ сервера по полям и общей строке формы.
    /// </summary>
    /// <remarks>
    /// <c>429</c> и занятый адрес проверяются раньше кодов: лимитер и уникальность — это
    /// одна ошибка формы, показывать её под полем бессмысленно. Устаревшая редакция Политики
    /// авторитетна: приложение просит обновиться, а не повторять отправку.
    /// </remarks>
    private void ApplyFailure(BarberSearchApiException exception)
    {
        if (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            FormError = RateLimitText;

            return;
        }

        if (HasCode(exception, EmailTakenCode))
        {
            FormError = EmailTakenText;

            return;
        }

        var hasFieldError = false;
        var hasUnknownCode = false;
        var isPolicyOutdated = false;

        foreach (var code in exception.ErrorCodes)
        {
            switch (code)
            {
                case PasswordRequiredCode:
                    PasswordError = PasswordRequiredText;
                    hasFieldError = true;
                    break;
                case PasswordTooShortCode:
                    PasswordError = PasswordTooShortText;
                    hasFieldError = true;
                    break;
                case PasswordTooLongCode:
                    PasswordError = PasswordTooLongText;
                    hasFieldError = true;
                    break;
                case PasswordCommonCode:
                    PasswordError = PasswordCommonText;
                    hasFieldError = true;
                    break;
                case PasswordEqualsEmailCode:
                    PasswordError = PasswordEqualsEmailText;
                    hasFieldError = true;
                    break;
                case PasswordEqualsNameCode:
                    PasswordError = PasswordEqualsNameText;
                    hasFieldError = true;
                    break;
                case EmailCode:
                    EmailError = EmailInvalidText;
                    hasFieldError = true;
                    break;
                case FirstNameCode:
                    FirstNameError = FirstNameRequiredText;
                    hasFieldError = true;
                    break;
                case LastNameCode:
                    LastNameError = LastNameRequiredText;
                    hasFieldError = true;
                    break;
                case ApiErrorCodes.Auth.RegisterConsentRequired:
                    ConsentError = ConsentRequiredText;
                    hasFieldError = true;
                    break;
                case ApiErrorCodes.Auth.RegisterConsentVersion:
                    isPolicyOutdated = true;
                    break;
                default:
                    hasUnknownCode = true;
                    break;
            }
        }

        FormError = isPolicyOutdated
            ? ConsentVersionText
            : hasUnknownCode || !hasFieldError
                ? FallbackText
                : null;
    }

    /// <summary>
    /// Стирает пароли из памяти после <c>201</c>.
    /// </summary>
    /// <remarks>
    /// Автовход использует локальную копию пароля до конца метода; в свойствах ему делать
    /// нечего — после выхода он не должен всплыть повторно.
    /// </remarks>
    private void ClearSecrets()
    {
        Password = string.Empty;
        PasswordRepeat = string.Empty;
    }

    /// <summary>
    /// Local-part адреса: сервер не разрешает пароль, совпадающий с ним.
    /// </summary>
    private static string EmailLocalPart(string email)
    {
        var at = email.LastIndexOf('@');

        return at > 0 ? email[..at] : email;
    }

    /// <summary>
    /// Проверяет наличие публичного кода ошибки.
    /// </summary>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Адрес вида <c>name@example.com</c>: та же проверка, что в вебе.
    /// </summary>
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();

    /// <summary>
    /// Правка поля снимает ошибку под ним и общую ошибку формы.
    /// </summary>
    partial void OnFirstNameChanged(string value)
    {
        _ = value;
        FirstNameError = null;
        FormError = null;
    }

    /// <summary>
    /// Правка поля снимает ошибку под ним и общую ошибку формы.
    /// </summary>
    partial void OnLastNameChanged(string value)
    {
        _ = value;
        LastNameError = null;
        FormError = null;
    }

    /// <summary>
    /// Правка поля снимает ошибку под ним и общую ошибку формы.
    /// </summary>
    partial void OnEmailChanged(string value)
    {
        _ = value;
        EmailError = null;
        FormError = null;
    }

    /// <summary>
    /// Правка поля снимает ошибку под ним и общую ошибку формы.
    /// </summary>
    partial void OnPasswordChanged(string value)
    {
        _ = value;
        PasswordError = null;
        FormError = null;
    }

    /// <summary>
    /// Правка поля снимает ошибку под ним и общую ошибку формы.
    /// </summary>
    partial void OnPasswordRepeatChanged(string value)
    {
        _ = value;
        PasswordRepeatError = null;
        FormError = null;
    }

    /// <summary>
    /// Отметка согласия снимает ошибку под ней.
    /// </summary>
    partial void OnIsConsentGivenChanged(bool value)
    {
        _ = value;

        if (value)
        {
            ConsentError = null;
        }
    }
}
