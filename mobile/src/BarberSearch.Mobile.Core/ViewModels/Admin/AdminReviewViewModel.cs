using System.Globalization;
using System.Net;
using BarberSearch.Contracts.Reviews.Admin;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels.Admin;

/// <summary>
/// Карточка отзыва: весь контекст, который показывает веб, и решение модератора.
/// </summary>
/// <remarks>
/// <para>
/// Карточка — «прочитал и решил»: сначала оценка, текст, клиент, мастер и визит, потом
/// действия в нижней полосе. Решение не смешивается со строкой очереди: там его пришлось бы
/// подтверждать вслепую, не видя текста.
/// </para>
/// <para>
/// Публикация и скрытие — записи: они не повторяются. Ошибка остаётся на экране, чтобы
/// человек видел, что решение не применилось; успех возвращает на очередь, и она
/// перечитывается при появлении. <c>404</c> — не ошибка: отзыв уже разобрали, и для
/// администратора это тот же результат, что успех.
/// </para>
/// <para>
/// Скрытие открывает панель причины прямо над нижней полосой — замену модального диалога веба.
/// Сервер причину не принимает (<c>HideReviewCommand</c> её не имеет), поэтому подтверждение
/// доступно сразу, а поле остаётся необязательным шагом подтверждения.
/// </para>
/// <para>
/// Состояния: первая загрузка, сбой с повтором, отказ в доступе, конец сессии и «отзыв уже
/// разобрали». Отказ не повторяется, а конец сессии возвращает на «Профиль».
/// </para>
/// </remarks>
public sealed partial class AdminReviewViewModel : ObservableObject
{
    private static string LoadErrorText => Loc.T("Не удалось загрузить отзыв");
    private static string PublishErrorText => Loc.T("Не удалось опубликовать отзыв. Попробуйте ещё раз.");
    private static string HideErrorText => Loc.T("Не удалось скрыть отзыв. Попробуйте ещё раз.");

    private readonly IAdminReviewsApi _reviewsApi;
    private readonly TokenSession _session;
    private readonly AutoRetryLoop _autoRetry;

    private Guid _reviewId;
    private int _generation;
    private bool _isPageVisible;
    private bool _hasLoaded;
    private ApiFailureKind _failureKind;

    /// <summary>
    /// Создаёт карточку.
    /// </summary>
    /// <param name="reviewsApi">Чтение отзыва и решение модератора.</param>
    /// <param name="session">Сессия: конец сессии при <c>401</c>.</param>
    /// <param name="autoRetry">Цикл повтора чтения; в тестах — расписание без ожидания.</param>
    public AdminReviewViewModel(
        IAdminReviewsApi reviewsApi,
        TokenSession session,
        AutoRetryLoop? autoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(reviewsApi);
        ArgumentNullException.ThrowIfNull(session);

        _reviewsApi = reviewsApi;
        _session = session;

        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;
    }

    /// <summary>
    /// Первая загрузка: индикатор вместо карточки.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstLoading = true;

    /// <summary>
    /// Цикл сам повторяет чтение: под ошибкой видна строка «Пробуем снова…».
    /// </summary>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Текст сбоя чтения; <c>null</c>, если карточка есть.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Сервер ответил <c>403</c>: роль не администратор, карточка не показывается.
    /// </summary>
    [ObservableProperty]
    private bool _isForbidden;

    /// <summary>
    /// Сервер ответил <c>401</c>: пары токенов больше нет, нужен вход.
    /// </summary>
    [ObservableProperty]
    private bool _isSignInRequired;

    /// <summary>
    /// Отзыв уже разобрали: сервер ответил <c>404</c>.
    /// </summary>
    [ObservableProperty]
    private bool _isNotFound;

    /// <summary>
    /// Идёт публикация: кнопка подписана «Публикуем…».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActing))]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(PublishButtonText))]
    private bool _isPublishing;

    /// <summary>
    /// Идёт скрытие: кнопка подписана «Скрываем…».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActing))]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(HideButtonText))]
    private bool _isHiding;

    /// <summary>
    /// Ошибка решения: остаётся на экране, потому что отзыв никуда не делся.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionError))]
    private string? _actionError;

    /// <summary>
    /// Панель причины скрытия открыта.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotReasonPanelOpen))]
    private bool _isReasonPanelOpen;

    /// <summary>
    /// Причина скрытия: необязательное поле подтверждения.
    /// </summary>
    [ObservableProperty]
    private string _hideReason = string.Empty;

    /// <summary>
    /// Оценка от 1 до 5.
    /// </summary>
    [ObservableProperty]
    private int _rating;

    /// <summary>
    /// Звёзды одной строкой: «★★★★☆».
    /// </summary>
    [ObservableProperty]
    private string _stars = string.Empty;

    /// <summary>
    /// Оценка числом: «4 / 5».
    /// </summary>
    [ObservableProperty]
    private string _ratingText = string.Empty;

    /// <summary>
    /// Опубликован ли отзыв; <c>false</c> — «на модерации».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPublish))]
    [NotifyPropertyChangedFor(nameof(ModerationHint))]
    private bool _isPublished;

    /// <summary>
    /// Подпись статуса: «Опубликован» или «На модерации».
    /// </summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// Текст отзыва как есть.
    /// </summary>
    [ObservableProperty]
    private string _reviewBody = string.Empty;

    /// <summary>
    /// У отзыва есть комментарий; <c>false</c> — показывается подпись оценки без текста.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReviewTextEmpty))]
    private bool _hasReviewText;

    /// <summary>
    /// Имя мастера из ответа.
    /// </summary>
    [ObservableProperty]
    private string _barberName = string.Empty;

    /// <summary>
    /// Флаги профиля мастера: «Не подтверждён», «Скрыт из поиска» через разделитель.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBarberFlags))]
    private string? _barberFlagsText;

    /// <summary>
    /// Полное имя клиента.
    /// </summary>
    [ObservableProperty]
    private string _customerName = string.Empty;

    /// <summary>
    /// Email клиента.
    /// </summary>
    [ObservableProperty]
    private string _customerEmail = string.Empty;

    /// <summary>
    /// Визит одной строкой: момент, услуга, салон и цена.
    /// </summary>
    [ObservableProperty]
    private string _visitLine = string.Empty;

    /// <summary>
    /// Когда отзыв оставлен.
    /// </summary>
    [ObservableProperty]
    private string _createdAtText = string.Empty;

    /// <summary>
    /// Когда отзыв последний раз менялся; <c>null</c> — не менялся.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdatedAt))]
    private string? _updatedAtText;

    /// <summary>
    /// Сводка рейтинга мастера: «Сейчас у мастера 4,8, 12 отзывов.».
    /// </summary>
    [ObservableProperty]
    private string _barberSummary = string.Empty;

    /// <summary>
    /// Что произойдёт с рейтингом после решения.
    /// </summary>
    [ObservableProperty]
    private string _moderationHint = string.Empty;

    /// <summary>
    /// Карточка видна.
    /// </summary>
    public bool ShowContent =>
        !IsFirstLoading && !ShowError && !ShowForbidden && !ShowSignInRequired && !ShowNotFound;

    /// <summary>
    /// Сбой чтения виден: причина и «Повторить».
    /// </summary>
    public bool ShowError =>
        ErrorMessage is not null && !ShowForbidden && !ShowSignInRequired && !ShowNotFound;

    /// <summary>
    /// Отказ в доступе виден.
    /// </summary>
    public bool ShowForbidden => IsForbidden && !IsSignInRequired;

    /// <summary>
    /// Просьба войти снова видна.
    /// </summary>
    public bool ShowSignInRequired => IsSignInRequired;

    /// <summary>
    /// «Отзыв уже разобрали» видно.
    /// </summary>
    public bool ShowNotFound => IsNotFound && !IsSignInRequired;

    /// <summary>
    /// Кнопка публикации видна: у опубликованного отзыва публиковать нечего.
    /// </summary>
    public bool ShowPublish => ShowContent && !IsPublished;

    /// <summary>
    /// Идёт любое решение: кнопки выключены, повторное нажатие ничего не делает.
    /// </summary>
    public bool IsActing => IsPublishing || IsHiding;

    /// <summary>
    /// Решение можно принять.
    /// </summary>
    public bool CanAct => ShowContent && !IsActing;

    /// <summary>
    /// Подпись кнопки публикации: «Опубликовать» либо «Публикуем…».
    /// </summary>
    public string PublishButtonText =>
        IsPublishing ? Loc.T("Публикуем…") : Loc.T("Опубликовать");

    /// <summary>
    /// Подпись кнопки скрытия: «Скрыть» либо «Скрываем…».
    /// </summary>
    public string HideButtonText => IsHiding ? Loc.T("Скрываем…") : Loc.T("Скрыть");

    /// <summary>
    /// Панель причины закрыта: видна полоса решений.
    /// </summary>
    public bool IsNotReasonPanelOpen => !IsReasonPanelOpen;

    /// <summary>
    /// Текст отзыва отсутствует: показывается подпись оценки без комментария.
    /// </summary>
    public bool IsReviewTextEmpty => !HasReviewText;

    /// <summary>
    /// У мастера есть флаги профиля.
    /// </summary>
    public bool HasBarberFlags => !string.IsNullOrEmpty(BarberFlagsText);

    /// <summary>
    /// Ошибка решения видна.
    /// </summary>
    public bool HasActionError => !string.IsNullOrEmpty(ActionError);

    /// <summary>
    /// У отзыва есть дата правки.
    /// </summary>
    public bool HasUpdatedAt => UpdatedAtText is not null;

    /// <summary>
    /// Читает карточку отзыва один раз за визит страницы.
    /// </summary>
    /// <param name="reviewId">Идентификатор отзыва из маршрута.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача чтения.</returns>
    public async Task InitializeAsync(Guid reviewId, CancellationToken cancellationToken = default)
    {
        if (_hasLoaded || reviewId == Guid.Empty)
        {
            return;
        }

        _reviewId = reviewId;
        _hasLoaded = true;

        await LoadAsync(LoadMode.Initial, cancellationToken);
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
    /// Выключает автоповтор: страница скрыта или идёт переход.
    /// </summary>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        _autoRetry.Stop();
    }

    /// <summary>
    /// Сбрасывает экран после конца сессии.
    /// </summary>
    public void ApplySignedOutState()
    {
        _autoRetry.Stop();

        _generation++;
        _failureKind = ApiFailureKind.None;

        IsSignInRequired = true;
        IsFirstLoading = false;
        IsForbidden = false;
        IsNotFound = false;
        IsPublishing = false;
        IsHiding = false;
        IsReasonPanelOpen = false;
        ErrorMessage = null;
        ActionError = null;

        NotifyViewState();
    }

    /// <summary>
    /// Цикл повтора чтения для тестов.
    /// </summary>
    internal AutoRetryLoop AutoRetry => _autoRetry;

    /// <summary>
    /// Завершение цикла повтора для тестов.
    /// </summary>
    internal Task AutoRetryCompletion => _autoRetry.Completion;

    /// <summary>
    /// «Повторить» после сбоя чтения: остановить фон и прочитать снова с индикатором.
    /// </summary>
    /// <returns>Задача чтения.</returns>
    [RelayCommand]
    private async Task RetryAsync()
    {
        _autoRetry.Stop();

        await LoadAsync(LoadMode.Initial, CancellationToken.None);
    }

    /// <summary>
    /// Открывает панель причины скрытия.
    /// </summary>
    /// <remarks>
    /// Причина серверу не уходит, поэтому панель не блокирует подтверждение: она заменяет
    /// модальное подтверждение веба и оставляет причину человеку, а не базе.
    /// </remarks>
    [RelayCommand]
    private void OpenHidePanel()
    {
        HideReason = string.Empty;
        ActionError = null;
        IsReasonPanelOpen = true;
    }

    /// <summary>
    /// Закрывает панель причины без записи.
    /// </summary>
    [RelayCommand]
    private void CancelHidePanel() => IsReasonPanelOpen = false;

    /// <summary>
    /// Публикует отзыв.
    /// </summary>
    /// <remarks>
    /// Возвращает текст ошибки либо <c>null</c>, когда решение применилось или отзыв уже
    /// разобрали. Успех страница распознаёт по <c>null</c> и уходит назад: очередь
    /// перечитывается при появлении.
    /// </remarks>
    /// <returns>Причина ошибки для показа либо <c>null</c>.</returns>
    public async Task<string?> PublishAsync()
    {
        if (IsActing || !ShowContent)
        {
            return null;
        }

        IsPublishing = true;
        ActionError = null;

        try
        {
            await _reviewsApi.PublishReviewAsync(_reviewId);

            return null;
        }
        catch (BarberSearchApiException exception)
            when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();

            return null;
        }
        catch (BarberSearchApiException exception)
            when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Отзыв уже разобрали — например, второй администратор: решение применено не нами,
            // но результат тот же, и очередь сейчас обновится.
            return null;
        }
        catch (BarberSearchApiException exception)
        {
            ActionError = AdminErrorText.Write(exception, PublishErrorText);

            return ActionError;
        }
        catch (HttpRequestException)
        {
            ActionError = AdminErrorText.Network;

            return ActionError;
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой при живом токене: для человека это нет связи.
            ActionError = AdminErrorText.Network;

            return ActionError;
        }
        finally
        {
            IsPublishing = false;
        }
    }

    /// <summary>
    /// Скрывает отзыв после подтверждения на странице.
    /// </summary>
    /// <remarks>
    /// Возвращает текст ошибки либо <c>null</c>, когда решение применилось или отзыв уже
    /// разобрали. Причина не отправляется: серверная команда скрытия её не принимает.
    /// </remarks>
    /// <returns>Причина ошибки для показа либо <c>null</c>.</returns>
    public async Task<string?> HideAsync()
    {
        if (IsActing || !ShowContent)
        {
            return null;
        }

        IsHiding = true;
        ActionError = null;

        try
        {
            await _reviewsApi.HideReviewAsync(_reviewId);

            IsReasonPanelOpen = false;

            return null;
        }
        catch (BarberSearchApiException exception)
            when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();

            return null;
        }
        catch (BarberSearchApiException exception)
            when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            IsReasonPanelOpen = false;

            return null;
        }
        catch (BarberSearchApiException exception)
        {
            ActionError = AdminErrorText.Write(exception, HideErrorText);

            return ActionError;
        }
        catch (HttpRequestException)
        {
            ActionError = AdminErrorText.Network;

            return ActionError;
        }
        catch (OperationCanceledException)
        {
            ActionError = AdminErrorText.Network;

            return ActionError;
        }
        finally
        {
            IsHiding = false;
        }
    }

    /// <summary>
    /// Читает карточку и раскладывает ответ по свойствам экрана.
    /// </summary>
    /// <param name="mode">Показать индикатор, повторить в фоне или прочитать впервые.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private async Task<ApiFailureKind> LoadAsync(LoadMode mode, CancellationToken cancellationToken)
    {
        _generation++;

        var generation = _generation;
        var kind = ApiFailureKind.None;

        if (mode != LoadMode.Retry)
        {
            ErrorMessage = null;
            _failureKind = ApiFailureKind.None;
            IsNotFound = false;
        }

        if (mode == LoadMode.Initial)
        {
            IsFirstLoading = true;
        }

        NotifyViewState();

        try
        {
            var review = await _reviewsApi.GetReviewAsync(_reviewId, cancellationToken);

            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            Apply(review);

            _failureKind = ApiFailureKind.None;
            ErrorMessage = null;
            ActionError = null;
            IsForbidden = false;
            IsSignInRequired = false;
            IsNotFound = false;

            NotifyViewState();
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

                NotifyViewState();
                TryStartAutoRetry();
            }
        }

        return kind;
    }

    /// <summary>
    /// Раскладывает сбой чтения по состояниям экрана.
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

        if (generation != _generation)
        {
            return;
        }

        if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.Forbidden })
        {
            IsForbidden = true;
            ErrorMessage = null;

            NotifyViewState();

            return;
        }

        if (exception is BarberSearchApiException { StatusCode: HttpStatusCode.NotFound })
        {
            // Терминальное состояние, а не ошибка: повторять 404 нечего, и «Повторить» здесь
            // не поможет — отзыв уже разобрали.
            IsNotFound = true;
            ErrorMessage = null;

            NotifyViewState();

            return;
        }

        ErrorMessage = AdminErrorText.Read(exception, kind, LoadErrorText);
        _failureKind = kind;

        NotifyViewState();
    }

    /// <summary>
    /// Одна попытка цикла: перечитать карточку, не гася ошибку до ответа.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё попытка.</returns>
    private Task<ApiFailureKind> RetryReadAsync(CancellationToken cancellationToken)
    {
        if (ErrorMessage is null || !_failureKind.IsRetryable())
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadAsync(LoadMode.Retry, cancellationToken);
    }

    /// <summary>
    /// Запускает цикл повтора, если страница видна, сбой повторяемый и ничего не грузится.
    /// </summary>
    private void TryStartAutoRetry()
    {
        if (!_isPageVisible || IsFirstLoading || IsSignInRequired || IsForbidden || IsNotFound)
        {
            return;
        }

        if (ErrorMessage is null || !_failureKind.IsRetryable())
        {
            return;
        }

        _autoRetry.Start(RetryReadAsync);
    }

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Переносит ответ API в свойства карточки.
    /// </summary>
    /// <param name="review">Отзыв из админского чтения.</param>
    private void Apply(AdminReviewResponse review)
    {
        ArgumentNullException.ThrowIfNull(review);

        Rating = review.Rating;
        IsPublished = review.IsPublished;
        Stars = ReviewText.Stars(review.Rating);
        RatingText = ReviewText.Rating(review.Rating);
        StatusText = ReviewText.Status(review.IsPublished);
        ReviewBody = review.Text?.Trim() ?? string.Empty;
        HasReviewText = !string.IsNullOrWhiteSpace(review.Text);

        BarberName = review.Barber.DisplayName;

        var flags = new List<string>(2);

        if (!review.Barber.IsVerified)
        {
            flags.Add(Loc.T("Не подтверждён"));
        }

        if (!review.Barber.IsActive)
        {
            flags.Add(Loc.T("Скрыт из поиска"));
        }

        BarberFlagsText = flags.Count == 0 ? null : string.Join(" · ", flags);

        CustomerName = review.Customer.FullName;
        CustomerEmail = review.Customer.Email;

        var timeZone = SalonClock.TryResolve(review.Visit.TimeZoneId);

        VisitLine = string.Join(
            " · ",
            new[]
            {
                ReviewText.VisitMoment(review.Visit.StartAt, timeZone),
                review.Visit.ServiceName,
                review.Visit.SalonName,
                BarberText.Price(review.Visit.Price),
            }.Where(part => !string.IsNullOrWhiteSpace(part)));

        CreatedAtText = Moment(review.CreatedAt);
        UpdatedAtText = review.UpdatedAt == review.CreatedAt
            ? null
            : Moment(review.UpdatedAt);

        BarberSummary = review.Barber.Rating is { } rating
            ? Loc.T(
                "Сейчас у мастера {0}, {1}.",
                BarberText.Rating(rating),
                Loc.Plural(PluralKey.Reviews, review.Barber.ReviewsCount))
            : Loc.T("У мастера пока нет опубликованных отзывов.");

        ModerationHint = review.IsPublished
            ? Loc.T("После скрытия рейтинг пересчитается.")
            : Loc.T("После публикации рейтинг пересчитается.");
    }

    /// <summary>
    /// Служебный момент в поясе устройства: «15 ноября 2026 в 18:30».
    /// </summary>
    /// <remarks>
    /// Год показывается всегда: отзыв живёт дольше года, и без года «15 ноября в 18:30»
    /// читалось бы как дата этого года. Формат повторяет <c>formatDateTime</c> веба.
    /// </remarks>
    private static string Moment(DateTimeOffset instant)
    {
        var local = instant.ToLocalTime();

        return Loc.T(
            "{0} в {1}",
            local.ToString("d MMMM yyyy", Loc.Culture),
            local.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Гасит сессию: пара мертва, страница вернёт на «Профиль».
    /// </summary>
    private async Task EndSessionAsync()
    {
        ApplySignedOutState();

        await _session.ClearAsync(CancellationToken.None);
    }

    /// <summary>
    /// Поднимает уведомления обо всех вычисляемых свойствах экрана.
    /// </summary>
    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowForbidden));
        OnPropertyChanged(nameof(ShowSignInRequired));
        OnPropertyChanged(nameof(ShowNotFound));
        OnPropertyChanged(nameof(ShowPublish));
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(IsNotReasonPanelOpen));
        OnPropertyChanged(nameof(IsReviewTextEmpty));
        OnPropertyChanged(nameof(HasBarberFlags));
        OnPropertyChanged(nameof(HasUpdatedAt));
    }

    /// <summary>
    /// Как показывается чтение карточки.
    /// </summary>
    private enum LoadMode
    {
        /// <summary>
        /// Первое чтение: индикатор вместо карточки.
        /// </summary>
        Initial,

        /// <summary>
        /// Попытка цикла: ошибка не стирается до ответа.
        /// </summary>
        Retry,
    }
}
