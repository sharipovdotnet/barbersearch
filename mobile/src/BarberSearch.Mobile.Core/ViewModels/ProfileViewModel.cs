using System.Net;
using BarberSearch.Contracts.Common;
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
/// Вкладка «Профиль»: форма входа без сессии и хаб аккаунта с сессией.
/// </summary>
/// <remarks>
/// <para>
/// Отдельной страницы входа нет: пока сессии нет, на экране форма, после входа — карточка
/// личности и переходы в разделы («Личные данные», «Мои отзывы», «Безопасность»). Экраны
/// разделов открываются обычными страницами Shell поверх стека «Профиля», поэтому вкладка
/// остаётся единственным экраном входа. Регистрация — исключение: это отдельная страница
/// поверх того же стека, и ссылка на неё стоит под формой входа.
/// </para>
/// <para>
/// Состояний больше, чем у других вкладок, и они не пересекаются: проверяем сессию, форма
/// входа, загрузка профиля, профиль, ошибка загрузки. Профиль читается из
/// <c>GET api/users/me</c>, а не из <c>/auth/me</c>: второй — проекция claims и источник
/// <c>userId</c> для «Записей», а имя, телефон и подтверждения меняются без перевыпуска
/// токена. Без сессии не делается ни одного запроса.
/// </para>
/// <para>
/// Повторное появление вкладки обновляет профиль тихо: уже показанные данные не стираются,
/// а ошибка тихого обновления их не убирает — из аккаунта сбой сети не выкидывает. Тексты
/// ошибок входа — дословно из веба, порядок проверок сохранён: лимит (<c>429</c>) раньше
/// кодов, <c>401</c> — после них. Английский <c>detail</c> не показывается никогда.
/// </para>
/// </remarks>
public sealed partial class ProfileViewModel : ObservableObject
{
    // Коды, которые сервер отдаёт при входе. В Contracts есть только lockedOut,
    // остальные живут в обработчиках Application и на клиенте объявлены здесь.
    private const string InvalidCredentialsCode = "auth.login.invalidCredentials";
    private const string InactiveCode = "auth.login.inactive";

    private const string InvalidCredentialsText = "Неверный email или пароль.";
    private const string TooManyAttemptsText = "Слишком много попыток входа. Подождите минуту и повторите.";
    private const string LockedOutText = "Слишком много неудачных попыток. Вход временно заблокирован — попробуйте позже.";
    private const string InactiveText = "Аккаунт деактивирован. Восстановить доступ поможет поддержка.";
    private const string SignInFailedText = "Не удалось войти. Попробуйте ещё раз.";
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";

    /// <summary>
    /// Заголовок ошибки загрузки профиля — дословно из веб-кабинета.
    /// </summary>
    private const string ProfileLoadFailedTitle = "Не удалось загрузить профиль";

    /// <summary>
    /// Причина ошибки второй строкой: тот же заголовок, но с точкой, как в вебе.
    /// </summary>
    private const string ProfileLoadFailedText = "Не удалось загрузить профиль.";

    private const string SessionExpiredText = "Сессия истекла — войдите снова.";

    private readonly IAuthApi _authApi;
    private readonly IAccountApi _accountApi;
    private readonly TokenSession _session;

    /// <summary>
    /// Профиль из базы; <c>null</c>, пока он не загружен или после выхода.
    /// </summary>
    private MyProfileResponse? _profile;

    /// <summary>
    /// Создаёт экран профиля.
    /// </summary>
    /// <param name="authApi">Вход и выход.</param>
    /// <param name="accountApi">Профиль текущего пользователя.</param>
    /// <param name="session">Сессия — источник правды о входе.</param>
    public ProfileViewModel(IAuthApi authApi, IAccountApi accountApi, TokenSession session)
    {
        ArgumentNullException.ThrowIfNull(authApi);
        ArgumentNullException.ThrowIfNull(accountApi);
        ArgumentNullException.ThrowIfNull(session);

        _authApi = authApi;
        _accountApi = accountApi;
        _session = session;
    }

    /// <summary>
    /// Нажата «Личные данные»: страница открывает экран профиля.
    /// </summary>
    public event EventHandler? EditProfileRequested;

    /// <summary>
    /// Нажаты «Мои отзывы»: страница открывает список отзывов.
    /// </summary>
    public event EventHandler? ReviewsRequested;

    /// <summary>
    /// Нажата «Безопасность» (в том числе предупреждение о неподтверждённом email).
    /// </summary>
    public event EventHandler? SecurityRequested;

    /// <summary>
    /// Нажата «Зарегистрироваться» под формой входа: страница открывает экран регистрации.
    /// </summary>
    public event EventHandler? RegisterRequested;

    /// <summary>
    /// Идёт проверка сессии при открытии экрана.
    /// </summary>
    /// <remarks>
    /// Стартует взведённым: до первой загрузки на экране должен быть индикатор, а не форма
    /// входа, которая через мгновение исчезнет.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormVisible))]
    [NotifyPropertyChangedFor(nameof(IsProfileVisible))]
    [NotifyPropertyChangedFor(nameof(ShowProfileLoading))]
    [NotifyPropertyChangedFor(nameof(ShowProfileError))]
    private bool _isLoading = true;

    /// <summary>
    /// Показывать ли аккаунт вместо формы входа.
    /// </summary>
    /// <remarks>
    /// <c>true</c> только когда пара есть и сервер её принял — хотя бы одним ответом.
    /// Пара в хранилище без подтверждения — ещё не повод показывать аккаунт: email
    /// показывать нечем, а врать пустой строкой хуже, чем попросить войти.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormVisible))]
    [NotifyPropertyChangedFor(nameof(IsProfileVisible))]
    [NotifyPropertyChangedFor(nameof(ShowProfileLoading))]
    [NotifyPropertyChangedFor(nameof(ShowProfileError))]
    private bool _isAuthenticated;

    /// <summary>
    /// Идёт чтение профиля.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileVisible))]
    [NotifyPropertyChangedFor(nameof(ShowProfileLoading))]
    [NotifyPropertyChangedFor(nameof(ShowProfileError))]
    private bool _isProfileLoading;

    /// <summary>
    /// Ошибка чтения профиля; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfileError))]
    [NotifyPropertyChangedFor(nameof(ShowProfileError))]
    private string? _profileError;

    /// <summary>
    /// Идёт вход: кнопка называется «Входим…» и заблокирована.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoginButtonText))]
    [NotifyPropertyChangedFor(nameof(IsFormEnabled))]
    private bool _isSigningIn;

    /// <summary>
    /// Идёт выход: кнопка называется «Выходим…» и заблокирована.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogoutButtonText))]
    private bool _isSigningOut;

    /// <summary>
    /// Введённый email.
    /// </summary>
    [ObservableProperty]
    private string _email = string.Empty;

    /// <summary>
    /// Введённый пароль; стирается сразу после успешного входа.
    /// </summary>
    [ObservableProperty]
    private string _password = string.Empty;

    /// <summary>
    /// Email вошедшего пользователя: из профиля либо из формы, пока профиль не ответил.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileVisible))]
    [NotifyPropertyChangedFor(nameof(ShowProfileLoading))]
    [NotifyPropertyChangedFor(nameof(ShowProfileError))]
    private string? _profileEmail;

    /// <summary>
    /// Имя и фамилия для шапки хаба.
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// Инициалы для круглой подписи вместо аватара.
    /// </summary>
    [ObservableProperty]
    private string _initials = string.Empty;

    /// <summary>
    /// Русская подпись роли: «Клиент», «Барбер», «Администратор».
    /// </summary>
    [ObservableProperty]
    private string _roleText = string.Empty;

    /// <summary>
    /// Статус подтверждения email: «Адрес подтверждён.» или «Адрес не подтверждён.».
    /// </summary>
    [ObservableProperty]
    private string _emailStatusText = string.Empty;

    /// <summary>
    /// Подтверждён ли адрес; <c>false</c> до загрузки профиля.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmailWarning))]
    private bool _isEmailVerified;

    /// <summary>
    /// Текст ошибки для пользователя; <c>null</c>, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>
    /// Есть ли ошибка, которую надо показать.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Видна ли форма входа; вычисляется из состояния экрана.
    /// </summary>
    public bool IsFormVisible => !IsAuthenticated && !IsLoading;

    /// <summary>
    /// Виден ли хаб аккаунта: сессия подтверждена, профиль загружен либо показан email из формы.
    /// </summary>
    public bool IsProfileVisible =>
        IsAuthenticated && !IsLoading && !ShowProfileLoading && !ShowProfileError;

    /// <summary>
    /// Идёт первая загрузка профиля: карточки ещё нечего показать.
    /// </summary>
    /// <remarks>
    /// Тихое обновление не показывает индикатор: данные на экране уже есть, и подменять их
    /// спиннером значит мигать при каждом возврате на вкладку.
    /// </remarks>
    public bool ShowProfileLoading => IsAuthenticated
        && IsProfileLoading
        && _profile is null
        && ProfileEmail is null;

    /// <summary>
    /// Профиль не загрузился, и показать нечего: ошибка с «Повторить» вместо хаба.
    /// </summary>
    /// <remarks>
    /// Если после только что выполненного входа известен email, хаб всё равно показывается:
    /// вход состоялся, и терять его из-за показа имени нельзя.
    /// </remarks>
    public bool ShowProfileError => IsAuthenticated
        && !IsProfileLoading
        && ProfileError is not null
        && _profile is null
        && ProfileEmail is null;

    /// <summary>
    /// Ошибка чтения профиля при уже показанном хабе: строка и «Повторить» под карточкой.
    /// </summary>
    public bool HasProfileError => ProfileError is not null;

    /// <summary>
    /// Показывать предупреждение «Email не подтверждён».
    /// </summary>
    public bool ShowEmailWarning => _profile is not null && !IsEmailVerified;

    /// <summary>
    /// Можно ли править поля и нажимать «Войти».
    /// </summary>
    public bool IsFormEnabled => !IsSigningIn;

    /// <summary>
    /// Надпись на кнопке входа: во время запроса — «Входим…».
    /// </summary>
    public string LoginButtonText => IsSigningIn ? "Входим…" : "Войти";

    /// <summary>
    /// Надпись на кнопке выхода: во время запроса — «Выходим…».
    /// </summary>
    public string LogoutButtonText => IsSigningOut ? "Выходим…" : "Выйти";

    /// <summary>
    /// Проверяет сохранённую сессию и тихо обновляет профиль при каждом появлении вкладки.
    /// </summary>
    /// <remarks>
    /// Без сохранённой пары сеть не трогается вовсе: на старте без входа не должно быть
    /// ни одного запроса. С парой экран читает профиль — первый раз с индикатором, повторно
    /// молча, не стирая уже показанные данные.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated && _profile is null && ProfileEmail is null)
        {
            IsLoading = true;
        }

        ErrorMessage = null;

        try
        {
            await _session.EnsureLoadedAsync(cancellationToken);

            if (!_session.IsAuthenticated)
            {
                ApplySignedOutState();
                return;
            }

            IsAuthenticated = true;

            await LoadProfileAsync(silent: _profile is not null || ProfileEmail is not null, cancellationToken);
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
    /// Возвращает экран к форме входа после завершения сессии.
    /// </summary>
    /// <remarks>
    /// Вызывается страницей по событию <see cref="TokenSession.SessionEnded"/>: событие
    /// приходит с сетевого потока, а менять привязки можно только на UI-потоке, о котором
    /// Core не знает. Заодно стирает пароль, профиль и адреса — данные прошлого пользователя
    /// не должны остаться на экране.
    /// </remarks>
    public void ApplySignedOutState()
    {
        IsAuthenticated = false;
        _profile = null;
        ProfileEmail = null;
        DisplayName = string.Empty;
        Initials = string.Empty;
        RoleText = string.Empty;
        EmailStatusText = string.Empty;
        IsEmailVerified = false;
        ProfileError = null;
        Email = string.Empty;
        Password = string.Empty;
    }

    /// <summary>
    /// Возвращает форму входа по событию завершения сессии.
    /// </summary>
    /// <remarks>
    /// <paramref name="isExpired"/> отличает истёкшую сессию от собственного выхода: в первом
    /// случае человеку нужно объяснить, почему его выкинуло, во втором он сам этого хотел и
    /// сообщение было бы шумом. Событие <see cref="TokenSession.SessionEnded"/> причины не несёт,
    /// поэтому решение принимает страница — она знает, был ли это выход.
    /// </remarks>
    /// <param name="isExpired"><c>true</c>, если сессия завершилась не по желанию пользователя.</param>
    public void ApplySessionEndedState(bool isExpired)
    {
        ApplySignedOutState();

        if (isExpired)
        {
            ErrorMessage = SessionExpiredText;
        }
    }

    /// <summary>
    /// Входит по email и паролю.
    /// </summary>
    /// <remarks>
    /// Пароль стирается сразу после того, как сервер выдал пару: держать его в памяти
    /// дольше не для чего, а после выхода он не должен утечь повторно. Профиль читается
    /// молча: email из формы уже показывается, и хаб не должен ждать второго запроса.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        IsSigningIn = true;
        ErrorMessage = null;

        var email = Email.Trim();

        try
        {
            var tokens = await _authApi.LoginAsync(email, Password, cancellationToken);

            await _session.ApplyAsync(tokens, cancellationToken);

            Password = string.Empty;
            ProfileEmail = email;
            IsAuthenticated = true;

            await LoadProfileAsync(silent: true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Отмена пользователем или жизненным циклом экрана — не ошибка.
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта тоже приходит отменой: для человека это отсутствие связи.
            ErrorMessage = NetworkText;
        }
        catch (BarberSearchApiException exception)
        {
            ErrorMessage = DescribeSignInError(exception);
        }
        catch (HttpRequestException)
        {
            ErrorMessage = NetworkText;
        }
        catch (InvalidOperationException)
        {
            // Пустой ответ или пара без refresh: сервер нарушил контракт, но экран
            // не должен падать из-за этого.
            ErrorMessage = SignInFailedText;
        }
        finally
        {
            IsSigningIn = false;
        }
    }

    /// <summary>
    /// Выходит из аккаунта.
    /// </summary>
    /// <remarks>
    /// Локальная сессия гасится всегда, даже если сервер отказал или сети нет: иначе
    /// «Выйти» на чужом телефоне работало бы только при хорошей связи. Ошибка сервера
    /// при этом не показывается — пользователь просил выйти, а не узнать причину.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task LogoutAsync(CancellationToken cancellationToken)
    {
        IsSigningOut = true;

        try
        {
            var refreshToken = _session.RefreshToken;

            try
            {
                await _authApi.LogoutAsync(refreshToken, cancellationToken);
            }
            catch (BarberSearchApiException)
            {
                // Сервер не отозвал токен — выход всё равно локальный.
            }
            catch (HttpRequestException)
            {
                // Сети нет — токен останется жить до истечения срока, сессия гаснет локально.
            }
            catch (OperationCanceledException)
            {
                // Таймаут транспорта приходит отменой; токен остаётся жить, выход всё равно локальный.
            }
            finally
            {
                // Очистка намеренно не отменяется вместе с запросом (CancellationToken.None):
                // иначе отменённый выход оставил бы пару в хранилище.
                await _session.ClearAsync(CancellationToken.None);
            }
        }
        finally
        {
            ApplySignedOutState();
            ErrorMessage = null;
            IsSigningOut = false;
        }
    }

    /// <summary>
    /// Повторяет чтение профиля после ошибки.
    /// </summary>
    [RelayCommand]
    private async Task RetryProfileAsync()
    {
        if (!IsAuthenticated || IsProfileLoading)
        {
            return;
        }

        ProfileError = null;

        await LoadProfileAsync(silent: false, CancellationToken.None);
    }

    /// <summary>
    /// Просит страницу открыть «Личные данные».
    /// </summary>
    [RelayCommand]
    private void EditProfile() => EditProfileRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Просит страницу открыть «Мои отзывы».
    /// </summary>
    [RelayCommand]
    private void Reviews() => ReviewsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Просит страницу открыть «Безопасность».
    /// </summary>
    [RelayCommand]
    private void Security() => SecurityRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Просит страницу открыть регистрацию.
    /// </summary>
    /// <remarks>
    /// Регистрация — отдельный экран, а не состояние формы входа: полей у неё вдвое больше,
    /// и возврат с неё должен оставлять вкладку «Профиль» на месте.
    /// </remarks>
    [RelayCommand]
    private void Register() => RegisterRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Читает профиль и раскладывает его по свойствам экрана.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Тихой ошибкой считается ошибка при уже показанных данных: текст остаётся в
    /// <see cref="ProfileError"/>, но карточки не стираются. Первая загрузка без данных
    /// показывает состояние ошибки с «Повторить».
    /// </para>
    /// <para>
    /// <c>401</c> — единственный ответ, который гасит сессию: пара мертва, и показывать
    /// аккаунт дальше нельзя. Сеть, <c>429</c> и <c>5xx</c> сессию не трогают.
    /// </para>
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

            ApplyProfile(profile);
            ProfileError = null;
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Обработчик уже пробовал продлить пару: второй <c>401</c> значит, что токен мёртв.
            await _session.ClearAsync(CancellationToken.None);

            ApplySignedOutState();
            ErrorMessage = SessionExpiredText;
        }
        catch (BarberSearchApiException exception)
        {
            ProfileError = DescribeFailure(exception, ProfileLoadFailedText);
        }
        catch (HttpRequestException)
        {
            ProfileError = NetworkText;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            ProfileError = NetworkText;
        }
        catch (InvalidOperationException)
        {
            // Пустой ответ сервера: для экрана это та же недоступность профиля.
            ProfileError = ProfileLoadFailedText;
        }
        finally
        {
            IsProfileLoading = false;
        }
    }

    /// <summary>
    /// Раскладывает профиль по свойствам хаба.
    /// </summary>
    private void ApplyProfile(MyProfileResponse profile)
    {
        _profile = profile;

        ProfileEmail = profile.Email;
        DisplayName = AccountText.DisplayName(profile);
        Initials = AccountText.Initials(profile);
        RoleText = AccountText.RoleText(profile.Role);
        IsEmailVerified = profile.IsEmailVerified;
        EmailStatusText = profile.IsEmailVerified ? "Адрес подтверждён." : "Адрес не подтверждён.";

        OnPropertyChanged(nameof(ShowEmailWarning));

        // Показ хаба зависит от наличия профиля и email: одно уведомление на оба.
        OnPropertyChanged(nameof(IsProfileVisible));
        OnPropertyChanged(nameof(ShowProfileLoading));
        OnPropertyChanged(nameof(ShowProfileError));
    }

    /// <summary>
    /// Переводит отказ входа в текст для пользователя.
    /// </summary>
    /// <remarks>
    /// Ветвление только по статусу и публичным кодам, никогда по <c>detail</c>. Порядок
    /// проверок повторяет веб: лимит раньше кодов, <c>401</c> — после них.
    /// </remarks>
    private static string DescribeSignInError(BarberSearchApiException exception)
    {
        if (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return TooManyAttemptsText;
        }

        if (exception.ErrorCodes.Contains(ApiErrorCodes.Auth.LockedOut, StringComparer.Ordinal))
        {
            return LockedOutText;
        }

        if (exception.ErrorCodes.Contains(InactiveCode, StringComparer.Ordinal))
        {
            return InactiveText;
        }

        if (exception.ErrorCodes.Contains(InvalidCredentialsCode, StringComparer.Ordinal)
            || exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            return InvalidCredentialsText;
        }

        return SignInFailedText;
    }

    /// <summary>
    /// Переводит сбой чтения профиля в текст, не показывая английский <c>detail</c>.
    /// </summary>
    private static string DescribeFailure(BarberSearchApiException exception, string fallback) =>
        exception.StatusCode == HttpStatusCode.TooManyRequests
            ? "Слишком много запросов. Подождите минуту и повторите."
            : fallback;
}
