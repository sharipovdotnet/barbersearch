using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using BarberSearch.Contracts.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран «Забыли пароль?»: запрос ссылки для сброса забытого пароля по email.
/// </summary>
/// <remarks>
/// <para>
/// Повторяет веб-страницу <c>frontend/src/pages/forgot-password-page.tsx</c>: форма с одним
/// полем, клиентская проверка адреса, карточка «Проверьте почту» и локальный кулдаун
/// повторной отправки в 60 секунд. Сервер отвечает <c>204</c> и для существующего, и для
/// неизвестного адреса, поэтому карточка успеха никогда не различает эти случаи; текст
/// говорит «если аккаунт с адресом … существует».
/// </para>
/// <para>
/// Ссылка из письма ведёт на веб-страницу сброса: приложение её не открывает, токен не
/// разбирает и новый пароль не принимает — так же, как подтверждение email и смена адреса.
/// Кулдаун сервер держит тихо (повторный запрос тоже получает <c>204</c>), поэтому отсчёт
/// живёт на клиенте и запускается после успешной отправки.
/// </para>
/// <para>
/// Английский <c>detail</c> не показывается никогда: ветвление идёт по статусу и публичным
/// кодам, как в <see cref="ProfileViewModel"/>. Пустой и кривой адрес отсекаются до сети —
/// сервер на них всё равно ответил бы <c>204</c>, и человек не узнал бы об опечатке.
/// </para>
/// </remarks>
public sealed partial class ForgotPasswordViewModel : ObservableObject
{
    /// <summary>
    /// Верхняя граница адреса — из серверного валидатора (3..254).
    /// </summary>
    private const int MaxEmailLength = 254;

    /// <summary>
    /// Длительность локального кулдауна повторной отправки.
    /// </summary>
    private const int ResendCooldownSeconds = 60;

    private const string EmailRequiredText = "Введите email.";
    private const string EmailInvalidText = "Проверьте email: нужен адрес вида name@example.com.";
    private const string TooManyAttemptsText = "Слишком много попыток. Подождите минуту и повторите.";
    private const string UnavailableText = "Сброс пароля по почте пока не настроен.";
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string FallbackText = "Не удалось отправить ссылку. Попробуйте позже.";

    private readonly IAuthApi _authApi;

    /// <summary>
    /// Создаёт экран запроса ссылки.
    /// </summary>
    /// <param name="authApi">Анонимный запрос ссылки сброса.</param>
    public ForgotPasswordViewModel(IAuthApi authApi)
    {
        ArgumentNullException.ThrowIfNull(authApi);

        _authApi = authApi;
    }

    /// <summary>
    /// Email из формы; срезается при отправке.
    /// </summary>
    [ObservableProperty]
    private string _email = string.Empty;

    /// <summary>
    /// Адрес, на который отправлена ссылка; показывается только на карточке успеха.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SentDescription))]
    private string _sentEmail = string.Empty;

    /// <summary>
    /// Ошибка поля «Email».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmailError))]
    private string? _emailError;

    /// <summary>
    /// Общая ошибка формы и карточки: сеть, лимит, недоступная почта.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormError))]
    private string? _formError;

    /// <summary>
    /// Идёт отправка: кнопка называется «Отправляем…» и заблокирована.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormEnabled))]
    [NotifyPropertyChangedFor(nameof(SendButtonText))]
    [NotifyPropertyChangedFor(nameof(CanResend))]
    [NotifyPropertyChangedFor(nameof(ResendButtonText))]
    private bool _isSubmitting;

    /// <summary>
    /// Ссылка запрошена: форма заменена карточкой «Проверьте почту».
    /// </summary>
    /// <remarks>
    /// Признак ставится только после <c>204</c>: до ответа форма остаётся на экране, и
    /// ошибка отправки её не подменяет — иначе человек потерял бы введённый адрес.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowForm))]
    [NotifyPropertyChangedFor(nameof(ShowSentCard))]
    [NotifyPropertyChangedFor(nameof(CanResend))]
    [NotifyPropertyChangedFor(nameof(ResendButtonText))]
    private bool _isSent;

    /// <summary>
    /// Остаток кулдауна повторной отправки; тикает таймер страницы.
    /// </summary>
    /// <remarks>
    /// Кулдаун локальный: сервер повторную отправку не отвергает, а тихо отвечает
    /// <c>204</c>, чтобы по времени ответа нельзя было узнать о существовании аккаунта.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCooldownActive))]
    [NotifyPropertyChangedFor(nameof(CanResend))]
    [NotifyPropertyChangedFor(nameof(ResendButtonText))]
    private int _cooldownRemaining;

    /// <summary>
    /// Видна ли форма ввода адреса.
    /// </summary>
    public bool ShowForm => !IsSent;

    /// <summary>
    /// Видна ли карточка «Проверьте почту».
    /// </summary>
    public bool ShowSentCard => IsSent;

    /// <summary>
    /// Можно ли править поле и нажимать «Отправить ссылку».
    /// </summary>
    public bool IsFormEnabled => !IsSubmitting;

    /// <summary>
    /// Надпись на кнопке отправки: во время запроса — «Отправляем…».
    /// </summary>
    public string SendButtonText => IsSubmitting ? "Отправляем…" : "Отправить ссылку";

    /// <summary>
    /// Есть ошибка поля «Email».
    /// </summary>
    public bool HasEmailError => !string.IsNullOrEmpty(EmailError);

    /// <summary>
    /// Есть общая ошибка формы или карточки.
    /// </summary>
    public bool HasFormError => !string.IsNullOrEmpty(FormError);

    /// <summary>
    /// Идёт кулдаун повторной отправки.
    /// </summary>
    public bool IsCooldownActive => CooldownRemaining > 0;

    /// <summary>
    /// Можно ли запросить ссылку ещё раз.
    /// </summary>
    /// <remarks>
    /// Только с карточки успеха и после кулдауна: на форме повторной отправки нет, а
    /// одновременный второй запрос дублировал бы письмо.
    /// </remarks>
    public bool CanResend => IsSent && !IsCooldownActive && !IsSubmitting;

    /// <summary>
    /// Надпись на кнопке повторной отправки.
    /// </summary>
    public string ResendButtonText => IsSubmitting
        ? "Отправляем…"
        : IsCooldownActive
            ? string.Concat(
                "Повторить через ",
                CooldownRemaining.ToString(CultureInfo.InvariantCulture),
                " с")
            : "Отправить ещё раз";

    /// <summary>
    /// Объяснение на карточке успеха: адрес и срок жизни ссылки.
    /// </summary>
    /// <remarks>
    /// Формулировка «если аккаунт с адресом … существует» — не осторожность, а контракт:
    /// сервер не сообщает, есть ли такой пользователь, и экран не имеет права намекнуть.
    /// </remarks>
    public string SentDescription => string.Concat(
        "Если аккаунт с адресом ",
        SentEmail,
        " существует, мы отправили на него ссылку для сброса пароля. Ссылка действует 30 минут.");

    /// <summary>
    /// Проверяет адрес и просит сервер отправить ссылку.
    /// </summary>
    /// <remarks>
    /// Повторное нажатие во время запроса игнорируется: второй <c>POST</c> только тратил бы
    /// лимит и мог бы отправить второе письмо. После успеха форма недоступна — команда
    /// защищена и от программного вызова.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        if (IsSubmitting || IsSent)
        {
            return;
        }

        FormError = null;

        var email = Email.Trim();

        EmailError = DescribeEmail(email);

        if (HasEmailError)
        {
            return;
        }

        if (!await TrySendAsync(email, cancellationToken))
        {
            return;
        }

        SentEmail = email;
        IsSent = true;
        StartCooldown();
    }

    /// <summary>
    /// Повторяет запрос ссылки с карточки успеха.
    /// </summary>
    /// <remarks>
    /// Отправляется тот же адрес, который принял первый запрос: человек не редактирует его
    /// на карточке. Ошибка оставляет карточку на месте и не запускает кулдаун — иначе
    /// сбой сети запирал бы повторную попытку на минуту.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task ResendAsync(CancellationToken cancellationToken)
    {
        if (!CanResend || SentEmail.Length == 0)
        {
            return;
        }

        FormError = null;

        if (await TrySendAsync(SentEmail, cancellationToken))
        {
            StartCooldown();
        }
    }

    /// <summary>
    /// Возвращает форму, чтобы указать другой адрес.
    /// </summary>
    /// <remarks>
    /// Введённый адрес сохраняется, как в вебе: чаще всего человек ошибся в одном символе,
    /// и стирать поле значило бы заставить вводить всё заново. Кулдаун не сбрасывается:
    /// он относится к повторной отправке, а форма отправит новое письмо и запустит его заново.
    /// </remarks>
    [RelayCommand]
    private void ChangeEmail()
    {
        EmailError = null;
        FormError = null;
        IsSent = false;
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
    /// Отправляет запрос и раскладывает отказ по экрану.
    /// </summary>
    /// <returns><c>true</c>, если сервер принял запрос.</returns>
    /// <remarks>
    /// Таймаут транспорта приходит отменой без отмены жизненного цикла — для человека это
    /// отсутствие связи, а не отказ.
    /// </remarks>
    private async Task<bool> TrySendAsync(string email, CancellationToken cancellationToken)
    {
        IsSubmitting = true;

        try
        {
            await _authApi.RequestPasswordResetAsync(email, cancellationToken);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Экран закрыли — не ошибка.
            return false;
        }
        catch (OperationCanceledException)
        {
            FormError = NetworkText;
            return false;
        }
        catch (BarberSearchApiException exception)
        {
            FormError = DescribeFailure(exception);
            return false;
        }
        catch (HttpRequestException)
        {
            FormError = NetworkText;
            return false;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    /// <summary>
    /// Переводит отказ сервера в текст для пользователя.
    /// </summary>
    /// <remarks>
    /// Порядок повторяет веб: <c>429</c> раньше кодов, потому что лимитер и код почты
    /// приходят вместе только при настоящей перегрузке. Остальные <c>5xx</c> и неизвестные
    /// коды — запасной текст, английский <c>detail</c> не показывается.
    /// </remarks>
    private static string DescribeFailure(BarberSearchApiException exception)
    {
        if (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return TooManyAttemptsText;
        }

        if (exception.ErrorCodes.Contains(ApiErrorCodes.Auth.PasswordResetUnavailable, StringComparer.Ordinal))
        {
            return UnavailableText;
        }

        return FallbackText;
    }

    /// <summary>
    /// Проверяет адрес теми же правилами, что веб и сервер.
    /// </summary>
    private static string? DescribeEmail(string email)
    {
        if (email.Length == 0)
        {
            return EmailRequiredText;
        }

        return email.Length > MaxEmailLength || !EmailPattern().IsMatch(email)
            ? EmailInvalidText
            : null;
    }

    /// <summary>
    /// Запускает кулдаун на полную длину.
    /// </summary>
    private void StartCooldown() => CooldownRemaining = ResendCooldownSeconds;

    /// <summary>
    /// Адрес вида <c>name@example.com</c>: та же проверка, что в вебе.
    /// </summary>
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();

    /// <summary>
    /// Правка поля снимает ошибку под ним и общую ошибку.
    /// </summary>
    partial void OnEmailChanged(string value)
    {
        _ = value;
        EmailError = null;
        FormError = null;
    }
}
