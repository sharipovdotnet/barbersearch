using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Admin;

/// <summary>
/// Вкладка очереди фотографий: аватары или работы портфолио.
/// </summary>
public enum AdminPhotosSegment
{
    /// <summary>
    /// Аватары пользователей, ждущие проверки.
    /// </summary>
    Avatars,

    /// <summary>
    /// Работы портфолио мастеров, ждущие проверки.
    /// </summary>
    Portfolio,
}

/// <summary>
/// Экран «Фотографии»: две независимые очереди премодерации.
/// </summary>
/// <remarks>
/// <para>
/// Это мобильная замена веб-экрана <c>/admin/photos</c>. Две вкладки веба стали двумя
/// сегментами, а каждая очередь живёт в своём <see cref="AdminPhotoModerationList"/>: у неё свой
/// список, курсор, прокрутка и свои циклы автоповтора. Переключение сегмента не
/// перечитывает соседнюю очередь — она остаётся такой, какой её оставил администратор.
/// </para>
/// <para>
/// Экрана карточки нет: на телефоне кадр лучше видно в самой строке, а решение — это
/// два нажатия (при отказе — причина и подтверждение). Поэтому страница не переходит
/// никуда, а после успешного решения очередь перечитывает первую страницу: решённый кадр
/// уходит из списка, потому что очередь — это только <c>Pending</c>.
/// </para>
/// <para>
/// Ошибки чтения повторяются сами, пока страница видна; решения не повторяются никогда.
/// <c>401</c> гасит сессию и показывает просьбу войти снова, <c>403</c> — отказ в доступе
/// без предложения повторить: роль не появится от повтора.
/// </para>
/// </remarks>
public sealed partial class AdminPhotosViewModel : ObservableObject
{
    /// <summary>
    /// Создаёт экран очередей фотографий.
    /// </summary>
    /// <param name="photosApi">Чтения очередей и решения по кадрам.</param>
    /// <param name="apiOptions">Базовый адрес API для склейки адресов картинок.</param>
    /// <param name="session">Сессия: конец сессии при <c>401</c>.</param>
    /// <param name="avatarsAutoRetry">
    /// Цикл автоповтора первой страницы аватаров; <c>null</c> — обычное расписание.
    /// </param>
    /// <param name="avatarsLoadMoreAutoRetry">
    /// Цикл автоповтора догрузки аватаров; <c>null</c> — обычное расписание.
    /// </param>
    /// <param name="portfolioAutoRetry">
    /// Цикл автоповтора первой страницы портфолио; <c>null</c> — обычное расписание.
    /// </param>
    /// <param name="portfolioLoadMoreAutoRetry">
    /// Цикл автоповтора догрузки портфолио; <c>null</c> — обычное расписание.
    /// </param>
    /// <param name="mediaApi">
    /// Чтение превью тем же авторизованным клиентом, что и списки; <c>null</c> — очередь
    /// работает без превью (подложка вместо кадра), как в проверках без сети.
    /// </param>
    public AdminPhotosViewModel(
        IAdminPhotosApi photosApi,
        BarberSearchApiOptions apiOptions,
        TokenSession session,
        AutoRetryLoop? avatarsAutoRetry = null,
        AutoRetryLoop? avatarsLoadMoreAutoRetry = null,
        AutoRetryLoop? portfolioAutoRetry = null,
        AutoRetryLoop? portfolioLoadMoreAutoRetry = null,
        IAdminMediaApi? mediaApi = null)
    {
        ArgumentNullException.ThrowIfNull(photosApi);
        ArgumentNullException.ThrowIfNull(apiOptions);
        ArgumentNullException.ThrowIfNull(session);

        Avatars = new AdminPhotoModerationList(
            AdminPhotosSegment.Avatars,
            photosApi,
            apiOptions,
            session,
            avatarsAutoRetry,
            avatarsLoadMoreAutoRetry,
            mediaApi);

        Portfolio = new AdminPhotoModerationList(
            AdminPhotosSegment.Portfolio,
            photosApi,
            apiOptions,
            session,
            portfolioAutoRetry,
            portfolioLoadMoreAutoRetry,
            mediaApi);

        Avatars.SessionEnded += OnQueueSessionEnded;
        Portfolio.SessionEnded += OnQueueSessionEnded;
    }

    /// <summary>
    /// Очередь аватаров.
    /// </summary>
    public AdminPhotoModerationList Avatars { get; }

    /// <summary>
    /// Очередь работ портфолио.
    /// </summary>
    public AdminPhotoModerationList Portfolio { get; }

    /// <summary>
    /// Выбранный сегмент; по умолчанию аватары, как вкладка веба по умолчанию.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvatarsSelected))]
    [NotifyPropertyChangedFor(nameof(IsPortfolioSelected))]
    private AdminPhotosSegment _selectedSegment = AdminPhotosSegment.Avatars;

    /// <summary>
    /// Сервер ответил <c>401</c>: пара токенов больше не действует.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSignInRequired))]
    [NotifyPropertyChangedFor(nameof(ShowPage))]
    private bool _isSignInRequired;

    /// <summary>
    /// Открыт сегмент аватаров.
    /// </summary>
    public bool IsAvatarsSelected => SelectedSegment == AdminPhotosSegment.Avatars;

    /// <summary>
    /// Открыт сегмент портфолио.
    /// </summary>
    public bool IsPortfolioSelected => SelectedSegment == AdminPhotosSegment.Portfolio;

    /// <summary>
    /// Нужен вход: данные очередей скрыты.
    /// </summary>
    public bool ShowSignInRequired => IsSignInRequired;

    /// <summary>
    /// Основное содержимое страницы видно: сессия ещё действует.
    /// </summary>
    public bool ShowPage => !IsSignInRequired;

    /// <summary>
    /// Читает выбранную очередь: первый раз с индикатором, при возврате — тихо.
    /// </summary>
    /// <remarks>
    /// Вторая очередь не читается: она загрузится при первом переключении сегмента и
    /// дальше не перечитывается.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        SelectedSegment == AdminPhotosSegment.Avatars
            ? Avatars.InitializeAsync(cancellationToken)
            : Portfolio.InitializeAsync(cancellationToken);

    /// <summary>
    /// Включает автоповтор обеих очередей: страница видна.
    /// </summary>
    public void StartAutoRetry()
    {
        Avatars.StartAutoRetry();
        Portfolio.StartAutoRetry();
    }

    /// <summary>
    /// Выключает автоповтор обеих очередей: страница скрыта.
    /// </summary>
    public void StopAutoRetry()
    {
        Avatars.StopAutoRetry();
        Portfolio.StopAutoRetry();
    }

    /// <summary>
    /// Гасит сессию: обе очереди очищаются, страница просит войти снова.
    /// </summary>
    public void ApplySignedOutState()
    {
        IsSignInRequired = true;

        Avatars.ApplySignedOutState();
        Portfolio.ApplySignedOutState();
    }

    /// <summary>
    /// Открывает сегмент аватаров; очередь читается, только если её ещё не читали.
    /// </summary>
    /// <returns>Задача возможного первого чтения.</returns>
    [RelayCommand]
    private async Task ShowAvatarsAsync()
    {
        if (SelectedSegment == AdminPhotosSegment.Avatars)
        {
            return;
        }

        SelectedSegment = AdminPhotosSegment.Avatars;

        await Avatars.LoadIfNeededAsync(CancellationToken.None);
    }

    /// <summary>
    /// Открывает сегмент портфолио; очередь читается, только если её ещё не читали.
    /// </summary>
    /// <returns>Задача возможного первого чтения.</returns>
    [RelayCommand]
    private async Task ShowPortfolioAsync()
    {
        if (SelectedSegment == AdminPhotosSegment.Portfolio)
        {
            return;
        }

        SelectedSegment = AdminPhotosSegment.Portfolio;

        await Portfolio.LoadIfNeededAsync(CancellationToken.None);
    }

    /// <summary>
    /// Очередь сообщила о конце сессии: экран показывает просьбу войти.
    /// </summary>
    /// <param name="sender">Очередь.</param>
    /// <param name="e">Аргументы события.</param>
    private void OnQueueSessionEnded(object? sender, EventArgs e) => ApplySignedOutState();
}

/// <summary>
/// Одна очередь премодерации: список, курсор, ошибки и решения по своим кадрам.
/// </summary>
/// <remarks>
/// <para>
/// Тип общий для аватаров и портфолио: различается только чтение и адрес решения, а
/// состояния, пагинация, автоповтор и тексты сбоев у очередей одинаковые. Поэтому
/// <see cref="Segment"/> решает, какой запрос уйдёт, а карточка получает готовую строку.
/// </para>
/// <para>
/// Список — только <c>Pending</c>: сервер не отдаёт историю, и фильтра статуса у запроса
/// нет. Успешное решение убирает кадр локально и перечитывает первую страницу — сервер
/// остаётся источником правды, а строка исчезает сразу, не дожидаясь сети.
/// </para>
/// <para>
/// У первой страницы и догрузки свои циклы автоповтора: это два разных снимка с разными
/// курсорами. Решения не повторяются: <c>POST</c> с причиной не идемпотентен с точки
/// зрения человека, который ждёт ответа.
/// </para>
/// </remarks>
public sealed partial class AdminPhotoModerationList : ObservableObject
{
    /// <summary>
    /// Размер страницы — как в вебе (<c>ADMIN_PHOTOS_PAGE_SIZE</c>).
    /// </summary>
    public const int PageSize = 20;

    private static string LoadErrorText => Loc.T("Не удалось загрузить фотографии на проверке.");
    private static string LoadMoreErrorText => Loc.T("Не удалось загрузить следующую страницу фотографий.");
    private static string CursorStaleText => Loc.T("Список устарел. Обновите его и продолжите с начала.");
    private static string NetworkErrorText => Loc.T(
        "Нет связи с сервером. Проверьте подключение и повторите попытку.");
    private static string TooManyActionsText => Loc.T(
        "Слишком много действий подряд. Подождите минуту и повторите.");
    private static string ForbiddenText => Loc.T("Раздел доступен только администратору.");
    private static string ReasonRequiredText => Loc.T("Укажите причину отказа.");
    private static string ReasonTooLongText => Loc.T("Слишком длинная причина — максимум 500 символов.");
    private static string NoAvatarText => Loc.T("У пользователя нет фото — проверять нечего.");
    private static string UserGoneText => Loc.T("Пользователь больше не существует. Обновите список.");
    private static string PhotoGoneText => Loc.T("Фото больше не существует. Обновите список.");
    private static string AlreadyReviewedText => Loc.T("Фото уже проверено. Обновите список.");
    private static string ActionFailedText => Loc.T(
        "Не удалось выполнить действие с фото. Попробуйте ещё раз.");
    private static string ApprovedAnnouncement => Loc.T("Фото одобрено.");
    private static string RejectedAnnouncement => Loc.T("Фото отклонено.");

    /// <summary>
    /// Сколько превью читать одновременно: превью — украшение, и три соединения на
    /// мобильной сети хватает, чтобы карточки наполнялись без гонки за каналом.
    /// </summary>
    private const int PreviewConcurrency = 3;

    /// <summary>
    /// Предел одновременных чтений превью — на всё приложение, а не на очередь.
    /// </summary>
    /// <remarks>
    /// Обе очереди и оба сегмента делят три разрешения: мобильный канал один, и десяток
    /// параллельных картинок не ускорил бы показ, а отнял бы его у списка. Поле статическое
    /// и живёт столько же, сколько процесс: очередь создаётся на визит экрана, а предел
    /// должен переживать её, иначе догрузка второй страницы удваивала бы параллельность.
    /// </remarks>
    private static readonly SemaphoreSlim PreviewGate =
        new(PreviewConcurrency, PreviewConcurrency);

    private readonly AdminPhotosSegment _segment;
    private readonly IAdminPhotosApi _api;
    private readonly BarberSearchApiOptions _apiOptions;
    private readonly TokenSession _session;
    private readonly IAdminMediaApi? _mediaApi;
    private readonly AutoRetryLoop _autoRetry;
    private readonly AutoRetryLoop _loadMoreAutoRetry;

    private string? _nextCursor;
    private int _generation;
    private ApiFailureKind _failureKind;
    private ApiFailureKind _loadMoreFailureKind;
    private string? _loadMoreRetryCursor;
    private bool _isPageVisible;
    private Task _previews = Task.CompletedTask;

    /// <summary>
    /// Создаёт очередь.
    /// </summary>
    /// <param name="segment">Аватары или портфолио.</param>
    /// <param name="api">Чтения очередей и решения по кадрам.</param>
    /// <param name="apiOptions">Базовый адрес API для склейки адресов картинок.</param>
    /// <param name="session">Сессия: конец сессии при <c>401</c>.</param>
    /// <param name="autoRetry">Цикл автоповтора первой страницы; <c>null</c> — обычное расписание.</param>
    /// <param name="loadMoreAutoRetry">Цикл автоповтора догрузки; <c>null</c> — обычное расписание.</param>
    /// <param name="mediaApi">
    /// Чтение превью с Bearer; <c>null</c> — очередь не грузит превью вовсе.
    /// </param>
    public AdminPhotoModerationList(
        AdminPhotosSegment segment,
        IAdminPhotosApi api,
        BarberSearchApiOptions apiOptions,
        TokenSession session,
        AutoRetryLoop? autoRetry = null,
        AutoRetryLoop? loadMoreAutoRetry = null,
        IAdminMediaApi? mediaApi = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(apiOptions);
        ArgumentNullException.ThrowIfNull(session);

        _segment = segment;
        _api = api;
        _apiOptions = apiOptions;
        _session = session;
        _mediaApi = mediaApi;

        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;

        _loadMoreAutoRetry = loadMoreAutoRetry ?? new AutoRetryLoop();
        _loadMoreAutoRetry.StateChanged += OnLoadMoreAutoRetryStateChanged;
    }

    /// <summary>
    /// Сессия закончилась: очередь больше не может читать и решать.
    /// </summary>
    public event EventHandler? SessionEnded;

    /// <summary>
    /// Какой очереди принадлежит список.
    /// </summary>
    public AdminPhotosSegment Segment => _segment;

    /// <summary>
    /// Работа портфолио, а не аватар: у неё другой адрес решения.
    /// </summary>
    public bool IsPortfolio => _segment == AdminPhotosSegment.Portfolio;

    /// <summary>
    /// Строки очереди в порядке, который вернул сервер.
    /// </summary>
    public ObservableCollection<AdminPhotoItem> Items { get; } = [];

    /// <summary>
    /// Идёт первая загрузка очереди.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isFirstLoading;

    /// <summary>
    /// Идёт обновление свайпом: индикатор держит <c>RefreshView</c>.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Идёт догрузка следующей страницы.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingMore;

    /// <summary>
    /// Цикл сам повторяет первую страницу: под причиной видна строка «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Цикл сам повторяет догрузку: в футере видна строка «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isLoadMoreAutoRetrying;

    /// <summary>
    /// Ошибка первой страницы; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private string? _errorMessage;

    /// <summary>
    /// Ошибка догрузки: уже загруженные строки остаются.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadMoreError))]
    private string? _loadMoreError;

    /// <summary>
    /// Сервер ответил <c>403</c>: роль не администратор, данные не показываются.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowForbidden))]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isForbidden;

    /// <summary>
    /// Первая страница уже читалась.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasLoaded;

    /// <summary>
    /// Баннер о результате решения: строка исчезает, и без него успех не виден.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnnouncement))]
    private string? _announcement;

    /// <summary>
    /// Показывать индикатор первой загрузки.
    /// </summary>
    public bool ShowLoading => IsFirstLoading;

    /// <summary>
    /// Показывать сбой первой страницы с «Повторить».
    /// </summary>
    public bool ShowError => !IsFirstLoading && ErrorMessage is not null && !IsForbidden;

    /// <summary>
    /// Показывать отказ в доступе.
    /// </summary>
    public bool ShowForbidden => IsForbidden;

    /// <summary>
    /// Показывать список или пустое состояние.
    /// </summary>
    public bool ShowContent =>
        HasLoaded && !IsFirstLoading && !ShowError && !IsForbidden;

    /// <summary>
    /// Показывать пустое состояние: очередь прочитана и пуста.
    /// </summary>
    public bool ShowEmptyState =>
        ShowContent && Items.Count == 0;

    /// <summary>
    /// Есть следующая страница.
    /// </summary>
    public bool CanLoadMore => _nextCursor is not null;

    /// <summary>
    /// Показывать ошибку догрузки в футере.
    /// </summary>
    public bool ShowLoadMoreError => LoadMoreError is not null;

    /// <summary>
    /// Виден ли баннер о результате решения.
    /// </summary>
    public bool HasAnnouncement => !string.IsNullOrEmpty(Announcement);

    /// <summary>
    /// Цикл автоповтора первой страницы: нужен тестам, приложению — нет.
    /// </summary>
    internal AutoRetryLoop AutoRetry => _autoRetry;

    /// <summary>
    /// Завершение цикла первой страницы: тесты ждут его вместо пауз.
    /// </summary>
    internal Task AutoRetryCompletion => _autoRetry.Completion;

    /// <summary>
    /// Цикл автоповтора догрузки.
    /// </summary>
    internal AutoRetryLoop LoadMoreAutoRetry => _loadMoreAutoRetry;

    /// <summary>
    /// Завершение цикла догрузки.
    /// </summary>
    internal Task LoadMoreAutoRetryCompletion => _loadMoreAutoRetry.Completion;

    /// <summary>
    /// Завершение чтения превью: тесты ждут его вместо пауз.
    /// </summary>
    /// <remarks>
    /// Задача накопительная: к ней присоединяется каждая следующая пачка превью, поэтому
    /// ожидание после обновления списка покрывает и ответы прошлого состава.
    /// </remarks>
    internal Task PreviewsCompletion => _previews;

    /// <summary>
    /// Читает очередь при появлении страницы: первый раз с индикатором, дальше тихо.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsFirstLoading)
        {
            return;
        }

        if (!await EnsureSessionAsync(cancellationToken))
        {
            return;
        }

        if (HasLoaded)
        {
            await LoadFirstPageAsync(FirstPageMode.Silent, cancellationToken);

            return;
        }

        HasLoaded = true;

        await LoadFirstPageAsync(FirstPageMode.Initial, cancellationToken);
    }

    /// <summary>
    /// Читает очередь, только если её ещё не читали: переключение сегмента не обновляет
    /// уже загруженный список.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача возможного чтения.</returns>
    public async Task LoadIfNeededAsync(CancellationToken cancellationToken = default)
    {
        if (HasLoaded || !await EnsureSessionAsync(cancellationToken))
        {
            return;
        }

        HasLoaded = true;

        await LoadFirstPageAsync(FirstPageMode.Initial, cancellationToken);
    }

    /// <summary>
    /// Включает автоповтор: страница видна.
    /// </summary>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartAutoRetry();
        TryStartLoadMoreAutoRetry();
    }

    /// <summary>
    /// Выключает автоповтор: страница скрыта или открыт другой сегмент.
    /// </summary>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        _autoRetry.Stop();
        _loadMoreAutoRetry.Stop();
    }

    /// <summary>
    /// Очищает очередь после завершения сессии.
    /// </summary>
    public void ApplySignedOutState()
    {
        _autoRetry.Stop();
        _loadMoreAutoRetry.Stop();

        _generation++;
        _nextCursor = null;
        _failureKind = ApiFailureKind.None;
        _loadMoreFailureKind = ApiFailureKind.None;
        _loadMoreRetryCursor = null;

        IsFirstLoading = false;
        IsRefreshing = false;
        IsLoadingMore = false;
        IsForbidden = false;
        HasLoaded = false;
        ErrorMessage = null;
        LoadMoreError = null;
        Announcement = null;

        Items.Clear();

        NotifyViewState();
    }

    /// <summary>
    /// Одобряет кадр строки и убирает её из очереди.
    /// </summary>
    /// <param name="item">Строка очереди.</param>
    /// <returns>Задача решения.</returns>
    public async Task ApproveAsync(AdminPhotoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.IsActing)
        {
            return;
        }

        item.BeginActing(isApproving: true);
        Announcement = null;

        try
        {
            if (IsPortfolio)
            {
                await _api.ApprovePortfolioPhotoAsync(
                    item.BarberProfileId!.Value,
                    item.Id,
                    CancellationToken.None);
            }
            else
            {
                await _api.ApproveAvatarAsync(item.Id, CancellationToken.None);
            }

            Announcement = ApprovedAnnouncement;

            await RemoveDecidedItemAsync(item);

            return;
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();

            return;
        }
        catch (BarberSearchApiException exception)
        {
            await ApplyDecisionFailureAsync(item, exception);
        }
        catch (HttpRequestException)
        {
            item.ApplyError(NetworkErrorText);
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            item.ApplyError(NetworkErrorText);
        }
        finally
        {
            item.EndActing();
        }
    }

    /// <summary>
    /// Отклоняет кадр строки с причиной и убирает её из очереди.
    /// </summary>
    /// <param name="item">Строка очереди.</param>
    /// <returns>Задача решения.</returns>
    public async Task RejectAsync(AdminPhotoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.IsActing)
        {
            return;
        }

        var reason = (item.Reason ?? string.Empty).Trim();

        if (reason.Length == 0)
        {
            item.ApplyError(ReasonRequiredText);

            return;
        }

        if (reason.Length > AdminPhotoItem.RejectionReasonMaxLength)
        {
            item.ApplyError(ReasonTooLongText);

            return;
        }

        item.BeginActing(isApproving: false);
        Announcement = null;

        try
        {
            if (IsPortfolio)
            {
                await _api.RejectPortfolioPhotoAsync(
                    item.BarberProfileId!.Value,
                    item.Id,
                    reason,
                    CancellationToken.None);
            }
            else
            {
                await _api.RejectAvatarAsync(item.Id, reason, CancellationToken.None);
            }

            item.HideRejectPanel();
            Announcement = RejectedAnnouncement;

            await RemoveDecidedItemAsync(item);

            return;
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();

            return;
        }
        catch (BarberSearchApiException exception)
        {
            await ApplyDecisionFailureAsync(item, exception);
        }
        catch (HttpRequestException)
        {
            item.ApplyError(NetworkErrorText);
        }
        catch (OperationCanceledException)
        {
            item.ApplyError(NetworkErrorText);
        }
        finally
        {
            item.EndActing();
        }
    }

    /// <summary>
    /// Открывает панель причины у строки и закрывает её у остальных: открыта всегда одна.
    /// </summary>
    /// <param name="item">Строка, у которой открывают причину.</param>
    public void OpenReject(AdminPhotoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        foreach (var other in Items)
        {
            if (!ReferenceEquals(other, item))
            {
                other.HideRejectPanel();
            }
        }

        Announcement = null;

        item.ShowRejectPanel();
    }

    /// <summary>
    /// Закрывает панель причины без отказа.
    /// </summary>
    /// <param name="item">Строка, у которой закрывают причину.</param>
    public void CloseReject(AdminPhotoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.HideRejectPanel();
        NotifyViewState();
    }

    /// <summary>
    /// Свайп вниз: перечитать первую страницу, оставив список на месте.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_session.IsAuthenticated)
        {
            IsRefreshing = false;

            return;
        }

        IsRefreshing = true;

        try
        {
            await LoadFirstPageAsync(FirstPageMode.Silent, CancellationToken.None);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// «Повторить» после сбоя: остановить фон и прочитать снова с индикатором.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RetryAsync()
    {
        if (!_session.IsAuthenticated)
        {
            return;
        }

        _autoRetry.Stop();

        ErrorMessage = null;

        NotifyViewState();

        await LoadFirstPageAsync(FirstPageMode.Initial, CancellationToken.None);

        TryStartAutoRetry();
    }

    /// <summary>
    /// Догружает следующую страницу.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        var cursor = _nextCursor;

        if (!_session.IsAuthenticated || IsFirstLoading || IsLoadingMore || cursor is null)
        {
            return;
        }

        await LoadMorePageAsync(cursor, LoadMoreMode.Normal, CancellationToken.None);
    }

    /// <summary>
    /// Повторяет догрузку после сбоя, не трогая уже показанные строки.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RetryLoadMoreAsync()
    {
        _loadMoreAutoRetry.Stop();

        var cursor = _nextCursor;

        if (!_session.IsAuthenticated || IsFirstLoading || cursor is null)
        {
            return;
        }

        await LoadMorePageAsync(cursor, LoadMoreMode.Normal, CancellationToken.None);
    }

    /// <summary>
    /// Закрывает баннер о результате решения.
    /// </summary>
    [RelayCommand]
    private void DismissAnnouncement() => Announcement = null;

    /// <summary>
    /// Проверяет сессию и, если её нет, показывает просьбу войти.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c>, если сессия есть.</returns>
    private async Task<bool> EnsureSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _session.EnsureLoadedAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (_session.IsAuthenticated)
        {
            return true;
        }

        ApplySignedOutState();

        SessionEnded?.Invoke(this, EventArgs.Empty);

        return false;
    }

    /// <summary>
    /// Читает первую страницу и заменяет список.
    /// </summary>
    /// <param name="mode">Показать индикатор, обновить молча или повторить в фоне.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private async Task<ApiFailureKind> LoadFirstPageAsync(
        FirstPageMode mode,
        CancellationToken cancellationToken)
    {
        _generation++;

        var generation = _generation;
        var kind = ApiFailureKind.None;

        if (mode != FirstPageMode.Retry)
        {
            ErrorMessage = null;
            LoadMoreError = null;
            _failureKind = ApiFailureKind.None;
        }

        if (mode == FirstPageMode.Initial)
        {
            IsFirstLoading = true;
        }

        NotifyViewState();

        try
        {
            var page = await FetchPageAsync(cursor: null, cancellationToken);

            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            Items.Clear();

            foreach (var item in page.Items)
            {
                Items.Add(item);
            }

            _nextCursor = page.NextCursor;
            ErrorMessage = null;
            LoadMoreError = null;
            IsForbidden = false;
            _failureKind = ApiFailureKind.None;
            _loadMoreFailureKind = ApiFailureKind.None;
            _loadMoreRetryCursor = null;

            NotifyViewState();
            StartPreviews(page.Items, generation);
        }
        catch (Exception exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Unauthorized })
                {
                    await EndSessionAsync();
                }
                else if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Forbidden })
                {
                    IsForbidden = true;
                    ErrorMessage = null;

                    NotifyViewState();
                }
                else if (generation == _generation)
                {
                    SetPageFailure(
                        generation,
                        DescribeReadFailure(exception, kind),
                        kind);
                }
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsFirstLoading = false;

                NotifyViewState();
                TryStartAutoRetry();
            }
        }

        return kind;
    }

    /// <summary>
    /// Читает следующую страницу — обычной догрузкой или тихой попыткой цикла.
    /// </summary>
    /// <param name="cursor">Курсор упавшей страницы: повтор несёт именно его.</param>
    /// <param name="mode">Обычная догрузка или тихая попытка цикла.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему второй цикл решает, нужна ли ещё попытка.</returns>
    private async Task<ApiFailureKind> LoadMorePageAsync(
        string cursor,
        LoadMoreMode mode,
        CancellationToken cancellationToken)
    {
        IsLoadingMore = true;

        if (mode == LoadMoreMode.Normal)
        {
            LoadMoreError = null;
            _loadMoreFailureKind = ApiFailureKind.None;
        }

        NotifyViewState();

        var generation = _generation;
        var kind = ApiFailureKind.None;

        try
        {
            var page = await FetchPageAsync(cursor, cancellationToken);

            if (generation != _generation || _nextCursor != cursor)
            {
                return ApiFailureKind.None;
            }

            AppendItems(page.Items);
            _nextCursor = page.NextCursor;
            LoadMoreError = null;
            _loadMoreFailureKind = ApiFailureKind.None;
            _loadMoreRetryCursor = null;

            NotifyViewState();
        }
        catch (Exception exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Unauthorized })
                {
                    await EndSessionAsync();
                }
                else if (generation == _generation && _nextCursor == cursor)
                {
                    SetLoadMoreFailure(
                        cursor,
                        DescribeLoadMoreFailure(exception, kind),
                        kind);
                }
            }
        }
        finally
        {
            IsLoadingMore = false;

            NotifyViewState();
        }

        return kind;
    }

    /// <summary>
    /// Читает одну страницу нужной очереди и собирает строки карточек.
    /// </summary>
    /// <param name="cursor">Курсор страницы либо <c>null</c>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Строки страницы и курсор продолжения.</returns>
    private async Task<PhotoPage> FetchPageAsync(
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (IsPortfolio)
        {
            var page = await _api.GetPortfolioAsync(cursor, PageSize, cancellationToken);
            var items = new List<AdminPhotoItem>(page.Items.Count);

            foreach (var photo in page.Items)
            {
                items.Add(AdminPhotoItem.FromPortfolio(this, photo, _apiOptions.BaseAddress));
            }

            return new PhotoPage(items, page.NextCursor);
        }

        var avatars = await _api.GetAvatarsAsync(cursor, PageSize, cancellationToken);
        var avatarItems = new List<AdminPhotoItem>(avatars.Items.Count);

        foreach (var photo in avatars.Items)
        {
            avatarItems.Add(AdminPhotoItem.FromAvatar(this, photo, _apiOptions.BaseAddress));
        }

        return new PhotoPage(avatarItems, avatars.NextCursor);
    }

    /// <summary>
    /// Показывает ошибку первой страницы, не стирая уже загруженные строки.
    /// </summary>
    /// <param name="generation">Поколение ответов, к которому относится сбой.</param>
    /// <param name="message">Текст для человека.</param>
    /// <param name="kind">Вид сбоя: по нему решает цикл автоповтора.</param>
    private void SetPageFailure(int generation, string message, ApiFailureKind kind)
    {
        if (generation != _generation)
        {
            return;
        }

        IsFirstLoading = false;
        _failureKind = kind;

        if (Items.Count == 0)
        {
            ErrorMessage = message;
        }

        NotifyViewState();
        TryStartAutoRetry();
    }

    /// <summary>
    /// Показывает ошибку догрузки и запоминает снимок упавшего запроса.
    /// </summary>
    /// <param name="cursor">Курсор упавшей страницы.</param>
    /// <param name="message">Текст для человека.</param>
    /// <param name="kind">Вид сбоя: по нему решает цикл автоповтора.</param>
    private void SetLoadMoreFailure(string cursor, string message, ApiFailureKind kind)
    {
        IsLoadingMore = false;
        LoadMoreError = message;
        _loadMoreFailureKind = kind;
        _loadMoreRetryCursor = cursor;

        NotifyViewState();
        TryStartLoadMoreAutoRetry();
    }

    /// <summary>
    /// Применяет сбой решения: устаревшая строка уходит из очереди, остальные ошибки
    /// остаются на карточке.
    /// </summary>
    /// <param name="item">Строка, по которой решали.</param>
    /// <param name="exception">Ответ сервера с ошибкой.</param>
    /// <returns>Задача обработки.</returns>
    private async Task ApplyDecisionFailureAsync(
        AdminPhotoItem item,
        BarberSearchApiException exception)
    {
        var message = DescribeActionFailure(exception);

        if (!IsStaleDecision(exception))
        {
            item.ApplyError(message);

            return;
        }

        item.HideRejectPanel();
        Announcement = message;

        await RemoveDecidedItemAsync(item);
    }

    /// <summary>
    /// Убирает решённую строку и перечитывает очередь: сервер — источник правды.
    /// </summary>
    /// <param name="item">Решённая строка.</param>
    /// <returns>Задача обновления.</returns>
    private async Task RemoveDecidedItemAsync(AdminPhotoItem item)
    {
        Items.Remove(item);

        NotifyViewState();

        await LoadFirstPageAsync(FirstPageMode.Silent, CancellationToken.None);
    }

    /// <summary>
    /// Запускает цикл первой страницы, если страница видна, сбой повторяемый и ничего
    /// не грузится.
    /// </summary>
    private void TryStartAutoRetry()
    {
        if (!_isPageVisible || IsFirstLoading || IsForbidden)
        {
            return;
        }

        if (ErrorMessage is null || !_failureKind.IsRetryable())
        {
            return;
        }

        _autoRetry.Start(RetryFirstPageAsync);
    }

    /// <summary>
    /// Одна попытка цикла: перечитать первую страницу, не гася ошибку.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя.</returns>
    private Task<ApiFailureKind> RetryFirstPageAsync(CancellationToken cancellationToken)
    {
        if (ErrorMessage is null || !_failureKind.IsRetryable())
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadFirstPageAsync(FirstPageMode.Retry, cancellationToken);
    }

    /// <summary>
    /// Запускает цикл догрузки, если страница видна, сбой повторяемый и есть снимок курсора.
    /// </summary>
    private void TryStartLoadMoreAutoRetry()
    {
        if (!_isPageVisible)
        {
            return;
        }

        if (_loadMoreRetryCursor is null
            || LoadMoreError is null
            || !_loadMoreFailureKind.IsRetryable()
            || IsFirstLoading)
        {
            return;
        }

        _loadMoreAutoRetry.Start(RetryLoadMoreAttemptAsync);
    }

    /// <summary>
    /// Одна попытка цикла: повторить упавшую догрузку тем же курсором.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя.</returns>
    private Task<ApiFailureKind> RetryLoadMoreAttemptAsync(CancellationToken cancellationToken)
    {
        if (_loadMoreRetryCursor is not { } cursor
            || LoadMoreError is null
            || !_loadMoreFailureKind.IsRetryable()
            || _nextCursor != cursor
            || IsLoadingMore)
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadMorePageAsync(cursor, LoadMoreMode.Retry, cancellationToken);
    }

    /// <summary>
    /// Дополняет список следующей страницей, не добавляя уже показанные строки.
    /// </summary>
    /// <param name="items">Строки страницы.</param>
    private void AppendItems(IReadOnlyList<AdminPhotoItem> items)
    {
        var added = new List<AdminPhotoItem>(items.Count);

        foreach (var item in items)
        {
            if (Items.Any(existing => existing.Id == item.Id))
            {
                continue;
            }

            Items.Add(item);
            added.Add(item);
        }

        NotifyViewState();
        StartPreviews(added, _generation);
    }

    /// <summary>
    /// Запускает чтение превью для новых строк и не ждёт его: превью — украшение.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Байты читает <see cref="IAdminMediaApi"/>, а не загрузчик картинок платформы: тот
    /// ходит без заголовков и получает <c>404</c> на неодобренный кадр. Ошибка превью
    /// заменяет кадр подложкой и не трогает ни список, ни решения.
    /// </para>
    /// <para>
    /// Одновременно читаются не больше <see cref="PreviewConcurrency"/> кадров, а ответ
    /// применяется, только если список с тех пор не заменили: счётчик поколений тот же,
    /// что у чтения страниц.
    /// </para>
    /// </remarks>
    /// <param name="items">Строки, которым нужны превью.</param>
    /// <param name="generation">Поколение списка, к которому относятся строки.</param>
    private void StartPreviews(IReadOnlyList<AdminPhotoItem> items, int generation)
    {
        if (_mediaApi is null || items.Count == 0)
        {
            return;
        }

        var loads = new Task[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            loads[index] = LoadPreviewAsync(items[index], generation);
        }

        _previews = Task.WhenAll(_previews, Task.WhenAll(loads));
    }

    /// <summary>
    /// Читает превью одной строки — одна попытка, без повтора.
    /// </summary>
    /// <remarks>
    /// Исключения гасятся здесь: превью не должно становиться исключением ни в очереди, ни
    /// в задаче, которую никто не ждёт. Устаревший ответ отбрасывается молча — и до запроса,
    /// и после него, чтобы заменивший список не получил чужие байты.
    /// </remarks>
    /// <param name="item">Строка, которой нужно превью.</param>
    /// <param name="generation">Поколение списка, к которому относится строка.</param>
    /// <returns>Задача чтения.</returns>
    private async Task LoadPreviewAsync(AdminPhotoItem item, int generation)
    {
        if (_mediaApi is not { } mediaApi)
        {
            return;
        }

        await PreviewGate.WaitAsync();

        try
        {
            if (generation != _generation)
            {
                return;
            }

            var bytes = await mediaApi.GetBytesAsync(item.ImageUrl, CancellationToken.None);

            if (generation != _generation)
            {
                return;
            }

            item.ImageBytes = bytes;
            item.ImageFailed = false;
        }
        catch (BarberSearchApiException)
        {
            MarkPreviewFailed(item, generation);
        }
        catch (HttpRequestException)
        {
            MarkPreviewFailed(item, generation);
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это тот же сбой превью.
            MarkPreviewFailed(item, generation);
        }
        finally
        {
            PreviewGate.Release();
        }
    }

    /// <summary>
    /// Помечает кадр «превью не показать», если его список всё ещё на экране.
    /// </summary>
    /// <param name="item">Строка, чьё превью не пришло.</param>
    /// <param name="generation">Поколение списка, к которому относится строка.</param>
    private void MarkPreviewFailed(AdminPhotoItem item, int generation)
    {
        if (generation != _generation)
        {
            return;
        }

        item.ImageBytes = null;
        item.ImageFailed = true;
    }

    /// <summary>
    /// Гасит сессию: пара мертва, страница просит войти снова.
    /// </summary>
    /// <returns>Задача очистки.</returns>
    private async Task EndSessionAsync()
    {
        ApplySignedOutState();

        await _session.ClearAsync(CancellationToken.None);

        SessionEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    /// <param name="sender">Цикл.</param>
    /// <param name="e">Признак изменения.</param>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Переключает строку «Пробуем снова…» в футере вслед за циклом догрузки.
    /// </summary>
    /// <param name="sender">Цикл.</param>
    /// <param name="e">Признак изменения.</param>
    private void OnLoadMoreAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsLoadMoreAutoRetrying = _loadMoreAutoRetry.IsRunning;

    /// <summary>
    /// Поднимает уведомления обо всех вычисляемых свойствах очереди.
    /// </summary>
    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowForbidden));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(ShowLoadMoreError));
        OnPropertyChanged(nameof(HasAnnouncement));
    }

    /// <summary>
    /// Переводит сбой чтения первой страницы в текст, не показывая английский <c>detail</c>.
    /// </summary>
    /// <param name="exception">Исключение из API.</param>
    /// <param name="kind">Вид сбоя.</param>
    /// <returns>Текст для человека.</returns>
    private static string DescribeReadFailure(Exception exception, ApiFailureKind kind)
    {
        if (kind == ApiFailureKind.Transport)
        {
            return NetworkErrorText;
        }

        if (exception is BarberSearchApiException apiException
            && HasCode(apiException, "admin.photos.cursor"))
        {
            return CursorStaleText;
        }

        return AdminErrorText.Read(exception, kind, LoadErrorText);
    }

    /// <summary>
    /// Переводит сбой догрузки в текст, не показывая английский <c>detail</c>.
    /// </summary>
    /// <param name="exception">Исключение из API.</param>
    /// <param name="kind">Вид сбоя.</param>
    /// <returns>Текст для человека.</returns>
    private static string DescribeLoadMoreFailure(Exception exception, ApiFailureKind kind)
    {
        if (exception is BarberSearchApiException apiException
            && HasCode(apiException, "admin.photos.cursor"))
        {
            return CursorStaleText;
        }

        return AdminErrorText.Read(exception, kind, LoadMoreErrorText);
    }

    /// <summary>
    /// Переводит сбой решения в текст веба.
    /// </summary>
    /// <param name="exception">Ответ сервера с ошибкой.</param>
    /// <returns>Текст для человека.</returns>
    private static string DescribeActionFailure(BarberSearchApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.StatusCode == HttpStatusCode.Forbidden
            || HasCode(exception, "auth.forbidden"))
        {
            return ForbiddenText;
        }

        // `user.avatarModeration.noAvatar` — тоже 409, но это не «уже проверено».
        if (HasCode(exception, "user.avatarModeration.noAvatar"))
        {
            return NoAvatarText;
        }

        if (HasCode(exception, "user.notFound"))
        {
            return UserGoneText;
        }

        if (exception.StatusCode == HttpStatusCode.NotFound
            || HasCode(exception, "barberProfile.notFound")
            || HasCode(exception, "portfolioPhoto.moderation.notFound"))
        {
            return PhotoGoneText;
        }

        if (exception.StatusCode == HttpStatusCode.Conflict
            || HasCode(exception, "user.avatarModeration.invalidStatus")
            || HasCode(exception, "portfolioPhoto.moderation.invalidStatus"))
        {
            return AlreadyReviewedText;
        }

        if (HasCode(exception, "user.avatarModeration.reasonRequired")
            || HasCode(exception, "portfolioPhoto.moderation.reasonRequired"))
        {
            return ReasonRequiredText;
        }

        if (HasCode(exception, "user.avatarModeration.reasonTooLong")
            || HasCode(exception, "portfolioPhoto.moderation.reasonTooLong"))
        {
            return ReasonTooLongText;
        }

        if (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return TooManyActionsText;
        }

        return ActionFailedText;
    }

    /// <summary>
    /// Строка уже не ждёт решения: кадр проверили, удалили или владелец пропал.
    /// </summary>
    /// <param name="exception">Ответ сервера с ошибкой.</param>
    /// <returns><c>true</c>, если строку пора убрать и перечитать очередь.</returns>
    private static bool IsStaleDecision(BarberSearchApiException exception) =>
        exception.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound
        || HasCode(exception, "user.avatarModeration.invalidStatus")
        || HasCode(exception, "portfolioPhoto.moderation.invalidStatus")
        || HasCode(exception, "user.avatarModeration.noAvatar")
        || HasCode(exception, "user.notFound")
        || HasCode(exception, "barberProfile.notFound")
        || HasCode(exception, "portfolioPhoto.moderation.notFound");

    /// <summary>
    /// Есть ли у ответа доменный код ошибки.
    /// </summary>
    /// <param name="exception">Ответ сервера с ошибкой.</param>
    /// <param name="code">Искомый код.</param>
    /// <returns><c>true</c>, если код присутствует.</returns>
    private static bool HasCode(BarberSearchApiException exception, string code) =>
        exception.ErrorCodes.Contains(code, StringComparer.Ordinal);

    /// <summary>
    /// Одна страница очереди после сборки строк.
    /// </summary>
    /// <param name="Items">Строки страницы.</param>
    /// <param name="NextCursor">Курсор следующей страницы либо <c>null</c>.</param>
    private sealed record PhotoPage(
        IReadOnlyList<AdminPhotoItem> Items,
        string? NextCursor);

    /// <summary>
    /// Как показывать чтение первой страницы.
    /// </summary>
    private enum FirstPageMode
    {
        /// <summary>
        /// Первое чтение: индикатор вместо списка.
        /// </summary>
        Initial,

        /// <summary>
        /// Тихое чтение после решения и при возврате на страницу.
        /// </summary>
        Silent,

        /// <summary>
        /// Попытка цикла автоповтора: причина ошибки не стирается до ответа.
        /// </summary>
        Retry,
    }

    /// <summary>
    /// Как показывать догрузку.
    /// </summary>
    private enum LoadMoreMode
    {
        /// <summary>
        /// Обычная догрузка по прокрутке или кнопке «Повторить».
        /// </summary>
        Normal,

        /// <summary>
        /// Тихая попытка цикла: причина и строки остаются до ответа.
        /// </summary>
        Retry,
    }
}
