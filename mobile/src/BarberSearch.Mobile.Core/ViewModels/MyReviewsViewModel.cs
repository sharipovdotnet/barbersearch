using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.Reviews.GetMyReviews;
using BarberSearch.Contracts.Reviews.UpdateReview;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Reviews;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Экран «Мои отзывы»: один список из опубликованных и ожидающих проверки.
/// </summary>
/// <remarks>
/// <para>
/// Список читается один раз за визит страницы: <c>GET api/reviews/mine</c> с размером
/// страницы 10, курсор возвращается серверу как есть. Повторно список обновляется только
/// свайпом; прокрутка догружает следующую страницу, фонового опроса нет. Правка и удаление
/// меняют свою строку на месте — полный <c>GET</c> сдвинул бы страницы и прокрутил список.
/// </para>
/// <para>
/// У загрузок есть поколение: ответ, пришедший после обновления или завершения сессии,
/// отбрасывается, иначе запоздавшая страница вернула бы данные прошлого пользователя.
/// </para>
/// <para>
/// Удаление убирает строку; если страница опустела, а курсор есть, следующая догружается
/// сразу. <c>404</c> на удаление — не ошибка: отзыв уже удалён, в том числе с другого
/// устройства, и для списка это тот же результат.
/// </para>
/// <para>
/// Ошибки чтения, пока страница видна, повторяются сами: у первой страницы и у догрузки
/// свои циклы, потому что это два разных чтения с разными снимками. Снимок догрузки — её
/// курсор на момент сбоя: тихая попытка несёт именно его, а не текущий курсор списка. Под
/// причиной при этом видна строка «Пробуем снова…», а «Повторить» остаётся и значит
/// «повторить сейчас». Удаление отзыва — запись: оно не повторяется никогда.
/// </para>
/// </remarks>
public sealed partial class MyReviewsViewModel : ObservableObject
{
    /// <summary>
    /// Размер страницы — как в вебе.
    /// </summary>
    public const int PageSize = 10;

    private static string LoadErrorText => Loc.T("Не удалось загрузить отзывы.");
    private static string LoadMoreErrorText => Loc.T("Не удалось загрузить следующие отзывы.");
    private static string NetworkErrorText => Loc.T(
        "Нет связи с сервером. Проверьте подключение и повторите попытку.");
    private static string TooManyRequestsText => Loc.T(
        "Слишком много запросов. Подождите минуту и повторите.");
    private static string DeletedAnnouncement => Loc.T("Отзыв удалён.");
    private static string AlreadyDeletedAnnouncement => Loc.T("Этот отзыв уже удалён.");
    private static string UpdatedAnnouncement => Loc.T("Отзыв обновлён.");
    private static string DeleteForbiddenText => Loc.T("Удалить отзыв может только его автор.");
    private static string DeleteFailedText => Loc.T("Не удалось удалить отзыв.");

    /// <summary>
    /// Текст пустого состояния.
    /// </summary>
    public static string EmptyTitle => Loc.T("Пока нет отзывов.");

    /// <summary>
    /// Объяснение пустого состояния.
    /// </summary>
    public static string EmptyDescription =>
        Loc.T("Отзыв можно оставить в «Моих записях» после завершённого визита.");

    private readonly IReviewsApi _reviewsApi;
    private readonly TokenSession _session;

    private readonly AutoRetryLoop _autoRetry;
    private readonly AutoRetryLoop _loadMoreAutoRetry;

    private string? _nextCursor;
    private int _generation;

    /// <summary>
    /// Вид последнего сбоя первой страницы: по нему решает цикл автоповтора.
    /// </summary>
    /// <remarks>
    /// Запоминается для каждого неуспеха, а не только для повторяемого: иначе «транспорт,
    /// потом 404» оставил бы в памяти старый <see cref="ApiFailureKind.Transport"/>, и кнопка
    /// снова запустила бы цикл на отказе, который не изменится.
    /// </remarks>
    private ApiFailureKind _firstPageFailureKind;

    /// <summary>
    /// Вид последнего сбоя догрузки: по нему решает второй цикл.
    /// </summary>
    /// <remarks>
    /// Хранится отдельно от вида сбоя первой страницы: у двух чтений разные сбои, и отказ
    /// догрузки не должен ни отменять повтор первой страницы, ни оживлять свой цикл.
    /// </remarks>
    private ApiFailureKind _loadMoreFailureKind;

    /// <summary>
    /// Курсор упавшей догрузки: повтор несёт его, а не текущий курсор списка.
    /// </summary>
    private string? _loadMoreRetryCursor;

    /// <summary>
    /// Страница сейчас видна: циклы автоповтора работают только пока это так.
    /// </summary>
    private bool _isPageVisible;

    /// <summary>
    /// Создаёт экран отзывов.
    /// </summary>
    /// <param name="reviewsApi">Список, правка и удаление отзывов.</param>
    /// <param name="session">Сессия — источник правды о входе.</param>
    /// <param name="autoRetry">
    /// Цикл автоповтора первой страницы; <c>null</c> — собственный цикл с обычным расписанием.
    /// Параметр необязательный, чтобы существующие позиционные вызовы (DI и тесты) не менялись.
    /// </param>
    /// <param name="loadMoreAutoRetry">
    /// Цикл автоповтора догрузки, отдельный от цикла первой страницы; <c>null</c> — собственный
    /// цикл с обычным расписанием. Параметр необязательный и стоит последним, чтобы
    /// существующие позиционные вызовы (DI и тесты) не менялись.
    /// </param>
    public MyReviewsViewModel(
        IReviewsApi reviewsApi,
        TokenSession session,
        AutoRetryLoop? autoRetry = null,
        AutoRetryLoop? loadMoreAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(reviewsApi);
        ArgumentNullException.ThrowIfNull(session);

        _reviewsApi = reviewsApi;
        _session = session;

        _autoRetry = autoRetry ?? new AutoRetryLoop();
        _autoRetry.StateChanged += OnAutoRetryStateChanged;

        _loadMoreAutoRetry = loadMoreAutoRetry ?? new AutoRetryLoop();
        _loadMoreAutoRetry.StateChanged += OnLoadMoreAutoRetryStateChanged;
    }

    /// <summary>
    /// Нажата «Изменить» у строки: страница открывает экран правки.
    /// </summary>
    public event EventHandler<MyReviewListItem>? EditRequested;

    /// <summary>
    /// Строки списка в порядке ответа сервера.
    /// </summary>
    public ObservableCollection<MyReviewListItem> Items { get; } = [];

    /// <summary>
    /// Текст пустого состояния: заголовок.
    /// </summary>
    public string EmptyStateTitle { get; } = EmptyTitle;

    /// <summary>
    /// Текст пустого состояния: объяснение.
    /// </summary>
    public string EmptyStateDescription { get; } = EmptyDescription;

    /// <summary>
    /// Пользователь вошёл.
    /// </summary>
    [ObservableProperty]
    private bool _isAuthenticated;

    /// <summary>
    /// Идёт первая загрузка списка.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
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
    /// Цикл сам повторяет первую страницу: под причиной ошибки видна строка «Пробуем снова…».
    /// </summary>
    /// <remarks>
    /// Состояние цикла, а не экрана: свойство переключается подпиской на
    /// <see cref="AutoRetryLoop.StateChanged"/> и ничего не решает само.
    /// </remarks>
    [ObservableProperty]
    private bool _isAutoRetrying;

    /// <summary>
    /// Цикл сам повторяет догрузку: в футере видна строка «Пробуем снова…».
    /// </summary>
    /// <remarks>
    /// Отдельно от <see cref="IsAutoRetrying"/>: это второй цикл, и его строка в футере не
    /// должна загораться от повтора первой страницы.
    /// </remarks>
    [ObservableProperty]
    private bool _isLoadMoreAutoRetrying;

    /// <summary>
    /// Ошибка первой страницы; <c>null</c>, когда её нет.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private string? _errorMessage;

    /// <summary>
    /// Ошибка догрузки: уже загруженные строки остаются.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadMoreError))]
    private string? _loadMoreError;

    /// <summary>
    /// Первая страница уже загружалась.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasLoaded;

    /// <summary>
    /// Баннер о результате действия.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnnouncement))]
    private string? _announcement;

    /// <summary>
    /// Показывать индикатор первой загрузки.
    /// </summary>
    public bool ShowLoading => IsAuthenticated && IsFirstLoading;

    /// <summary>
    /// Показывать ошибку первой страницы с «Повторить».
    /// </summary>
    public bool ShowError => IsAuthenticated && !IsFirstLoading && ErrorMessage is not null;

    /// <summary>
    /// Показывать список.
    /// </summary>
    public bool ShowList => IsAuthenticated && !ShowError && !IsFirstLoading;

    /// <summary>
    /// Показывать пустое состояние: только когда загрузка закончилась и строк нет.
    /// </summary>
    public bool ShowEmptyState => IsAuthenticated && HasLoaded && !IsFirstLoading && Items.Count == 0;

    /// <summary>
    /// Есть следующая страница.
    /// </summary>
    public bool CanLoadMore => _nextCursor is not null;

    /// <summary>
    /// Показывать ошибку догрузки в футере.
    /// </summary>
    public bool ShowLoadMoreError => LoadMoreError is not null;

    /// <summary>
    /// Виден ли баннер о результате действия.
    /// </summary>
    public bool HasAnnouncement => !string.IsNullOrEmpty(Announcement);

    /// <summary>
    /// Проверяет сессию и читает первую страницу один раз за визит страницы.
    /// </summary>
    /// <remarks>
    /// Повторное появление экрана в рамках сессии ничего не перечитывает: список живёт до
    /// ухода со страницы. Обновить его можно только свайпом — так решение среза не гоняет
    /// сеть на каждый возврат.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _session.EnsureLoadedAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_session.IsAuthenticated)
        {
            ApplySignedOutState();
            return;
        }

        IsAuthenticated = true;

        if (HasLoaded)
        {
            return;
        }

        HasLoaded = true;

        await LoadFirstPageAsync(FirstPageMode.Initial, cancellationToken);
    }

    /// <summary>
    /// Сообщает, что страница видна, и запускает автоповтор ошибок первой страницы и догрузки.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Зовётся страницей при каждом появлении и возврате из фона. Идемпотентен: без ошибки,
    /// при окончательном отказе или уже работающем цикле ничего не делает.
    /// </para>
    /// <para>
    /// Оба цикла поднимаются одним сигналом видимости: у догрузки нет своего жизненного
    /// цикла, а второй подписки на окно быть не должно. Запуск и остановка обязаны идти из
    /// UI-потока: <see cref="AutoRetryLoop.StateChanged"/> синхронно меняет привязанные
    /// <see cref="IsAutoRetrying"/> и <see cref="IsLoadMoreAutoRetrying"/>.
    /// </para>
    /// </remarks>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartAutoRetry();
        TryStartLoadMoreAutoRetry();
    }

    /// <summary>
    /// Сообщает, что страница скрыта, и останавливает оба автоповтора.
    /// </summary>
    /// <remarks>
    /// Идемпотентен. Остановка отменяет паузу и запрос в полёте, а строки «Пробуем снова…»
    /// сразу гаснут: скрытая страница не показывает работающий повтор и не ходит в сеть.
    /// </remarks>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        StopAutoRetryLoop();
        StopLoadMoreAutoRetryLoop();
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
    /// Завершается, когда цикл повтора догрузки останавливается: тесты ждут его вместо пауз.
    /// </summary>
    internal Task LoadMoreAutoRetryCompletion => _loadMoreAutoRetry.Completion;

    /// <summary>
    /// Цикл повтора догрузки: тестам нужен счётчик попыток, приложению — нет.
    /// </summary>
    internal AutoRetryLoop LoadMoreAutoRetry => _loadMoreAutoRetry;

    /// <summary>
    /// Сбрасывает экран после завершения сессии.
    /// </summary>
    /// <remarks>
    /// Поколение сдвигается: ответ, который ещё летит, не вернёт отзывы прошлого пользователя
    /// на экран.
    /// </remarks>
    public void ApplySignedOutState()
    {
        // Выход — это и есть «повторять больше нечего»: циклы могли ждать паузу, пока сессию
        // гасили, и без остановки сходили бы в сеть без токена.
        StopAutoRetryLoop();
        StopLoadMoreAutoRetryLoop();

        _generation++;
        _nextCursor = null;
        _firstPageFailureKind = ApiFailureKind.None;
        _loadMoreFailureKind = ApiFailureKind.None;
        _loadMoreRetryCursor = null;

        IsAuthenticated = false;
        IsFirstLoading = false;
        IsLoadingMore = false;
        IsRefreshing = false;
        HasLoaded = false;
        ErrorMessage = null;
        LoadMoreError = null;
        Announcement = null;

        Items.Clear();

        NotifyViewState();
    }

    /// <summary>
    /// Применяет результат правки: строка меняется на месте, список не перечитывается.
    /// </summary>
    /// <param name="item">Строка, которую правили.</param>
    /// <param name="response">Ответ сервера с новым статусом и временем правки.</param>
    /// <param name="rating">Отправленная оценка.</param>
    /// <param name="text">Отправленный текст; <c>null</c> — без комментария.</param>
    public void ApplyUpdated(
        MyReviewListItem item,
        UpdateReviewResponse response,
        int rating,
        string? text)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(response);

        item.ApplyUpdated(response, rating, text);
        Announcement = UpdatedAnnouncement;
    }

    /// <summary>
    /// Удаляет отзыв после подтверждения на странице.
    /// </summary>
    /// <remarks>
    /// Возвращает текст для нативного диалога либо <c>null</c>, когда всё получилось (или
    /// сессия истекла — тогда страница уже ушла на вкладку «Профиль»). Повторное нажатие
    /// во время запроса игнорируется: строка помечена <see cref="MyReviewListItem.IsDeleting"/>.
    /// </remarks>
    /// <param name="item">Удаляемая строка.</param>
    /// <returns>Причина ошибки для показа либо <c>null</c>.</returns>
    public async Task<string?> DeleteAsync(MyReviewListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.IsDeleting)
        {
            return null;
        }

        item.IsDeleting = true;

        try
        {
            await _reviewsApi.DeleteReviewAsync(item.Id);

            RemoveItem(item);
            Announcement = DeletedAnnouncement;

            await FillEmptiedPageAsync();

            return null;
        }
        catch (BarberSearchApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await EndSessionAsync();

            return null;
        }
        catch (BarberSearchApiException exception)
        {
            if (exception.StatusCode == HttpStatusCode.NotFound)
            {
                // Отзыв уже удалён — например, с другого устройства: для списка это успех.
                RemoveItem(item);
                Announcement = AlreadyDeletedAnnouncement;

                await FillEmptiedPageAsync();

                return null;
            }

            return exception.StatusCode switch
            {
                HttpStatusCode.Forbidden => DeleteForbiddenText,
                HttpStatusCode.TooManyRequests => TooManyRequestsText,
                _ => DeleteFailedText,
            };
        }
        catch (HttpRequestException)
        {
            return NetworkErrorText;
        }
        catch (OperationCanceledException)
        {
            // Таймаут транспорта приходит отменой: для человека это отсутствие связи.
            return NetworkErrorText;
        }
        finally
        {
            item.IsDeleting = false;
        }
    }

    /// <summary>
    /// Обновляет первую страницу свайпом.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsAuthenticated)
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
    /// Догружает следующую страницу.
    /// </summary>
    /// <remarks>
    /// Обычная догрузка: показывает «Идёт догрузка…» и стирает старую ошибку, потому что её
    /// запустил человек прокруткой. Само чтение живёт в <see cref="LoadMorePageAsync"/>: тем же
    /// путём ходят тихая попытка цикла и кнопка «Повторить».
    /// </remarks>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        var cursor = _nextCursor;

        if (!IsAuthenticated || IsFirstLoading || IsLoadingMore || cursor is null)
        {
            return;
        }

        await LoadMorePageAsync(cursor, LoadMoreMode.Normal, CancellationToken.None);
    }

    /// <summary>
    /// Повторяет первую загрузку после ошибки.
    /// </summary>
    /// <remarks>
    /// «Повторить» значит «повторить сейчас»: цикл останавливается приватно (публичный
    /// <see cref="StopAutoRetry"/> ещё и пометил бы страницу скрытой), страница читается без
    /// паузы, а цикл запускается заново только если новый сбой снова повторяемый — это делает
    /// <see cref="SetPageFailure"/>. Успех цикл не запускает: повторять больше нечего.
    /// </remarks>
    [RelayCommand]
    private async Task RetryAsync()
    {
        if (!IsAuthenticated)
        {
            return;
        }

        StopAutoRetryLoop();

        ErrorMessage = null;
        NotifyViewState();

        await LoadFirstPageAsync(FirstPageMode.Initial, CancellationToken.None);

        TryStartAutoRetry();
    }

    /// <summary>
    /// Повторяет догрузку после ошибки, не трогая загруженные строки.
    /// </summary>
    /// <remarks>
    /// Цикл догрузки останавливается, страница читается сразу, а цикл возвращается к жизни
    /// только при новом повторяемом сбое — это делает <see cref="SetLoadMoreFailure"/>.
    /// Нажатие не «оживляет» скрытую страницу: запуск цикла проверяет <c>_isPageVisible</c>.
    /// </remarks>
    [RelayCommand]
    private async Task RetryLoadMoreAsync()
    {
        StopLoadMoreAutoRetryLoop();

        var cursor = _nextCursor;

        if (!IsAuthenticated || IsFirstLoading || cursor is null)
        {
            return;
        }

        await LoadMorePageAsync(cursor, LoadMoreMode.Normal, CancellationToken.None);
    }

    /// <summary>
    /// Закрывает баннер о результате действия.
    /// </summary>
    [RelayCommand]
    private void DismissAnnouncement() => Announcement = null;

    /// <summary>
    /// Просит страницу открыть правку отзыва.
    /// </summary>
    /// <param name="item">Строка списка.</param>
    [RelayCommand]
    private void Edit(MyReviewListItem? item)
    {
        if (item is null)
        {
            return;
        }

        EditRequested?.Invoke(this, item);
    }

    /// <summary>
    /// Читает первую страницу и заменяет список.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Поколение сдвигается на старте: ответ, пришедший после обновления или завершения
    /// сессии, отбрасывается. Ошибка тихого обновления не стирает уже загруженные строки.
    /// </para>
    /// <para>
    /// Режим <see cref="FirstPageMode.Retry"/> — попытка цикла автоповтора: причина ошибки
    /// остаётся на экране до ответа, иначе каждый повтор мигал бы пустым экраном и кнопкой,
    /// которую некуда нажать. Запрос первой страницы не имеет параметров, поэтому снимка у
    /// него нет: повторяемость определяется видом сбоя.
    /// </para>
    /// </remarks>
    /// <param name="mode">Показать индикатор, обновить молча или повторить в фоне.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private async Task<ApiFailureKind> LoadFirstPageAsync(
        FirstPageMode mode,
        CancellationToken cancellationToken)
    {
        _generation++;
        var generation = _generation;

        if (mode != FirstPageMode.Retry)
        {
            ErrorMessage = null;
            LoadMoreError = null;
            _firstPageFailureKind = ApiFailureKind.None;
        }

        if (mode == FirstPageMode.Initial)
        {
            IsFirstLoading = true;
        }

        NotifyViewState();

        var kind = ApiFailureKind.None;

        try
        {
            var page = await _reviewsApi.GetMyReviewsAsync(cursor: null, PageSize, cancellationToken);

            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            Items.Clear();

            foreach (var review in page.Items)
            {
                Items.Add(new MyReviewListItem(review));
            }

            _nextCursor = page.NextCursor;
            IsFirstLoading = false;
            ErrorMessage = null;
            LoadMoreError = null;
            _firstPageFailureKind = ApiFailureKind.None;
            _loadMoreFailureKind = ApiFailureKind.None;
            _loadMoreRetryCursor = null;

            NotifyViewState();
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
                    // 401 окончателен по классификации и вдобавок гасит сессию: возврат на
                    // вкладку «Профиль» важнее любого текста в списке.
                    await EndSessionAsync();
                }
                else
                {
                    SetPageFailure(generation, DescribeFirstPageFailure(exception, kind), kind);
                }
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsFirstLoading = false;
                NotifyViewState();
            }
        }

        return kind;
    }

    /// <summary>
    /// Одна попытка цикла: перечитать первую страницу, не гася ошибку.
    /// </summary>
    /// <remarks>
    /// Условия перепроверяются на момент попытки: пока цикл ждал паузу, список могли
    /// перечитать вручную. Тогда повторять нечего, и цикл останавливается.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    private Task<ApiFailureKind> RetryFirstPageAsync(CancellationToken cancellationToken)
    {
        if (ErrorMessage is null || !_firstPageFailureKind.IsRetryable())
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return LoadFirstPageAsync(FirstPageMode.Retry, cancellationToken);
    }

    /// <summary>
    /// Показывает ошибку страницы, но не стирает уже загруженные строки.
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
        _firstPageFailureKind = kind;

        if (Items.Count == 0)
        {
            ErrorMessage = message;
        }

        NotifyViewState();

        // Сбой пришёл, пока страница видна: цикл может повторить чтение сам. Если он уже
        // работает, Start ничего не сделает — второй цикл удвоил бы запросы.
        TryStartAutoRetry();
    }

    /// <summary>
    /// Запускает цикл первой страницы, если страница видна, сбой повторяемый и ничего не грузится.
    /// </summary>
    /// <remarks>
    /// Отдельный метод нужен потому, что «видна» — сигнал страницы, а не следствие нажатия:
    /// повтор после сбоя запускается и из завершения загрузки, но только пока страница видна.
    /// </remarks>
    private void TryStartAutoRetry()
    {
        if (!_isPageVisible || !IsAuthenticated || ShowLoading)
        {
            return;
        }

        if (ErrorMessage is null || !_firstPageFailureKind.IsRetryable())
        {
            return;
        }

        _autoRetry.Start(RetryFirstPageAsync);
    }

    /// <summary>
    /// Останавливает цикл первой страницы, не трогая признак «страница видна».
    /// </summary>
    /// <remarks>
    /// Нужен кнопке «Повторить»: она останавливает фон, но страница остаётся видимой, и после
    /// ответа цикл имеет право запуститься снова. Публичный <see cref="StopAutoRetry"/>
    /// дополнительно помечает страницу скрытой — для кнопки это неверно.
    /// </remarks>
    private void StopAutoRetryLoop() => _autoRetry.Stop();

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _autoRetry.IsRunning;

    /// <summary>
    /// Читает следующую страницу — обычной догрузкой или тихой попыткой цикла.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Курсор приходит аргументом, а не берётся из текущего <c>_nextCursor</c>: тихая попытка
    /// обязана повторить упавший запрос, даже если список успели обновить. Успех очищает
    /// ошибку; неудача оставляет строки, причину и курсор как были.
    /// </para>
    /// <para>
    /// Оба режима поднимают <see cref="IsLoadingMore"/>: это по-прежнему одно чтение одной
    /// страницы. Поэтому догрузка, которую запустит прокрутка во время тихой попытки, просто
    /// не начнётся — второе чтение того же курсора добавило бы те же строки дважды. Попытка,
    /// которая должна начаться поверх уже идущей обычной догрузки, уступает ей и не делает
    /// запроса вовсе (см. <see cref="RetryLoadMoreAttemptAsync"/>).
    /// </para>
    /// </remarks>
    /// <param name="cursor">Курсор упавшей страницы: повтор несёт именно его.</param>
    /// <param name="mode">Обычная догрузка или тихая попытка цикла.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вид сбоя: по нему второй цикл решает, нужна ли ещё одна попытка.</returns>
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
            var page = await _reviewsApi.GetMyReviewsAsync(cursor, PageSize, cancellationToken);

            if (generation != _generation || _nextCursor != cursor)
            {
                // Пока читали, список перечитали первой страницей или страницу уже догрузила
                // обычная догрузка: ответ устарел. Курсор, снятый на старте, и есть защита от
                // второго добавления той же страницы.
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
                if (exception is BarberSearchApiException
                    {
                        StatusCode: HttpStatusCode.Unauthorized,
                    })
                {
                    await EndSessionAsync();
                }
                else if (generation == _generation && _nextCursor == cursor)
                {
                    SetLoadMoreFailure(cursor, DescribeLoadMoreFailure(exception, kind), kind);
                }
            }
        }
        finally
        {
            // Флаг снимается всегда, а не только при совпавшем поколении: первая страница,
            // перечитанная поверх догрузки, поколение сдвигает, но флаг не трогает — иначе
            // он остался бы поднятым навсегда и догрузка больше не запустилась бы.
            // Тихая попытка поднимала тот же флаг, поэтому снимает его тоже она.
            IsLoadingMore = false;
            NotifyViewState();
        }

        return kind;
    }

    /// <summary>
    /// Одна попытка цикла: повторить упавшую догрузку тем же курсором.
    /// </summary>
    /// <remarks>
    /// Условия перепроверяются на момент попытки: пока цикл ждал паузу, ошибку могли снять
    /// или страницу могли уже догрузить. Тогда повторять нечего, и цикл останавливается —
    /// так терминальный сбой после повторяемого не оживляет цикл.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
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
    /// Показывает ошибку догрузки и запоминает снимок упавшего запроса.
    /// </summary>
    /// <remarks>
    /// Вид сбоя запоминается для каждого неуспеха, а не только для повторяемого: иначе
    /// «транспорт, потом 404» оставил бы в памяти старый <see cref="ApiFailureKind.Transport"/>,
    /// и «Повторить» снова запустила бы цикл на отказе, который не изменится.
    /// </remarks>
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

        // Сбой пришёл, пока страница видна: цикл может повторить догрузку сам. Если он уже
        // работает, Start ничего не сделает — второй цикл удвоил бы запросы.
        TryStartLoadMoreAutoRetry();
    }

    /// <summary>
    /// Запускает цикл догрузки, если страница видна, сбой повторяемый и есть снимок курсора.
    /// </summary>
    /// <remarks>
    /// Снимок — курсор на момент сбоя, а не текущий: обновление списка между попытками не
    /// должно подменить повторяемый запрос. Уступать обычной догрузке цикл умеет сам —
    /// <see cref="RetryLoadMoreAttemptAsync"/> перепроверяет условия.
    /// </remarks>
    private void TryStartLoadMoreAutoRetry()
    {
        if (!_isPageVisible || !IsAuthenticated)
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
    /// Останавливает цикл догрузки, не трогая признак «страница видна».
    /// </summary>
    /// <remarks>
    /// Нужен кнопке «Повторить» в футере: она останавливает фон, но страница остаётся
    /// видимой, и после ответа цикл имеет право запуститься снова. Публичный
    /// <see cref="StopAutoRetry"/> дополнительно пометил бы страницу скрытой.
    /// </remarks>
    private void StopLoadMoreAutoRetryLoop() => _loadMoreAutoRetry.Stop();

    /// <summary>
    /// Переключает строку «Пробуем снова…» в футере вслед за состоянием цикла догрузки.
    /// </summary>
    private void OnLoadMoreAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsLoadMoreAutoRetrying = _loadMoreAutoRetry.IsRunning;

    /// <summary>
    /// Переводит сбой чтения первой страницы в текст, не показывая английский <c>detail</c>.
    /// </summary>
    private static string DescribeFirstPageFailure(Exception exception, ApiFailureKind kind) =>
        kind == ApiFailureKind.Transport
            ? NetworkErrorText
            : exception is BarberSearchApiException apiException
                ? DescribeFailure(apiException, LoadErrorText)
                : LoadErrorText;

    /// <summary>
    /// Переводит сбой догрузки в текст, не показывая английский <c>detail</c>.
    /// </summary>
    /// <remarks>
    /// Таймаут транспорта классификатор уже считает «нет связи»: раньше он приходил
    /// отменой и получал общий текст догрузки, а человек не видел, что ответа не было.
    /// </remarks>
    private static string DescribeLoadMoreFailure(Exception exception, ApiFailureKind kind) =>
        kind == ApiFailureKind.Transport
            ? NetworkErrorText
            : exception is BarberSearchApiException apiException
                ? DescribeFailure(apiException, LoadMoreErrorText)
                : LoadMoreErrorText;

    /// <summary>
    /// Переводит сбой API в текст, не показывая английский <c>detail</c>.
    /// </summary>
    private static string DescribeFailure(BarberSearchApiException exception, string fallback) =>
        exception.StatusCode == HttpStatusCode.TooManyRequests ? TooManyRequestsText : fallback;

    /// <summary>
    /// Догружает следующую страницу, если удаление опустошило список.
    /// </summary>
    /// <remarks>
    /// Пустая страница при наличии курсора — это не конец списка: следующая страница
    /// существует, и показывать пустое состояние значило бы врать. Если курсора нет —
    /// отзывов действительно больше нет.
    /// </remarks>
    private async Task FillEmptiedPageAsync()
    {
        NotifyViewState();

        if (Items.Count == 0 && CanLoadMore)
        {
            await LoadMoreAsync();
        }
    }

    /// <summary>
    /// Убирает строку из списка.
    /// </summary>
    private void RemoveItem(MyReviewListItem item)
    {
        Items.Remove(item);
        NotifyViewState();
    }

    /// <summary>
    /// Дополняет список следующей страницей.
    /// </summary>
    /// <remarks>
    /// Отзыв, который уже есть в списке, не добавляется второй раз. Повтор упавшей догрузки
    /// идёт тем же курсором, и сервер может вернуть ту же строку: без этой проверки повтор
    /// добавил бы дубль и сломал порядок. Обычной догрузке проверка ничего не меняет — в норме
    /// страницы не пересекаются.
    /// </remarks>
    private void AppendItems(IReadOnlyList<MyReviewResponse> reviews)
    {
        foreach (var review in reviews)
        {
            if (Items.Any(item => item.Id == review.Id))
            {
                continue;
            }

            Items.Add(new MyReviewListItem(review));
        }

        NotifyViewState();
    }

    /// <summary>
    /// Гасит сессию: пара мертва, страница вернёт на вкладку «Профиль».
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
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(ShowLoadMoreError));
    }

    /// <summary>
    /// Режим загрузки первой страницы: показать индикатор, обновить молча или повторить в фоне.
    /// </summary>
    private enum FirstPageMode
    {
        Initial,

        Silent,

        /// <summary>
        /// Попытка цикла автоповтора: как <see cref="Silent"/>, но ошибка не стирается до ответа.
        /// </summary>
        /// <remarks>
        /// Иначе карточка с причиной и «Повторить» исчезала бы на каждой паузе, и человек видел
        /// бы мигание пустого экрана вместо того, что уже знает.
        /// </remarks>
        Retry,
    }

    /// <summary>
    /// Режим догрузки: обычная по прокрутке или тихая попытка цикла.
    /// </summary>
    /// <remarks>
    /// Третьего режима нет: кнопка «Повторить» в футере останавливает цикл и читает страницу
    /// обычным режимом — для неё это и есть «повторить сейчас», с индикатором и свежим ответом.
    /// </remarks>
    private enum LoadMoreMode
    {
        /// <summary>
        /// Обычная догрузка: индикатор, старая ошибка стирается, потому что её сменил жест.
        /// </summary>
        Normal,

        /// <summary>
        /// Тихая попытка цикла: причина и строки остаются, ответ решает исход.
        /// </summary>
        Retry,
    }
}
