using System.Net;
using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Domain.Users;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Api.BarberRequests;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Push;
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
/// поверх того же стека, и ссылка на неё стоит под формой входа. Запрос ссылки сброса
/// пароля — вторая такая страница, и открывается она оттуда же.
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
/// <para>
/// Строка «Стать мастером» — второе, независимое чтение хаба: после профиля и только для
/// клиента читается своя заявка на роль мастера. У этого чтения нет ни индикатора, ни своей
/// ошибки на экране: сбой не прячет строку и не блокирует хаб, а подробности человек увидит
/// на странице заявки — она остаётся источником правды. У профиля и заявки свои циклы
/// автоповтора, и ошибка одного не взводит повтор другого.
/// </para>
/// </remarks>
public sealed partial class ProfileViewModel : ObservableObject
{
    // Коды, которые сервер отдаёт при входе. В Contracts есть только lockedOut,
    // остальные живут в обработчиках Application и на клиенте объявлены здесь.
    private const string InvalidCredentialsCode = "auth.login.invalidCredentials";
    private const string InactiveCode = "auth.login.inactive";

    private static string InvalidCredentialsText => Loc.T("Неверный email или пароль.");
    private static string TooManyAttemptsText => Loc.T("Слишком много попыток входа. Подождите минуту и повторите.");
    private static string LockedOutText => Loc.T("Слишком много неудачных попыток. Вход временно заблокирован — попробуйте позже.");
    private static string InactiveText => Loc.T("Аккаунт деактивирован. Восстановить доступ поможет поддержка.");
    private static string SignInFailedText => Loc.T("Не удалось войти. Попробуйте ещё раз.");
    private static string NetworkText => Loc.T("Нет связи с сервером. Проверьте подключение и повторите попытку.");

    /// <summary>
    /// Заголовок ошибки загрузки профиля — дословно из веб-кабинета.
    /// </summary>
    private static string ProfileLoadFailedTitle => Loc.T("Не удалось загрузить профиль");

    /// <summary>
    /// Причина ошибки второй строкой: тот же заголовок, но с точкой, как в вебе.
    /// </summary>
    private static string ProfileLoadFailedText => Loc.T("Не удалось загрузить профиль.");

    private static string SessionExpiredText => Loc.T("Сессия истекла — войдите снова.");

    private readonly IAuthApi _authApi;
    private readonly IAccountApi _accountApi;
    private readonly IBarberRequestsApi? _barberRequestsApi;
    private readonly IAdminSummaryApi? _adminSummaryApi;
    private readonly TokenSession _session;
    private readonly IPushRegistrar _push;

    /// <summary>
    /// Сколько ждём снятия устройства при выходе.
    /// </summary>
    /// <remarks>
    /// Снятие идёт с Bearer и должно случиться раньше, чем logout отзовёт пару, но сбой
    /// сети не имеет права задерживать выход: пять секунд — это уже щедро для одного
    /// <c>POST</c> без тела.
    /// </remarks>
    private static readonly TimeSpan PushUnregisterTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Профиль из базы; <c>null</c>, пока он не загружен или после выхода.
    /// </summary>
    private MyProfileResponse? _profile;

    /// <summary>
    /// Цикл автоповтора чтения профиля: под причиной ошибки видна строка «Пробуем снова…».
    /// </summary>
    private readonly AutoRetryLoop _autoRetry;

    /// <summary>
    /// Вид последнего сбоя чтения профиля: по нему цикл решает, повторять ли чтение.
    /// </summary>
    /// <remarks>
    /// Запоминается для каждого неуспеха, а не только для повторяемого: иначе «транспорт,
    /// потом 404» оставил бы в памяти старый <see cref="ApiFailureKind.Transport"/>, и
    /// «Повторить» снова запустила бы цикл на отказе, который не изменится.
    /// </remarks>
    private ApiFailureKind _profileFailureKind;

    /// <summary>
    /// Цикл автоповтора чтения заявки на роль мастера — второй, независимый от профильного.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Зеркалит <see cref="_autoRetry"/>, но ничего не рисует: у строки хаба нет своей ошибки
    /// на экране. Поэтому подписки на <see cref="AutoRetryLoop.StateChanged"/> у него нет —
    /// строка «Пробуем снова…» принадлежит сбою профиля, и связывать состояния двух циклов
    /// значило бы показывать чужой повтор.
    /// </para>
    /// <para>
    /// Ошибки циклов независимы: сбой заявки не взводит чтение профиля и наоборот. Общий у
    /// них только сигнал «вкладка видна» — его и раздают <c>StartAutoRetry</c> и
    /// <c>StopAutoRetry</c>.
    /// </para>
    /// </remarks>
    private readonly AutoRetryLoop _barberRequestRetry;

    /// <summary>
    /// Вид последнего сбоя чтения заявки; <c>null</c> — сбоя нет, повторять нечего.
    /// </summary>
    /// <remarks>
    /// В отличие от <see cref="_profileFailureKind"/>, здесь <c>null</c> — не «нет ошибки»
    /// ради единообразия, а единственный признак того, что цикл заявки можно взводить:
    /// своего текста ошибки у строки нет. Сбрасывается успехом и выходом, поэтому повтор
    /// не может родиться из сбоя прошлой сессии.
    /// </remarks>
    private ApiFailureKind? _barberRequestFailureKind;

    /// <summary>
    /// Вкладка сейчас видна: цикл автоповтора работает только пока это так.
    /// </summary>
    private bool _isPageVisible;

    /// <summary>
    /// Создаёт экран профиля.
    /// </summary>
    /// <param name="authApi">Вход и выход.</param>
    /// <param name="accountApi">Профиль текущего пользователя.</param>
    /// <param name="session">Сессия — источник правды о входе.</param>
    /// <param name="push">Регистратор push: переключатель напоминаний.</param>
    /// <param name="autoRetry">
    /// Цикл автоповтора чтения профиля; <c>null</c> — собственный цикл с обычным расписанием.
    /// Параметр необязательный и стоит после обязательных, чтобы существующие позиционные
    /// вызовы (DI и тесты) не менялись.
    /// </param>
    /// <param name="barberRequestsApi">
    /// Заявка на роль мастера; <c>null</c> — второе чтение не делается. Параметр добавлен
    /// необязательным после <paramref name="autoRetry"/>: существующие вызовы без заявки
    /// собираются как раньше, а DI подставит зарегистрированный клиент.
    /// </param>
    /// <param name="barberRequestRetry">
    /// Цикл автоповтора чтения заявки; <c>null</c> — собственный цикл с обычным расписанием.
    /// Стоит последним: порядок уже существующих аргументов не меняется.
    /// </param>
    /// <param name="adminSummaryApi">
    /// Сводка очередей модерации для бейджа строки «Администрирование»; <c>null</c> — бейджа
    /// нет. Параметр добавлен необязательным в конец по той же причине, что и заявка:
    /// существующие вызовы собираются как раньше, а DI подставит зарегистрированный клиент.
    /// </param>
    public ProfileViewModel(
        IAuthApi authApi,
        IAccountApi accountApi,
        TokenSession session,
        IPushRegistrar push,
        AutoRetryLoop? autoRetry = null,
        IBarberRequestsApi? barberRequestsApi = null,
        AutoRetryLoop? barberRequestRetry = null,
        IAdminSummaryApi? adminSummaryApi = null)
    {
        ArgumentNullException.ThrowIfNull(authApi);
        ArgumentNullException.ThrowIfNull(accountApi);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(push);

        _authApi = authApi;
        _accountApi = accountApi;
        _barberRequestsApi = barberRequestsApi;
        _adminSummaryApi = adminSummaryApi;
        _session = session;
        _push = push;

        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;

        _barberRequestRetry = barberRequestRetry ?? new AutoRetryLoop();
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
    /// Нажата «Забыли пароль?» под формой входа: страница открывает запрос ссылки сброса.
    /// </summary>
    /// <remarks>
    /// Сама ссылка из письма ведёт на веб-страницу сброса: приложение её не открывает и
    /// новый пароль не принимает — так же, как подтверждение email и смену адреса.
    /// </remarks>
    public event EventHandler? ForgotPasswordRequested;

    /// <summary>
    /// Нажата строка «Стать мастером»: страница открывает экран заявки.
    /// </summary>
    /// <remarks>
    /// Событие, а не навигация: Core не знает о Shell. Страница заявки живёт поверх стека
    /// «Профиля», поэтому возврат оставляет хаб на месте — так же, как у «Личных данных».
    /// </remarks>
    public event EventHandler? BarberRequestRequested;

    /// <summary>
    /// Нажата строка «Администрирование»: страница открывает хаб очередей модерации.
    /// </summary>
    /// <remarks>
    /// Событие, а не навигация: Core не знает о Shell. Хаб живёт поверх стека «Профиля»,
    /// поэтому возврат оставляет вкладку и загруженный профиль на месте.
    /// </remarks>
    public event EventHandler? AdminRequested;

    /// <summary>
    /// Роль из профиля — администратор: строка администрирования видна.
    /// </summary>
    /// <remarks>
    /// Проверка роли на клиенте — только удобство: действия всё равно закрыты политикой
    /// <c>AdminOnly</c> на сервере, а сам хаб покажет отказ, если роль успели снять.
    /// </remarks>
    [ObservableProperty]
    private bool _isAdmin;

    /// <summary>
    /// Сколько всего ждёт решения администратора.
    /// </summary>
    /// <remarks>
    /// Сумма очередей из сводки — только для бейджа строки. Числа считает сервер
    /// (<c>GET api/admin/summary</c>), клиент их не пересчитывает: разбивка по разделам
    /// видна в самом хабе, а здесь нужно одно число «сколько накопилось».
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdminPending))]
    [NotifyPropertyChangedFor(nameof(AdminPendingText))]
    [NotifyPropertyChangedFor(nameof(AdminDescription))]
    private int _adminPendingCount;

    /// <summary>
    /// Есть ли что показывать бейджем: при нуле бейджа нет.
    /// </summary>
    public bool HasAdminPending => AdminPendingCount > 0;

    /// <summary>
    /// Подпись бейджа: до 99 цифрами, дальше «99+».
    /// </summary>
    public string AdminPendingText => AdminText.BadgeCount(AdminPendingCount);

    /// <summary>
    /// Имя строки для скринридера: число ждущих входит в него словами.
    /// </summary>
    /// <remarks>
    /// Цифра в кружке — сосед названия, а не его часть, поэтому без этой строки скринридер
    /// прочитал бы только «Администрирование» и не сказал бы, что там что-то ждёт.
    /// </remarks>
    public string AdminDescription => HasAdminPending
        ? Loc.T("Администрирование, ждёт решения: {0}", AdminPendingCount)
        : Loc.T("Администрирование");

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
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
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
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowProfileLoading))]
    [NotifyPropertyChangedFor(nameof(ShowProfileError))]
    private bool _isAuthenticated;

    /// <summary>
    /// Идёт чтение профиля.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileVisible))]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
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
    /// Цикл сам повторяет чтение профиля: под причиной ошибки видна строка «Пробуем снова…».
    /// </summary>
    /// <remarks>
    /// Состояние цикла, а не экрана: свойство переключается подпиской на
    /// <see cref="AutoRetryLoop.StateChanged"/> и ничего не решает само.
    /// </remarks>
    [ObservableProperty]
    private bool _isAutoRetrying;

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
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
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
    /// Русская подпись роли: «Клиент», «Мастер», «Администратор».
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
    private bool _isEmailVerified;

    /// <summary>
    /// Видна ли строка «Стать мастером»: профиль загружен и роль — клиент.
    /// </summary>
    /// <remarks>
    /// Строка обещает действие, которое сервер разрешает только клиенту: мастеру и
    /// администратору <c>POST api/barber-requests</c> ответил бы <c>403</c>. Поэтому чужая
    /// роль не просто прячет строку — заявка для неё и не читается. Пересчитывается там,
    /// где применяется профиль, и гаснет при выходе.
    /// </remarks>
    [ObservableProperty]
    private bool _canRequestBarberRole;

    /// <summary>
    /// Заголовок строки: состояние заявки, а без заявки — приглашение.
    /// </summary>
    /// <remarks>
    /// Второй строки у карточки нет: статус читается одним взглядом, а подробности живут на
    /// странице заявки. Заголовок — единственное, что меняет тихое чтение, и сбой чтения его
    /// не трогает: «Стать мастером» остаётся входом в заявку, даже когда сеть упала.
    /// </remarks>
    [ObservableProperty]
    private string _barberRequestTitle = Loc.T("Стать мастером");

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
    /// Настроен ли push: строка «Присылать напоминания» видна только тогда.
    /// </summary>
    /// <remarks>
    /// Без <c>push.json</c> приложение не показывает ни переключателя, ни разрешений: обещать
    /// настройку, которой нет, хуже, чем не показывать её вовсе.
    /// </remarks>
    public bool IsPushConfigured => _push.IsConfigured;

    /// <summary>
    /// Переключатель «Присылать напоминания».
    /// </summary>
    /// <remarks>
    /// Значение — выбор человека, а не фактическая доставка: при запрещённом разрешении
    /// переключатель остаётся включённым, а <see cref="NotificationsStatusText"/> объясняет,
    /// что именно нужно разрешить.
    /// </remarks>
    [ObservableProperty]
    private bool _notificationsEnabled;

    /// <summary>
    /// Подсказка под переключателем: почему напоминания не придут.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotificationsStatus))]
    private string _notificationsStatusText = string.Empty;

    /// <summary>
    /// Показывать ли «Открыть настройки»: только когда разрешение запрещено.
    /// </summary>
    [ObservableProperty]
    private bool _showNotificationSettings;

    /// <summary>
    /// Есть ли подсказка о состоянии уведомлений.
    /// </summary>
    public bool HasNotificationsStatus => !string.IsNullOrEmpty(NotificationsStatusText);

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
    /// Виден ли индикатор загрузки: проверка сессии либо первая загрузка профиля.
    /// </summary>
    /// <remarks>
    /// Одно состояние на оба шага. На старте проверка сессии идёт без паузы, и отдельный
    /// спиннер профиля оказывался на экране вместе с общим: две окружности в одной ячейке
    /// сетки пересекались. Тихое обновление сюда не входит — данные уже показаны, и
    /// подменять их спиннером значит мигать при каждом возврате на вкладку.
    /// </remarks>
    public bool ShowLoading => IsLoading || ShowProfileLoading;

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
    /// Можно ли править поля и нажимать «Войти».
    /// </summary>
    public bool IsFormEnabled => !IsSigningIn;

    /// <summary>
    /// Надпись на кнопке входа: во время запроса — «Входим…».
    /// </summary>
    public string LoginButtonText => IsSigningIn ? Loc.T("Входим…") : Loc.T("Войти");

    /// <summary>
    /// Надпись на кнопке выхода: во время запроса — «Выходим…».
    /// </summary>
    public string LogoutButtonText => IsSigningOut ? Loc.T("Выходим…") : Loc.T("Выйти");

    /// <summary>
    /// Проверяет сохранённую сессию и тихо обновляет профиль при каждом появлении вкладки.
    /// </summary>
    /// <remarks>
    /// Без сохранённой пары сеть не трогается вовсе: на старте без входа не должно быть
    /// ни одного запроса. С парой экран читает профиль — первый раз с индикатором, повторно
    /// молча, не стирая уже показанные данные. После профиля, и только клиенту, тихо
    /// читается своя заявка: это второе, независимое чтение, и его сбой хаб не блокирует
    /// и не прячет — строка остаётся «Стать мастером».
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

            // Хаб уже можно показывать: статус заявки — вспомогательная подпись, и держать
            // из-за неё индикатор профиля значит задерживать экран ради второго чтения.
            IsLoading = false;

            // Заявка читается молча: сбой ничего не прячет и не блокирует, а подробности
            // человек увидит на странице заявки.
            await LoadBarberRequestAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Отмена — не ошибка: экран просто закрыли.
        }
        finally
        {
            IsLoading = false;
        }

        // Push не держит экран в состоянии загрузки: диалог разрешения и токен FCM могут
        // занять секунды, а индикатор профиля уже не нужен. Регистрацию ведут старт
        // приложения и вход; здесь только тихо обновляется строка состояния.
        if (IsAuthenticated)
        {
            _ = RefreshNotificationsAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Сообщает, что вкладка видна, и запускает автоповтор неудачных чтений профиля и заявки.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Зовётся страницей при каждом появлении и каждом возврате из фона. Идемпотентен: без
    /// ошибки, при окончательном отказе или уже работающем цикле ничего не делает.
    /// </para>
    /// <para>
    /// Циклов два, и они независимы: у профиля и у заявки свои сбои. Вместе они запускаются
    /// только потому, что «вкладка видна» — общий для них сигнал; ошибка одного не взводит
    /// повтор другого.
    /// </para>
    /// <para>
    /// Запуск и остановка обязаны идти из UI-потока: <see cref="AutoRetryLoop.StateChanged"/>
    /// профильного цикла синхронно меняет привязанный <see cref="IsAutoRetrying"/>. Поэтому
    /// метод зовут только из жизненного цикла страницы, а не из фонового <c>catch</c>.
    /// </para>
    /// </remarks>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartAutoRetry();
        TryStartBarberRequestAutoRetry();
    }

    /// <summary>
    /// Сообщает, что вкладка скрыта, и останавливает оба цикла автоповтора.
    /// </summary>
    /// <remarks>
    /// Идемпотентен. Остановка отменяет паузу и запрос в полёте, а <see cref="IsAutoRetrying"/>
    /// профильного цикла сразу становится <c>false</c>: скрытая вкладка не показывает
    /// работающий повтор и не ходит в сеть. Цикл заявки останавливается здесь же — иначе он
    /// продолжил бы читать статус под открытым дочерним экраном, которого человек не просил.
    /// </remarks>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        StopAutoRetryLoop();
        _barberRequestRetry.Stop();
    }

    /// <summary>
    /// Завершается, когда цикл автоповтора останавливается: тесты ждут его вместо пауз.
    /// </summary>
    internal Task AutoRetryCompletion => _autoRetry.Completion;

    /// <summary>
    /// Цикл автоповтора: тестам нужен счётчик попыток, приложению — нет.
    /// </summary>
    internal AutoRetryLoop AutoRetry => _autoRetry;

    /// <summary>
    /// Цикл автоповтора заявки: тестам нужен счётчик попыток, приложению — нет.
    /// </summary>
    internal AutoRetryLoop BarberRequestAutoRetry => _barberRequestRetry;

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
        // Выход — это и есть «повторять больше нечего»: цикл мог ждать паузу, пока сессию
        // гасили, и без остановки сходил бы в сеть без токена. Второй цикл — по той же
        // причине: заявка читается с Bearer, которого после выхода уже нет.
        StopAutoRetryLoop();
        _barberRequestRetry.Stop();

        IsAuthenticated = false;
        _profile = null;
        ProfileEmail = null;
        DisplayName = string.Empty;
        Initials = string.Empty;
        RoleText = string.Empty;
        EmailStatusText = string.Empty;
        IsEmailVerified = false;
        ProfileError = null;

        // Строка заявки гаснет вместе с профилем: роль прошлого пользователя больше не
        // известна, а показывать его статус следующему человеку нельзя. Вид сбоя обнулён,
        // иначе повтор после нового входа взвёлся бы по чужой ошибке.
        CanRequestBarberRole = false;
        BarberRequestTitle = Loc.T("Стать мастером");
        _barberRequestFailureKind = null;

        // Строка администрирования — тоже: чужую очередь модерации показывать нельзя.
        IsAdmin = false;
        AdminPendingCount = 0;

        Email = string.Empty;
        Password = string.Empty;

        // Переключатель возвращается к сохранённому выбору: после следующего входа устройство
        // зарегистрируется за новым пользователем, и строка не должна показывать чужое состояние.
        NotificationsEnabled = _push.IsUserEnabled;
        NotificationsStatusText = string.Empty;
        ShowNotificationSettings = false;
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

        // Регистрация после входа идёт своим ходом: системный диалог разрешения и ответ FCM
        // не должны держать кнопку «Входим…» — вход уже состоялся.
        if (IsAuthenticated)
        {
            _ = RefreshNotificationsAsync(CancellationToken.None);
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

            // Снимать устройство нужно до запроса выхода: POST unregister идёт с Bearer,
            // а logout отзывает refresh-токен — после него пара мертва и запрос получил бы
            // 401, оставив токен за вышедшим аккаунтом. Таймаут короткий: сбой push не
            // должен задерживать выход.
            using (var pushTimeout = new CancellationTokenSource(PushUnregisterTimeout))
            {
                await _push.UnregisterAsync(pushTimeout.Token);
            }

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
    /// Повторяет чтение профиля после ошибки — сразу, по кнопке.
    /// </summary>
    /// <remarks>
    /// Кнопка «Повторить» значит «повторить сейчас»: сначала цикл автоповтора останавливается,
    /// затем профиль читается без паузы. Если сбой снова повторяемый, цикл запускается заново;
    /// успех цикл не запускает — повторять больше нечего.
    /// </remarks>
    [RelayCommand]
    private async Task RetryProfileAsync()
    {
        if (!IsAuthenticated || IsProfileLoading)
        {
            return;
        }

        StopAutoRetryLoop();

        ProfileError = null;

        await LoadProfileAsync(silent: false, CancellationToken.None);

        TryStartAutoRetry();
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
    /// Просит страницу открыть «Забыли пароль?».
    /// </summary>
    /// <remarks>
    /// Ссылка стоит под полем пароля, как в диалоге входа веба: человек, который не помнит
    /// пароль, ищет её именно там. Страница живёт поверх стека «Профиля», поэтому возврат
    /// оставляет форму входа на месте.
    /// </remarks>
    [RelayCommand]
    private void ForgotPassword() => ForgotPasswordRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Просит страницу открыть заявку на роль мастера.
    /// </summary>
    /// <remarks>
    /// Строка хаба — единственный вход в заявку, поэтому команда только поднимает событие:
    /// остановка повторов, навигация и сама страница — работа страницы, а не Core.
    /// </remarks>
    [RelayCommand]
    private void OpenBarberRequest() => BarberRequestRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Просит страницу открыть администрирование.
    /// </summary>
    /// <remarks>
    /// Команда только поднимает событие: навигация и остановка повторов — работа страницы,
    /// а Core про Shell не знает.
    /// </remarks>
    [RelayCommand]
    private void OpenAdmin() => AdminRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Включает или выключает напоминания по переключателю «Присылать напоминания».
    /// </summary>
    /// <remarks>
    /// Команда не показывает ошибку: включение без разрешения — нормальное состояние,
    /// а не сбой, и объясняется строкой под переключателем. Значение переключателя
    /// возвращается к сохранённому выбору регистратора — человек мог не дать разрешение,
    /// но сам выбор остаётся его.
    /// </remarks>
    /// <param name="enabled">Новое положение переключателя.</param>
    [RelayCommand]
    private async Task ToggleNotificationsAsync(bool enabled)
    {
        if (!_push.IsConfigured)
        {
            return;
        }

        NotificationsEnabled = enabled;

        var result = await _push.SetEnabledAsync(enabled);

        NotificationsEnabled = _push.IsUserEnabled;
        ApplyPushResult(result);
    }

    /// <summary>
    /// Открывает настройки телефона, где включаются уведомления приложения.
    /// </summary>
    [RelayCommand]
    private void OpenNotificationSettings() => _push.OpenSettings();

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
    /// Причина ошибки не стирается в начале чтения, а индикатор первой загрузки не
    /// переключается: тихая попытка цикла не имеет права гасить блок с причиной и
    /// «Повторить», иначе на каждой паузе экран мигал бы пустым местом.
    /// </para>
    /// <para>
    /// <c>401</c> — единственный ответ, который гасит сессию: пара мертва, и показывать
    /// аккаунт дальше нельзя. Сеть, <c>429</c> и <c>5xx</c> сессию не трогают.
    /// </para>
    /// </remarks>
    /// <param name="silent">Не показывать индикатор первой загрузки: данные уже на экране.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private async Task<ApiFailureKind> LoadProfileAsync(bool silent, CancellationToken cancellationToken)
    {
        if (!silent)
        {
            IsProfileLoading = true;
        }

        var kind = ApiFailureKind.None;

        try
        {
            var profile = await _accountApi.GetMyProfileAsync(cancellationToken);

            ApplyProfile(profile);
            ProfileError = null;
            _profileFailureKind = ApiFailureKind.None;

            // Счётчик очередей читается вторым запросом и только администратору: сводка
            // закрыта ролью, а остальным бейдж не нужен. Его сбой не портит профиль.
            await RefreshAdminPendingAsync(cancellationToken);
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Обработчик уже пробовал продлить пару: второй <c>401</c> значит, что токен мёртв.
            kind = ApiFailureKind.Terminal;

            await _session.ClearAsync(CancellationToken.None);

            ApplySignedOutState();
            ErrorMessage = SessionExpiredText;
        }
        catch (BarberSearchApiException exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);
            ProfileError = DescribeFailure(exception, ProfileLoadFailedText);
        }
        catch (HttpRequestException exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);
            ProfileError = NetworkText;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            kind = ApiFailureKind.Transport;
            ProfileError = NetworkText;
        }
        catch (InvalidOperationException)
        {
            // Пустой ответ сервера: для экрана это та же недоступность профиля.
            kind = ApiFailureKind.Terminal;
            ProfileError = ProfileLoadFailedText;
        }
        finally
        {
            // Тихая попытка не трогает флаг первой загрузки: он ей не принадлежит.
            if (!silent)
            {
                IsProfileLoading = false;
            }
        }

        if (kind == ApiFailureKind.None)
        {
            // Настоящая отмена уже прервала метод, поэтому сюда приходит только успех:
            // повторять больше нечего.
            return kind;
        }

        // Вид сбоя запоминается для каждого неуспеха, а не только для повторяемого.
        _profileFailureKind = kind;

        // Сбой пришёл, пока вкладка видна: цикл может повторить чтение сам. Если он уже
        // работает, Start ничего не сделает — второй цикл удвоил бы запросы.
        TryStartAutoRetry();

        return kind;
    }

    /// <summary>
    /// Одна попытка цикла: перечитать профиль, не гася причину ошибки и карточки.
    /// </summary>
    /// <remarks>
    /// Условия перепроверяются на момент попытки: пока цикл ждал паузу, ошибку могли снять
    /// или сессию могли погасить. Тогда повторять нечего, и цикл останавливается.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    private Task<ApiFailureKind> RetryProfileAttemptAsync(CancellationToken cancellationToken)
    {
        if (ProfileError is null || !_profileFailureKind.IsRetryable())
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadProfileAsync(silent: true, cancellationToken);
    }

    /// <summary>
    /// Запускает цикл, если вкладка видна, а ошибка чтения профиля повторяемая.
    /// </summary>
    /// <remarks>
    /// Отдельный метод нужен потому, что «видна» — сигнал страницы, а не следствие нажатия:
    /// повтор после сбоя запускается и из завершения загрузки, но только пока вкладка видна.
    /// </remarks>
    private void TryStartAutoRetry()
    {
        if (!_isPageVisible || !IsAuthenticated || IsProfileLoading)
        {
            return;
        }

        if (ProfileError is null || !_profileFailureKind.IsRetryable())
        {
            return;
        }

        _autoRetry.Start(RetryProfileAttemptAsync);
    }

    /// <summary>
    /// Останавливает цикл, не трогая признак «вкладка видна».
    /// </summary>
    /// <remarks>
    /// Нужен кнопке «Повторить» и навигации в дочерние экраны: они останавливают фон, но
    /// вкладка остаётся видимой, и после ответа цикл имеет право запуститься снова. Публичный
    /// <see cref="StopAutoRetry"/> дополнительно помечает вкладку скрытой — для них это неверно.
    /// </remarks>
    private void StopAutoRetryLoop() => _autoRetry.Stop();

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Тихо читает свою заявку на роль мастера и вносит её статус в заголовок строки.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Чтение вспомогательное: у него нет ни индикатора, ни своей ошибки на экране. Поэтому
    /// сбой ничего не прячет и не блокирует — заголовок остаётся прежним, а человек всё равно
    /// открывает страницу заявки, где ошибка показана с «Повторить».
    /// </para>
    /// <para>
    /// <c>401</c> сессию не гасит: пару проверяет чтение профиля, и второй <c>401</c> от
    /// заявки не должен выкидывать человека из аккаунта раньше профильного цикла. Для той же
    /// цели окончательный отказ, <c>401</c> в том числе, цикл заявки не взводит: повтор лишь
    /// повторит отказ.
    /// </para>
    /// <para>
    /// Мастеру и администратору заявка не читается вовсе: сервер ответил бы <c>403</c>, а
    /// строка им не показывается. Без клиента API (старый вызов без него) — тоже молча.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private async Task<ApiFailureKind> LoadBarberRequestAsync(CancellationToken cancellationToken)
    {
        if (_barberRequestsApi is null || !CanRequestBarberRole)
        {
            return ApiFailureKind.None;
        }

        var kind = ApiFailureKind.None;

        try
        {
            var response = await _barberRequestsApi.GetMineAsync(cancellationToken);

            _barberRequestFailureKind = null;
            ApplyBarberRequest(response.Request);
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Сессию гасит профильное чтение: заявка — вспомогательная подпись, и решать
            // судьбу входа по ней нельзя. Окончательный отказ цикла не взводит.
            kind = ApiFailureKind.Terminal;
        }
        catch (BarberSearchApiException exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Таймаут транспорта приходит отменой: для цикла это отсутствие связи.
            kind = ApiFailureKind.Transport;
        }
        catch (InvalidOperationException)
        {
            // Пустое тело ответа — нарушение контракта; повтор его не исправит.
            kind = ApiFailureKind.Terminal;
        }

        if (kind == ApiFailureKind.None)
        {
            return kind;
        }

        _barberRequestFailureKind = kind;

        // Сбой пришёл, пока вкладка видна: цикл заявки повторит именно это чтение. Профильный
        // цикл здесь не трогается — у него своя ошибка и своё состояние.
        TryStartBarberRequestAutoRetry();

        return kind;
    }

    /// <summary>
    /// Одна попытка цикла заявки: перечитать свою заявку, не трогая профиль.
    /// </summary>
    /// <remarks>
    /// Условия перепроверяются на момент попытки: пока цикл ждал паузу, человек мог выйти, а
    /// роль могла перестать быть клиентской. Тогда повторять нечего, и цикл останавливается.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    private Task<ApiFailureKind> RetryBarberRequestAttemptAsync(CancellationToken cancellationToken)
    {
        if (_barberRequestFailureKind?.IsRetryable() != true || !CanRequestBarberRole)
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadBarberRequestAsync(cancellationToken);
    }

    /// <summary>
    /// Запускает цикл заявки, если вкладка видна, роль клиентская, а сбой повторяемый.
    /// </summary>
    /// <remarks>
    /// Отдельный метод зеркалит <see cref="TryStartAutoRetry"/>: «видна» — сигнал страницы, а
    /// не следствие нажатия, поэтому повтор запускается и из завершения чтения. Профильный
    /// цикл это не взводит: состояние заявки живёт только здесь.
    /// </remarks>
    private void TryStartBarberRequestAutoRetry()
    {
        if (!_isPageVisible || !IsAuthenticated || _barberRequestsApi is null || !CanRequestBarberRole)
        {
            return;
        }

        if (_barberRequestFailureKind?.IsRetryable() != true)
        {
            return;
        }

        _barberRequestRetry.Start(RetryBarberRequestAttemptAsync);
    }

    /// <summary>
    /// Переводит статус заявки в заголовок строки хаба.
    /// </summary>
    /// <remarks>
    /// Тексты — из таблицы веба (<c>frontend/src/components/barber/barber-request-status.tsx</c>):
    /// заявки нет — приглашение, дальше три решения администратора. Неизвестный статус
    /// показывается как «заявки нет»: выдумывать подпись для значения, которого клиент не
    /// знает, хуже, чем оставить вход в заявку.
    /// </remarks>
    /// <param name="request">Заявка из ответа либо <c>null</c>, если её нет.</param>
    private void ApplyBarberRequest(BarberRequestResponse? request)
    {
        if (request is null)
        {
            BarberRequestTitle = Loc.T("Стать мастером");

            return;
        }

        BarberRequestTitle = request.Status switch
        {
            BarberRequestStatus.Pending => Loc.T("Заявка на рассмотрении"),
            BarberRequestStatus.Approved => Loc.T("Заявка одобрена"),
            BarberRequestStatus.Rejected => Loc.T("Заявка отклонена"),
            _ => Loc.T("Стать мастером"),
        };
    }

    /// <summary>
    /// Читает сводку очередей для бейджа строки «Администрирование».
    /// </summary>
    /// <remarks>
    /// <para>
    /// Запрос второй и необязательный: у роли не администратора сводка закрыта, поэтому
    /// без роли он не делается вовсе. Сбой не попадает ни в <see cref="ProfileError"/>,
    /// ни в цикл автоповтора: бейдж — подсказка, а не данные экрана, и без сети строка
    /// просто остаётся без числа.
    /// </para>
    /// <para>
    /// Сумма считается здесь, а не на сервере: сводке нужен один ответ на все разделы,
    /// а хабу — разбивка, поэтому сервер отдаёт очереди по отдельности.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task RefreshAdminPendingAsync(CancellationToken cancellationToken)
    {
        if (_adminSummaryApi is null || !IsAdmin)
        {
            AdminPendingCount = 0;

            return;
        }

        try
        {
            var summary = await _adminSummaryApi.GetSummaryAsync(cancellationToken);

            AdminPendingCount = summary.Reviews.PendingCount
                + summary.Photos.PendingAvatarsCount
                + summary.Photos.PendingPortfolioCount
                + summary.Barbers.UnverifiedCount
                + summary.BarberRequests.PendingCount;
        }
        catch (Exception)
        {
            // Молча: сводка — украшение строки, у неё нет ни своего состояния, ни «Повторить».
            AdminPendingCount = 0;
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
        EmailStatusText = profile.IsEmailVerified
            ? Loc.T("Адрес подтверждён.")
            : Loc.T("Адрес не подтверждён.");

        // Строка «Стать мастером» — только клиенту: мастеру и администратору сервер ответил бы
        // 403, и предлагать действие, которое запрещено, нельзя.
        CanRequestBarberRole = profile.Role == UserRole.Customer;

        // Строка администрирования — только администратору: раздел закрыт политикой AdminOnly,
        // а строка, ведущая на «нет доступа», обещает то, чего нет.
        IsAdmin = profile.Role == UserRole.Admin;

        if (!IsAdmin)
        {
            AdminPendingCount = 0;
        }

        // Показ хаба зависит от наличия профиля и email: одно уведомление на оба.
        OnPropertyChanged(nameof(IsProfileVisible));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowProfileLoading));
        OnPropertyChanged(nameof(ShowProfileError));
    }

    /// <summary>
    /// Сверяет состояние push с телефоном и сервером.
    /// </summary>
    /// <remarks>
    /// Первый вызов после входа — единственное место, где запрашивается разрешение на
    /// уведомления: <c>EnsureRegisteredAsync</c> делает это сам, а экран только показывает
    /// результат. Без <c>push.json</c> метод не делает ничего, поэтому настройка не появляется
    /// и разрешение не спрашивается.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task RefreshNotificationsAsync(CancellationToken cancellationToken)
    {
        if (!_push.IsConfigured)
        {
            return;
        }

        try
        {
            NotificationsEnabled = _push.IsUserEnabled;

            var result = await _push.EnsureRegisteredAsync(cancellationToken);

            ApplyPushResult(result);
        }
        catch (Exception)
        {
            // Метод запускается без ожидания: сбой push не должен становиться
            // необработанным исключением в фоне. Регистратор и сам не бросает — это страховка.
        }
    }

    /// <summary>
    /// Переводит состояние токена в строку под переключателем.
    /// </summary>
    /// <remarks>
    /// Тексты — только для двух случаев, когда человеку есть что сделать: запрещённое
    /// разрешение (включить в настройках) и устройство без сервисов Google Play. Временный
    /// сбой сети, успех и выключенный переключатель молчат: подсказка на каждый визит была
    /// бы шумом, а «недоступно на устройстве» было бы неправдой при обычном обрыве связи.
    /// </remarks>
    private void ApplyPushResult(PushTokenResult result)
    {
        switch (result.Status)
        {
            case PushTokenStatus.Denied:
                NotificationsStatusText = Loc.T(
                    "Уведомления выключены в настройках телефона. Разрешите их, чтобы получать напоминания.");
                ShowNotificationSettings = true;
                break;

            case PushTokenStatus.Unavailable:
                NotificationsStatusText = Loc.T("Уведомления недоступны на этом устройстве");
                ShowNotificationSettings = false;
                break;

            default:
                // Available, Disabled и TransientFailure: показывать нечего, попытка повторится.
                NotificationsStatusText = string.Empty;
                ShowNotificationSettings = false;
                break;
        }
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
            ? Loc.T("Слишком много запросов. Подождите минуту и повторите.")
            : fallback;
}
