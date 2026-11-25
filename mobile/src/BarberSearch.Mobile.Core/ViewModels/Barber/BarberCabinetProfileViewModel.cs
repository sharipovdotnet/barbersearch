using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Раздел «Профиль» кабинета мастера: карточка, прайс и специализации.
/// </summary>
/// <remarks>
/// <para>
/// Раздел показывает тот же агрегат, что и веб (<c>barber-profile-page.tsx</c>), но без
/// форм: правка карточки и услуги — отдельные экраны поверх кабинета, поэтому раздел
/// поднимает события, а не переходит сам. После возврата с дочернего экрана страница
/// зовёт <see cref="ActivateAsync"/> или <see cref="ApplyProfileUpdated"/>, и раздел
/// перечитывает карточку — иначе сохранённая цена осталась бы старой.
/// </para>
/// <para>
/// Скрытие услуги — не удаление: строка остаётся ради истории записей и возвращается
/// повторным добавлением с текущей ценой и длительностью. Поэтому у строки два действия,
/// и они не конфликтуют: <c>DELETE</c> гасит видимость, <c>POST</c> возвращает её.
/// </para>
/// <para>
/// Чтение карточки лечится автоповтором, записи — нет: <c>DELETE</c> и <c>POST</c> не
/// повторяются вслепую, решение остаётся за человеком. Справочник специализаций
/// запрашивается лениво — только когда пикер впервые открыли — и тоже лечится
/// автоповтором, но своим: он живёт, пока пикер открыт, и останавливается вместе с ним.
/// </para>
/// <para>
/// Аватар — отдельный срез: карточка мастера отдаёт только одобренный и не скрытый кадр,
/// поэтому состояние проверки и сырой URL читаются из профиля аккаунта
/// (<c>GET api/users/me</c>). Сбой этого чтения не превращает прочитанную карточку в
/// ошибку: раздел остаётся с тем, что дала карточка. Загрузка и удаление — записи, они
/// не повторяются автоматически.
/// </para>
/// </remarks>
public sealed partial class BarberCabinetProfileViewModel : ObservableObject, IBarberCabinetSection
{
    private static string LoadFailedText => Loc.T("Не удалось загрузить профиль.");
    private static string OtherServicesGroupTitle => Loc.T("Прочие услуги");
    private static string HideFailedText => Loc.T("Не удалось скрыть услугу. Попробуйте ещё раз.");
    private static string RestoreFailedText => Loc.T("Не удалось вернуть услугу. Попробуйте ещё раз.");
    private static string RemoveSpecializationFailedText => Loc.T("Не удалось убрать специализацию. Попробуйте ещё раз.");
    private static string AddSpecializationFailedText => Loc.T("Не удалось добавить специализацию. Попробуйте ещё раз.");
    private static string CatalogFailedText => Loc.T("Не удалось загрузить справочник специализаций.");

    private readonly IBarberProfileApi _profileApi;
    private readonly IBarberSearchApi _searchApi;
    private readonly IAccountAvatarApi _avatarApi;
    private readonly IPortfolioPhotoPicker _photoPicker;
    private readonly TimeProvider _timeProvider;
    private readonly AutoRetryLoop _autoRetry;
    private readonly AutoRetryLoop _specializationsAutoRetry;
    private readonly List<SpecializationResponse> _allSpecializations = [];

    private ApiFailureKind _lastFailureKind;
    private bool _isPageVisible;
    private bool _isCatalogLoaded;

    // Аватар из /me: null, пока профиль аккаунта не прочитан. Карточка даёт только
    // одобренный кадр, поэтому её URL — запасной вариант на случай сбоя этого чтения.
    private AccountAvatar? _accountAvatar;
    private string? _cardAvatarUrl;
    private bool _isAvatarUploading;
    private bool _isAvatarDeleting;
    private string? _avatarError;
    private string? _avatarAnnouncement;

    /// <summary>
    /// Создаёт раздел профиля.
    /// </summary>
    /// <param name="profileApi">Собственная карточка и мутации прайса.</param>
    /// <param name="searchApi">Публичный справочник специализаций.</param>
    /// <param name="avatarApi">Аватар аккаунта: состояние проверки, загрузка и удаление.</param>
    /// <param name="photoPicker">Системный выбор файла с подготовкой кадра в приложении.</param>
    /// <param name="timeProvider">Часы: время последнего чтения показывает шапка.</param>
    /// <param name="autoRetry">
    /// Цикл автоповтора карточки; <c>null</c> — собственный цикл с обычным расписанием.
    /// </param>
    /// <param name="specializationsAutoRetry">
    /// Цикл автоповтора справочника специализаций; <c>null</c> — собственный цикл.
    /// Параметры необязательные и стоят последними, чтобы DI и тесты не зависели от
    /// их порядка.
    /// </param>
    public BarberCabinetProfileViewModel(
        IBarberProfileApi profileApi,
        IBarberSearchApi searchApi,
        IAccountAvatarApi avatarApi,
        IPortfolioPhotoPicker photoPicker,
        TimeProvider timeProvider,
        AutoRetryLoop? autoRetry = null,
        AutoRetryLoop? specializationsAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(profileApi);
        ArgumentNullException.ThrowIfNull(searchApi);
        ArgumentNullException.ThrowIfNull(avatarApi);
        ArgumentNullException.ThrowIfNull(photoPicker);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _profileApi = profileApi;
        _searchApi = searchApi;
        _avatarApi = avatarApi;
        _photoPicker = photoPicker;
        _timeProvider = timeProvider;

        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;

        _specializationsAutoRetry = specializationsAutoRetry ?? new AutoRetryLoop();
        _specializationsAutoRetry.StateChanged += OnSpecializationsAutoRetryStateChanged;

        // Лимит портфолио меняется вместе с коллекцией: добавление, очистка и перечитывание
        // карточки одинаково должны переключить «Добавить фото» и подсказку о лимите.
        Photos.CollectionChanged += (_, _) => NotifyPhotoLimitState();
    }

    /// <summary>
    /// Идентификатор профиля, которому принадлежит раздел.
    /// </summary>
    /// <remarks>
    /// <see cref="Guid.Empty"/>, пока раздел не инициализирован: страница проверяет это
    /// значение вместо прежнего <c>null</c>, а дочерние экраны получают готовый
    /// идентификатор без распаковки nullable.
    /// </remarks>
    public Guid BarberProfileId { get; private set; }

    /// <summary>
    /// Идентификатор для общего контракта раздела: <c>null</c> до инициализации.
    /// </summary>
    /// <remarks>
    /// Публичное свойство отдаёт <see cref="Guid"/> — фото-странице нужен готовый ключ без
    /// распаковки. Контракт <see cref="IBarberCabinetSection"/> исторически nullable, и
    /// страница кабинета сравнивает его с профилем сессии; явная реализация сохраняет это
    /// поведение, не возвращая nullable в публичную поверхность раздела.
    /// </remarks>
    Guid? IBarberCabinetSection.BarberProfileId =>
        BarberProfileId == Guid.Empty ? null : BarberProfileId;

    /// <summary>
    /// Сырой ответ карточки; <c>null</c> до первого успешного чтения.
    /// </summary>
    [ObservableProperty]
    private BarberProfileResponse? _profile;

    /// <summary>
    /// Идёт первая загрузка: вместо карточки индикатор.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstLoading;

    /// <summary>
    /// Идёт тихое обновление уже показанной карточки.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Карточка загружена: раздел можно показывать.
    /// </summary>
    [ObservableProperty]
    private bool _isReady;

    /// <summary>
    /// Сервер ответил <c>403</c>: профиль чужой или роль не та.
    /// </summary>
    [ObservableProperty]
    private bool _isForbidden;

    /// <summary>
    /// Сервер ответил <c>404</c>: профиль удалён или ещё не создан.
    /// </summary>
    [ObservableProperty]
    private bool _isNotFound;

    /// <summary>
    /// Сессии больше нет: страница покажет форму входа.
    /// </summary>
    [ObservableProperty]
    private bool _isSignInRequired;

    /// <summary>
    /// Причина сбоя чтения; <c>null</c>, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Цикл сам повторяет чтение карточки.
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Время последнего чтения: «Обновлено в 14:32».
    /// </summary>
    [ObservableProperty]
    private string? _updatedAtText;

    /// <summary>
    /// Причина сбоя мутации; карточка при этом остаётся на экране.
    /// </summary>
    [ObservableProperty]
    private string? _actionError;

    /// <summary>
    /// Идёт запись: кнопки строк блокируются, чтобы не отправить две мутации.
    /// </summary>
    [ObservableProperty]
    private bool _isMutating;

    /// <summary>
    /// Пикер специализаций открыт.
    /// </summary>
    [ObservableProperty]
    private bool _isSpecializationPickerOpen;

    /// <summary>
    /// Справочник специализаций читается.
    /// </summary>
    [ObservableProperty]
    private bool _isSpecializationLoading;

    /// <summary>
    /// Справочник не ответил, и цикл уже пробует снова: строка «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isSpecializationAutoRetrying;

    /// <summary>
    /// Причина сбоя справочника; живёт внутри пикера.
    /// </summary>
    [ObservableProperty]
    private string? _specializationError;

    /// <summary>
    /// Строки прайса в порядке ответа сервера.
    /// </summary>
    public ObservableCollection<BarberServiceItem> Services { get; } = [];

    /// <summary>
    /// Тот же прайс, разложенный по специализациям: заголовок группы и её строки.
    /// </summary>
    /// <remarks>
    /// Источник для блока «Услуги»: специализация читается заголовком, а не бейджем на каждой
    /// строке, поэтому длинное название больше не обрезается. Порядок групп и строк повторяет
    /// <see cref="Services"/> (порядок ответа API), скрытые услуги остаются в своих группах,
    /// а строки без специализации собраны в последнюю группу «Прочие услуги».
    /// </remarks>
    public ObservableCollection<BarberCabinetServiceGroup> ServiceGroups { get; } = [];

    /// <summary>
    /// Специализации мастера.
    /// </summary>
    public ObservableCollection<BarberSpecializationItem> Specializations { get; } = [];

    /// <summary>
    /// Фотографии портфолио, включая скрытые и отклонённые.
    /// </summary>
    public ObservableCollection<BarberProfilePhotoItem> Photos { get; } = [];

    /// <summary>
    /// Дополнительные услуги, которые мастер заполнил сам.
    /// </summary>
    public ObservableCollection<string> AdditionalServices { get; } = [];

    /// <summary>
    /// Сертификаты и обучение.
    /// </summary>
    public ObservableCollection<string> Certificates { get; } = [];

    /// <summary>
    /// Достижения.
    /// </summary>
    public ObservableCollection<string> Achievements { get; } = [];

    /// <summary>
    /// Предпочтительный формат общения.
    /// </summary>
    public ObservableCollection<string> CommunicationFormat { get; } = [];

    /// <summary>
    /// Комфорт в кресле.
    /// </summary>
    public ObservableCollection<string> Goodies { get; } = [];

    /// <summary>
    /// Специализации справочника, которых у мастера ещё нет.
    /// </summary>
    public ObservableCollection<BarberSpecializationOption> AvailableSpecializations { get; } = [];

    /// <summary>
    /// Имя мастера из карточки.
    /// </summary>
    public string DisplayName => Profile?.DisplayName ?? string.Empty;

    /// <summary>
    /// Инициалы для заглушки аватара.
    /// </summary>
    public string Initials => BarberText.Initials(DisplayName);

    /// <summary>
    /// Грейд: «Старший мастер», «Без грейда».
    /// </summary>
    public string GradeText => Profile is null ? string.Empty : BarberProfileText.GradeText(Profile.Grade);

    /// <summary>
    /// Описание о себе.
    /// </summary>
    public string Bio => Profile?.Bio ?? string.Empty;

    /// <summary>
    /// Стаж: «8 лет».
    /// </summary>
    public string ExperienceText => Profile is null
        ? string.Empty
        : BarberProfileText.ExperienceText(Profile.YearsOfExperience);

    /// <summary>
    /// Рейтинг: «4,8» или «—».
    /// </summary>
    public string RatingText => BarberProfileText.RatingText(Profile?.Rating);

    /// <summary>
    /// Счётчик отзывов: «12 отзывов».
    /// </summary>
    public string ReviewsText => Profile is null
        ? string.Empty
        : BarberProfileText.ReviewsText(Profile.ReviewsCount);

    /// <summary>
    /// Профиль подтверждён администратором.
    /// </summary>
    public bool IsVerified => Profile?.IsVerified ?? false;

    /// <summary>
    /// Профиль активен.
    /// </summary>
    public bool IsActive => Profile?.IsActive ?? false;

    /// <summary>
    /// Подсказка неподтверждённому мастеру: карточка уже заполняется, но в поиске её нет.
    /// </summary>
    public string VerificationHint => IsVerified
        ? string.Empty
        : Loc.T(
            "Профиль на проверке. Заполняйте карточку — она появится в поиске после подтверждения администратором.");

    /// <summary>
    /// Можно ли открыть публичную карточку: только подтверждённый и активный профиль.
    /// </summary>
    public bool CanPreviewPublic => IsVerified && IsActive;

    /// <summary>
    /// Языки общения строкой: «Русский, English».
    /// </summary>
    public string LanguagesText => Profile is null
        ? string.Empty
        : string.Join(", ", Profile.Languages.Select(BarberProfileText.LanguageText));

    /// <summary>
    /// Способы оплаты строкой: «Наличные, СБП».
    /// </summary>
    public string PaymentMethodsText => Profile is null
        ? string.Empty
        : string.Join(", ", Profile.PaymentMethods.Select(BarberProfileText.PaymentText));

    /// <summary>
    /// Правило опозданий; пустая строка, если не заполнено.
    /// </summary>
    public string LatePolicyText => Profile?.LatePolicy ?? string.Empty;

    /// <summary>
    /// В прайсе есть строки.
    /// </summary>
    public bool HasServices => Services.Count > 0;

    /// <summary>
    /// У мастера есть специализации.
    /// </summary>
    public bool HasSpecializations => Specializations.Count > 0;

    /// <summary>
    /// В портфолио есть фотографии.
    /// </summary>
    public bool HasPhotos => Photos.Count > 0;

    /// <summary>
    /// В портфолио можно добавить ещё одну фотографию.
    /// </summary>
    public bool CanAddPhoto => Photos.Count < PortfolioPhotoLimits.MaxPhotos;

    /// <summary>
    /// Достигнут предел портфолио: сначала нужно удалить кадр.
    /// </summary>
    public bool IsPhotoLimitReached => Photos.Count >= PortfolioPhotoLimits.MaxPhotos;

    /// <summary>
    /// В справочнике есть что добавить.
    /// </summary>
    public bool HasAvailableSpecializations => AvailableSpecializations.Count > 0;

    /// <summary>
    /// Сырой URL аватара: состояние аккаунта важнее карточки, пока оно прочитано.
    /// </summary>
    /// <remarks>
    /// Адрес остаётся относительным: в абсолютный его разворачивает <c>MediaUrl.Resolve</c>
    /// на стороне представления, как и у фотографий портфолио.
    /// </remarks>
    public string? AvatarUrl => _accountAvatar is not null
        ? _accountAvatar.Url
        : _cardAvatarUrl;

    /// <summary>
    /// Аватар есть и не скрыт администратором: вместо инициалов показывается кадр.
    /// </summary>
    public bool HasAvatar => !IsAvatarBlocked && AvatarUrl is not null;

    /// <summary>
    /// Вместо кадра показываются инициалы.
    /// </summary>
    public bool ShowInitials => !HasAvatar;

    /// <summary>
    /// Фото скрыто администратором: публично его не видно, но владелец видит состояние.
    /// </summary>
    public bool IsAvatarBlocked => _accountAvatar?.IsBlocked == true;

    /// <summary>
    /// Есть что сказать о проверке: кадр не скрыт и ждёт решения либо отклонён.
    /// </summary>
    public bool HasAvatarStatus =>
        VisibleAvatarStatus is PhotoModerationStatus.Pending or PhotoModerationStatus.Rejected;

    /// <summary>
    /// Состояние проверки: «На проверке», «Отклонено» или <c>null</c>.
    /// </summary>
    public string? AvatarStatusText => VisibleAvatarStatus switch
    {
        PhotoModerationStatus.Pending => Loc.T("На проверке"),
        PhotoModerationStatus.Rejected => Loc.T("Отклонено"),
        _ => null,
    };

    /// <summary>
    /// Машинный код состояния проверки для триггеров XAML: <c>pending</c>, <c>rejected</c>
    /// или пустая строка.
    /// </summary>
    public string AvatarStatusKind => VisibleAvatarStatus switch
    {
        PhotoModerationStatus.Pending => "pending",
        PhotoModerationStatus.Rejected => "rejected",
        _ => string.Empty,
    };

    /// <summary>
    /// Администратор отклонил кадр и назвал причину.
    /// </summary>
    public bool HasAvatarRejection =>
        VisibleAvatarStatus == PhotoModerationStatus.Rejected
        && !string.IsNullOrWhiteSpace(_accountAvatar?.RejectionReason);

    /// <summary>
    /// Причина отказа администратора: «Причина: …»; <c>null</c>, если её нет.
    /// </summary>
    public string? AvatarRejectionText => HasAvatarRejection
        ? Loc.T("Причина: {0}", _accountAvatar?.RejectionReason ?? string.Empty)
        : null;

    /// <summary>
    /// Пояснение к скрытому администратором фото.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Свойство связано XAML: статическое свойство сломало бы привязку.")]
    public string AvatarBlockedText => Loc.T("Фото скрыто администратором");

    /// <summary>
    /// Что делать со скрытым фото: новое появится публично после проверки.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Свойство связано XAML: статическое свойство сломало бы привязку.")]
    public string AvatarBlockedHint => Loc.T(
        "Его не видно публично. Новое фото появится публично после проверки, а вернуть старое может только администратор.");

    /// <summary>
    /// Аватар можно удалить: показан хотя бы один сырой адрес.
    /// </summary>
    public bool CanDeleteAvatar => AvatarUrl is not null;

    /// <summary>
    /// Подпись кнопки аватара: «Загрузить фото», «Изменить фото» или «Загрузить другое фото».
    /// </summary>
    public string AvatarActionText =>
        IsAvatarBlocked || VisibleAvatarStatus == PhotoModerationStatus.Rejected
            ? Loc.T("Загрузить другое фото")
            : AvatarUrl is not null
                ? Loc.T("Изменить фото")
                : Loc.T("Загрузить фото");

    /// <summary>
    /// Аватар можно менять: сейчас нет запроса.
    /// </summary>
    public bool CanChangeAvatar => !IsAvatarBusy;

    /// <summary>
    /// Идёт загрузка аватара.
    /// </summary>
    public bool IsAvatarUploading
    {
        get => _isAvatarUploading;
        private set
        {
            if (SetProperty(ref _isAvatarUploading, value))
            {
                NotifyAvatarBusyState();
            }
        }
    }

    /// <summary>
    /// Идёт удаление аватара.
    /// </summary>
    public bool IsAvatarDeleting
    {
        get => _isAvatarDeleting;
        private set
        {
            if (SetProperty(ref _isAvatarDeleting, value))
            {
                NotifyAvatarBusyState();
            }
        }
    }

    /// <summary>
    /// Идёт любое действие с аватаром: кнопки среза заблокированы.
    /// </summary>
    public bool IsAvatarBusy => IsAvatarUploading || IsAvatarDeleting;

    /// <summary>
    /// Причина сбоя действия с аватаром; <c>null</c>, когда ошибки нет.
    /// </summary>
    public string? AvatarError
    {
        get => _avatarError;
        private set => SetProperty(ref _avatarError, value);
    }

    /// <summary>
    /// Сообщение после успешного действия с аватаром.
    /// </summary>
    public string? AvatarAnnouncement
    {
        get => _avatarAnnouncement;
        private set => SetProperty(ref _avatarAnnouncement, value);
    }

    /// <summary>
    /// Просит страницу открыть правку карточки.
    /// </summary>
    public event EventHandler? OpenEditRequested;

    /// <summary>
    /// Просит страницу открыть добавление услуги.
    /// </summary>
    public event EventHandler? AddServiceRequested;

    /// <summary>
    /// Просит страницу открыть правку выбранной услуги.
    /// </summary>
    public event EventHandler<BarberServiceItem>? EditServiceRequested;

    /// <summary>
    /// Просит страницу открыть публичную карточку.
    /// </summary>
    public event EventHandler? PreviewRequested;

    /// <summary>
    /// Просит страницу поделиться карточкой.
    /// </summary>
    public event EventHandler? ShareProfileRequested;

    /// <inheritdoc />
    public void Initialize(Guid barberProfileId)
    {
        if (barberProfileId == Guid.Empty)
        {
            throw new ArgumentException(
                "Идентификатор профиля не может быть пустым.",
                nameof(barberProfileId));
        }

        _autoRetry.Stop();
        _specializationsAutoRetry.Stop();

        BarberProfileId = barberProfileId;
        OnPropertyChanged(nameof(BarberProfileId));
        _lastFailureKind = ApiFailureKind.None;
        IsReady = false;
        IsFirstLoading = false;
        IsRefreshing = false;
        ErrorMessage = null;
        ActionError = null;
        IsForbidden = false;
        IsNotFound = false;
        IsSignInRequired = false;
        IsSpecializationPickerOpen = false;
        SpecializationError = null;
        _accountAvatar = null;
        _cardAvatarUrl = null;
        IsAvatarUploading = false;
        IsAvatarDeleting = false;
        AvatarError = null;
        AvatarAnnouncement = null;
        NotifyAvatarState();
        Profile = null;
        ClearCollections();
    }

    /// <summary>
    /// Показывает раздел: первое чтение или тихое обновление уже показанной карточки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        _isPageVisible = true;

        var profileId = BarberProfileId;

        if (profileId == Guid.Empty)
        {
            return;
        }

        await LoadAsync(silent: Profile is not null, cancellationToken);

        if (IsSpecializationPickerOpen && !_isCatalogLoaded)
        {
            StartSpecializationsAutoRetry(await LoadSpecializationsAsync(cancellationToken));
        }
    }

    /// <summary>
    /// Скрывает раздел: останавливает оба автоповтора, не сбрасывая карточку.
    /// </summary>
    /// <remarks>
    /// Пикер остаётся открытым: вернувшийся раздел прочитает справочник заново и снова
    /// вооружит его цикл, если сбой повторяемый.
    /// </remarks>
    public void Deactivate()
    {
        _isPageVisible = false;
        _autoRetry.Stop();
        _specializationsAutoRetry.Stop();
    }

    /// <summary>
    /// Цикл повтора чтения карточки: тестам нужен счётчик попыток и завершение.
    /// </summary>
    internal AutoRetryLoop ReadAutoRetry => _autoRetry;

    /// <summary>
    /// Завершается, когда цикл повтора чтения останавливается.
    /// </summary>
    internal Task ReadRetryCompletion => _autoRetry.Completion;

    /// <summary>
    /// Завершается, когда цикл повтора справочника останавливается: тесты ждут его вместо пауз.
    /// </summary>
    internal Task SpecializationsRetryCompletion => _specializationsAutoRetry.Completion;

    /// <summary>
    /// Применяет карточку, полученную дочерним экраном после сохранения.
    /// </summary>
    /// <remarks>
    /// Нужен, когда правка вернула уже прочитанный профиль: второй <c>GET</c> не обязателен,
    /// а экран должен показать новое имя или цену сразу.
    /// </remarks>
    /// <param name="profile">Свежая карточка мастера.</param>
    public void ApplyProfileUpdated(BarberProfileResponse profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ApplyProfile(profile);
    }

    /// <summary>
    /// Перечитывает карточку по кнопке — сразу, без паузы.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        _autoRetry.Stop();

        await LoadAsync(silent: Profile is not null, cancellationToken);
    }

    /// <summary>
    /// Поднимает событие правки карточки.
    /// </summary>
    [RelayCommand]
    private void RequestEdit() => OpenEditRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Поднимает событие добавления услуги.
    /// </summary>
    [RelayCommand]
    private void RequestAddService() => AddServiceRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Поднимает событие правки услуги.
    /// </summary>
    /// <param name="item">Строка прайса.</param>
    [RelayCommand]
    private void RequestEditService(BarberServiceItem? item)
    {
        if (item is not null)
        {
            EditServiceRequested?.Invoke(this, item);
        }
    }

    /// <summary>
    /// Поднимает событие открытия публичной карточки.
    /// </summary>
    [RelayCommand]
    private void Preview() => PreviewRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Поднимает событие «поделиться карточкой».
    /// </summary>
    [RelayCommand]
    private void Share() => ShareProfileRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Скрывает услугу: <c>DELETE</c> деактивирует строку, не удаляя её.
    /// </summary>
    /// <remarks>
    /// Подтверждение — в интерфейсе: Core не показывает диалогов, а страница знает,
    /// как спросить человека. После успеха карточка перечитывается, чтобы бейдж «Скрыта»
    /// и состояние кнопок совпали с сервером.
    /// </remarks>
    /// <param name="item">Строка прайса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task HideServiceAsync(
        BarberServiceItem? item,
        CancellationToken cancellationToken)
    {
        if (item is null || IsMutating || BarberProfileId == Guid.Empty)
        {
            return;
        }

        var profileId = BarberProfileId;
        IsMutating = true;
        ActionError = null;

        try
        {
            await _profileApi.RemoveServiceAsync(profileId, item.HairServiceId, cancellationToken);

            await LoadAsync(silent: true, cancellationToken);
        }
        catch (Exception exception)
        {
            ActionError = DescribeWriteFailure(exception, HideFailedText, WriteTarget.Service);
        }
        finally
        {
            IsMutating = false;
        }
    }

    /// <summary>
    /// Возвращает скрытую услугу в прайс с её текущей ценой и длительностью.
    /// </summary>
    /// <param name="item">Скрытая строка прайса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RestoreServiceAsync(
        BarberServiceItem? item,
        CancellationToken cancellationToken)
    {
        if (item is null || IsMutating || BarberProfileId == Guid.Empty)
        {
            return;
        }

        var profileId = BarberProfileId;
        IsMutating = true;
        ActionError = null;

        try
        {
            await _profileApi.AddServiceAsync(
                profileId,
                new Contracts.BarberProfiles.AddBarberProfileService.AddBarberProfileServiceRequest(
                    item.HairServiceId,
                    item.Price,
                    item.DurationMinutes),
                cancellationToken);

            await LoadAsync(silent: true, cancellationToken);
        }
        catch (Exception exception)
        {
            ActionError = DescribeWriteFailure(exception, RestoreFailedText, WriteTarget.Service);
        }
        finally
        {
            IsMutating = false;
        }
    }

    /// <summary>
    /// Убирает специализацию: удаление физическое и необратимое.
    /// </summary>
    /// <param name="item">Строка специализации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RemoveSpecializationAsync(
        BarberSpecializationItem? item,
        CancellationToken cancellationToken)
    {
        if (item is null || IsMutating || BarberProfileId == Guid.Empty)
        {
            return;
        }

        var profileId = BarberProfileId;
        IsMutating = true;
        ActionError = null;

        try
        {
            await _profileApi.RemoveSpecializationAsync(
                profileId,
                item.SpecializationId,
                cancellationToken);

            await LoadAsync(silent: true, cancellationToken);
        }
        catch (Exception exception)
        {
            ActionError = DescribeWriteFailure(
                exception,
                RemoveSpecializationFailedText,
                WriteTarget.Specialization);
        }
        finally
        {
            IsMutating = false;
        }
    }

    /// <summary>
    /// Открывает пикер и подгружает справочник при первом открытии.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task OpenSpecializationPickerAsync(CancellationToken cancellationToken)
    {
        IsSpecializationPickerOpen = true;
        ActionError = null;

        if (_isCatalogLoaded)
        {
            RebuildAvailableSpecializations();

            return;
        }

        StartSpecializationsAutoRetry(await LoadSpecializationsAsync(cancellationToken));
    }

    /// <summary>
    /// Закрывает пикер, не сбрасывая уже прочитанный справочник.
    /// </summary>
    [RelayCommand]
    private void CloseSpecializationPicker()
    {
        IsSpecializationPickerOpen = false;
        SpecializationError = null;
        _specializationsAutoRetry.Stop();
    }

    /// <summary>
    /// Одна попытка цикла: перечитывает справочник, не стирая причину до ответа.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему решает цикл.</returns>
    private Task<ApiFailureKind> RetrySpecializationsAsync(CancellationToken cancellationToken) =>
        IsSpecializationPickerOpen && _isPageVisible
            ? LoadSpecializationsAsync(cancellationToken, silent: true)
            : Task.FromResult(ApiFailureKind.Terminal);

    /// <summary>
    /// Запускает цикл справочника, пока пикер открыт и сбой повторяемый.
    /// </summary>
    /// <param name="kind">Вид сбоя первого чтения.</param>
    private void StartSpecializationsAutoRetry(ApiFailureKind kind)
    {
        if (!IsSpecializationPickerOpen
            || !_isPageVisible
            || !kind.IsRetryable()
            || _specializationsAutoRetry.IsRunning)
        {
            return;
        }

        _specializationsAutoRetry.Start(RetrySpecializationsAsync);
    }

    /// <summary>
    /// Добавляет специализацию из справочника.
    /// </summary>
    /// <param name="specializationId">Идентификатор специализации справочника.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task AddSpecializationAsync(
        Guid specializationId,
        CancellationToken cancellationToken)
    {
        if (specializationId == Guid.Empty
            || IsMutating
            || BarberProfileId == Guid.Empty)
        {
            return;
        }

        var profileId = BarberProfileId;
        IsMutating = true;
        ActionError = null;
        SpecializationError = null;

        try
        {
            await _profileApi.AddSpecializationAsync(
                profileId,
                specializationId,
                cancellationToken);

            IsSpecializationPickerOpen = false;

            await LoadAsync(silent: true, cancellationToken);
            RebuildAvailableSpecializations();
        }
        catch (Exception exception)
        {
            SpecializationError = DescribeWriteFailure(
                exception,
                AddSpecializationFailedText,
                WriteTarget.Specialization);
        }
        finally
        {
            IsMutating = false;
        }
    }

    /// <summary>
    /// Выбирает новое фото аватара и загружает его.
    /// </summary>
    /// <remarks>
    /// Выбор и загрузка — одно действие: отмена пикера ничего не меняет и не показывает
    /// ошибку. Любая загрузка ждёт проверки, поэтому успех сразу показывает «На проверке»,
    /// а публично кадр появится только после решения администратора.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task ChangeAvatarAsync(CancellationToken cancellationToken)
    {
        if (IsAvatarBusy)
        {
            return;
        }

        AvatarError = null;
        AvatarAnnouncement = null;
        IsAvatarUploading = true;

        try
        {
            var image = await _photoPicker.PickAsync(cancellationToken);

            if (image is null)
            {
                return;
            }

            var uploaded = await _avatarApi.UploadAvatarAsync(image, cancellationToken);

            _accountAvatar = new(
                uploaded.AvatarUrl,
                IsBlocked: false,
                uploaded.ModerationStatus,
                RejectionReason: null);

            AvatarAnnouncement = Loc.T("Аватар загружен и ждёт проверки.");
            NotifyAvatarState();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Человек ушёл со страницы: пикер закрыт, причина не показывается.
        }
        catch (PortfolioImageException exception)
        {
            AvatarError = PortfolioPhotoErrors.DescribePicker(exception.Kind).Message;
        }
        catch (Exception exception)
        {
            AvatarError = AvatarErrors.Describe(exception);
        }
        finally
        {
            IsAvatarUploading = false;
        }
    }

    /// <summary>
    /// Удаляет аватар: фото исчезает и публично, и в кабинете.
    /// </summary>
    /// <remarks>
    /// Подтверждение — в интерфейсе: Core не показывает диалогов, а страница знает, как
    /// спросить человека. Удаление необратимо: вернуть старый кадр может только администратор.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task DeleteAvatarAsync(CancellationToken cancellationToken)
    {
        if (IsAvatarBusy || AvatarUrl is null)
        {
            return;
        }

        AvatarError = null;
        AvatarAnnouncement = null;
        IsAvatarDeleting = true;

        try
        {
            await _avatarApi.DeleteAvatarAsync(cancellationToken);

            _accountAvatar = new(null, false, PhotoModerationStatus.Approved, null);
            AvatarAnnouncement = Loc.T("Аватар удалён.");
            NotifyAvatarState();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Отмена вызывающим: кадр на сервере остался, ошибку не показываем.
        }
        catch (Exception exception)
        {
            AvatarError = AvatarErrors.Describe(exception);
        }
        finally
        {
            IsAvatarDeleting = false;
        }
    }

    /// <summary>
    /// Пересчитывает поля, зависящие от карточки.
    /// </summary>
    partial void OnProfileChanged(BarberProfileResponse? value) => NotifyProfileState();

    /// <summary>
    /// Читает карточку и раскладывает ответ по состояниям раздела.
    /// </summary>
    /// <param name="silent">Тихое обновление: уже показанная карточка не прячется.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему решает цикл.</returns>
    private async Task<ApiFailureKind> LoadAsync(
        bool silent,
        CancellationToken cancellationToken)
    {
        var profileId = BarberProfileId;

        if (profileId == Guid.Empty)
        {
            return ApiFailureKind.None;
        }

        if (silent)
        {
            IsRefreshing = true;
        }
        else
        {
            IsFirstLoading = true;
        }

        ErrorMessage = null;
        IsForbidden = false;
        IsNotFound = false;
        IsSignInRequired = false;

        try
        {
            var profile = await _profileApi.GetOwnAsync(profileId, cancellationToken);

            ApplyProfile(profile);
            await LoadAvatarAsync(cancellationToken);
            _lastFailureKind = ApiFailureKind.None;
            ErrorMessage = null;

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

            if (exception is BarberSearchApiException apiException)
            {
                switch (apiException.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        IsSignInRequired = true;
                        break;
                    case HttpStatusCode.Forbidden:
                        IsForbidden = true;
                        break;
                    case HttpStatusCode.NotFound:
                        IsNotFound = true;
                        break;
                }
            }

            if (!IsForbidden && !IsNotFound && !IsSignInRequired)
            {
                ErrorMessage = BarberProfileText.ReadError(exception, kind, LoadFailedText);
            }

            if (_isPageVisible && kind.IsRetryable())
            {
                _autoRetry.Start(RetryReadAsync);
            }

            return kind;
        }
        finally
        {
            IsFirstLoading = false;
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Записывает карточку в состояние раздела.
    /// </summary>
    /// <param name="profile">Карточка мастера.</param>
    private void ApplyProfile(BarberProfileResponse profile)
    {
        Profile = profile;
        IsReady = true;
        IsFirstLoading = false;
        IsForbidden = false;
        IsNotFound = false;
        IsSignInRequired = false;
        ErrorMessage = null;
        UpdatedAtText = Loc.T(
            "Обновлено в {0}",
            _timeProvider.GetLocalNow().ToString("HH:mm", Loc.Culture));

        ClearCollections();

        foreach (var service in profile.Services)
        {
            Services.Add(new BarberServiceItem(service));
        }

        // Группы собираются из уже разложенного прайса: заголовок специализации заменяет её
        // бейдж на каждой строке, а порядок API и скрытые строки сохраняются.
        foreach (var group in BarberCabinetServiceGroups.Build(Services, OtherServicesGroupTitle))
        {
            ServiceGroups.Add(group);
        }

        foreach (var specialization in profile.Specializations)
        {
            Specializations.Add(new BarberSpecializationItem(specialization));
        }

        foreach (var photo in profile.Photos)
        {
            Photos.Add(new BarberProfilePhotoItem(photo));
        }

        AddRange(AdditionalServices, profile.AdditionalServices);
        AddRange(Certificates, profile.CertificatesAndTraining);
        AddRange(Achievements, profile.Achievements);
        AddRange(CommunicationFormat, profile.CommunicationFormat);
        AddRange(Goodies, profile.Goodies);

        RebuildAvailableSpecializations();
        _cardAvatarUrl = profile.AvatarUrl;
        NotifyProfileState();
        NotifyAvatarState();
    }

    /// <summary>
    /// Дополняет карточку состоянием аватара из профиля аккаунта.
    /// </summary>
    /// <remarks>
    /// Карточка мастера отдаёт только одобренный и не скрытый кадр, поэтому владелец не
    /// увидел бы ни «На проверке», ни причину отказа. Сбой этого чтения — не ошибка раздела:
    /// состояние остаётся прежним, а карточка уже показана. Автоповтор сюда не относится:
    /// он лечит чтение самой карточки, а не дополнение к ней.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    private async Task LoadAvatarAsync(CancellationToken cancellationToken)
    {
        try
        {
            var me = await _avatarApi.GetMyProfileAsync(cancellationToken);

            _accountAvatar = new AccountAvatar(
                me.AvatarUrl,
                me.IsAvatarBlocked,
                me.AvatarModerationStatus,
                me.AvatarRejectionReason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Отмена вызывающим: состояние аватара не перечитано, прежнее остаётся на экране.
        }
        catch (Exception)
        {
            // Профиль аккаунта — дополнение: без него остаётся одобренный кадр карточки.
        }
        finally
        {
            NotifyAvatarState();
        }
    }

    /// <summary>
    /// Очищает всё, что построено из карточки.
    /// </summary>
    private void ClearCollections()
    {
        Services.Clear();
        ServiceGroups.Clear();
        Specializations.Clear();
        Photos.Clear();
        AdditionalServices.Clear();
        Certificates.Clear();
        Achievements.Clear();
        CommunicationFormat.Clear();
        Goodies.Clear();
        AvailableSpecializations.Clear();
        NotifyCollectionState();
    }

    /// <summary>
    /// Добавляет непустые строки списка в коллекцию показа.
    /// </summary>
    private static void AddRange(ObservableCollection<string> target, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                target.Add(value);
            }
        }
    }

    /// <summary>
    /// Перечитывает справочник специализаций.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="silent">
    /// Попытка цикла: причина остаётся на экране до ответа, индикатор загрузки не мигает.
    /// </param>
    /// <returns>Вид сбоя: по нему решает цикл.</returns>
    private async Task<ApiFailureKind> LoadSpecializationsAsync(
        CancellationToken cancellationToken,
        bool silent = false)
    {
        if (IsSpecializationLoading)
        {
            return ApiFailureKind.None;
        }

        if (!silent)
        {
            IsSpecializationLoading = true;
            SpecializationError = null;
        }

        try
        {
            var items = await _searchApi.GetSpecializationsAsync(cancellationToken);

            _allSpecializations.Clear();
            _allSpecializations.AddRange(items);
            _isCatalogLoaded = true;
            SpecializationError = null;

            RebuildAvailableSpecializations();

            return ApiFailureKind.None;
        }
        catch (Exception exception)
        {
            var kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                SpecializationError = BarberProfileText.ReadError(
                    exception,
                    kind,
                    CatalogFailedText);
            }

            return kind;
        }
        finally
        {
            IsSpecializationLoading = false;
        }
    }

    /// <summary>
    /// Оставляет в пикере только специализации, которых у мастера ещё нет.
    /// </summary>
    private void RebuildAvailableSpecializations()
    {
        if (!_isCatalogLoaded)
        {
            return;
        }

        var attached = Specializations
            .Select(item => item.SpecializationId)
            .ToHashSet();

        AvailableSpecializations.Clear();

        foreach (var option in _allSpecializations)
        {
            if (!attached.Contains(option.Id))
            {
                AvailableSpecializations.Add(new BarberSpecializationOption(option));
            }
        }

        OnPropertyChanged(nameof(HasAvailableSpecializations));
    }

    /// <summary>
    /// Одна попытка цикла: перечитывает карточку тихо.
    /// </summary>
    private Task<ApiFailureKind> RetryReadAsync(CancellationToken cancellationToken) =>
        LoadAsync(silent: true, cancellationToken);

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Переключает строку «Пробуем снова…» в пикере вслед за состоянием его цикла.
    /// </summary>
    private void OnSpecializationsAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsSpecializationAutoRetrying = _specializationsAutoRetry.IsRunning;

    /// <summary>
    /// Текст сбоя записи: коды домена важнее кода HTTP.
    /// </summary>
    private static string DescribeWriteFailure(
        Exception exception,
        string fallback,
        WriteTarget target)
    {
        if (exception is not BarberSearchApiException apiException)
        {
            return fallback;
        }

        var code = apiException.ErrorCodes.Count > 0 ? apiException.ErrorCodes[0] : null;

        var message = code switch
        {
            "barberProfile.addService.duplicate" => Loc.T("Эта услуга уже есть в прайсе."),
            "barberProfile.updateService.notFound" => Loc.T("Эта услуга уже скрыта. Обновите страницу."),
            "barberProfile.removeService.notFound" => Loc.T("Эта услуга уже скрыта. Обновите страницу."),
            "barberProfile.addService.hairServiceNotFound" => Loc.T("Услуга справочника не найдена."),
            "barberProfile.addService.price" => Loc.T("Цена не может быть отрицательной."),
            "barberProfile.addService.duration" => Loc.T("Длительность — от 1 до 1440 минут."),
            "barberService.updatePrice" => Loc.T("Цена не может быть отрицательной."),
            "barberService.updateDuration" => Loc.T("Длительность — от 1 до 1440 минут."),
            "barberProfile.addSpecialization.duplicate" => Loc.T("Эта специализация уже добавлена."),
            "barberProfile.addSpecialization.specializationNotFound" => Loc.T("Специализация не найдена."),
            "barberProfile.removeSpecialization.notFound" => Loc.T("Специализация уже убрана. Обновите страницу."),
            _ => null,
        };

        if (message is not null)
        {
            return message;
        }

        if (apiException.StatusCode == HttpStatusCode.Forbidden)
        {
            return target == WriteTarget.Service
                ? Loc.T("Прайс доступен только владельцу профиля.")
                : Loc.T("Специализации доступны только владельцу профиля.");
        }

        return BarberProfileText.WriteError(apiException, fallback);
    }

    /// <summary>
    /// Сообщает об изменении вычисляемых полей карточки.
    /// </summary>
    private void NotifyProfileState()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(GradeText));
        OnPropertyChanged(nameof(Bio));
        OnPropertyChanged(nameof(ExperienceText));
        OnPropertyChanged(nameof(RatingText));
        OnPropertyChanged(nameof(ReviewsText));
        OnPropertyChanged(nameof(IsVerified));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(VerificationHint));
        OnPropertyChanged(nameof(CanPreviewPublic));
        OnPropertyChanged(nameof(LanguagesText));
        OnPropertyChanged(nameof(PaymentMethodsText));
        OnPropertyChanged(nameof(LatePolicyText));
        NotifyCollectionState();
    }

    /// <summary>
    /// Сообщает о наличии строк в коллекциях раздела.
    /// </summary>
    private void NotifyCollectionState()
    {
        OnPropertyChanged(nameof(HasServices));
        OnPropertyChanged(nameof(HasSpecializations));
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(CanAddPhoto));
        OnPropertyChanged(nameof(IsPhotoLimitReached));
        OnPropertyChanged(nameof(HasAvailableSpecializations));
    }

    /// <summary>
    /// Сообщает о лимите портфолио после изменения коллекции фотографий.
    /// </summary>
    private void NotifyPhotoLimitState()
    {
        OnPropertyChanged(nameof(CanAddPhoto));
        OnPropertyChanged(nameof(IsPhotoLimitReached));
    }

    /// <summary>
    /// Состояние проверки аватара, которое видно владельцу: скрытый кадр статуса не имеет.
    /// </summary>
    private PhotoModerationStatus? VisibleAvatarStatus =>
        IsAvatarBlocked ? null : _accountAvatar?.Status;

    /// <summary>
    /// Сообщает обо всех вычисляемых полях среза аватара.
    /// </summary>
    /// <remarks>
    /// Один вызов на любое изменение состояния — прочитанный <c>/me</c>, ответ карточки,
    /// успешная загрузка или удаление: иначе представление показало бы старый кадр с новым
    /// бейджем.
    /// </remarks>
    private void NotifyAvatarState()
    {
        OnPropertyChanged(nameof(AvatarUrl));
        OnPropertyChanged(nameof(HasAvatar));
        OnPropertyChanged(nameof(ShowInitials));
        OnPropertyChanged(nameof(IsAvatarBlocked));
        OnPropertyChanged(nameof(HasAvatarStatus));
        OnPropertyChanged(nameof(AvatarStatusText));
        OnPropertyChanged(nameof(AvatarStatusKind));
        OnPropertyChanged(nameof(HasAvatarRejection));
        OnPropertyChanged(nameof(AvatarRejectionText));
        OnPropertyChanged(nameof(AvatarBlockedText));
        OnPropertyChanged(nameof(AvatarBlockedHint));
        OnPropertyChanged(nameof(CanDeleteAvatar));
        OnPropertyChanged(nameof(AvatarActionText));
        OnPropertyChanged(nameof(CanChangeAvatar));
        OnPropertyChanged(nameof(IsAvatarUploading));
        OnPropertyChanged(nameof(IsAvatarDeleting));
        OnPropertyChanged(nameof(IsAvatarBusy));
        OnPropertyChanged(nameof(AvatarError));
        OnPropertyChanged(nameof(AvatarAnnouncement));
    }

    /// <summary>
    /// Сообщает о занятости среза аватара: от неё зависят доступность и подсказка действий.
    /// </summary>
    private void NotifyAvatarBusyState()
    {
        OnPropertyChanged(nameof(IsAvatarBusy));
        OnPropertyChanged(nameof(CanChangeAvatar));
    }

    /// <summary>
    /// Аватар из <c>GET api/users/me</c>: сырой URL и состояние проверки.
    /// </summary>
    /// <param name="Url">Относительный media-URL; <c>null</c>, если фото нет.</param>
    /// <param name="IsBlocked">Фото скрыто администратором.</param>
    /// <param name="Status">Допуск к публикации.</param>
    /// <param name="RejectionReason">Причина отказа администратора либо <c>null</c>.</param>
    private sealed record AccountAvatar(
        string? Url,
        bool IsBlocked,
        PhotoModerationStatus Status,
        string? RejectionReason);

    /// <summary>
    /// Куда адресована мутация: по этому выбирается текст отказа <c>403</c>.
    /// </summary>
    private enum WriteTarget
    {
        Service,
        Specialization,
    }
}

/// <summary>
/// Строка пикера специализаций: специализация справочника, которой у мастера ещё нет.
/// </summary>
public sealed class BarberSpecializationOption
{
    /// <summary>
    /// Создаёт строку из ответа справочника.
    /// </summary>
    /// <param name="specialization">Специализация справочника.</param>
    public BarberSpecializationOption(SpecializationResponse specialization)
    {
        ArgumentNullException.ThrowIfNull(specialization);

        Id = specialization.Id;
        Name = specialization.Name;
        Description = specialization.Description;
    }

    /// <summary>
    /// Идентификатор специализации: его принимает добавление.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Название специализации.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Описание; <c>null</c>, если его нет.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Описание заполнено.
    /// </summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}
