using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.Admin;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Admin;

/// <summary>
/// Хаб администрирования: очереди модерации одной страницей.
/// </summary>
/// <remarks>
/// <para>
/// Это мобильная замена рейлу веба (<c>admin-rail.tsx</c>) и экрану «Сводка»
/// (<c>summary-screen.tsx</c>) сразу. Веб раскладывает одно и то же дважды: рейл показывает
/// разделы со счётчиками, сводка — блоки с теми же числами и подсказками. На телефоне
/// два экрана про одно и то же — это лишний переход, поэтому здесь один список: строка
/// и есть раздел, число ждущих и есть бейдж, а пояснение к очереди живёт второй строкой.
/// Наведения, из которого веб достаёт подсказку рейла, на телефоне нет, поэтому подсказка
/// видна всегда.
/// </para>
/// <para>
/// Числа считает сервер (<c>GET api/admin/summary</c>) теми же предикатами, что фильтры
/// разделов: сводка не может разойтись с очередью. Данные перечитываются при каждом
/// появлении страницы — возврат из раздела и есть обновление после действия, — а таймера
/// нет: хаб не меняется сам по себе, как меняется список визитов.
/// </para>
/// <para>
/// Состояния: первая загрузка, сбой с повтором, отказ в доступе и конец сессии. Отказ не
/// повторяется — роль не появится от повтора, — а конец сессии возвращает на «Профиль»,
/// где стоит форма входа.
/// </para>
/// </remarks>
public sealed partial class AdminHubViewModel : ObservableObject
{
    /// <summary>
    /// Как читается снимок: он один, а показывается по-разному.
    /// </summary>
    private enum LoadMode
    {
        /// <summary>
        /// Первое чтение: экран показывает индикатор вместо содержимого.
        /// </summary>
        Initial,

        /// <summary>
        /// Тихое чтение при возврате на страницу и из цикла автоповтора.
        /// </summary>
        Silent,

        /// <summary>
        /// Свайп вниз: свой индикатор обновления, содержимое остаётся.
        /// </summary>
        Refresh,
    }

    private static string LoadErrorText => Loc.T("Не удалось загрузить сводку");

    private readonly IAdminSummaryApi _summaryApi;
    private readonly TokenSession _session;
    private readonly AutoRetryLoop _autoRetry;
    private readonly TimeProvider _timeProvider;

    private readonly AdminSectionItem _reviews;
    private readonly AdminSectionItem _photos;
    private readonly AdminSectionItem _masters;
    private readonly AdminSectionItem _requests;

    private ApiFailureKind _failureKind;
    private int _generation;
    private bool _isPageVisible;
    private bool _hasLoaded;

    /// <summary>
    /// Создаёт хаб.
    /// </summary>
    /// <param name="summaryApi">Чтение сводки.</param>
    /// <param name="session">Сессия: конец сессии при <c>401</c>.</param>
    /// <param name="timeProvider">Часы: ожидание в очереди считается от них.</param>
    /// <param name="autoRetry">Цикл повтора; в тестах подменяется расписанием без ожидания.</param>
    public AdminHubViewModel(
        IAdminSummaryApi summaryApi,
        TokenSession session,
        TimeProvider timeProvider,
        AutoRetryLoop? autoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(summaryApi);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _summaryApi = summaryApi;
        _session = session;
        _timeProvider = timeProvider;

        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;

        _reviews = new AdminSectionItem(AdminRoutes.Reviews, Loc.T("Отзывы"));
        _photos = new AdminSectionItem(AdminRoutes.Photos, Loc.T("Фото"));
        _masters = new AdminSectionItem(AdminRoutes.Masters, Loc.T("Мастера"));
        _requests = new AdminSectionItem(AdminRoutes.Requests, Loc.T("Заявки"));

        Sections = [_reviews, _photos, _masters, _requests];
    }

    /// <summary>
    /// Разделы администрирования в постоянном порядке: от самой частой очереди к самой редкой.
    /// </summary>
    /// <remarks>
    /// Коллекция создаётся один раз: снимок обновляет строки на месте. Пересборка списка
    /// сбросила бы прокрутку и мигала бы при каждом возврате на страницу.
    /// </remarks>
    public ObservableCollection<AdminSectionItem> Sections { get; }

    /// <summary>
    /// Первая загрузка: индикатор вместо списка.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstLoading;

    /// <summary>
    /// Свайп вниз: спиннер обновления, содержимое остаётся на месте.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Текст сбоя чтения; <c>null</c>, если снимок есть.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Идёт тихий повтор: под текстом сбоя видно «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

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
    /// Время снимка: «Обновлено в 14:32»; <c>null</c>, пока снимка нет.
    /// </summary>
    [ObservableProperty]
    private string? _updatedAtText;

    /// <summary>
    /// Есть ли время снимка.
    /// </summary>
    public bool HasUpdatedAt => UpdatedAtText is not null;

    /// <summary>
    /// Список разделов виден.
    /// </summary>
    public bool ShowContent =>
        !IsFirstLoading && !ShowError && !IsForbidden && !IsSignInRequired;

    /// <summary>
    /// Сбой чтения виден: причина и «Повторить».
    /// </summary>
    /// <remarks>
    /// Причина не гаснет ни при тихом повторе, ни при возврате на страницу: сбой снимает только
    /// успех. Иначе на каждой паузе цикла экран мигал бы — блок ошибки исчезал бы вместе с
    /// причиной, вместо него показывался бы список разделов с пустыми строками, а между
    /// попытками ошибка возвращалась бы. Первая загрузка закрывает ошибку своим индикатором:
    /// два блока об одном и том же одновременно не показываются.
    /// </remarks>
    public bool ShowError =>
        ErrorMessage is not null && !IsFirstLoading && !ShowForbidden && !IsSignInRequired;

    /// <summary>
    /// Отказ в доступе виден.
    /// </summary>
    public bool ShowForbidden => IsForbidden && !IsSignInRequired;

    /// <summary>
    /// Просьба войти снова видна.
    /// </summary>
    public bool ShowSignInRequired => IsSignInRequired;

    /// <summary>
    /// Читает снимок: первый раз с индикатором, дальше тихо.
    /// </summary>
    /// <remarks>
    /// Вызывается при каждом появлении страницы. Возврат из раздела после действия — это
    /// и есть «обновить»: отдельного события «очередь изменилась» телефону не нужно.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(_hasLoaded ? LoadMode.Silent : LoadMode.Initial, cancellationToken);

    /// <summary>
    /// Включает автоповтор: страница видна.
    /// </summary>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartAutoRetry();
    }

    /// <summary>
    /// Выключает автоповтор: страница скрыта или открыт раздел.
    /// </summary>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        StopAutoRetryLoop();
    }

    /// <summary>
    /// Гасит сессию: пара мертва, страница вернёт на «Профиль» с формой входа.
    /// </summary>
    public void ApplySignedOutState()
    {
        StopAutoRetryLoop();

        IsSignInRequired = true;

        NotifyViewState();
    }

    /// <summary>
    /// Цикл повтора для тестов: проверка «повтор идёт» без ожидания расписания.
    /// </summary>
    internal AutoRetryLoop AutoRetry => _autoRetry;

    /// <summary>
    /// Завершение цикла повтора для тестов.
    /// </summary>
    internal Task AutoRetryCompletion => _autoRetry.Completion;

    /// <summary>
    /// Свайп вниз: перечитать снимок, оставив список на месте.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync(LoadMode.Refresh, CancellationToken.None);

    /// <summary>
    /// «Повторить» после сбоя: остановить фон и прочитать снова с индикатором.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RetryAsync()
    {
        StopAutoRetryLoop();

        await LoadAsync(LoadMode.Initial, CancellationToken.None);
    }

    /// <summary>
    /// Читает снимок и раскладывает его по строкам разделов.
    /// </summary>
    /// <param name="mode">Как показывать чтение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему решает цикл автоповтора.</returns>
    private async Task<ApiFailureKind> LoadAsync(LoadMode mode, CancellationToken cancellationToken)
    {
        _generation++;

        var generation = _generation;
        var kind = ApiFailureKind.None;

        if (mode == LoadMode.Initial)
        {
            IsFirstLoading = true;
        }

        if (mode == LoadMode.Refresh)
        {
            IsRefreshing = true;
        }

        NotifyViewState();

        try
        {
            var summary = await _summaryApi
                .GetSummaryAsync(cancellationToken);

            // Устаревший ответ (страница успела прочитать снимок заново) не применяется:
            // иначе поздний ответ вернул бы старые числа поверх новых.
            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            ApplySummary(summary);

            _hasLoaded = true;
            _failureKind = ApiFailureKind.None;
            ErrorMessage = null;
            IsForbidden = false;
            IsSignInRequired = false;
            UpdatedAtText = AdminText.UpdatedAt(_timeProvider.GetUtcNow());
        }
        catch (Exception exception)
        {
            kind = ApiFailureClassifier.Classify(exception, cancellationToken);

            if (kind != ApiFailureKind.None)
            {
                await ApplyFailureAsync(exception, kind, generation);
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsFirstLoading = false;
                IsRefreshing = false;

                NotifyViewState();
                TryStartAutoRetry();
            }
        }

        return kind;
    }

    /// <summary>
    /// Раскладывает сбой по состояниям экрана.
    /// </summary>
    /// <param name="exception">Исключение из API.</param>
    /// <param name="kind">Вид сбоя.</param>
    /// <param name="generation">Поколение чтения.</param>
    /// <returns>Задача обработки.</returns>
    private async Task ApplyFailureAsync(Exception exception, ApiFailureKind kind, int generation)
    {
        if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Unauthorized })
        {
            ApplySignedOutState();

            await _session.ClearAsync(CancellationToken.None);

            return;
        }

        if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Forbidden })
        {
            IsForbidden = true;
            ErrorMessage = null;
            UpdatedAtText = null;

            NotifyViewState();

            return;
        }

        if (generation != _generation)
        {
            return;
        }

        ErrorMessage = AdminErrorText.Read(exception, kind, LoadErrorText);
        _failureKind = kind;

        NotifyViewState();
    }

    /// <summary>
    /// Переносит снимок в строки разделов.
    /// </summary>
    /// <remarks>
    /// Слова те же, что у веба: «очередь пуста — все отзывы проверены», «все профили
    /// подтверждены». Пустая очередь не прячется: раздел открывают и тогда, когда ждущих нет,
    /// чтобы посмотреть уже решённое.
    /// </remarks>
    /// <param name="summary">Снимок сводки.</param>
    private void ApplySummary(AdminSummaryResponse summary)
    {
        var now = _timeProvider.GetUtcNow();
        var reviews = summary.Reviews.PendingCount;

        _reviews.Apply(
            reviews,
            reviews == 0
                ? Loc.T("Очередь пуста — все отзывы проверены.")
                : Loc.T("{0} на модерации", Loc.Plural(PluralKey.Reviews, reviews)),
            summary.Reviews.Oldest is { } oldest
                ? AdminText.OldestReview(oldest.AwaitingSince, now)
                : null);

        var avatars = summary.Photos.PendingAvatarsCount;
        var portfolio = summary.Photos.PendingPortfolioCount;

        _photos.Apply(
            avatars + portfolio,
            avatars + portfolio == 0
                ? Loc.T("Все фотографии проверены.")
                : Loc.T(
                    "На модерации: аватары — {0}, портфолио — {1}",
                    avatars,
                    portfolio),
            null);

        var unverified = summary.Barbers.UnverifiedCount;

        _masters.Apply(
            unverified,
            unverified == 0
                ? Loc.T("Все профили подтверждены.")
                : Loc.T("{0} без подтверждения", Loc.Plural(PluralKey.Profiles, unverified)),
            null);

        var requests = summary.BarberRequests.PendingCount;

        _requests.Apply(
            requests,
            requests == 0
                ? Loc.T("Новых заявок нет.")
                : Loc.T("{0} в очереди", Loc.Plural(PluralKey.Requests, requests)),
            null);
    }

    /// <summary>
    /// Запускает цикл повтора, если страница видна, сбой повторяемый и ничего не грузится.
    /// </summary>
    private void TryStartAutoRetry()
    {
        if (!_isPageVisible || IsFirstLoading || IsSignInRequired || IsForbidden)
        {
            return;
        }

        if (ErrorMessage is null || !_failureKind.IsRetryable())
        {
            return;
        }

        _autoRetry.Start(cancellationToken => LoadAsync(LoadMode.Silent, cancellationToken));
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
    /// Поднимает уведомления обо всех вычисляемых свойствах экрана.
    /// </summary>
    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowForbidden));
        OnPropertyChanged(nameof(ShowSignInRequired));
        OnPropertyChanged(nameof(HasUpdatedAt));
    }
}
