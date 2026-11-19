using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Admin;

/// <summary>
/// Карточка мастера: контакты, чек-лист готовности, портфолио и решение о подтверждении.
/// </summary>
/// <remarks>
/// <para>
/// Карточка читается отдельным запросом по идентификатору: строка очереди не несёт
/// ни контактов, ни чек-листа, ни фотографий. Возврат к очереди после решения — это и есть
/// обновление списка; сама карточка после успеха закрывается.
/// </para>
/// <para>
/// Действия живут внизу экрана: подтверждение — основная кнопка, снятие подтверждения —
/// второстепенная, открывающая встроенную панель подтверждения. Записи не повторяются
/// автоповтором; повторяемый сбой чтения, пока экран виден, лечится сам.
/// </para>
/// <para>
/// Подтвердить скрытый из поиска профиль домен не даёт
/// (<c>barberProfile.verify.inactive</c>), поэтому кнопка гаснет, а причина видна словами.
/// Чек-лист готовности остаётся подсказкой, как и у веба: подтвердить профиль можно
/// и с минусами, и подтверждение не блокируется неполным чек-листом.
/// </para>
/// </remarks>
public sealed partial class AdminMasterViewModel : ObservableObject
{
    /// <summary>
    /// Код отказа домена: скрытый профиль подтвердить нельзя.
    /// </summary>
    private const string VerifyInactiveCode = "barberProfile.verify.inactive";

    private static string LoadFailedText => Loc.T("Не удалось загрузить профиль мастера.");
    private static string VerifyFailedText => Loc.T("Не удалось подтвердить профиль. Попробуйте ещё раз.");
    private static string UnverifyFailedText => Loc.T("Не удалось снять подтверждение. Попробуйте ещё раз.");

    private readonly IAdminMastersApi _mastersApi;
    private readonly BarberSearchApiOptions _apiOptions;
    private readonly TokenSession _session;
    private readonly TimeProvider _timeProvider;
    private readonly AutoRetryLoop _autoRetry;

    private AdminBarberDetailResponse? _detail;
    private Guid _masterId;
    private Guid? _loadedId;
    private int _generation;
    private ApiFailureKind _failureKind;
    private bool _isPageVisible;

    /// <summary>
    /// Создаёт карточку мастера.
    /// </summary>
    /// <param name="mastersApi">Чтение карточки и записи подтверждения.</param>
    /// <param name="apiOptions">Адрес API: от него зависят абсолютные адреса картинок.</param>
    /// <param name="session">Сессия: конец сессии при <c>401</c>.</param>
    /// <param name="timeProvider">Часы: «Профиль создан» и «Обновлён» считаются от них.</param>
    /// <param name="autoRetry">
    /// Цикл автоповтора чтения; <c>null</c> — собственный цикл с обычным расписанием.
    /// </param>
    public AdminMasterViewModel(
        IAdminMastersApi mastersApi,
        BarberSearchApiOptions apiOptions,
        TokenSession session,
        TimeProvider timeProvider,
        AutoRetryLoop? autoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(mastersApi);
        ArgumentNullException.ThrowIfNull(apiOptions);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _mastersApi = mastersApi;
        _apiOptions = apiOptions;
        _session = session;
        _timeProvider = timeProvider;

        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;
    }

    /// <summary>
    /// Чек-лист готовности: шесть пунктов, по которым видно, чего не хватает.
    /// </summary>
    public ObservableCollection<AdminReadinessItem> ReadinessItems { get; } = [];

    /// <summary>
    /// Фотографии портфолио в порядке сервера, включая скрытые и неодобренные.
    /// </summary>
    public ObservableCollection<AdminMasterPhotoItem> Photos { get; } = [];

    /// <summary>
    /// Идёт первая загрузка карточки.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>
    /// Профиль не найден: ссылка устарела или запись удалена.
    /// </summary>
    [ObservableProperty]
    private bool _isNotFound;

    /// <summary>
    /// Текст сбоя чтения; <c>null</c>, если карточка есть.
    /// </summary>
    [ObservableProperty]
    private string? _loadErrorText;

    /// <summary>
    /// Сервер ответил <c>403</c>: роль не администратор, данные не показываются.
    /// </summary>
    [ObservableProperty]
    private bool _isForbidden;

    /// <summary>
    /// Сервер ответил <c>401</c>: пары токенов больше нет, нужен вход.
    /// </summary>
    [ObservableProperty]
    private bool _isSignInRequired;

    /// <summary>
    /// Цикл сам повторяет чтение: под причиной видна строка «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Текст сбоя действия; <c>null</c>, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    private string? _actionErrorText;

    /// <summary>
    /// Идёт запись подтверждения: кнопки заблокированы, повтор игнорируется.
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Панель подтверждения снятия открыта.
    /// </summary>
    [ObservableProperty]
    private bool _isUnverifyPanelOpen;

    /// <summary>
    /// Имя мастера.
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// Email владельца профиля.
    /// </summary>
    [ObservableProperty]
    private string _email = string.Empty;

    /// <summary>
    /// Грейд по-русски.
    /// </summary>
    [ObservableProperty]
    private string _gradeText = string.Empty;

    /// <summary>
    /// Телефон владельца либо «Не указан».
    /// </summary>
    [ObservableProperty]
    private string _phoneText = string.Empty;

    /// <summary>
    /// Описание о себе либо подпись, что его нет.
    /// </summary>
    [ObservableProperty]
    private string _bioText = string.Empty;

    /// <summary>
    /// Стаж в годах словами.
    /// </summary>
    [ObservableProperty]
    private string _experienceText = string.Empty;

    /// <summary>
    /// Рейтинг и отзывы одной строкой; без отзывов — «Опубликованных отзывов нет».
    /// </summary>
    [ObservableProperty]
    private string _ratingText = string.Empty;

    /// <summary>
    /// Когда профиль создан.
    /// </summary>
    [ObservableProperty]
    private string _createdAtText = string.Empty;

    /// <summary>
    /// Когда профиль последний раз менялся.
    /// </summary>
    [ObservableProperty]
    private string _updatedAtText = string.Empty;

    /// <summary>
    /// Абсолютный адрес аватара; <c>null</c> — кадра нет.
    /// </summary>
    [ObservableProperty]
    private string? _avatarImageUrl;

    /// <summary>
    /// Состояние аватара словами: «Фото скрыто», «Отклонено» или «На проверке».
    /// </summary>
    [ObservableProperty]
    private string? _avatarStateText;

    /// <summary>
    /// Причина отказа по аватару; <c>null</c>, пока кадр не отклонён.
    /// </summary>
    [ObservableProperty]
    private string? _avatarRejectionText;

    /// <summary>
    /// Профиль подтверждён администратором.
    /// </summary>
    [ObservableProperty]
    private bool _isVerified;

    /// <summary>
    /// Профиль виден в поиске.
    /// </summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// Есть ли загруженная карточка.
    /// </summary>
    public bool HasDetail => _detail is not null;

    /// <summary>
    /// Содержимое карточки видно.
    /// </summary>
    public bool ShowContent =>
        _detail is not null && !IsLoading && !IsNotFound && !ShowError && !ShowForbidden
        && !IsSignInRequired;

    /// <summary>
    /// Сбой чтения виден: причина и «Повторить».
    /// </summary>
    public bool ShowError => LoadErrorText is not null && !ShowForbidden && !IsSignInRequired;

    /// <summary>
    /// Отказ в доступе виден.
    /// </summary>
    public bool ShowForbidden => IsForbidden && !IsSignInRequired;

    /// <summary>
    /// Просьба войти снова видна.
    /// </summary>
    public bool ShowSignInRequired => IsSignInRequired;

    /// <summary>
    /// Нижняя панель действий видна.
    /// </summary>
    public bool ShowActions => ShowContent;

    /// <summary>
    /// Есть ли ошибка действия.
    /// </summary>
    public bool HasActionError => ActionErrorText is not null;

    /// <summary>
    /// Есть ли аватар для показа картинкой.
    /// </summary>
    public bool HasAvatar => AvatarImageUrl is not null;

    /// <summary>
    /// Показывать ли инициалы вместо картинки.
    /// </summary>
    public bool ShowInitials => !HasAvatar;

    /// <summary>
    /// Инициалы мастера для заглушки аватара.
    /// </summary>
    public string Initials => BarberText.Initials(DisplayName);

    /// <summary>
    /// Состояние аватара известно.
    /// </summary>
    public bool HasAvatarState => AvatarStateText is not null;

    /// <summary>
    /// Причина отказа по аватару известна.
    /// </summary>
    public bool HasAvatarRejection => AvatarRejectionText is not null;

    /// <summary>
    /// Состояние профиля: «На проверке», «Подтверждён» или «Скрыт из поиска».
    /// </summary>
    public string StatusText =>
        _detail is null ? string.Empty : AdminMasterItem.MasterStatus(IsActive, IsVerified);

    /// <summary>
    /// Что означает текущее подтверждение для поиска и записи.
    /// </summary>
    public string VerificationStateText => IsVerified
        ? Loc.T("Профиль подтверждён и участвует в поиске.")
        : Loc.T("Профиль ждёт проверки и в поиске не показывается.");

    /// <summary>
    /// Почему подтверждение недоступно; <c>null</c> — доступно.
    /// </summary>
    /// <remarks>
    /// Домен отказывает только скрытому профилю (<c>barberProfile.verify.inactive</c>):
    /// чек-лист готовности — подсказка, а не блокировка, как и у веба.
    /// </remarks>
    public string? VerifyBlockedReason => _detail is not null && !IsActive
        ? Loc.T("Сначала верните профиль в поиск: подтверждение скрытого профиля домен не разрешает.")
        : null;

    /// <summary>
    /// Причина недоступности подтверждения известна.
    /// </summary>
    public bool HasVerifyBlockedReason => VerifyBlockedReason is not null;

    /// <summary>
    /// Кнопка подтверждения доступна.
    /// </summary>
    public bool CanVerify => ShowContent && !IsVerified && IsActive && !IsBusy;

    /// <summary>
    /// Кнопка снятия подтверждения доступна.
    /// </summary>
    public bool CanUnverify => ShowContent && IsVerified && !IsBusy && !IsUnverifyPanelOpen;

    /// <summary>
    /// Подтверждение снятия доступно: панель открыта и запись не идёт.
    /// </summary>
    public bool CanConfirmUnverify => IsUnverifyPanelOpen && !IsBusy;

    /// <summary>
    /// Все пункты чек-листа готовности выполнены.
    /// </summary>
    public bool IsReadinessComplete => ReadinessItems.All(item => item.IsDone);

    /// <summary>
    /// Сколько пунктов готовности выполнено: «4 из 6».
    /// </summary>
    public string ReadinessDoneText => Loc.T(
        "{0} из {1}",
        ReadinessItems.Count(item => item.IsDone).ToString(CultureInfo.InvariantCulture),
        ReadinessItems.Count.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Есть ли фотографии портфолио.
    /// </summary>
    public bool HasPhotos => Photos.Count > 0;

    /// <summary>
    /// Портфолио пусто.
    /// </summary>
    public bool HasNoPhotos => Photos.Count == 0;

    /// <summary>
    /// Цикл автоповтора чтения: тестам нужен счётчик попыток.
    /// </summary>
    internal AutoRetryLoop AutoRetry => _autoRetry;

    /// <summary>
    /// Завершается, когда цикл автоповтора останавливается.
    /// </summary>
    internal Task AutoRetryCompletion => _autoRetry.Completion;

    /// <summary>
    /// Читает карточку по идентификатору из маршрута.
    /// </summary>
    /// <param name="masterId">Идентификатор профиля; <c>null</c> — ссылка без id.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public async Task InitializeAsync(Guid? masterId, CancellationToken cancellationToken = default)
    {
        if (masterId is null)
        {
            ApplyNotFoundState();

            return;
        }

        if (_masterId != masterId.Value)
        {
            _masterId = masterId.Value;
            _loadedId = null;
            _detail = null;

            IsLoading = true;
            IsNotFound = false;
            LoadErrorText = null;
            IsForbidden = false;
            IsSignInRequired = false;

            NotifyDetailState();
        }

        await LoadAsync(silent: _loadedId == _masterId, cancellationToken);
    }

    /// <summary>
    /// Включает автоповтор: страница видна.
    /// </summary>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartAutoRetry();
    }

    /// <summary>
    /// Выключает автоповтор: страница скрыта или открыта другая.
    /// </summary>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        StopAutoRetryLoop();
    }

    /// <summary>
    /// Гасит карточку после завершения сессии.
    /// </summary>
    public void ApplySignedOutState()
    {
        StopAutoRetryLoop();

        _generation++;
        _detail = null;
        _loadedId = null;

        IsLoading = false;
        IsNotFound = false;
        LoadErrorText = null;
        IsForbidden = false;
        IsSignInRequired = true;
        IsBusy = false;
        IsUnverifyPanelOpen = false;
        ActionErrorText = null;
        AvatarImageUrl = null;
        AvatarStateText = null;
        AvatarRejectionText = null;

        ReadinessItems.Clear();
        Photos.Clear();

        NotifyDetailState();
    }

    /// <summary>
    /// Подтверждает профиль: после успеха страница закрывается к очереди.
    /// </summary>
    /// <remarks>
    /// Повторное нажатие во время запроса игнорируется. <c>401</c> гасит сессию и возвращает
    /// <c>null</c>, как и успех; страница различает случаи по <see cref="IsSignInRequired"/>.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Текст ошибки либо <c>null</c>, когда всё получилось.</returns>
    public async Task<string?> VerifyAsync(CancellationToken cancellationToken = default)
    {
        if (_detail is null || !CanVerify)
        {
            return null;
        }

        IsBusy = true;
        ActionErrorText = null;

        NotifyDetailState();

        try
        {
            await _mastersApi.VerifyMasterAsync(_detail.Id, cancellationToken);

            ApplyVerification(verified: true);

            return null;
        }
        catch (BarberSearchApiException exception)
            when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();

            return null;
        }
        catch (Exception exception)
        {
            return SetActionFailure(exception, VerifyFailedText, cancellationToken);
        }
        finally
        {
            IsBusy = false;

            NotifyDetailState();
        }
    }

    /// <summary>
    /// Снимает подтверждение после подтверждения в панели.
    /// </summary>
    /// <remarks>
    /// Панель показывает последствие и ждёт второго нажатия; после успеха страница
    /// закрывается к очереди.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Текст ошибки либо <c>null</c>, когда всё получилось.</returns>
    public async Task<string?> ConfirmUnverifyAsync(CancellationToken cancellationToken = default)
    {
        if (_detail is null || !CanConfirmUnverify)
        {
            return null;
        }

        IsBusy = true;
        ActionErrorText = null;

        NotifyDetailState();

        try
        {
            await _mastersApi.UnverifyMasterAsync(_detail.Id, cancellationToken);

            ApplyVerification(verified: false);

            IsUnverifyPanelOpen = false;

            return null;
        }
        catch (BarberSearchApiException exception)
            when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();

            return null;
        }
        catch (Exception exception)
        {
            return SetActionFailure(exception, UnverifyFailedText, cancellationToken);
        }
        finally
        {
            IsBusy = false;

            NotifyDetailState();
        }
    }

    /// <summary>
    /// Перечитывает карточку после ошибки — сразу, по кнопке.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RetryAsync()
    {
        StopAutoRetryLoop();

        LoadErrorText = null;

        NotifyDetailState();

        await LoadAsync(silent: false, CancellationToken.None);

        TryStartAutoRetry();
    }

    /// <summary>
    /// Открывает панель подтверждения снятия.
    /// </summary>
    [RelayCommand]
    private void OpenUnverifyPanel()
    {
        if (!CanUnverify)
        {
            return;
        }

        ActionErrorText = null;
        IsUnverifyPanelOpen = true;

        NotifyDetailState();
    }

    /// <summary>
    /// Закрывает панель, ничего не меняя.
    /// </summary>
    [RelayCommand]
    private void CancelUnverify()
    {
        IsUnverifyPanelOpen = false;

        NotifyDetailState();
    }

    /// <summary>
    /// Читает карточку и раскладывает её по полям экрана.
    /// </summary>
    /// <param name="silent">Попытка цикла: причина и индикатор не сбрасываются на старте.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему решает цикл автоповтора.</returns>
    private async Task<ApiFailureKind> LoadAsync(bool silent, CancellationToken cancellationToken)
    {
        if (_masterId == Guid.Empty)
        {
            ApplyNotFoundState();

            return ApiFailureKind.Terminal;
        }

        _generation++;

        var generation = _generation;
        var kind = ApiFailureKind.None;

        if (!silent)
        {
            IsLoading = true;
            IsNotFound = false;
            LoadErrorText = null;
            IsForbidden = false;
            IsSignInRequired = false;
            ActionErrorText = null;

            NotifyDetailState();
        }

        try
        {
            var detail = await _mastersApi.GetMasterAsync(_masterId, cancellationToken);

            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            ApplyDetail(detail);

            _loadedId = _masterId;
            _failureKind = ApiFailureKind.None;
            LoadErrorText = null;
            IsForbidden = false;
            IsSignInRequired = false;
        }
        catch (Exception exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                if (exception is BarberSearchApiException
                    {
                        StatusCode: HttpStatusCode.Unauthorized,
                    })
                {
                    await EndSessionAsync();
                }
                else if (exception is BarberSearchApiException
                {
                    StatusCode: HttpStatusCode.Forbidden,
                })
                {
                    ApplyForbiddenState();
                }
                else if (exception is BarberSearchApiException
                {
                    StatusCode: HttpStatusCode.NotFound,
                })
                {
                    ApplyNotFoundState();
                    kind = ApiFailureKind.Terminal;
                }
                else if (generation == _generation)
                {
                    LoadErrorText = AdminErrorText.Read(exception, kind, LoadFailedText);
                    _failureKind = kind;

                    NotifyDetailState();
                }
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;

                NotifyDetailState();
                TryStartAutoRetry();
            }
        }

        return kind;
    }

    /// <summary>
    /// Переносит ответ API в поля карточки.
    /// </summary>
    /// <param name="detail">Карточка мастера.</param>
    private void ApplyDetail(AdminBarberDetailResponse detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var now = _timeProvider.GetUtcNow();
        var avatarUrl = detail.AvatarUrl;
        var phoneNumber = detail.PhoneNumber;
        var bio = detail.Bio;
        var rejectionReason = detail.AvatarRejectionReason;

        _detail = detail;

        DisplayName = detail.DisplayName;
        Email = detail.Email;
        GradeText = BarberText.GradeText(detail.Grade);
        PhoneText = string.IsNullOrWhiteSpace(phoneNumber)
            ? Loc.T("Не указан")
            : phoneNumber.Trim();
        BioText = string.IsNullOrWhiteSpace(bio)
            ? Loc.T("Мастер не заполнил описание о себе.")
            : bio.Trim();
        ExperienceText = BarberText.Experience(detail.YearsOfExperience);
        RatingText = detail.Rating is null
            ? Loc.T("Опубликованных отзывов нет")
            : string.Concat(
                BarberText.Rating(detail.Rating),
                " · ",
                BarberText.Reviews(detail.ReviewsCount));
        CreatedAtText = AccountText.Moment(detail.CreatedAt, TimeZoneInfo.Local, now);
        UpdatedAtText = AccountText.Moment(detail.UpdatedAt, TimeZoneInfo.Local, now);
        IsActive = detail.IsActive;
        IsVerified = detail.IsVerified;
        AvatarImageUrl = string.IsNullOrWhiteSpace(avatarUrl)
            ? null
            : MediaUrl.Resolve(
                _apiOptions.BaseAddress,
                MediaUrl.Resize(avatarUrl, "md"));
        AvatarStateText = AvatarState(detail.IsAvatarBlocked, detail.AvatarModerationStatus);
        AvatarRejectionText = detail.AvatarModerationStatus == PhotoModerationStatus.Rejected
            && !string.IsNullOrWhiteSpace(rejectionReason)
                ? Loc.T("Причина: {0}", rejectionReason.Trim())
                : null;

        ApplyReadiness(detail.Readiness);
        ApplyPhotos(detail);

        NotifyDetailState();
    }

    /// <summary>
    /// Складывает чек-лист готовности в строки экрана.
    /// </summary>
    /// <param name="readiness">Счётчики готовности из карточки.</param>
    private void ApplyReadiness(AdminBarberReadinessResponse readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        ReadinessItems.Clear();

        ReadinessItems.Add(new AdminReadinessItem(
            Loc.T("Профиль заполнен"),
            readiness.IsProfileFilled ? Loc.T("Описание и опыт есть") : Loc.T("Нет описания или опыта"),
            readiness.IsProfileFilled));

        ReadinessItems.Add(new AdminReadinessItem(
            Loc.T("Email подтверждён"),
            readiness.IsEmailVerified ? Loc.T("Подтверждён") : Loc.T("Не подтверждён"),
            readiness.IsEmailVerified));

        ReadinessItems.Add(new AdminReadinessItem(
            Loc.T("Услуги"),
            readiness.ServicesCount == 0
                ? Loc.T("Ни одной услуги")
                : readiness.ServicesCount.ToString(CultureInfo.InvariantCulture),
            readiness.ServicesCount > 0));

        ReadinessItems.Add(new AdminReadinessItem(
            Loc.T("Салоны"),
            readiness.SalonsCount == 0
                ? Loc.T("Не привязан ни к одному салону")
                : readiness.SalonsCount.ToString(CultureInfo.InvariantCulture),
            readiness.SalonsCount > 0));

        ReadinessItems.Add(new AdminReadinessItem(
            Loc.T("Расписание"),
            readiness.ScheduleDaysCount == 0
                ? Loc.T("Рабочих дней нет")
                : Loc.Plural(PluralKey.Days, readiness.ScheduleDaysCount),
            readiness.ScheduleDaysCount > 0));

        ReadinessItems.Add(new AdminReadinessItem(
            Loc.T("Фото в портфолио"),
            readiness.PhotosCount == 0
                ? Loc.T("Портфолио пустое")
                : readiness.PhotosCount.ToString(CultureInfo.InvariantCulture),
            readiness.PhotosCount > 0));
    }

    /// <summary>
    /// Складывает фотографии портфолио в плитки экрана.
    /// </summary>
    /// <param name="detail">Карточка с фотографиями.</param>
    private void ApplyPhotos(AdminBarberDetailResponse detail)
    {
        Photos.Clear();

        for (var index = 0; index < detail.Photos.Count; index++)
        {
            Photos.Add(AdminMasterPhotoItem.From(
                detail.Photos[index],
                _apiOptions.BaseAddress,
                detail.DisplayName,
                index + 1));
        }
    }

    /// <summary>
    /// Применяет результат записи подтверждения.
    /// </summary>
    /// <param name="verified">Новое состояние профиля.</param>
    private void ApplyVerification(bool verified)
    {
        IsVerified = verified;
        ActionErrorText = null;

        NotifyDetailState();
    }

    /// <summary>
    /// Превращает сбой действия в текст на экране.
    /// </summary>
    /// <param name="exception">Исключение записи.</param>
    /// <param name="fallback">Строка действия, если причина не уточняется.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Текст ошибки либо <c>null</c>, если запись отменили уходом со страницы.</returns>
    private string? SetActionFailure(
        Exception exception,
        string fallback,
        CancellationToken cancellationToken)
    {
        var kind = ApiFailureClassifier.Classify(exception, cancellationToken);

        if (kind == ApiFailureKind.None)
        {
            return null;
        }

        var text = kind == ApiFailureKind.Transport
            ? AdminErrorText.Network
            : exception is BarberSearchApiException apiException
                ? DescribeActionFailure(apiException, fallback)
                : fallback;

        ActionErrorText = text;

        NotifyDetailState();

        return text;
    }

    /// <summary>
    /// Уточняет текст сбоя записи по коду ответа: скрытый профиль и исчезнувшая запись важнее.
    /// </summary>
    /// <param name="exception">Исключение записи.</param>
    /// <param name="fallback">Строка действия, если причина не уточняется.</param>
    /// <returns>Текст для человека.</returns>
    private static string DescribeActionFailure(
        BarberSearchApiException exception,
        string fallback)
    {
        if (exception.ErrorCodes.Contains(VerifyInactiveCode, StringComparer.Ordinal))
        {
            return Loc.T(
                "Нельзя подтвердить профиль, скрытый из поиска. Сначала верните его в поиск.");
        }

        if (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return Loc.T("Профиль больше не существует. Обновите список.");
        }

        return AdminErrorText.Write(exception, fallback);
    }

    /// <summary>
    /// Одна попытка цикла: перечитать карточку, не гася причину до ответа.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private Task<ApiFailureKind> RetryLoadAsync(CancellationToken cancellationToken)
    {
        if (LoadErrorText is null || !_failureKind.IsRetryable())
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadAsync(silent: true, cancellationToken);
    }

    /// <summary>
    /// Запускает цикл повтора, если страница видна и сбой повторяемый.
    /// </summary>
    private void TryStartAutoRetry()
    {
        if (!_isPageVisible || IsLoading || IsSignInRequired || IsForbidden || IsNotFound)
        {
            return;
        }

        if (LoadErrorText is null || !_failureKind.IsRetryable())
        {
            return;
        }

        _autoRetry.Start(RetryLoadAsync);
    }

    /// <summary>
    /// Останавливает цикл повтора, не трогая признак «страница видна».
    /// </summary>
    private void StopAutoRetryLoop() => _autoRetry.Stop();

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    /// <param name="sender">Цикл.</param>
    /// <param name="e">Признак изменения.</param>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Включает состояние «профиль не найден»: ссылка устарела или запись удалена.
    /// </summary>
    private void ApplyNotFoundState()
    {
        StopAutoRetryLoop();

        _generation++;
        _detail = null;
        _loadedId = null;

        IsLoading = false;
        IsNotFound = true;
        LoadErrorText = null;
        IsForbidden = false;
        IsSignInRequired = false;

        ReadinessItems.Clear();
        Photos.Clear();

        NotifyDetailState();
    }

    /// <summary>
    /// Включает отказ в доступе: роль не появится от повтора, содержимое скрывается.
    /// </summary>
    private void ApplyForbiddenState()
    {
        StopAutoRetryLoop();

        _generation++;
        _detail = null;
        _loadedId = null;

        IsLoading = false;
        IsNotFound = false;
        LoadErrorText = null;
        IsForbidden = true;
        IsSignInRequired = false;

        ReadinessItems.Clear();
        Photos.Clear();

        NotifyDetailState();
    }

    /// <summary>
    /// Гасит сессию: пара мертва, экран вернёт к форме входа.
    /// </summary>
    /// <returns>Задача очистки сессии.</returns>
    private async Task EndSessionAsync()
    {
        ApplySignedOutState();

        await _session.ClearAsync(CancellationToken.None);
    }

    /// <summary>
    /// Переключает доступность подтверждения при открытии и закрытии панели.
    /// </summary>
    /// <param name="value">Панель открыта.</param>
    partial void OnIsUnverifyPanelOpenChanged(bool value) => NotifyDetailState();

    /// <summary>
    /// Переключает кнопки при начале и конце записи.
    /// </summary>
    /// <param name="value">Запись идёт.</param>
    partial void OnIsBusyChanged(bool value) => NotifyDetailState();

    /// <summary>
    /// Поднимает уведомление о наличии ошибки действия.
    /// </summary>
    /// <param name="value">Новый текст ошибки.</param>
    partial void OnActionErrorTextChanged(string? value) =>
        OnPropertyChanged(nameof(HasActionError));

    /// <summary>
    /// Поднимает уведомления обо всех вычисляемых свойствах карточки.
    /// </summary>
    private void NotifyDetailState()
    {
        OnPropertyChanged(nameof(HasDetail));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowForbidden));
        OnPropertyChanged(nameof(ShowSignInRequired));
        OnPropertyChanged(nameof(ShowActions));
        OnPropertyChanged(nameof(HasActionError));
        OnPropertyChanged(nameof(HasAvatar));
        OnPropertyChanged(nameof(ShowInitials));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(HasAvatarState));
        OnPropertyChanged(nameof(HasAvatarRejection));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(VerificationStateText));
        OnPropertyChanged(nameof(VerifyBlockedReason));
        OnPropertyChanged(nameof(HasVerifyBlockedReason));
        OnPropertyChanged(nameof(CanVerify));
        OnPropertyChanged(nameof(CanUnverify));
        OnPropertyChanged(nameof(CanConfirmUnverify));
        OnPropertyChanged(nameof(IsReadinessComplete));
        OnPropertyChanged(nameof(ReadinessDoneText));
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(HasNoPhotos));
    }

    /// <summary>
    /// Подпись состояния аватара по приоритету веба: блокировка важнее отказа.
    /// </summary>
    /// <param name="isBlocked">Кадр скрыт администратором.</param>
    /// <param name="status">Состояние проверки кадра.</param>
    /// <returns>Текст состояния либо <c>null</c>, если кадр одобрен и виден.</returns>
    private static string? AvatarState(bool isBlocked, PhotoModerationStatus status)
    {
        if (isBlocked)
        {
            return Loc.T("Скрыто администратором");
        }

        return status switch
        {
            PhotoModerationStatus.Rejected => Loc.T("Отклонено"),
            PhotoModerationStatus.Pending => Loc.T("На проверке"),
            _ => null,
        };
    }
}

/// <summary>
/// Строка чек-листа готовности: пункт, его значение и отметка выполнения.
/// </summary>
/// <remarks>
/// Значение приходит уже подписанным: экран не форматирует счётчики сам, а показывает
/// те же слова, что и веб. Отметка — текстовая, потому что иконок в приложении нет.
/// </remarks>
public sealed class AdminReadinessItem
{
    /// <summary>
    /// Создаёт строку чек-листа.
    /// </summary>
    /// <param name="label">Название пункта.</param>
    /// <param name="value">Значение пункта словами.</param>
    /// <param name="isDone">Пункт выполнен.</param>
    public AdminReadinessItem(string label, string value, bool isDone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(value);

        Label = label;
        Value = value;
        IsDone = isDone;
    }

    /// <summary>
    /// Название пункта.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Значение пункта словами.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Пункт выполнен.
    /// </summary>
    public bool IsDone { get; }

    /// <summary>
    /// Текстовая отметка: «✓» для выполненного и «○» для недостающего.
    /// </summary>
    public string Marker => IsDone ? "✓" : "○";
}

/// <summary>
/// Плитка портфолио в админской карточке: кадр, подпись и состояние проверки.
/// </summary>
/// <remarks>
/// Решений по кадрам здесь нет — их принимает срез модерации фотографий. Карточка мастера
/// показывает все кадры, включая скрытые и отклонённые, чтобы администратор видел, что
/// именно скрыто, и назвала причину отказа.
/// </remarks>
public sealed class AdminMasterPhotoItem
{
    private AdminMasterPhotoItem(
        Guid id,
        string caption,
        string altText,
        string imageUrl,
        string statusText,
        string? rejectionText)
    {
        Id = id;
        Caption = caption;
        AltText = altText;
        ImageUrl = imageUrl;
        StatusText = statusText;
        RejectionText = rejectionText;
    }

    /// <summary>
    /// Идентификатор фотографии.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Подпись кадра либо «Без подписи».
    /// </summary>
    public string Caption { get; }

    /// <summary>
    /// Описание для экранного диктора.
    /// </summary>
    public string AltText { get; }

    /// <summary>
    /// Абсолютный адрес миниатюры.
    /// </summary>
    public string ImageUrl { get; }

    /// <summary>
    /// Состояние кадра словами: опубликован, скрыт владельцем, отклонён или скрыт админом.
    /// </summary>
    public string StatusText { get; }

    /// <summary>
    /// Причина отказа; <c>null</c>, пока кадр не отклонён.
    /// </summary>
    public string? RejectionText { get; }

    /// <summary>
    /// Причина отказа известна.
    /// </summary>
    public bool HasRejection => RejectionText is not null;

    /// <summary>
    /// Собирает плитку из фотографии карточки.
    /// </summary>
    /// <param name="photo">Фотография портфолио из ответа API.</param>
    /// <param name="baseAddress">Базовый адрес API для склейки относительных URL.</param>
    /// <param name="masterName">Имя мастера для шаблона описания.</param>
    /// <param name="number">Номер кадра с единицы — порядок сервера не пересчитывается.</param>
    /// <returns>Плитка для админской карточки.</returns>
    public static AdminMasterPhotoItem From(
        BarberProfilePhotoResponse photo,
        Uri baseAddress,
        string masterName,
        int number)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(baseAddress);

        var tileUrl = string.IsNullOrWhiteSpace(photo.ThumbnailUrl)
            ? MediaUrl.Resize(photo.Url, "md")
            : photo.ThumbnailUrl;

        return new AdminMasterPhotoItem(
            photo.Id,
            string.IsNullOrWhiteSpace(photo.Description)
                ? Loc.T("Без подписи")
                : photo.Description.Trim(),
            BarberText.PhotoAlt(masterName, number, photo.Description),
            MediaUrl.Resolve(baseAddress, tileUrl),
            Status(photo),
            photo.ModerationStatus == PhotoModerationStatus.Rejected
                && !string.IsNullOrWhiteSpace(photo.RejectionReason)
                    ? Loc.T("Причина: {0}", photo.RejectionReason.Trim())
                    : null);
    }

    /// <summary>
    /// Состояние кадра по приоритету веба: блокировка важнее отказа, отказ — публикации.
    /// </summary>
    /// <param name="photo">Фотография из ответа API.</param>
    /// <returns>Подпись состояния.</returns>
    private static string Status(BarberProfilePhotoResponse photo)
    {
        if (photo.IsBlocked)
        {
            return Loc.T("Скрыто администратором");
        }

        return photo.ModerationStatus switch
        {
            PhotoModerationStatus.Rejected => Loc.T("Отклонено"),
            PhotoModerationStatus.Pending => Loc.T("На проверке"),
            _ when !photo.IsPublished => Loc.T("Скрыто владельцем"),
            _ => Loc.T("Опубликовано"),
        };
    }
}
