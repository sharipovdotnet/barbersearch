using System.Net;
using BarberSearch.Contracts.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран «Профиль»: форма входа или данные вошедшего пользователя с выходом.
/// </summary>
/// <remarks>
/// <para>
/// Отдельной страницы входа нет: пока сессии нет, на экране форма, после входа — email и
/// «Выйти» (решение среза). Состояний три, как у любого экрана: проверяем сессию, показываем
/// форму, показываем профиль. Промежуточные «не удалось проверить» здесь не появляются —
/// экран возвращается к форме и объясняет причину одной строкой.
/// </para>
/// <para>
/// Тексты ошибок — дословно из веба (<c>frontend/src/lib/sign-in-errors.ts</c>), порядок
/// проверок сохранён: лимит (<c>429</c>) раньше кодов, <c>401</c> — после них. Английский
/// <c>detail</c> не показывается никогда: он не является контрактом.
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
    /// Текст веб-карточки «сессию проверить не удалось» (<c>session-unavailable.tsx</c>).
    /// </summary>
    private const string SessionNotVerifiedText = "Не удалось проверить сессию";

    private readonly IAuthApi _authApi;
    private readonly IAccountApi _accountApi;
    private readonly TokenSession _session;

    /// <summary>
    /// Создаёт экран профиля.
    /// </summary>
    /// <param name="authApi">Вход и выход.</param>
    /// <param name="accountApi">Текущий пользователь по токену.</param>
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
    /// Идёт проверка сессии при открытии экрана.
    /// </summary>
    /// <remarks>
    /// Стартует взведённым: до первой загрузки на экране должен быть индикатор, а не форма
    /// входа, которая через мгновение исчезнет.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormVisible))]
    [NotifyPropertyChangedFor(nameof(IsProfileVisible))]
    private bool _isLoading = true;

    /// <summary>
    /// Показывать ли профиль вместо формы входа.
    /// </summary>
    /// <remarks>
    /// <c>true</c> только когда пара есть и сервер подтвердил её через <c>/me</c>. Пара
    /// в хранилище без подтверждения — ещё не повод показывать профиль: email показывать
    /// нечем, а врать пустой строкой хуже, чем попросить войти.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormVisible))]
    [NotifyPropertyChangedFor(nameof(IsProfileVisible))]
    private bool _isAuthenticated;

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
    /// Email вошедшего пользователя как его знает сервер.
    /// </summary>
    [ObservableProperty]
    private string? _profileEmail;

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
    /// Виден ли профиль; вычисляется из состояния экрана.
    /// </summary>
    public bool IsProfileVisible => IsAuthenticated && !IsLoading;

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
    /// Проверяет сохранённую сессию при каждом появлении экрана.
    /// </summary>
    /// <remarks>
    /// Без сохранённой пары сеть не трогается вовсе: на старте без входа не должно быть
    /// ни одного запроса. С парой экран спрашивает <c>/me</c> — только он даёт email,
    /// потому что в хранилище лежат токены, а не профиль.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await _session.EnsureLoadedAsync(cancellationToken);

            if (!_session.IsAuthenticated)
            {
                ApplySignedOutState();
                return;
            }

            await ShowProfileAsync(null, cancellationToken);
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
    /// Core не знает. Заодно стирает пароль и адрес — на телефоне им не место после выхода.
    /// </remarks>
    public void ApplySignedOutState()
    {
        IsAuthenticated = false;
        ProfileEmail = null;
        Email = string.Empty;
        Password = string.Empty;
    }

    /// <summary>
    /// Входит по email и паролю.
    /// </summary>
    /// <remarks>
    /// Пароль стирается сразу после того, как сервер выдал пару: держать его в памяти
    /// дольше не для чего, а после выхода он не должен утечь повторно.
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

            await ShowProfileAsync(email, cancellationToken);
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
    /// Показывает профиль или возвращает форму, если данных пользователя нет.
    /// </summary>
    /// <param name="fallbackEmail">
    /// Адрес, который пользователь только что ввёл: если <c>/me</c> не ответил, профиль
    /// всё равно показывается — вход уже состоялся, и терять его из-за показа адреса нельзя.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task ShowProfileAsync(string? fallbackEmail, CancellationToken cancellationToken)
    {
        try
        {
            var me = await _accountApi.GetMeAsync(cancellationToken);

            ProfileEmail = me.Email;
            IsAuthenticated = true;
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Обработчик уже обновил пару один раз: второй <c>401</c> значит, что токен мёртв.
            await _session.ClearAsync(CancellationToken.None);

            ApplySignedOutState();
        }
        catch (BarberSearchApiException)
        {
            ShowProfileOrError(fallbackEmail, SessionNotVerifiedText);
        }
        catch (HttpRequestException)
        {
            ShowProfileOrError(fallbackEmail, NetworkText);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Таймаут транспорта приходит отменой: после входа профиль уже можно показать.
            ShowProfileOrError(fallbackEmail, NetworkText);
        }
    }

    /// <summary>
    /// Оставляет профиль, если адрес уже известен, иначе возвращает форму с объяснением.
    /// </summary>
    private void ShowProfileOrError(string? fallbackEmail, string errorText)
    {
        if (fallbackEmail is not null)
        {
            ProfileEmail = fallbackEmail;
            IsAuthenticated = true;

            return;
        }

        ApplySignedOutState();
        ErrorMessage = errorText;
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
}
