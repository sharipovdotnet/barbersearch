using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using BarberSearch.Contracts.BarberProfiles.CreateBarberProfile;
using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Auth;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Api.BarberRequests;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Онбординг мастера: создание профиля по одобренной заявке и первый салон.
/// </summary>
/// <remarks>
/// <para>
/// Регистрация всегда создаёт клиента, а роль <c>Barber</c> выдаёт администратор. До этого
/// среза после выдачи роли кабинет закрывался тупиком «профиль не привязан»; здесь профиль
/// создаётся существующим <c>POST api/barber-profiles</c>, доступным ровно этой роли.
/// </para>
/// <para>
/// <c>barber_profile_id</c> живёт в claim access-токена. После <c>201</c> старый токен честно
/// отвечает <c>null</c>, поэтому сразу после создания пара токенов обновляется
/// (<see cref="IAuthApi.RefreshAsync"/> + <see cref="TokenSession.ApplyAsync"/>): без этого
/// кабинет остался бы «без профиля» до повторного входа. Провал обновления — не повод
/// создавать профиль заново: <c>POST</c> уже прошёл, и экран предлагает только повторить
/// продление или войти снова.
/// </para>
/// <para>
/// Имя, опыт и «О себе» заявитель уже написал в заявке, поэтому шаг читает
/// <c>GET api/barber-requests/mine</c> и подставляет заполненные значения только для чтения.
/// Пустое «О себе» — не данные, поэтому поле остаётся редактируемым; остальные поля заявки
/// сюда не переходят. Неодобренная заявка форму не блокирует: роль уже выдана, и профиль
/// можно заполнить вручную.
/// </para>
/// <para>
/// Шаг «Добавьте место приёма» — тот же поиск по публичному каталогу, что в «Моих салонах»:
/// пауза 300 мс, минимум два символа и привязка первого салона как основного. Шаг можно
/// пропустить: кабинет откроется и без салона, но расписание будет пустым. Завершение
/// выражается событиями <see cref="CabinetRequested"/> и <see cref="SignInRequested"/>:
/// Core не знает ни про Shell, ни про MAUI.
/// </para>
/// </remarks>
public sealed partial class BarberOnboardingViewModel : ObservableObject
{
    /// <summary>
    /// Минимум символов поиска каталога: короче сервер отвечает ошибкой.
    /// </summary>
    public const int MinimumCatalogQueryLength = 2;

    /// <summary>
    /// Пауза между последним нажатием и запросом каталога: как у веба.
    /// </summary>
    public static readonly TimeSpan CatalogDebounce = TimeSpan.FromMilliseconds(300);

    private const int MaxDisplayNameLength = 120;
    private const int MinExperienceYears = 1;
    private const int MaxExperienceYears = 70;
    private const int MaxBioLength = 2000;

    private const string AlreadyExistsCode = "barberProfile.create.alreadyExists";
    private const string DuplicateSalonCode = "barberProfile.addSalon.duplicate";
    private const string MissingSalonCode = "barberProfile.addSalon.salonNotFound";

    private static string ReadFailedText => Loc.T("Не удалось проверить заявку.");
    private static string CreateFailedText => Loc.T("Не удалось создать профиль. Попробуйте ещё раз.");
    private static string ForbiddenText => Loc.T("Нет прав на создание профиля.");
    private static string AddFailedText => Loc.T("Не удалось добавить салон. Попробуйте ещё раз.");
    private static string CatalogFailedText => Loc.T("Не удалось загрузить каталог.");
    private static string AddedText => Loc.T("Салон добавлен.");
    private static string DisplayNameRequiredText => Loc.T("Укажите, как вас представить.");
    private static string DisplayNameTooLongText => Loc.T("Не длиннее 120 символов.");
    private static string ExperienceRequiredText => Loc.T("Укажите опыт работы в годах.");
    private static string ExperienceRangeText => Loc.T("Опыт — от 1 до 70 лет.");
    private static string LanguagesRequiredText => Loc.T("Выберите хотя бы один язык.");
    private static string BioTooLongText => Loc.T("О себе — не длиннее 2000 символов.");

    private readonly IBarberProfileApi _profileApi;
    private readonly IBarberRequestsApi _requestsApi;
    private readonly IAuthApi _authApi;
    private readonly TokenSession _session;
    private readonly IBarberSearchApi _searchApi;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly AutoRetryLoop _readAutoRetry;
    private readonly AutoRetryLoop _catalogAutoRetry;

    private bool _prefilledName;
    private bool _prefilledExperience;
    private bool _prefilledBio;
    private bool _isLoaded;
    private bool _isPageVisible;
    private bool _catalogLoaded;
    private Guid _profileId = Guid.Empty;
    private bool _profileCreated;
    private CancellationTokenSource? _catalogRequest;
    private Task _catalogOperation = Task.CompletedTask;
    private ApiFailureKind _readFailureKind;
    private ApiFailureKind _catalogFailureKind;
    private string? _catalogFailedCity;
    private string? _catalogFailedQuery;

    /// <summary>
    /// Создаёт онбординг мастера.
    /// </summary>
    /// <param name="profileApi">Создание профиля и привязка первого салона.</param>
    /// <param name="requestsApi">Чтение своей заявки для предзаполнения.</param>
    /// <param name="authApi">Продление пары токенов после создания профиля.</param>
    /// <param name="session">Сессия приложения: владелец пары токенов.</param>
    /// <param name="searchApi">Публичный каталог салонов для шага «место приёма».</param>
    /// <param name="readAutoRetry">
    /// Цикл повтора чтения заявки; <c>null</c> — собственный цикл.
    /// </param>
    /// <param name="catalogAutoRetry">
    /// Цикл повтора каталога; <c>null</c> — собственный цикл.
    /// </param>
    public BarberOnboardingViewModel(
        IBarberProfileApi profileApi,
        IBarberRequestsApi requestsApi,
        IAuthApi authApi,
        TokenSession session,
        IBarberSearchApi searchApi,
        AutoRetryLoop? readAutoRetry = null,
        AutoRetryLoop? catalogAutoRetry = null)
        : this(
            profileApi,
            requestsApi,
            authApi,
            session,
            searchApi,
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
            readAutoRetry,
            catalogAutoRetry)
    {
    }

    /// <summary>
    /// Создаёт онбординг с подменяемой паузой — для тестов без ожидания таймера.
    /// </summary>
    /// <param name="profileApi">Создание профиля и привязка первого салона.</param>
    /// <param name="requestsApi">Чтение своей заявки для предзаполнения.</param>
    /// <param name="authApi">Продление пары токенов после создания профиля.</param>
    /// <param name="session">Сессия приложения: владелец пары токенов.</param>
    /// <param name="searchApi">Публичный каталог салонов.</param>
    /// <param name="delay">Пауза перед запросом каталога.</param>
    /// <param name="readAutoRetry">Цикл повтора чтения заявки.</param>
    /// <param name="catalogAutoRetry">Цикл повтора каталога.</param>
    internal BarberOnboardingViewModel(
        IBarberProfileApi profileApi,
        IBarberRequestsApi requestsApi,
        IAuthApi authApi,
        TokenSession session,
        IBarberSearchApi searchApi,
        Func<TimeSpan, CancellationToken, Task> delay,
        AutoRetryLoop? readAutoRetry = null,
        AutoRetryLoop? catalogAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(profileApi);
        ArgumentNullException.ThrowIfNull(requestsApi);
        ArgumentNullException.ThrowIfNull(authApi);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(searchApi);
        ArgumentNullException.ThrowIfNull(delay);

        _profileApi = profileApi;
        _requestsApi = requestsApi;
        _authApi = authApi;
        _session = session;
        _searchApi = searchApi;
        _delay = delay;

        _readAutoRetry = readAutoRetry ?? new AutoRetryLoop();
        _catalogAutoRetry = catalogAutoRetry ?? new AutoRetryLoop();
        _readAutoRetry.StateChanged += OnReadAutoRetryStateChanged;
        _catalogAutoRetry.StateChanged += OnCatalogAutoRetryStateChanged;

        foreach (var language in BarberLanguageOption.Order)
        {
            // Русский — язык продукта и самый частый ответ; остальные мастер отмечает сам.
            Languages.Add(new BarberLanguageOption(language, language == Language.Ru));
        }
    }

    /// <summary>
    /// Профиль создан и сессия обновлена: пора переходить в кабинет.
    /// </summary>
    /// <remarks>
    /// Событие, а не вызов навигации: Core не знает про Shell. Оно же закрывает шаг салона
    /// и «Пропустить».
    /// </remarks>
    public event EventHandler? CabinetRequested;

    /// <summary>
    /// Нужен повторный вход: сессия мертва или человек сам выбрал «Войти».
    /// </summary>
    public event EventHandler? SignInRequested;

    /// <summary>
    /// Языки общения с отметками выбора; по умолчанию выбран русский.
    /// </summary>
    public ObservableCollection<BarberLanguageOption> Languages { get; } = [];

    /// <summary>
    /// Результаты последнего поиска каталога для шага «место приёма».
    /// </summary>
    public ObservableCollection<BarberSalonCatalogItem> Catalog { get; } = [];

    /// <summary>
    /// Идёт чтение своей заявки: форма ещё не показывается.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>
    /// Причина неудачи чтения заявки; <c>null</c>, когда читать удалось.
    /// </summary>
    [ObservableProperty]
    private string? _readError;

    /// <summary>
    /// Цикл сам повторяет упавшее чтение заявки.
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Сессия закончилась: форму показывать нечему, нужен вход.
    /// </summary>
    [ObservableProperty]
    private bool _isSignInRequired;

    /// <summary>
    /// Имя, которое увидят клиенты.
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// Опыт в годах строкой: поле ввода не умеет хранить число.
    /// </summary>
    [ObservableProperty]
    private string _experienceYears = string.Empty;

    /// <summary>
    /// Короткое описание о себе; пустое уходит как <c>null</c>.
    /// </summary>
    [ObservableProperty]
    private string _bio = string.Empty;

    /// <summary>
    /// Ошибка поля «Как вас представить».
    /// </summary>
    [ObservableProperty]
    private string? _displayNameError;

    /// <summary>
    /// Ошибка поля «Опыт, лет».
    /// </summary>
    [ObservableProperty]
    private string? _experienceError;

    /// <summary>
    /// Ошибка выбора языков.
    /// </summary>
    [ObservableProperty]
    private string? _languagesError;

    /// <summary>
    /// Ошибка поля «О себе».
    /// </summary>
    [ObservableProperty]
    private string? _bioError;

    /// <summary>
    /// Общая ошибка создания или отказа, не относящегося к известному полю.
    /// </summary>
    [ObservableProperty]
    private string? _formError;

    /// <summary>
    /// Повтор отправки может помочь: сеть, таймаут, <c>429</c>, <c>5xx</c>.
    /// </summary>
    [ObservableProperty]
    private bool _canRetrySubmit;

    /// <summary>
    /// Идёт создание профиля: кнопка показывает индикатор и не нажимается дважды.
    /// </summary>
    [ObservableProperty]
    private bool _isSubmitting;

    /// <summary>
    /// Профиль создан, пара токенов обновляется.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshingSession;

    /// <summary>
    /// Шаг «Добавьте место приёма»: профиль и сессия готовы.
    /// </summary>
    [ObservableProperty]
    private bool _isSalonStep;

    /// <summary>
    /// Обновить пару не удалось окончательно: остаётся войти снова.
    /// </summary>
    [ObservableProperty]
    private bool _isSessionExpired;

    /// <summary>
    /// Обновить пару не удалось временно: можно повторить, не создавая профиль заново.
    /// </summary>
    [ObservableProperty]
    private bool _isSessionRefreshFailed;

    /// <summary>
    /// Первый салон привязан: форма выбора скрывается, остаётся «Перейти в кабинет».
    /// </summary>
    [ObservableProperty]
    private bool _salonAdded;

    /// <summary>
    /// Идёт привязка выбранного салона.
    /// </summary>
    [ObservableProperty]
    private bool _isAddingSalon;

    /// <summary>
    /// Причина неудачи привязки салона.
    /// </summary>
    [ObservableProperty]
    private string? _addSalonError;

    /// <summary>
    /// Город поиска каталога на шаге салона.
    /// </summary>
    [ObservableProperty]
    private string _catalogCity = string.Empty;

    /// <summary>
    /// Строка поиска каталога: название или адрес.
    /// </summary>
    [ObservableProperty]
    private string _catalogQuery = string.Empty;

    /// <summary>
    /// Идёт поиск каталога, включая паузу перед запросом.
    /// </summary>
    [ObservableProperty]
    private bool _isCatalogLoading;

    /// <summary>
    /// Причина неудачи поиска каталога.
    /// </summary>
    [ObservableProperty]
    private string? _catalogError;

    /// <summary>
    /// Цикл сам повторяет упавший поиск каталога.
    /// </summary>
    [ObservableProperty]
    private bool _isCatalogAutoRetrying;

    /// <summary>
    /// Выбранная строка каталога для привязки.
    /// </summary>
    [ObservableProperty]
    private BarberSalonCatalogItem? _selectedCatalogSalon;

    /// <summary>
    /// Показывать форму: чтение заявки закончилось без ошибки и профиль ещё не создан.
    /// </summary>
    public bool ShowForm => !IsLoading
        && ReadError is null
        && !IsSignInRequired
        && !IsRefreshingSession
        && !IsSalonStep
        && !IsSessionExpired
        && !IsSessionRefreshFailed;

    /// <summary>
    /// Показывать ошибку чтения заявки.
    /// </summary>
    public bool ShowReadError => !IsLoading && ReadError is not null && !IsSignInRequired;

    /// <summary>
    /// Показывать состояние сессии: обновление, окончательный или временный сбой.
    /// </summary>
    public bool ShowSessionState => IsRefreshingSession || IsSessionExpired || IsSessionRefreshFailed;

    /// <summary>
    /// Заголовок состояния сессии: «Профиль создан…» и две ветки сбоя.
    /// </summary>
    public string SessionStateTitle
    {
        get
        {
            if (IsRefreshingSession)
            {
                return Loc.T("Профиль создан. Обновляем сессию…");
            }

            if (IsSessionExpired)
            {
                return Loc.T("Профиль создан, войдите снова");
            }

            return IsSessionRefreshFailed
                ? Loc.T("Профиль создан, но сессия не обновилась")
                : string.Empty;
        }
    }

    /// <summary>
    /// Пояснение к состоянию сессии: почему профиль уже есть и что делать дальше.
    /// </summary>
    public string SessionStateDescription
    {
        get
        {
            if (IsSessionExpired)
            {
                return Loc.T(
                    "Профиль мастера уже привязан к аккаунту, но сессия закончилась. Войдите — и кабинет откроется.");
            }

            return IsSessionRefreshFailed
                ? Loc.T(
                    "Повторять форму не нужно — профиль уже существует. Продлите сессию ещё раз или войдите заново.")
                : string.Empty;
        }
    }

    /// <summary>
    /// Заголовок шага «Добавьте место приёма».
    /// </summary>
    public string SalonStepTitle => IsSalonStep ? Loc.T("Добавьте место приёма") : string.Empty;

    /// <summary>
    /// Пояснение шага салона.
    /// </summary>
    public string SalonStepDescription => IsSalonStep
        ? Loc.T(
            "Расписание и запись ведутся по паре «мастер — место приёма». Выберите место из каталога или пропустите шаг — кабинет откроется и без него, но расписание будет пустым.")
        : string.Empty;

    /// <summary>
    /// Подсказка шага салона: расписание можно настроить позже.
    /// </summary>
    public string SalonStepHint => IsSalonStep
        ? Loc.T("Настроить расписание можно позже — в разделе «Расписание».")
        : string.Empty;

    /// <summary>
    /// Сообщение «Салон добавлен.» после успешной привязки.
    /// </summary>
    public string SalonAddedText => SalonAdded ? AddedText : string.Empty;

    /// <summary>
    /// Подпись кнопки создания профиля.
    /// </summary>
    public string SubmitButtonText => IsSubmitting
        ? Loc.T("Создаём профиль…")
        : Loc.T("Создать профиль");

    /// <summary>
    /// Форма принимает ввод: не идёт отправка и она вообще показана.
    /// </summary>
    public bool IsFormEnabled => ShowForm && !IsSubmitting;

    /// <summary>
    /// Профиль создан этим экраном: форма больше не вернётся.
    /// </summary>
    public bool HasCreatedProfile => _profileCreated;

    /// <summary>
    /// Имя взято из одобренной заявки и не редактируется.
    /// </summary>
    public bool IsDisplayNameLocked => _prefilledName;

    /// <summary>
    /// Опыт взят из одобренной заявки и не редактируется.
    /// </summary>
    public bool IsExperienceLocked => _prefilledExperience;

    /// <summary>
    /// «О себе» взято из одобренной заявки и не редактируется.
    /// </summary>
    public bool IsBioLocked => _prefilledBio;

    /// <summary>
    /// Хотя бы одно поле перенесено из заявки.
    /// </summary>
    public bool IsPrefilled => IsDisplayNameLocked || IsExperienceLocked || IsBioLocked;

    /// <summary>
    /// Подсказка под перенесённым полем.
    /// </summary>
    public string LockedHint => IsPrefilled
        ? Loc.T("Перенесено из заявки — изменить нельзя.")
        : string.Empty;

    /// <summary>
    /// Повторить продление сессии имеет смысл: временный сбой.
    /// </summary>
    public bool CanRetrySession => IsSessionRefreshFailed && !IsRefreshingSession;

    /// <summary>
    /// Есть ошибка поля «Имя».
    /// </summary>
    public bool HasDisplayNameError => DisplayNameError is not null;

    /// <summary>
    /// Есть ошибка поля «Опыт».
    /// </summary>
    public bool HasExperienceError => ExperienceError is not null;

    /// <summary>
    /// Есть ошибка выбора языков.
    /// </summary>
    public bool HasLanguagesError => LanguagesError is not null;

    /// <summary>
    /// Есть ошибка поля «О себе».
    /// </summary>
    public bool HasBioError => BioError is not null;

    /// <summary>
    /// Есть общая ошибка формы.
    /// </summary>
    public bool HasFormError => FormError is not null;

    /// <summary>
    /// Запрос каталога достаточно длинный.
    /// </summary>
    public bool CanSearchCatalog => CatalogQuery.Trim().Length >= MinimumCatalogQueryLength;

    /// <summary>
    /// Подсказка под полем поиска: строка короче минимума.
    /// </summary>
    public string CatalogHint => CanSearchCatalog
        ? string.Empty
        : Loc.T(
            "Начните вводить название или адрес — минимум {0} символа.",
            MinimumCatalogQueryLength);

    /// <summary>
    /// В каталоге есть результаты.
    /// </summary>
    public bool ShowCatalogResults => Catalog.Count > 0;

    /// <summary>
    /// Поиск закончился, но ничего не нашёл.
    /// </summary>
    public bool ShowCatalogEmpty => CanSearchCatalog
        && _catalogLoaded
        && !IsCatalogLoading
        && CatalogError is null
        && Catalog.Count == 0;

    /// <summary>
    /// Поиск каталога упал.
    /// </summary>
    public bool ShowCatalogError => CatalogError is not null;

    /// <summary>
    /// Привязка доступна: выбран салон, профиль известен и первый салон ещё не привязан.
    /// </summary>
    public bool CanAddSalon => !SalonAdded
        && SelectedCatalogSalon is not null
        && !IsAddingSalon
        && _profileId != Guid.Empty;

    /// <summary>
    /// Подпись кнопки привязки.
    /// </summary>
    public string AddSalonButtonText => IsAddingSalon
        ? Loc.T("Добавляем…")
        : Loc.T("Добавить");

    /// <summary>
    /// Есть ошибка привязки салона.
    /// </summary>
    public bool HasAddSalonError => AddSalonError is not null;

    /// <summary>
    /// Цикл повтора чтения заявки: тестам нужен счётчик попыток и завершение.
    /// </summary>
    internal AutoRetryLoop ReadAutoRetry => _readAutoRetry;

    /// <summary>
    /// Цикл повтора каталога: тестам нужен счётчик попыток и завершение.
    /// </summary>
    internal AutoRetryLoop CatalogAutoRetry => _catalogAutoRetry;

    /// <summary>
    /// Завершается, когда цикл чтения заявки останавливается.
    /// </summary>
    internal Task ReadRetryCompletion => _readAutoRetry.Completion;

    /// <summary>
    /// Завершается, когда цикл каталога останавливается.
    /// </summary>
    internal Task CatalogRetryCompletion => _catalogAutoRetry.Completion;

    /// <summary>
    /// Завершение последнего поиска каталога — тестам, чтобы не ждать таймер.
    /// </summary>
    internal Task CatalogOperation => _catalogOperation;

    /// <summary>
    /// Читает заявку и готовит форму при первом появлении страницы.
    /// </summary>
    /// <remarks>
    /// Идемпотентен: после успешного чтения повторный вызов ничего не перечитывает и не
    /// затирает правки. После неудачи вызов повторяет чтение — страница может прийти из
    /// фона и попробовать снова.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isLoaded || IsRefreshingSession || IsSalonStep || IsSessionExpired)
        {
            return;
        }

        await LoadRequestAsync(silent: false, cancellationToken);
    }

    /// <summary>
    /// Сообщает, что страница видна, и запускает автоповтор упавших чтений.
    /// </summary>
    /// <remarks>
    /// Идемпотентен. Если страница вернулась с набранным, но не выполненным запросом
    /// каталога (уход в фон отменил паузу), поиск повторяется — иначе поле осталось бы
    /// с текстом и пустым списком без объяснения.
    /// </remarks>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartReadAutoRetry();

        if (CanSearchCatalog
            && !_catalogLoaded
            && _catalogRequest is null
            && CatalogError is null
            && Catalog.Count == 0)
        {
            BeginCatalogSearch(debounce: false);
        }

        TryStartCatalogAutoRetry();
    }

    /// <summary>
    /// Сообщает, что страница скрыта, и останавливает все фоновые чтения.
    /// </summary>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        _readAutoRetry.Stop();
        _catalogAutoRetry.Stop();
        CancelCatalogRequest();
    }

    /// <summary>
    /// Повторяет чтение заявки по кнопке — сразу, без паузы цикла.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryReadAsync(CancellationToken cancellationToken)
    {
        if (IsLoading)
        {
            return;
        }

        _readAutoRetry.Stop();

        await LoadRequestAsync(silent: false, cancellationToken);

        TryStartReadAutoRetry();
    }

    /// <summary>
    /// Создаёт профиль, обновляет пару токенов и предлагает добавить салон.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task SubmitAsync(CancellationToken cancellationToken)
    {
        if (IsSubmitting || !ShowForm)
        {
            return;
        }

        if (!Validate(out var years))
        {
            return;
        }

        _readAutoRetry.Stop();
        IsSubmitting = true;
        FormError = null;
        CanRetrySubmit = false;

        try
        {
            var id = await _profileApi.CreateAsync(
                BuildRequest(years),
                cancellationToken);

            _profileId = id;
            _profileCreated = true;

            await RefreshSessionAsync(goToSalonStep: true, cancellationToken);
        }
        catch (BarberSearchApiException exception) when (HasCode(exception, AlreadyExistsCode))
        {
            // Профиль уже создан — гонка двух устройств или повтор после таймаута.
            // Это успех, но идентификатор неизвестен, поэтому шаг салона пропускается.
            _profileCreated = true;

            await RefreshSessionAsync(goToSalonStep: false, cancellationToken);
        }
        catch (BarberSearchApiException exception)
        {
            ApplyCreateFailure(exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Страница ушла: исход отправки неизвестен, повторять молча нельзя.
        }
        catch (Exception exception)
        {
            FormError = exception is HttpRequestException or OperationCanceledException
                ? BarberProfileText.Network
                : CreateFailedText;
            CanRetrySubmit = true;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    /// <summary>
    /// Повторяет обновление пары токенов, не создавая профиль заново.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetrySessionAsync(CancellationToken cancellationToken)
    {
        if (IsRefreshingSession)
        {
            return;
        }

        await RefreshSessionAsync(_profileId != Guid.Empty, cancellationToken);
    }

    /// <summary>
    /// Выходит из аккаунта и просит страницу открыть вход.
    /// </summary>
    /// <remarks>
    /// Пара после терминального отказа продления уже мертва, а после временного сбоя
    /// повторный вход — честный способ начать заново. Ошибка очистки хранилища наружу
    /// не выходит: человеку нужен экран входа, а не сообщение вместо него.
    /// </remarks>
    /// <returns>Задача очистки сессии.</returns>
    [RelayCommand]
    private async Task SignInAsync()
    {
        StopAutoRetry();

        try
        {
            await _session.ClearAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Устройство без рабочего хранилища: в памяти пары уже нет.
        }

        SignInRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Переходит в кабинет: шаг салона пропущен или завершён.
    /// </summary>
    [RelayCommand]
    private void GoToCabinet()
    {
        StopAutoRetry();

        CabinetRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Привязывает первый салон и остаётся на шаге, чтобы можно было перейти в кабинет.
    /// </summary>
    /// <remarks>
    /// Дубликат означает, что салон уже привязан (например, повтор после таймаута), и это
    /// успех: показывать ошибку на уже выполненном действии незачем.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand(CanExecute = nameof(CanAddSalon))]
    private async Task AddSalonAsync(CancellationToken cancellationToken)
    {
        if (SelectedCatalogSalon is not { } salon
            || IsAddingSalon
            || SalonAdded
            || _profileId == Guid.Empty)
        {
            return;
        }

        IsAddingSalon = true;
        AddSalonError = null;

        try
        {
            await _profileApi.AddSalonAsync(
                _profileId,
                salon.Id,
                isPrimary: true,
                cancellationToken);

            CompleteSalonAttachment(salon);
        }
        catch (BarberSearchApiException exception) when (HasCode(exception, DuplicateSalonCode))
        {
            CompleteSalonAttachment(salon);
        }
        catch (BarberSearchApiException exception)
            when (exception.StatusCode == HttpStatusCode.NotFound
                || HasCode(exception, MissingSalonCode))
        {
            AddSalonError = BarberSalonText.WriteError(exception, AddFailedText);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Страница ушла: ошибку показывать некому.
        }
        catch (Exception exception)
        {
            AddSalonError = BarberSalonText.WriteError(exception, AddFailedText);
        }
        finally
        {
            IsAddingSalon = false;
        }
    }

    /// <summary>
    /// Запускает поиск каталога сразу, без паузы: кнопка «Найти» на клавиатуре.
    /// </summary>
    [RelayCommand]
    private void SearchCatalog() => BeginCatalogSearch(debounce: false);

    /// <summary>
    /// Убирает ошибку привязки салона.
    /// </summary>
    [RelayCommand]
    private void DismissAddSalonError() => AddSalonError = null;

    /// <summary>
    /// Читает свою заявку и подставляет поля из одобренной.
    /// </summary>
    /// <param name="silent">Тихая попытка цикла: не показывать индикатор и не стирать причину.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему решает цикл повтора.</returns>
    private async Task<ApiFailureKind> LoadRequestAsync(
        bool silent,
        CancellationToken cancellationToken)
    {
        if (!silent)
        {
            IsLoading = true;
            ReadError = null;
        }

        var kind = ApiFailureKind.None;

        try
        {
            var response = await _requestsApi.GetMineAsync(cancellationToken);

            ReadError = null;
            IsSignInRequired = false;
            _readFailureKind = ApiFailureKind.None;
            _isLoaded = true;

            ApplyPrefill(response.Request);
        }
        catch (BarberSearchApiException exception)
            when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            kind = ApiFailureKind.Terminal;
            _readFailureKind = kind;
            ReadError = null;
            IsSignInRequired = true;
        }
        catch (Exception exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                _readFailureKind = kind;
                ReadError = BarberProfileText.ReadError(exception, kind, ReadFailedText);
            }
        }
        finally
        {
            if (!silent)
            {
                IsLoading = false;
            }

            NotifyFormState();
        }

        if (kind.IsRetryable())
        {
            TryStartReadAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Подставляет имя, опыт и «О себе» из одобренной заявки.
    /// </summary>
    /// <remarks>
    /// Заполненные значения блокируются: спрашивать одно и то же дважды незачем. Пустое
    /// «О себе» — не данные, поэтому поле остаётся редактируемым; неодобренная заявка не
    /// блокирует ничего.
    /// </remarks>
    /// <param name="request">Заявка из ответа; <c>null</c> — заявки нет.</param>
    private void ApplyPrefill(BarberRequestResponse? request)
    {
        if (request is not { Status: BarberRequestStatus.Approved })
        {
            NotifyFormState();

            return;
        }

        _prefilledName = !string.IsNullOrWhiteSpace(request.DisplayName);
        _prefilledExperience = request.YearsOfExperience > 0;
        _prefilledBio = !string.IsNullOrWhiteSpace(request.About);

        DisplayName = request.DisplayName;
        ExperienceYears = request.YearsOfExperience.ToString(CultureInfo.InvariantCulture);

        if (_prefilledBio)
        {
            Bio = request.About!;
        }

        NotifyFormState();
    }

    /// <summary>
    /// Обновляет пару токенов и решает, что показать дальше.
    /// </summary>
    /// <param name="goToSalonStep">Профиль создан с известным идентификатором.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача обновления.</returns>
    private async Task RefreshSessionAsync(
        bool goToSalonStep,
        CancellationToken cancellationToken)
    {
        IsRefreshingSession = true;
        IsSessionExpired = false;
        IsSessionRefreshFailed = false;
        FormError = null;

        try
        {
            // Сессия ленивая: после восстановления приложения RefreshToken ещё не прочитан
            // из хранилища, и без загрузки мы бы решили, что продлевать нечем.
            await _session.EnsureLoadedAsync(cancellationToken);

            var refreshToken = _session.RefreshToken;

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                IsSessionExpired = true;

                return;
            }

            var tokens = await _authApi.RefreshAsync(refreshToken, cancellationToken);

            await _session.ApplyAsync(tokens, cancellationToken);

            IsRefreshingSession = false;

            if (goToSalonStep && _profileId != Guid.Empty)
            {
                IsSalonStep = true;
            }
            else
            {
                // Идентификатор неизвестен (409) — предложить салон нечем, сразу в кабинет.
                StopAutoRetry();
                CabinetRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (BarberSearchApiException exception)
            when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            IsRefreshingSession = false;
            IsSessionExpired = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Страница ушла: исход продления неизвестен, состояние не меняем на ошибку.
            IsRefreshingSession = false;
        }
        catch (Exception)
        {
            // Сеть, 5xx, 429 и неожиданный сбой: профиль уже создан, повторять его нельзя.
            IsRefreshingSession = false;
            IsSessionRefreshFailed = true;
        }
        finally
        {
            NotifyFormState();
        }
    }

    /// <summary>
    /// Проверяет поля и раскладывает ошибки по ним.
    /// </summary>
    /// <param name="years">Разобранный опыт, если проверка прошла.</param>
    /// <returns><c>true</c>, если можно отправлять.</returns>
    private bool Validate(out int years)
    {
        ClearErrors();

        var name = DisplayName.Trim();
        var hasError = false;

        if (name.Length == 0)
        {
            DisplayNameError = DisplayNameRequiredText;
            hasError = true;
        }
        else if (name.Length > MaxDisplayNameLength)
        {
            DisplayNameError = DisplayNameTooLongText;
            hasError = true;
        }

        years = 0;

        if (!int.TryParse(
                ExperienceYears.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            ExperienceError = ExperienceRequiredText;
            hasError = true;
        }
        else if (parsed < MinExperienceYears || parsed > MaxExperienceYears)
        {
            ExperienceError = ExperienceRangeText;
            hasError = true;
        }
        else
        {
            years = parsed;
        }

        if (!Languages.Any(language => language.IsSelected))
        {
            LanguagesError = LanguagesRequiredText;
            hasError = true;
        }

        if (Bio.Trim().Length > MaxBioLength)
        {
            BioError = BioTooLongText;
            hasError = true;
        }

        return !hasError;
    }

    /// <summary>
    /// Собирает тело создания профиля.
    /// </summary>
    /// <param name="years">Проверенный опыт.</param>
    /// <returns>Тело <c>POST api/barber-profiles</c>.</returns>
    private BarberProfileCreateRequest BuildRequest(int years) =>
        new(
            DisplayName.Trim(),
            years,
            string.IsNullOrWhiteSpace(Bio) ? null : Bio.Trim(),
            Languages
                .Where(language => language.IsSelected)
                .Select(language => language.Language)
                .ToList(),
            CertificatesAndTraining: null,
            Achievements: null,
            PaymentMethods: [],
            CommunicationFormat: null,
            Goodies: null,
            LatePolicy: null);

    /// <summary>
    /// Разбирает отказ создания: коды полей важнее общей строки.
    /// </summary>
    /// <param name="exception">Отказ сервера.</param>
    private void ApplyCreateFailure(BarberSearchApiException exception)
    {
        var hasFieldError = false;

        foreach (var code in exception.ErrorCodes)
        {
            switch (code)
            {
                case "barberProfile.displayName":
                    DisplayNameError = DisplayNameRequiredText;
                    hasFieldError = true;
                    break;
                case "barberProfile.displayName.tooLong":
                    DisplayNameError = DisplayNameTooLongText;
                    hasFieldError = true;
                    break;
                case "barberProfile.yearsOfExperience":
                    ExperienceError = ExperienceRangeText;
                    hasFieldError = true;
                    break;
                case "barberProfile.languages.unknown":
                    LanguagesError = Loc.T("Выберите языки из списка.");
                    hasFieldError = true;
                    break;
                default:
                    break;
            }
        }

        if (hasFieldError)
        {
            return;
        }

        FormError = exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized => Loc.T("Сеанс истёк. Войдите заново."),
            HttpStatusCode.Forbidden => ForbiddenText,
            HttpStatusCode.TooManyRequests => BarberProfileText.TooManyRequests,
            _ => CreateFailedText,
        };

        CanRetrySubmit = ApiFailureClassifier.Classify(exception).IsRetryable();
    }

    /// <summary>
    /// Сбрасывает все ошибки перед новой проверкой.
    /// </summary>
    private void ClearErrors()
    {
        DisplayNameError = null;
        ExperienceError = null;
        LanguagesError = null;
        BioError = null;
        FormError = null;
        CanRetrySubmit = false;
    }

    /// <summary>
    /// Завершает привязку салона: выбранная строка уходит из каталога, шаг остаётся открытым.
    /// </summary>
    /// <param name="salon">Привязанный салон.</param>
    private void CompleteSalonAttachment(BarberSalonCatalogItem salon)
    {
        Catalog.Remove(salon);
        SelectedCatalogSalon = null;
        SalonAdded = true;

        AddSalonError = null;
    }

    /// <summary>
    /// Начинает поиск каталога: отменяет прошлый и запускает новый с паузой или сразу.
    /// </summary>
    /// <param name="debounce">Ждать паузу перед запросом.</param>
    private void BeginCatalogSearch(bool debounce)
    {
        CancelCatalogRequest();

        CatalogError = null;
        AddSalonError = null;
        _catalogFailureKind = ApiFailureKind.None;
        _catalogFailedCity = null;
        _catalogFailedQuery = null;
        _catalogLoaded = false;

        Catalog.Clear();
        SelectedCatalogSalon = null;

        if (!CanSearchCatalog)
        {
            NotifyCatalogState();

            return;
        }

        var city = CatalogCity.Trim();
        var query = CatalogQuery.Trim();
        var request = new CancellationTokenSource();

        _catalogRequest = request;
        IsCatalogLoading = true;
        NotifyCatalogState();

        _catalogOperation = debounce
            ? SearchAfterDebounceAsync(city, query, request)
            : LoadCatalogAsync(city, query, silent: false, request);
    }

    /// <summary>
    /// Ждёт паузу и читает каталог; отмена нового строкой — не ошибка.
    /// </summary>
    /// <param name="city">Город запроса.</param>
    /// <param name="query">Строка запроса.</param>
    /// <param name="request">Источник отмены этого запроса.</param>
    /// <returns>Задача поиска.</returns>
    private async Task SearchAfterDebounceAsync(
        string city,
        string query,
        CancellationTokenSource request)
    {
        var cancellationToken = request.Token;

        try
        {
            await _delay(CatalogDebounce, cancellationToken).ConfigureAwait(true);

            await LoadCatalogAsync(city, query, silent: false, request);
        }
        catch (OperationCanceledException)
        {
            // Новый запрос отменил паузу: ответ устарел, следующий обновит список.
        }
    }

    /// <summary>
    /// Читает каталог и раскладывает ответ по строкам.
    /// </summary>
    /// <param name="city">Город запроса.</param>
    /// <param name="query">Строка запроса.</param>
    /// <param name="silent">Попытка цикла: не показывать индикатор и не стирать причину.</param>
    /// <param name="request">Источник отмены этого запроса.</param>
    /// <returns>Вид сбоя: по нему решает цикл повтора.</returns>
    private async Task<ApiFailureKind> LoadCatalogAsync(
        string city,
        string query,
        bool silent,
        CancellationTokenSource request)
    {
        var cancellationToken = request.Token;
        var kind = ApiFailureKind.None;

        if (!silent)
        {
            IsCatalogLoading = true;
            CatalogError = null;
        }

        try
        {
            var results = await _searchApi.SearchSalonsAsync(city, query, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return ApiFailureKind.None;
            }

            var selectedId = SelectedCatalogSalon?.Id;

            Catalog.Clear();

            foreach (var salon in results)
            {
                Catalog.Add(new BarberSalonCatalogItem(salon));
            }

            _catalogLoaded = true;
            CatalogError = null;
            _catalogFailureKind = ApiFailureKind.None;
            _catalogFailedCity = null;
            _catalogFailedQuery = null;
            SelectedCatalogSalon = selectedId is { } id
                ? Catalog.FirstOrDefault(salon => salon.Id == id)
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ApiFailureKind.None;
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ApiFailureKind.None;
            }

            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                _catalogFailureKind = kind;
                _catalogFailedCity = city;
                _catalogFailedQuery = query;
                CatalogError = BarberProfileText.ReadError(
                    exception,
                    kind,
                    CatalogFailedText);
            }
        }
        finally
        {
            if (ReferenceEquals(_catalogRequest, request))
            {
                _catalogRequest = null;
                IsCatalogLoading = false;
                request.Dispose();
            }

            NotifyCatalogState();
        }

        if (kind.IsRetryable())
        {
            TryStartCatalogAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Запускает цикл повтора чтения заявки, если страница видна и сбой повторяемый.
    /// </summary>
    private void TryStartReadAutoRetry()
    {
        if (!_isPageVisible
            || _isLoaded
            || IsSignInRequired
            || ReadError is null
            || !_readFailureKind.IsRetryable())
        {
            return;
        }

        _readAutoRetry.Start(RetryReadAttemptAsync);
    }

    /// <summary>
    /// Запускает цикл повтора каталога, если страница видна и повторяется тот же запрос.
    /// </summary>
    private void TryStartCatalogAutoRetry()
    {
        if (!_isPageVisible
            || _catalogFailedQuery is null
            || !_catalogFailureKind.IsRetryable()
            || !string.Equals(CatalogQuery.Trim(), _catalogFailedQuery, StringComparison.Ordinal)
            || !string.Equals(
                CatalogCity.Trim(),
                _catalogFailedCity ?? string.Empty,
                StringComparison.Ordinal))
        {
            return;
        }

        _catalogAutoRetry.Start(RetryCatalogAsync);
    }

    /// <summary>
    /// Одна попытка цикла: перечитывает заявку, не гася причину ошибки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetryReadAttemptAsync(CancellationToken cancellationToken) =>
        ReadError is null || _isLoaded
            ? Task.FromResult(ApiFailureKind.None)
            : LoadRequestAsync(silent: true, cancellationToken);

    /// <summary>
    /// Одна попытка цикла: повторяет ровно тот запрос каталога, который упал.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены цикла.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetryCatalogAsync(CancellationToken cancellationToken)
    {
        if (!_isPageVisible || _catalogFailedQuery is not { } query)
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        var city = _catalogFailedCity ?? string.Empty;

        if (!string.Equals(CatalogQuery.Trim(), query, StringComparison.Ordinal)
            || !string.Equals(CatalogCity.Trim(), city, StringComparison.Ordinal))
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = _catalogRequest;

        _catalogRequest = request;

        previous?.Cancel();
        previous?.Dispose();

        return LoadCatalogAsync(city, query, silent: true, request);
    }

    /// <summary>
    /// Отменяет незавершённый поиск каталога: паузу или запрос.
    /// </summary>
    private void CancelCatalogRequest()
    {
        var previous = _catalogRequest;

        _catalogRequest = null;

        previous?.Cancel();
        previous?.Dispose();

        IsCatalogLoading = false;
    }

    /// <summary>
    /// Проверяет наличие публичного кода ошибки.
    /// </summary>
    /// <param name="exception">Отказ сервера.</param>
    /// <param name="code">Публичный код.</param>
    /// <returns><c>true</c>, если код есть в ответе.</returns>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Переключает строку «Пробуем снова…» чтения заявки вслед за циклом.
    /// </summary>
    private void OnReadAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _readAutoRetry.IsRunning;

    /// <summary>
    /// Переключает строку «Пробуем снова…» каталога вслед за циклом.
    /// </summary>
    private void OnCatalogAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsCatalogAutoRetrying = _catalogAutoRetry.IsRunning;

    /// <summary>
    /// Поднимает уведомления обо всём, что зависит от стадии онбординга и формы.
    /// </summary>
    private void NotifyFormState()
    {
        OnPropertyChanged(nameof(ShowForm));
        OnPropertyChanged(nameof(ShowReadError));
        OnPropertyChanged(nameof(ShowSessionState));
        OnPropertyChanged(nameof(SessionStateTitle));
        OnPropertyChanged(nameof(SessionStateDescription));
        OnPropertyChanged(nameof(SalonStepTitle));
        OnPropertyChanged(nameof(SalonStepDescription));
        OnPropertyChanged(nameof(SalonStepHint));
        OnPropertyChanged(nameof(SalonAddedText));
        OnPropertyChanged(nameof(SubmitButtonText));
        OnPropertyChanged(nameof(IsFormEnabled));
        OnPropertyChanged(nameof(CanRetrySession));
        OnPropertyChanged(nameof(HasCreatedProfile));
        OnPropertyChanged(nameof(IsDisplayNameLocked));
        OnPropertyChanged(nameof(IsExperienceLocked));
        OnPropertyChanged(nameof(IsBioLocked));
        OnPropertyChanged(nameof(IsPrefilled));
        OnPropertyChanged(nameof(LockedHint));
        OnPropertyChanged(nameof(HasAddSalonError));
    }

    /// <summary>
    /// Поднимает уведомления обо всём, что зависит от поиска и выбора каталога.
    /// </summary>
    private void NotifyCatalogState()
    {
        OnPropertyChanged(nameof(CanSearchCatalog));
        OnPropertyChanged(nameof(CatalogHint));
        OnPropertyChanged(nameof(ShowCatalogResults));
        OnPropertyChanged(nameof(ShowCatalogEmpty));
        OnPropertyChanged(nameof(ShowCatalogError));
        OnPropertyChanged(nameof(CanAddSalon));
        OnPropertyChanged(nameof(AddSalonButtonText));

        AddSalonCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Чтение закончилось или началось: переключаются формы и состояния.
    /// </summary>
    partial void OnIsLoadingChanged(bool value) => NotifyFormState();

    /// <summary>
    /// Причина чтения изменилась: переключаются форма и ошибка.
    /// </summary>
    partial void OnReadErrorChanged(string? value) => NotifyFormState();

    /// <summary>
    /// Требование входа изменилось: форма скрывается.
    /// </summary>
    partial void OnIsSignInRequiredChanged(bool value) => NotifyFormState();

    /// <summary>
    /// Отправка началась или закончилась: кнопка меняет подпись.
    /// </summary>
    partial void OnIsSubmittingChanged(bool value) => NotifyFormState();

    /// <summary>
    /// Обновление сессии началось или закончилось.
    /// </summary>
    partial void OnIsRefreshingSessionChanged(bool value) => NotifyFormState();

    /// <summary>
    /// Открылся или закрылся шаг салона.
    /// </summary>
    partial void OnIsSalonStepChanged(bool value) => NotifyFormState();

    /// <summary>
    /// Продление пары окончилось окончательным отказом.
    /// </summary>
    partial void OnIsSessionExpiredChanged(bool value) => NotifyFormState();

    /// <summary>
    /// Продление пары окончилось временным сбоем.
    /// </summary>
    partial void OnIsSessionRefreshFailedChanged(bool value) => NotifyFormState();

    /// <summary>
    /// Салон привязан: шаг скрывает выбор и пересчитывает доступность.
    /// </summary>
    partial void OnSalonAddedChanged(bool value)
    {
        NotifyFormState();
        NotifyCatalogState();
    }

    /// <summary>
    /// Привязка началась или закончилась: кнопка меняет подпись.
    /// </summary>
    partial void OnIsAddingSalonChanged(bool value) => NotifyCatalogState();

    /// <summary>
    /// Ошибка привязки появилась или исчезла.
    /// </summary>
    partial void OnAddSalonErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasAddSalonError));

    /// <summary>
    /// Поле города изменилось: каталог ищется заново с паузой.
    /// </summary>
    partial void OnCatalogCityChanged(string value) => BeginCatalogSearch(debounce: true);

    /// <summary>
    /// Строка поиска изменилась: каталог ищется заново с паузой.
    /// </summary>
    partial void OnCatalogQueryChanged(string value) => BeginCatalogSearch(debounce: true);

    /// <summary>
    /// Загрузка каталога началась или закончилась.
    /// </summary>
    partial void OnIsCatalogLoadingChanged(bool value) => NotifyCatalogState();

    /// <summary>
    /// Ошибка каталога появилась или исчезла.
    /// </summary>
    partial void OnCatalogErrorChanged(string? value) => NotifyCatalogState();

    /// <summary>
    /// Выбранная строка каталога изменилась: пересчитывается доступность привязки.
    /// </summary>
    partial void OnSelectedCatalogSalonChanged(BarberSalonCatalogItem? value) =>
        NotifyCatalogState();

    /// <summary>
    /// Имя изменилось: ошибка поля и общая ошибка снимаются.
    /// </summary>
    partial void OnDisplayNameChanged(string value) => ClearSubmitFeedback();

    /// <summary>
    /// Опыт изменился: ошибка поля и общая ошибка снимаются.
    /// </summary>
    partial void OnExperienceYearsChanged(string value) => ClearSubmitFeedback();

    /// <summary>
    /// «О себе» изменилось: ошибка поля и общая ошибка снимаются.
    /// </summary>
    partial void OnBioChanged(string value) => ClearSubmitFeedback();

    /// <summary>
    /// Ошибка поля «Имя» появилась или исчезла.
    /// </summary>
    partial void OnDisplayNameErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasDisplayNameError));

    /// <summary>
    /// Ошибка поля «Опыт» появилась или исчезла.
    /// </summary>
    partial void OnExperienceErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasExperienceError));

    /// <summary>
    /// Ошибка выбора языков появилась или исчезла.
    /// </summary>
    partial void OnLanguagesErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasLanguagesError));

    /// <summary>
    /// Ошибка поля «О себе» появилась или исчезла.
    /// </summary>
    partial void OnBioErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasBioError));

    /// <summary>
    /// Общая ошибка формы появилась или исчезла.
    /// </summary>
    partial void OnFormErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasFormError));

    /// <summary>
    /// Снимает ошибки отправки после правки поля.
    /// </summary>
    private void ClearSubmitFeedback()
    {
        DisplayNameError = null;
        ExperienceError = null;
        LanguagesError = null;
        BioError = null;
        FormError = null;
        CanRetrySubmit = false;
    }
}
