using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Formatting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BarberSearch.Mobile.Core.ViewModels;

/// <summary>
/// Секция «Отзывы» внутри карточки: сводка, страницы и их ошибки.
/// </summary>
/// <remarks>
/// <para>
/// Отзывы вынесены из <see cref="BarberCardViewModel"/> в отдельную секцию, потому что
/// у них свой жизненный цикл — страницы, курсоры, ошибка следующей страницы и повтор.
/// Это не второй <c>BindingContext</c> экрана, а вложенное состояние: страница биндится
/// к нему через свойство карточки <c>Reviews</c>.
/// </para>
/// <para>
/// Пагинация курсорная: вперёд — только по <c>nextCursor</c> из ответа, смещений клиент
/// не выдумывает. Страницы кэшируются в памяти по номеру, поэтому «Назад» не ходит
/// в сеть. Сводка приходит из профиля (<c>rating</c> и <c>reviewsCount</c>): среднее по
/// загруженной странице при пагинации было бы неверным.
/// </para>
/// <para>
/// В полёте один запрос: повторное нажатие игнорируется, устаревший ответ отбрасывается
/// поколением, отмена экрана гасит запрос. Ошибка следующей страницы не стирает уже
/// показанные строки, а «Дальше» превращается в «Повторить» с тем же курсором.
/// </para>
/// </remarks>
public sealed partial class BarberReviewsSection : ObservableObject
{
    /// <summary>
    /// Размер страницы отзывов: как в вебе, 10 строк.
    /// </summary>
    public const int PageSize = 10;

    private const string FirstPageFailedValue = "Не удалось загрузить отзывы.";
    private const string NextPageFailedValue = "Не удалось загрузить следующие отзывы.";
    private const string RateLimitValue = "Слишком много запросов. Подождите минуту и повторите.";
    private const string NetworkValue = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string RetryValue = "Повторить";
    private const string NextValue = "Дальше";

    private readonly IBarberSearchApi _api;
    private readonly List<ReviewPage> _pages = [];

    private Guid _profileId;
    private TimeZoneInfo? _timeZone;
    private int _total;
    private int _pageIndex;
    private int _generation;
    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// Создаёт секцию отзывов поверх публичных чтений.
    /// </summary>
    /// <param name="api">Клиент чтений: страницы отзывов.</param>
    public BarberReviewsSection(IBarberSearchApi api)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;
    }

    /// <summary>
    /// Строки текущей страницы.
    /// </summary>
    public ObservableCollection<BarberReviewItem> Items { get; } = [];

    /// <summary>
    /// Сводка из профиля: «4,5 · 12 отзывов»; пусто, когда отзывов нет.
    /// </summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>
    /// Грузится первая страница: вместо строк индикатор.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstLoading;

    /// <summary>
    /// Текст ошибки первой страницы; <c>null</c>, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    private string? _firstErrorText;

    /// <summary>
    /// Текст ошибки следующей страницы; <c>null</c>, когда ошибки нет.
    /// </summary>
    [ObservableProperty]
    private string? _pageErrorText;

    /// <summary>
    /// Грузится следующая страница: строки остаются на месте.
    /// </summary>
    [ObservableProperty]
    private bool _isPageLoading;

    /// <summary>
    /// Отзывов нет вовсе или первая страница пришла пустой.
    /// </summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>
    /// Текущая страница (дальше первой) пришла пустой.
    /// </summary>
    [ObservableProperty]
    private bool _isPageEmpty;

    /// <summary>
    /// Видна ли сводка: она есть только при ненулевом числе отзывов.
    /// </summary>
    public bool HasSummary => SummaryText.Length > 0;

    /// <summary>
    /// Видна ли ошибка первой страницы.
    /// </summary>
    public bool HasFirstError => FirstErrorText is not null;

    /// <summary>
    /// Видна ли ошибка следующей страницы.
    /// </summary>
    public bool HasPageError => PageErrorText is not null;

    /// <summary>
    /// Виден ли пагинатор: страница одна — пагинатора нет вовсе.
    /// </summary>
    public bool IsPagerVisible => !IsFirstLoading && !HasFirstError && (CanGoPrevious || CanGoNext);

    /// <summary>
    /// Диапазон строк страницы: «1–10 из 12».
    /// </summary>
    public string PageRangeText => BarberText.PageRange(Offset, Items.Count, _total);

    /// <summary>
    /// Можно ли вернуться на предыдущую страницу: она уже в кэше.
    /// </summary>
    public bool CanGoPrevious => _pageIndex > 0;

    /// <summary>
    /// Можно ли идти дальше: есть курсор или ошибку страницы надо повторить.
    /// </summary>
    public bool CanGoNext => HasPageError || CurrentPage?.NextCursor is not null;

    /// <summary>
    /// Надпись на кнопке «Дальше»: при ошибке страницы она повторяет тот же курсор.
    /// </summary>
    public string NextPageButtonText => HasPageError ? RetryValue : NextValue;

    /// <summary>
    /// Страница сменилась: карточка прокручивается к заголовку секции.
    /// </summary>
    public event EventHandler? PageChanged;

    /// <summary>
    /// Загружает первую страницу и запоминает параметры показа.
    /// </summary>
    /// <remarks>
    /// Вызывается карточкой после загрузки профиля. При <paramref name="total"/> = 0 запрос
    /// не делается вовсе: сервер вернул бы пустую страницу, а экран и так знает ответ.
    /// </remarks>
    /// <param name="profileId">Идентификатор профиля барбера.</param>
    /// <param name="timeZoneId">IANA-пояс основного салона; неизвестный — UTC.</param>
    /// <param name="total">Число опубликованных отзывов из профиля.</param>
    /// <param name="summaryText">Сводка — тот же текст рейтинга, что в шапке карточки.</param>
    /// <param name="cancellationToken">Токен отмены: уход с экрана гасит запрос.</param>
    public async Task InitializeAsync(
        Guid profileId,
        string? timeZoneId,
        int total,
        string summaryText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summaryText);

        _profileId = profileId;
        _timeZone = SalonClock.TryResolve(timeZoneId);
        _total = Math.Max(total, 0);
        _pageIndex = 0;
        _pages.Clear();
        Items.Clear();
        FirstErrorText = null;
        PageErrorText = null;
        IsPageEmpty = false;
        IsFirstLoading = false;
        SummaryText = _total > 0 ? summaryText : string.Empty;
        IsEmpty = _total == 0;

        NotifyState();

        if (_total == 0)
        {
            return;
        }

        await LoadFirstPageAsync(cancellationToken);
    }

    partial void OnSummaryTextChanged(string value) => OnPropertyChanged(nameof(HasSummary));

    partial void OnIsFirstLoadingChanged(bool value) => NotifyState();

    partial void OnFirstErrorTextChanged(string? value) => NotifyState();

    partial void OnPageErrorTextChanged(string? value) => NotifyState();

    partial void OnIsPageLoadingChanged(bool value) => NotifyState();

    partial void OnIsEmptyChanged(bool value) => NotifyState();

    partial void OnIsPageEmptyChanged(bool value) => NotifyState();

    /// <summary>
    /// Повторяет загрузку первой страницы после ошибки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryFirstPageAsync(CancellationToken cancellationToken)
    {
        if (IsFirstLoading || _profileId == Guid.Empty)
        {
            return;
        }

        await LoadFirstPageAsync(cancellationToken);
    }

    /// <summary>
    /// Идёт на следующую страницу или повторяет упавший запрос тем же курсором.
    /// </summary>
    /// <remarks>
    /// Пока запрос в полёте, повторное нажатие игнорируется: страницы не должны
    /// дублироваться. Курсор берётся только из ответа — клиент его не разбирает.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextPageAsync(CancellationToken cancellationToken)
    {
        if (IsFirstLoading || IsPageLoading || HasFirstError)
        {
            return;
        }

        var current = CurrentPage;

        if (current is null)
        {
            return;
        }

        var cursor = current.NextCursor;
        var index = _pageIndex + 1;

        if (HasPageError)
        {
            // Повтор идёт тем же курсором: страница не загрузилась, кэша у неё нет.
            await LoadPageAsync(cursor, index, cancellationToken);

            return;
        }

        if (cursor is null)
        {
            return;
        }

        if (_pages.Count > index)
        {
            ShowPage(index, notifyPageChanged: true);

            return;
        }

        await LoadPageAsync(cursor, index, cancellationToken);
    }

    /// <summary>
    /// Возвращает на предыдущую страницу из кэша — без запроса в сеть.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousPage()
    {
        if (IsPageLoading || _pageIndex == 0)
        {
            return;
        }

        PageErrorText = null;

        ShowPage(_pageIndex - 1, notifyPageChanged: true);
    }

    /// <summary>
    /// Текущая страница из кэша; <c>null</c> до первой загрузки.
    /// </summary>
    private ReviewPage? CurrentPage => _pageIndex < _pages.Count ? _pages[_pageIndex] : null;

    /// <summary>
    /// Сумма строк предыдущих страниц: с неё начинается диапазон текущей.
    /// </summary>
    private int Offset
    {
        get
        {
            var offset = 0;

            for (var index = 0; index < _pageIndex && index < _pages.Count; index++)
            {
                offset += _pages[index].Items.Count;
            }

            return offset;
        }
    }

    /// <summary>
    /// Грузит первую страницу и заменяет кэш.
    /// </summary>
    /// <remarks>
    /// Ошибка первой страницы оставляет секцию в состоянии ошибки: строк нет, показан текст
    /// и «Повторить». Транспортный таймаут приходит отменой, поэтому отличается от отмены
    /// экрана по токену.
    /// </remarks>
    private async Task LoadFirstPageAsync(CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        var cts = StartRequest(cancellationToken);

        IsFirstLoading = true;
        FirstErrorText = null;
        PageErrorText = null;
        IsEmpty = false;
        IsPageEmpty = false;
        Items.Clear();

        NotifyState();

        try
        {
            var page = await _api.GetReviewsAsync(_profileId, null, PageSize, cts.Token);

            if (generation != _generation)
            {
                return;
            }

            _pages.Clear();
            _pages.Add(ToPage(page));
            _pageIndex = 0;

            ShowPage(0, notifyPageChanged: false);
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation && !cancellationToken.IsCancellationRequested)
            {
                // Отменил не экран, а транспорт: для человека это отсутствие связи.
                FirstErrorText = NetworkValue;
            }
        }
        catch (BarberSearchApiException exception)
        {
            if (generation == _generation)
            {
                FirstErrorText = DescribeFailure(exception, FirstPageFailedValue);
            }
        }
        catch (HttpRequestException)
        {
            if (generation == _generation)
            {
                FirstErrorText = NetworkValue;
            }
        }
        catch (InvalidOperationException)
        {
            if (generation == _generation)
            {
                // Пустое тело при 200: контракт нарушен, но экран показывает «Повторить».
                FirstErrorText = FirstPageFailedValue;
            }
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
                IsFirstLoading = false;
            }

            cts.Dispose();
        }
    }

    /// <summary>
    /// Грузит страницу по курсору и добавляет её в кэш.
    /// </summary>
    /// <remarks>
    /// Строки текущей страницы не стираются ни на время запроса, ни при ошибке: человек
    /// видит те же отзывы и «Повторить» у пагинатора.
    /// </remarks>
    private async Task LoadPageAsync(string? cursor, int index, CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        var cts = StartRequest(cancellationToken);

        IsPageLoading = true;
        PageErrorText = null;
        IsPageEmpty = false;

        NotifyState();

        try
        {
            var page = await _api.GetReviewsAsync(_profileId, cursor, PageSize, cts.Token);

            if (generation != _generation)
            {
                return;
            }

            var loaded = ToPage(page);

            if (_pages.Count == index)
            {
                _pages.Add(loaded);
            }
            else
            {
                _pages[index] = loaded;

                // Хвост кэша относился к другой ветке курсоров и больше не нужен.
                if (_pages.Count > index + 1)
                {
                    _pages.RemoveRange(index + 1, _pages.Count - index - 1);
                }
            }

            ShowPage(index, notifyPageChanged: true);
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation && !cancellationToken.IsCancellationRequested)
            {
                PageErrorText = NetworkValue;
            }
        }
        catch (BarberSearchApiException exception)
        {
            if (generation == _generation)
            {
                PageErrorText = DescribeFailure(exception, NextPageFailedValue);
            }
        }
        catch (HttpRequestException)
        {
            if (generation == _generation)
            {
                PageErrorText = NetworkValue;
            }
        }
        catch (InvalidOperationException)
        {
            if (generation == _generation)
            {
                PageErrorText = NextPageFailedValue;
            }
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
                IsPageLoading = false;
            }

            cts.Dispose();
        }
    }

    /// <summary>
    /// Показывает страницу из кэша и обновляет все зависящие от неё тексты.
    /// </summary>
    private void ShowPage(int index, bool notifyPageChanged)
    {
        _pageIndex = index;
        Items.Clear();

        var page = _pages[index];

        foreach (var item in page.Items)
        {
            Items.Add(item);
        }

        IsEmpty = page.Items.Count == 0 && index == 0;
        IsPageEmpty = page.Items.Count == 0 && index > 0;

        NotifyState();

        if (notifyPageChanged)
        {
            PageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Раскладывает ответ сервера по строкам и курсору.
    /// </summary>
    private ReviewPage ToPage(PagedResponse<ReviewResponse> page)
    {
        var items = (page.Items ?? [])
            .Select(review => new BarberReviewItem(review, _timeZone))
            .ToArray();

        return new ReviewPage(items, page.NextCursor);
    }

    /// <summary>
    /// Начинает новый запрос, отменяя предыдущий.
    /// </summary>
    /// <remarks>
    /// Прежний источник только отменяется: освобождает его тот запрос, который его создал,
    /// в своём <c>finally</c> — иначе гонка с ещё живым <c>await</c> на его токене.
    /// </remarks>
    private CancellationTokenSource StartRequest(CancellationToken cancellationToken)
    {
        _loadCts?.Cancel();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _loadCts = cts;

        return cts;
    }

    /// <summary>
    /// Текст ошибки чтения: <c>429</c> — про лимит, остальное — подпись раздела.
    /// </summary>
    private static string DescribeFailure(BarberSearchApiException exception, string fallback) =>
        exception.StatusCode == HttpStatusCode.TooManyRequests ? RateLimitValue : fallback;

    /// <summary>
    /// Сообщает об изменении всего, что зависит от страниц и ошибок.
    /// </summary>
    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasFirstError));
        OnPropertyChanged(nameof(HasPageError));
        OnPropertyChanged(nameof(IsPagerVisible));
        OnPropertyChanged(nameof(PageRangeText));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(NextPageButtonText));
        NextPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Страница отзывов в кэше: строки и курсор следующей страницы.
    /// </summary>
    private sealed record ReviewPage(IReadOnlyList<BarberReviewItem> Items, string? NextCursor);
}
