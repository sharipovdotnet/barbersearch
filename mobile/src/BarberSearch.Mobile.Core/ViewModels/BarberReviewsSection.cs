using System.Collections.ObjectModel;
using System.Net;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.Localization;
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
/// <para>
/// Пока страница видна, повторяемый сбой чтения лечится сам: цикл секции перечитывает
/// упавший запрос по снимку (номер страницы и её курсор), а под причиной видна строка
/// «Пробуем снова…». Цикл принадлежит карточке: второго <c>BindingContext</c> и второго
/// жизненного цикла у секции нет — карточка армит и снимает его теми же
/// <c>StartAutoRetry</c>/<c>StopAutoRetry</c>, что и своё чтение. «Повторить» у первой
/// страницы и у пагинатора значит «повторить сейчас».
/// </para>
/// </remarks>
public sealed partial class BarberReviewsSection : ObservableObject
{
    /// <summary>
    /// Размер страницы отзывов: как в вебе, 10 строк.
    /// </summary>
    public const int PageSize = 10;

    private static string FirstPageFailedValue => Loc.T("Не удалось загрузить отзывы.");
    private static string NextPageFailedValue => Loc.T("Не удалось загрузить следующие отзывы.");
    private static string RateLimitValue => Loc.T("Слишком много запросов. Подождите минуту и повторите.");
    private static string NetworkValue => Loc.T("Нет связи с сервером. Проверьте подключение и повторите попытку.");
    private static string RetryValue => Loc.T("Повторить");
    private static string NextValue => Loc.T("Дальше");

    private readonly IBarberSearchApi _api;
    private readonly List<ReviewPage> _pages = [];
    private readonly AutoRetryLoop _reviewsAutoRetry;

    private Guid _profileId;
    private TimeZoneInfo? _timeZone;
    private int _total;
    private int _pageIndex;
    private int _generation;
    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// Снимок упавшего чтения: номер страницы и её курсор; <c>null</c>, когда повторять нечего.
    /// </summary>
    /// <remarks>
    /// Хранится отдельно от текущей позиции: тихая попытка обязана повторить именно упавший
    /// запрос. Собранный заново из текущей страницы, он ушёл бы за её следующим курсором или
    /// за другой страницей, если секцию успели перелистнуть, и успех перескочил бы страницу.
    /// </remarks>
    private FailedPage? _failedPage;

    /// <summary>
    /// Вид последнего сбоя чтения: по нему решает цикл, повторять ли ещё раз.
    /// </summary>
    /// <remarks>
    /// Запоминается для каждого неуспеха, а не только для повторяемого: иначе «транспорт,
    /// потом 404» оставил бы в памяти старый <see cref="ApiFailureKind.Transport"/>, и кнопка
    /// снова запустила бы цикл на отказе, который не изменится.
    /// </remarks>
    private ApiFailureKind _lastFailureKind;

    /// <summary>
    /// Страница сейчас видна: об этом сообщает карточка, а не секция.
    /// </summary>
    /// <remarks>
    /// Второй подписки на жизненный цикл у секции нет: сигнал приходит из
    /// <see cref="BarberCardViewModel.StartAutoRetry"/> и
    /// <see cref="BarberCardViewModel.StopAutoRetry"/>, поэтому и цикл работает только при
    /// видимой карточке.
    /// </remarks>
    private bool _isPageVisible;

    /// <summary>
    /// Создаёт секцию отзывов поверх публичных чтений.
    /// </summary>
    /// <param name="api">Клиент чтений: страницы отзывов.</param>
    /// <param name="reviewsAutoRetry">
    /// Цикл автоповтора отзывов; <c>null</c> — собственный цикл с обычным расписанием.
    /// Параметр необязательный, чтобы существующие позиционные вызовы не менялись. Секция не
    /// заводит себе второй жизненный цикл: цикл армит карточка, а параметр нужен, чтобы тест
    /// подменил паузы и не ждал реального времени.
    /// </param>
    public BarberReviewsSection(IBarberSearchApi api, AutoRetryLoop? reviewsAutoRetry = null)
    {
        ArgumentNullException.ThrowIfNull(api);

        _api = api;

        _reviewsAutoRetry = reviewsAutoRetry ?? new AutoRetryLoop();
        _reviewsAutoRetry.StateChanged += OnAutoRetryStateChanged;
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
    /// Цикл сам повторяет упавшее чтение отзывов: под причиной видна строка «Пробуем снова…».
    /// </summary>
    /// <remarks>
    /// Состояние цикла, а не секции: свойство переключается подпиской на
    /// <see cref="AutoRetryLoop.StateChanged"/> и ничего не решает само. У карточки отзывов
    /// своё состояние с тем же именем, потому что это второй цикл, а не тот же самый.
    /// </remarks>
    [ObservableProperty]
    private bool _isAutoRetrying;

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
    /// <param name="profileId">Идентификатор профиля мастера.</param>
    /// <param name="timeZoneId">IANA-пояс основного салона; неизвестный — UTC.</param>
    /// <param name="total">Число опубликованных отзывов из профиля.</param>
    /// <param name="summaryText">Сводка — тот же текст рейтинга, что в шапке карточки.</param>
    /// <param name="cancellationToken">Токен отмены: уход с экрана гасит запрос.</param>
    /// <returns>Вид сбоя первой страницы: по нему карточка решает, нужен ли ещё повтор.</returns>
    public async Task<ApiFailureKind> InitializeAsync(
        Guid profileId,
        string? timeZoneId,
        int total,
        string summaryText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summaryText);

        // Секция перечитывается заново — прежний упавший запрос больше не актуален. Оставить
        // цикл взведённым значило бы отправить его по снимку, которого уже нет.
        StopAutoRetryLoop();

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
        _failedPage = null;
        _lastFailureKind = ApiFailureKind.None;
        SummaryText = _total > 0 ? summaryText : string.Empty;
        IsEmpty = _total == 0;

        NotifyState();

        if (_total == 0)
        {
            return ApiFailureKind.None;
        }

        return await LoadFirstPageAsync(cancellationToken);
    }

    partial void OnSummaryTextChanged(string value) => OnPropertyChanged(nameof(HasSummary));

    partial void OnIsFirstLoadingChanged(bool value) => NotifyState();

    partial void OnFirstErrorTextChanged(string? value) => NotifyState();

    partial void OnPageErrorTextChanged(string? value) => NotifyState();

    partial void OnIsPageLoadingChanged(bool value) => NotifyState();

    partial void OnIsEmptyChanged(bool value) => NotifyState();

    partial void OnIsPageEmptyChanged(bool value) => NotifyState();

    /// <summary>
    /// Сообщает, что страница видна, и запускает автоповтор упавшего чтения отзывов.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Зовётся карточкой при каждом появлении и возврате из фона — здесь своего жизненного
    /// цикла нет, и второй подписки на окно быть не должно. Идемпотентен: без снимка упавшего
    /// запроса, при окончательном отказе или уже работающем цикле ничего не делает.
    /// </para>
    /// <para>
    /// Запуск и остановка обязаны идти из UI-потока: <see cref="AutoRetryLoop.StateChanged"/>
    /// синхронно меняет привязанное <see cref="IsAutoRetrying"/>, а секция не знает про
    /// диспетчер MAUI.
    /// </para>
    /// </remarks>
    public void StartAutoRetry()
    {
        _isPageVisible = true;

        TryStartAutoRetry();
    }

    /// <summary>
    /// Сообщает, что страница скрыта, и останавливает автоповтор отзывов.
    /// </summary>
    /// <remarks>
    /// Идемпотентен. Остановка отменяет паузу и запрос в полёте, а <see cref="IsAutoRetrying"/>
    /// сразу становится <c>false</c>: ушедшая карточка не показывает повтор и не ходит в сеть.
    /// </remarks>
    public void StopAutoRetry()
    {
        _isPageVisible = false;

        StopAutoRetryLoop();
    }

    /// <summary>
    /// Завершается, когда цикл автоповтора отзывов останавливается: тесты ждут его вместо пауз.
    /// </summary>
    internal Task ReviewsAutoRetryCompletion => _reviewsAutoRetry.Completion;

    /// <summary>
    /// Цикл автоповтора отзывов: тестам нужен счётчик попыток, приложению — нет.
    /// </summary>
    internal AutoRetryLoop ReviewsAutoRetry => _reviewsAutoRetry;

    /// <summary>
    /// Повторяет загрузку первой страницы после ошибки — сразу, по кнопке.
    /// </summary>
    /// <remarks>
    /// Кнопка значит «повторить сейчас»: цикл останавливается приватно (публичный
    /// <see cref="StopAutoRetry"/> ещё и пометил бы страницу скрытой), страница читается без
    /// паузы, а цикл запускается заново только если новый сбой снова повторяемый — это делает
    /// завершение загрузки. Успех цикл не запускает: повторять больше нечего.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand]
    private async Task RetryFirstPageAsync(CancellationToken cancellationToken)
    {
        if (IsFirstLoading || _profileId == Guid.Empty)
        {
            return;
        }

        StopAutoRetryLoop();

        await LoadFirstPageAsync(cancellationToken);

        TryStartAutoRetry();
    }

    /// <summary>
    /// Идёт на следующую страницу или повторяет упавший запрос тем же курсором.
    /// </summary>
    /// <remarks>
    /// Пока запрос в полёте, повторное нажатие игнорируется: страницы не должны
    /// дублироваться. Курсор берётся только из ответа — клиент его не разбирает.
    /// На ошибке кнопка значит «повторить сейчас»: цикл останавливается приватно, а запрос
    /// берётся из снимка упавшей страницы, поэтому успех показывает именно её, а не следующую.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextPageAsync(CancellationToken cancellationToken)
    {
        if (IsFirstLoading || IsPageLoading || HasFirstError)
        {
            return;
        }

        if (HasPageError)
        {
            // Кнопка «Повторить» не имеет права оживить скрытую карточку: запуск цикла
            // проверит признак видимости, а сам повтор сейчас идёт без паузы.
            if (_failedPage is not { Index: > 0 } failed)
            {
                return;
            }

            StopAutoRetryLoop();

            await LoadPageAsync(failed.Cursor, failed.Index, cancellationToken);

            TryStartAutoRetry();

            return;
        }

        var cursor = CurrentPage?.NextCursor;

        if (cursor is null)
        {
            return;
        }

        var index = _pageIndex + 1;

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
    /// <remarks>
    /// Ошибка следующей страницы при этом снимается: человек ушёл от неё, и повторять
    /// невидимый сбой цикл больше не должен — иначе причина вернулась бы на экран сама.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousPage()
    {
        if (IsPageLoading || _pageIndex == 0)
        {
            return;
        }

        StopAutoRetryLoop();

        PageErrorText = null;
        _failedPage = null;
        _lastFailureKind = ApiFailureKind.None;

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
    /// <para>
    /// Ошибка первой страницы оставляет секцию в состоянии ошибки: строк нет, показан текст
    /// и «Повторить». Транспортный таймаут приходит отменой, поэтому отличается от отмены
    /// экрана по токену.
    /// </para>
    /// <para>
    /// Тихий режим — попытка цикла: причина и строки остаются на экране до ответа, индикатор
    /// первой загрузки не включается. Иначе блок ошибки с «Повторить» исчезал бы на каждой
    /// паузе, и человек видел бы пустую секцию вместо уже известного состояния.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="silent">
    /// Попытка цикла автоповтора: причина, строки и индикатор не меняются на старте.
    /// </param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private async Task<ApiFailureKind> LoadFirstPageAsync(
        CancellationToken cancellationToken,
        bool silent = false)
    {
        var generation = ++_generation;
        var cts = StartRequest(cancellationToken);

        if (!silent)
        {
            IsFirstLoading = true;
            FirstErrorText = null;
            PageErrorText = null;
            IsEmpty = false;
            IsPageEmpty = false;
            _failedPage = null;
            _lastFailureKind = ApiFailureKind.None;
            Items.Clear();

            NotifyState();
        }

        var kind = ApiFailureKind.None;

        try
        {
            var page = await _api.GetReviewsAsync(_profileId, null, PageSize, cts.Token);

            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            _pages.Clear();
            _pages.Add(ToPage(page));
            _pageIndex = 0;
            FirstErrorText = null;
            PageErrorText = null;
            _failedPage = null;
            _lastFailureKind = ApiFailureKind.None;

            ShowPage(0, notifyPageChanged: false);
        }
        catch (Exception exception)
        {
            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            kind = ApiFailureClassifier.Classify(exception, cts.Token);

            if (kind != ApiFailureKind.None)
            {
                // Причина обновляется и у тихой попытки: экран обязан показать то, чем
                // кончилось чтение, — иначе 404 после транспорта выглядел бы как «нет связи».
                FirstErrorText = DescribeReadFailure(exception, kind, FirstPageFailedValue);
                _failedPage = new FailedPage(0, null);
                _lastFailureKind = kind;

                NotifyState();
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

        if (kind.IsRetryable())
        {
            TryStartAutoRetry();
        }

        return kind;
    }

    /// <summary>
    /// Грузит страницу по курсору и ставит её на место упавшей или следующей.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Строки текущей страницы не стираются ни на время запроса, ни при ошибке: человек
    /// видит те же отзывы и «Повторить» у пагинатора.
    /// </para>
    /// <para>
    /// Тихий режим — попытка цикла: причина, строки, курсор и пагинатор не меняются на старте,
    /// а индикатор загрузки не показывается (в разметке его и нет). Ответ решает исход: успех
    /// снимает ошибку и показывает ту самую страницу, неудача оставляет всё как было и
    /// возвращает вид сбоя.
    /// </para>
    /// </remarks>
    /// <param name="cursor">Курсор запроса: у повтора он из снимка упавшей страницы.</param>
    /// <param name="index">Номер страницы, на место которого встанет ответ.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="silent">Попытка цикла автоповтора: видимое состояние не трогается.</param>
    /// <returns>Вид сбоя: по нему цикл решает, нужна ли ещё одна попытка.</returns>
    private async Task<ApiFailureKind> LoadPageAsync(
        string? cursor,
        int index,
        CancellationToken cancellationToken,
        bool silent = false)
    {
        var generation = ++_generation;
        var cts = StartRequest(cancellationToken);

        // Флаг поднимается в обоих режимах: это по-прежнему одно чтение одной страницы, и
        // второе (жест или попытка) поверх него добавило бы ту же страницу дважды. На экран
        // он не выводится, поэтому тихую попытку не выдаёт.
        IsPageLoading = true;

        if (!silent)
        {
            PageErrorText = null;
            IsPageEmpty = false;
            _failedPage = null;
            _lastFailureKind = ApiFailureKind.None;
        }

        NotifyState();

        var kind = ApiFailureKind.None;

        try
        {
            var page = await _api.GetReviewsAsync(_profileId, cursor, PageSize, cts.Token);

            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            var loaded = ToPage(page);

            if (_pages.Count > index)
            {
                _pages[index] = loaded;

                // Хвост кэша относился к другой ветке курсоров и больше не нужен.
                if (_pages.Count > index + 1)
                {
                    _pages.RemoveRange(index + 1, _pages.Count - index - 1);
                }
            }
            else
            {
                _pages.Add(loaded);
            }

            PageErrorText = null;
            _failedPage = null;
            _lastFailureKind = ApiFailureKind.None;

            // Успешный повтор показывает именно упавшую страницу, а не следующую за ней.
            if (_pageIndex == index || _pageIndex + 1 == index)
            {
                ShowPage(index, notifyPageChanged: true);
            }
            else
            {
                // Пока читали, человек вернулся назад: кэш пополнен, но смотреть эту страницу
                // он не просил — ошибка снята, а позиция осталась его.
                NotifyState();
            }
        }
        catch (Exception exception)
        {
            if (generation != _generation)
            {
                return ApiFailureKind.None;
            }

            kind = ApiFailureClassifier.Classify(exception, cts.Token);

            if (kind != ApiFailureKind.None)
            {
                // Строки не трогаются: неудача тихой попытки не имеет права унести с собой
                // уже показанные отзывы. Причина обновляется — экран говорит, чем кончилось.
                PageErrorText = DescribeReadFailure(exception, kind, NextPageFailedValue);
                _failedPage = new FailedPage(index, cursor);
                _lastFailureKind = kind;

                NotifyState();
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

        if (kind.IsRetryable())
        {
            TryStartAutoRetry();
        }

        return kind;
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
    /// Одна попытка цикла: перечитывает ровно тот запрос, который упал.
    /// </summary>
    /// <remarks>
    /// Снимок берётся из сбоя, а не из текущей позиции: пока цикл ждал паузу, страницу могли
    /// перелистнуть, и запрос, собранный заново, ушёл бы за другой страницей или за её
    /// следующим курсором. Первая страница параметров не имеет вовсе, поэтому её снимок —
    /// нулевая страница.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    private Task<ApiFailureKind> RetryFailedReadAsync(CancellationToken cancellationToken)
    {
        if (_failedPage is not { } failed || !_lastFailureKind.IsRetryable())
        {
            return Task.FromResult(ApiFailureKind.None);
        }

        return failed.Index == 0
            ? LoadFirstPageAsync(cancellationToken, silent: true)
            : LoadPageAsync(failed.Cursor, failed.Index, cancellationToken, silent: true);
    }

    /// <summary>
    /// Запускает цикл, если страница видна, сбой повторяемый и есть снимок упавшего запроса.
    /// </summary>
    /// <remarks>
    /// «Видна» — сигнал карточки, а не следствие нажатия: сбой приходит из загрузки, а цикл всё
    /// равно имеет право повторить чтение, пока человек смотрит на секцию. Уже работающий цикл
    /// второй раз не запускается — <see cref="AutoRetryLoop.Start"/> это игнорирует.
    /// </remarks>
    private void TryStartAutoRetry()
    {
        if (!_isPageVisible || IsFirstLoading || _profileId == Guid.Empty)
        {
            return;
        }

        if (_failedPage is null || !_lastFailureKind.IsRetryable())
        {
            return;
        }

        _reviewsAutoRetry.Start(RetryFailedReadAsync);
    }

    /// <summary>
    /// Останавливает цикл, не трогая признак «страница видна».
    /// </summary>
    /// <remarks>
    /// Нужен кнопкам «Повторить»: они останавливают фон, но карточка остаётся видимой, и после
    /// ответа цикл имеет право запуститься снова. Публичный <see cref="StopAutoRetry"/>
    /// дополнительно помечает страницу скрытой — для кнопок это неверно.
    /// </remarks>
    private void StopAutoRetryLoop() => _reviewsAutoRetry.Stop();

    /// <summary>
    /// Переключает строку «Пробуем снова…» вслед за состоянием цикла.
    /// </summary>
    private void OnAutoRetryStateChanged(object? sender, EventArgs e) =>
        IsAutoRetrying = _reviewsAutoRetry.IsRunning;

    /// <summary>
    /// Текст ошибки чтения: «нет связи» — свой, <c>429</c> — про лимит, остальное — подпись раздела.
    /// </summary>
    private static string DescribeReadFailure(
        Exception exception,
        ApiFailureKind kind,
        string fallback) =>
        kind == ApiFailureKind.Transport
            ? NetworkValue
            : exception is BarberSearchApiException apiException
                ? DescribeFailure(apiException, fallback)
                : fallback;

    /// <summary>
    /// Текст сбоя API: <c>429</c> — про лимит, остальное — подпись раздела.
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

    /// <summary>
    /// Снимок упавшего чтения: номер страницы и её курсор.
    /// </summary>
    /// <remarks>
    /// Номер нужен, чтобы успешный повтор встал на место упавшей страницы, а не следующей;
    /// курсор — чтобы запрос был ровно тем же, даже если секцию успели перелистнуть. Нулевая
    /// страница означает первую: у неё параметров нет.
    /// </remarks>
    private sealed record FailedPage(int Index, string? Cursor);
}
