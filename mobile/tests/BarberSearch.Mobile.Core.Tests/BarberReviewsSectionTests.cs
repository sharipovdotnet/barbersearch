using System.Net;
using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки секции «Отзывы»: первая страница, курсорная пагинация, кэш страниц и ошибки.
/// </summary>
/// <remarks>
/// <para>
/// Сеть подменяется фейком <see cref="IBarberSearchApi"/>: тест не поднимает бэкенд, но
/// проверяет именно параметры запросов — размер страницы, курсор и то, что «Назад» в сеть
/// не ходит. Размер страницы 10 — часть контракта с вебом, а не настройка экрана.
/// </para>
/// <para>
/// «Висящие» ответы задаются <see cref="TaskCompletionSource"/>: так проверяются устаревший
/// ответ и двойное нажатие «Дальше» без ожидания реальной сети.
/// </para>
/// </remarks>
public sealed class BarberReviewsSectionTests
{
    private static readonly Guid ProfileId = Guid.Parse("9e300000-0000-4000-8000-000000000001");

    private static readonly DateTimeOffset Instant = new(2026, 11, 14, 22, 30, 0, TimeSpan.Zero);

    private const string SummaryText = "4,8 · 12 отзывов";
    private const string FirstPageFailedText = "Не удалось загрузить отзывы.";
    private const string NextPageFailedText = "Не удалось загрузить следующие отзывы.";
    private const string RateLimitText = "Слишком много запросов. Подождите минуту и повторите.";
    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";

    [Fact]
    public async Task InitializeAsync_FirstPage_LoadsTenItemsWithPageSizeAndNoCursor()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, _, _) => Task.FromResult(Page(Reviews(10), NextCursor: null)),
        };
        var section = new BarberReviewsSection(api);

        await InitializeSectionAsync(section);

        Assert.Equal(10, section.Items.Count);

        var request = Assert.Single(api.ReviewCalls);

        Assert.Equal(ProfileId, request.BarberProfileId);
        Assert.Null(request.Cursor);
        Assert.Equal(10, request.PageSize);

        Assert.Equal("1–10 из 12", section.PageRangeText);
        Assert.Equal(SummaryText, section.SummaryText);
        Assert.True(section.HasSummary);
        Assert.False(section.IsEmpty);
    }

    [Fact]
    public async Task InitializeAsync_ZeroTotal_DoesNotRequestAndShowsEmptyState()
    {
        var api = new FakeBarberApi();
        var section = new BarberReviewsSection(api);

        // Число отзывов уже известно из профиля: при нуле запрос вернул бы пустую страницу,
        // а экран и так знает ответ.
        await section.InitializeAsync(ProfileId, "Europe/Moscow", total: 0, SummaryText);

        Assert.Empty(api.ReviewCalls);
        Assert.True(section.IsEmpty);
        Assert.False(section.HasSummary);
        Assert.Equal(string.Empty, section.SummaryText);
        Assert.Empty(section.Items);
    }

    [Fact]
    public async Task PreviousPage_FromSecondPage_ReturnsWithoutNetworkCall()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, cursor, _) => Task.FromResult(cursor is null
                ? Page(Reviews(10), "c1")
                : Page(Reviews(2), NextCursor: null)),
        };
        var section = new BarberReviewsSection(api);

        await InitializeSectionAsync(section);
        await section.NextPageCommand.ExecuteAsync(null);
        section.PreviousPageCommand.Execute(null);

        // «Назад» идёт по кэшу страниц: сеть трогается только «Дальше».
        Assert.Equal(2, api.ReviewCalls.Count);
        Assert.Equal(10, section.Items.Count);
        Assert.Equal("1–10 из 12", section.PageRangeText);
        Assert.False(section.CanGoPrevious);
    }

    [Fact]
    public async Task NextPage_UsesCursorFromResponse()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, cursor, _) => Task.FromResult(cursor is null
                ? Page(Reviews(10), "c1")
                : Page(Reviews(2), NextCursor: null)),
        };
        var section = new BarberReviewsSection(api);

        await InitializeSectionAsync(section);
        await section.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(2, api.ReviewCalls.Count);
        // Клиент не разбирает курсор и не выдумывает смещение: он передаёт строку из ответа.
        Assert.Equal("c1", api.ReviewCalls[1].Cursor);
        Assert.Equal(2, section.Items.Count);
        Assert.Equal("11–12 из 12", section.PageRangeText);
    }

    [Fact]
    public async Task NextPage_CachedPage_DoesNotRequestAgain()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, cursor, _) => Task.FromResult(cursor is null
                ? Page(Reviews(10), "c1")
                : Page(Reviews(2), NextCursor: null)),
        };
        var section = new BarberReviewsSection(api);

        await InitializeSectionAsync(section);
        await section.NextPageCommand.ExecuteAsync(null);
        section.PreviousPageCommand.Execute(null);
        await section.NextPageCommand.ExecuteAsync(null);

        // Вторая страница уже в кэше: возврат на неё не повторяет запрос.
        Assert.Equal(2, api.ReviewCalls.Count);
        Assert.Equal(2, section.Items.Count);
        Assert.Equal("11–12 из 12", section.PageRangeText);
    }

    [Fact]
    public async Task IsPagerVisible_SinglePage_IsHidden()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, _, _) => Task.FromResult(Page(Reviews(10), NextCursor: null)),
        };
        var section = new BarberReviewsSection(api);

        await InitializeSectionAsync(section);

        // Страница одна — пагинатора нет вовсе: две кнопки без хода только путали бы.
        Assert.False(section.IsPagerVisible);
        Assert.False(section.CanGoNext);
        Assert.False(section.CanGoPrevious);
    }

    [Fact]
    public async Task IsPagerVisible_TwoPages_IsVisible()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, cursor, _) => Task.FromResult(cursor is null
                ? Page(Reviews(10), "c1")
                : Page(Reviews(2), NextCursor: null)),
        };
        var section = new BarberReviewsSection(api);

        await InitializeSectionAsync(section);

        Assert.True(section.IsPagerVisible);
        Assert.True(section.CanGoNext);

        await section.NextPageCommand.ExecuteAsync(null);

        // На последней странице «Дальше» уже нет, но пагинатор остаётся из-за «Назад».
        Assert.True(section.IsPagerVisible);
        Assert.True(section.CanGoPrevious);
        Assert.False(section.CanGoNext);
    }

    [Fact]
    public async Task InitializeAsync_EmptyFirstPage_SetsIsEmpty()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, _, _) => Task.FromResult(Page([], NextCursor: null)),
        };
        var section = new BarberReviewsSection(api);

        await InitializeSectionAsync(section);

        // Профиль обещал отзывы, а страница пустая: показывается пустое состояние, а не
        // пагинатор с «0 из 12».
        Assert.True(section.IsEmpty);
        Assert.False(section.IsPageEmpty);
        Assert.Empty(section.Items);
        Assert.False(section.IsPagerVisible);
    }

    [Fact]
    public async Task NextPage_EmptyPage_SetsIsPageEmpty()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, cursor, _) => Task.FromResult(cursor is null
                ? Page(Reviews(10), "c1")
                : Page([], NextCursor: null)),
        };
        var section = new BarberReviewsSection(api);

        await InitializeSectionAsync(section);
        await section.NextPageCommand.ExecuteAsync(null);

        // Пустая страница дальше первой — отдельное состояние: «Пока нет отзывов» здесь
        // было бы неправдой, на первой странице отзывы есть.
        Assert.True(section.IsPageEmpty);
        Assert.False(section.IsEmpty);
        Assert.Empty(section.Items);
    }

    [Fact]
    public async Task InitializeAsync_FirstPageError_ShowsTextAndRetryReloads()
    {
        var attempt = 0;

        var api = new FakeBarberApi
        {
            Reviews = (_, _, _) => ++attempt == 1
                ? Task.FromException<PagedResponse<ReviewResponse>>(ApiError(HttpStatusCode.InternalServerError))
                : Task.FromResult(Page(Reviews(10), NextCursor: null)),
        };
        var section = new BarberReviewsSection(api);

        await InitializeSectionAsync(section);

        Assert.Equal(FirstPageFailedText, section.FirstErrorText);
        Assert.True(section.HasFirstError);
        Assert.Empty(section.Items);

        await section.RetryFirstPageCommand.ExecuteAsync(null);

        Assert.Null(section.FirstErrorText);
        Assert.False(section.HasFirstError);
        Assert.Equal(10, section.Items.Count);
        Assert.Equal(2, api.ReviewCalls.Count);
    }

    [Fact]
    public async Task NextPage_Error_KeepsItemsAndRetriesSameCursor()
    {
        var attempt = 0;

        var api = new FakeBarberApi
        {
            Reviews = (_, cursor, _) =>
            {
                attempt++;

                if (cursor is null)
                {
                    return Task.FromResult(Page(Reviews(10), "c1"));
                }

                return attempt == 2
                    ? Task.FromException<PagedResponse<ReviewResponse>>(
                        ApiError(HttpStatusCode.InternalServerError))
                    : Task.FromResult(Page(Reviews(2), NextCursor: null));
            },
        };
        var section = new BarberReviewsSection(api);

        await InitializeSectionAsync(section);
        await section.NextPageCommand.ExecuteAsync(null);

        // Ошибка следующей страницы не стирает уже показанные строки: человек продолжает
        // видеть те же отзывы, а «Дальше» превращается в «Повторить».
        Assert.Equal(10, section.Items.Count);
        Assert.True(section.HasPageError);
        Assert.Equal(NextPageFailedText, section.PageErrorText);
        Assert.Equal("Повторить", section.NextPageButtonText);
        Assert.True(section.CanGoNext);

        await section.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(3, api.ReviewCalls.Count);
        // Повтор идёт тем же курсором: страница не загрузилась, другого курсора у неё нет.
        Assert.Equal("c1", api.ReviewCalls[2].Cursor);
        Assert.Null(section.PageErrorText);
        Assert.Equal("Дальше", section.NextPageButtonText);
        Assert.Equal(2, section.Items.Count);
        Assert.Equal("11–12 из 12", section.PageRangeText);
    }

    [Fact]
    public async Task InitializeAsync_TooManyRequests_ShowsRateLimitText()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, _, _) => Task.FromException<PagedResponse<ReviewResponse>>(
                ApiError(HttpStatusCode.TooManyRequests)),
        };
        var section = new BarberReviewsSection(api);

        await InitializeSectionAsync(section);

        // 429 — не общая ошибка раздела: человеку говорят подождать, а не «не удалось».
        Assert.Equal(RateLimitText, section.FirstErrorText);
    }

    [Fact]
    public async Task InitializeAsync_NetworkFailure_ShowsNetworkText()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, _, _) => Task.FromException<PagedResponse<ReviewResponse>>(
                new HttpRequestException("refused")),
        };
        var section = new BarberReviewsSection(api);

        await InitializeSectionAsync(section);

        Assert.Equal(NetworkText, section.FirstErrorText);
    }

    [Fact]
    public async Task InitializeAsync_StaleResponse_DoesNotReplaceNewerPage()
    {
        var stale = new TaskCompletionSource<PagedResponse<ReviewResponse>>();
        var newer = new TaskCompletionSource<PagedResponse<ReviewResponse>>();
        var calls = 0;

        var api = new FakeBarberApi
        {
            Reviews = (_, _, _) => ++calls == 1 ? stale.Task : newer.Task,
        };
        var section = new BarberReviewsSection(api);

        var first = InitializeSectionAsync(section);
        var second = InitializeSectionAsync(section);

        newer.SetResult(Page([Review("Новый")], NextCursor: null));
        await second;

        // Устаревший ответ приходит позже нового и относится к прошлому поколению запроса:
        // он не должен подменить уже показанную страницу.
        stale.SetResult(Page([Review("Старый")], NextCursor: null));
        await first;

        var item = Assert.Single(section.Items);

        Assert.Equal("Новый", item.BodyText);
    }

    [Fact]
    public async Task NextPage_DoubleClick_IssuesSingleRequest()
    {
        var nextPage = new TaskCompletionSource<PagedResponse<ReviewResponse>>();

        var api = new FakeBarberApi
        {
            Reviews = (_, cursor, _) => cursor is null
                ? Task.FromResult(Page(Reviews(10), "c1"))
                : nextPage.Task,
        };
        var section = new BarberReviewsSection(api);

        await InitializeSectionAsync(section);

        var firstClick = section.NextPageCommand.ExecuteAsync(null);
        var secondClick = section.NextPageCommand.ExecuteAsync(null);

        // В полёте один запрос: второе нажатие игнорируется, страницы не дублируются.
        Assert.Equal(2, api.ReviewCalls.Count);

        nextPage.SetResult(Page(Reviews(2), NextCursor: null));
        await Task.WhenAll(firstClick, secondClick);

        Assert.Equal(2, api.ReviewCalls.Count);
        Assert.Equal(2, section.Items.Count);
        Assert.Equal("11–12 из 12", section.PageRangeText);
    }

    [Fact]
    public async Task PageChanged_RaisedOnPageChange_NotOnInitialLoad()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, cursor, _) => Task.FromResult(cursor is null
                ? Page(Reviews(10), "c1")
                : Page(Reviews(2), NextCursor: null)),
        };
        var section = new BarberReviewsSection(api);

        var pageChanges = 0;
        section.PageChanged += (_, _) => pageChanges++;

        await InitializeSectionAsync(section);

        // Первая загрузка — не смена страницы: прокручивать карточку к заголовку не нужно.
        Assert.Equal(0, pageChanges);

        await section.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(1, pageChanges);

        section.PreviousPageCommand.Execute(null);
        Assert.Equal(2, pageChanges);
    }

    [Fact]
    public async Task StartAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        var api = new FakeBarberApi();
        var calls = 0;

        api.Reviews = (_, _, _) =>
        {
            calls++;

            return calls <= 2
                ? Task.FromException<PagedResponse<ReviewResponse>>(new HttpRequestException("refused"))
                : Task.FromResult(Page(Reviews(10), NextCursor: null));
        };

        var section = new BarberReviewsSection(api, FastRetry());

        await InitializeSectionAsync(section);

        Assert.True(section.HasFirstError);
        Assert.Equal(NetworkText, section.FirstErrorText);

        section.StartAutoRetry();
        await section.ReviewsAutoRetryCompletion;

        Assert.Equal(2, section.ReviewsAutoRetry.Attempts);
        Assert.False(section.HasFirstError);
        Assert.Equal(10, section.Items.Count);
        Assert.False(section.IsAutoRetrying);
    }

    [Fact]
    public async Task StartAutoRetry_NextPageTransportFailure_RetriesUntilSuccess()
    {
        var api = new FakeBarberApi();
        var nextCalls = 0;

        api.Reviews = (_, cursor, _) =>
        {
            if (cursor is null)
            {
                return Task.FromResult(Page(Reviews(10), "c1"));
            }

            nextCalls++;

            return nextCalls <= 2
                ? Task.FromException<PagedResponse<ReviewResponse>>(new HttpRequestException("refused"))
                : Task.FromResult(Page(Reviews(2), NextCursor: null));
        };

        var section = new BarberReviewsSection(api, FastRetry());

        await InitializeSectionAsync(section);
        await section.NextPageCommand.ExecuteAsync(null);

        Assert.True(section.HasPageError);
        Assert.Equal(NetworkText, section.PageErrorText);

        section.StartAutoRetry();
        await section.ReviewsAutoRetryCompletion;

        Assert.Equal(2, section.ReviewsAutoRetry.Attempts);
        Assert.False(section.HasPageError);
        Assert.Equal(2, section.Items.Count);
        Assert.Equal("11–12 из 12", section.PageRangeText);
        Assert.Equal("Дальше", section.NextPageButtonText);
        Assert.False(section.IsAutoRetrying);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task StartAutoRetry_TerminalFailure_DoesNotStart(HttpStatusCode statusCode)
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, _, _) => Task.FromException<PagedResponse<ReviewResponse>>(ApiError(statusCode)),
        };
        var section = new BarberReviewsSection(api, FastRetry());

        await InitializeSectionAsync(section);

        section.StartAutoRetry();

        // Окончательный отказ повторять нечего: цикл не стартует, текст и «Повторить» остаются.
        Assert.Equal(0, section.ReviewsAutoRetry.Attempts);
        Assert.False(section.IsAutoRetrying);
        Assert.True(section.HasFirstError);
    }

    [Fact]
    public async Task StopAutoRetry_WhileWaiting_StopsFurtherAttempts()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, _, _) =>
                Task.FromException<PagedResponse<ReviewResponse>>(new HttpRequestException("refused")),
        };
        var section = new BarberReviewsSection(api, StopAfterFirstAttemptRetry());

        await InitializeSectionAsync(section);

        section.StartAutoRetry();

        // Первая пауза проходит сразу, вторая висит: попытка уже сделана, следующая ждёт.
        Assert.Equal(1, section.ReviewsAutoRetry.Attempts);
        Assert.True(section.IsAutoRetrying);

        section.StopAutoRetry();
        await section.ReviewsAutoRetryCompletion;

        Assert.Equal(1, section.ReviewsAutoRetry.Attempts);
        Assert.False(section.IsAutoRetrying);
        Assert.True(section.HasFirstError);
    }

    [Fact]
    public async Task RetryFirstPageCommand_WhileAutoRetrying_LoadsImmediatelyAndSuccessDoesNotRestart()
    {
        var api = new FakeBarberApi();
        var calls = 0;

        api.Reviews = (_, _, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<PagedResponse<ReviewResponse>>(new HttpRequestException("refused"))
                : Task.FromResult(Page(Reviews(10), NextCursor: null));
        };

        var section = new BarberReviewsSection(api, HangingRetry());

        await InitializeSectionAsync(section);

        section.StartAutoRetry();

        Assert.True(section.IsAutoRetrying);
        Assert.Equal(0, section.ReviewsAutoRetry.Attempts);

        // «Повторить» — это «повторить сейчас»: цикл останавливается, чтение идёт сразу.
        await section.RetryFirstPageCommand.ExecuteAsync(null);

        Assert.Equal(2, api.ReviewCalls.Count);
        Assert.Equal(0, section.ReviewsAutoRetry.Attempts);
        Assert.False(section.IsAutoRetrying);
        Assert.False(section.HasFirstError);
        Assert.Equal(10, section.Items.Count);
    }

    [Fact]
    public async Task RetryFirstPageCommand_WhileAutoRetrying_LoadFailsRetryable_RestartsAndKeepsRetryingUntilSuccess()
    {
        var restartGate = new TaskCompletionSource();
        var silentAttempt = new TaskCompletionSource<PagedResponse<ReviewResponse>>();
        var api = new FakeBarberApi();
        var calls = 0;

        api.Reviews = (_, _, _) =>
        {
            calls++;

            return calls switch
            {
                1 or 2 => Task.FromException<PagedResponse<ReviewResponse>>(new HttpRequestException("refused")),
                3 => silentAttempt.Task,
                _ => Task.FromResult(Page(Reviews(10), NextCursor: null)),
            };
        };

        var section = new BarberReviewsSection(api, StopThenGateRetry(restartGate.Task));

        await InitializeSectionAsync(section);

        section.StartAutoRetry();

        Assert.True(section.IsAutoRetrying);
        Assert.Equal(0, section.ReviewsAutoRetry.Attempts);

        // Кнопка при работающем цикле читает сразу: к возврату команды ручной запрос уже ушёл,
        // а перезапущенный цикл ждёт паузы — второго запроса без неё нет.
        await section.RetryFirstPageCommand.ExecuteAsync(null);

        Assert.Equal(2, calls);
        Assert.Equal(0, section.ReviewsAutoRetry.Attempts);
        Assert.True(section.IsAutoRetrying);
        Assert.Equal(NetworkText, section.FirstErrorText);

        // Повторяемый сбой кнопки возвращает цикл к жизни: тихая попытка начинается, но причина
        // на экране остаётся — иначе блок ошибки исчез бы на время запроса.
        restartGate.SetResult();

        Assert.Equal(1, section.ReviewsAutoRetry.Attempts);
        Assert.Equal(NetworkText, section.FirstErrorText);
        Assert.False(section.IsFirstLoading);
        Assert.True(section.IsAutoRetrying);

        silentAttempt.SetException(new HttpRequestException("refused"));
        await section.ReviewsAutoRetryCompletion;

        Assert.Equal(2, section.ReviewsAutoRetry.Attempts);
        Assert.Equal(4, calls);
        Assert.False(section.HasFirstError);
        Assert.Equal(10, section.Items.Count);
        Assert.False(section.IsAutoRetrying);
    }

    [Fact]
    public async Task NextPageCommand_WhileAutoRetrying_LoadsImmediatelyAndSuccessDoesNotRestart()
    {
        var api = new FakeBarberApi();
        var nextCalls = 0;

        api.Reviews = (_, cursor, _) =>
        {
            if (cursor is null)
            {
                return Task.FromResult(Page(Reviews(10), "c1"));
            }

            nextCalls++;

            return nextCalls == 1
                ? Task.FromException<PagedResponse<ReviewResponse>>(new HttpRequestException("refused"))
                : Task.FromResult(Page(Reviews(2), NextCursor: null));
        };

        var section = new BarberReviewsSection(api, HangingRetry());

        await InitializeSectionAsync(section);
        await section.NextPageCommand.ExecuteAsync(null);

        section.StartAutoRetry();

        Assert.True(section.IsAutoRetrying);
        Assert.Equal(0, section.ReviewsAutoRetry.Attempts);

        await section.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(3, api.ReviewCalls.Count);
        Assert.Equal("c1", api.ReviewCalls[^1].Cursor);
        Assert.Equal(0, section.ReviewsAutoRetry.Attempts);
        Assert.False(section.IsAutoRetrying);
        Assert.False(section.HasPageError);
        Assert.Equal(2, section.Items.Count);
        Assert.Equal("11–12 из 12", section.PageRangeText);
    }

    [Fact]
    public async Task NextPageCommand_WhileAutoRetrying_LoadFailsRetryable_RestartsAndKeepsRetryingUntilSuccess()
    {
        var restartGate = new TaskCompletionSource();
        var silentAttempt = new TaskCompletionSource<PagedResponse<ReviewResponse>>();
        var api = new FakeBarberApi();
        var nextCalls = 0;

        api.Reviews = (_, cursor, _) =>
        {
            if (cursor is null)
            {
                return Task.FromResult(Page(Reviews(10), "c1"));
            }

            nextCalls++;

            return nextCalls switch
            {
                1 or 2 => Task.FromException<PagedResponse<ReviewResponse>>(new HttpRequestException("refused")),
                3 => silentAttempt.Task,
                _ => Task.FromResult(Page(Reviews(2), NextCursor: null)),
            };
        };

        var section = new BarberReviewsSection(api, StopThenGateRetry(restartGate.Task));

        await InitializeSectionAsync(section);
        await section.NextPageCommand.ExecuteAsync(null);

        section.StartAutoRetry();

        await section.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(2, nextCalls);
        Assert.Equal(0, section.ReviewsAutoRetry.Attempts);
        Assert.True(section.IsAutoRetrying);
        Assert.Equal(10, section.Items.Count);
        Assert.True(section.HasPageError);

        // Повторяемый сбой кнопки возвращает цикл к жизни, а строки и «Повторить» остаются.
        restartGate.SetResult();

        Assert.Equal(1, section.ReviewsAutoRetry.Attempts);
        Assert.Equal(10, section.Items.Count);
        Assert.True(section.HasPageError);

        silentAttempt.SetException(new HttpRequestException("refused"));
        await section.ReviewsAutoRetryCompletion;

        Assert.Equal(2, section.ReviewsAutoRetry.Attempts);
        Assert.Equal(4, nextCalls);
        Assert.False(section.HasPageError);
        Assert.Equal(2, section.Items.Count);
        Assert.Equal("11–12 из 12", section.PageRangeText);
        Assert.False(section.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_FailedSilentAttempt_KeepsReviews()
    {
        var retry = new TaskCompletionSource<PagedResponse<ReviewResponse>>();
        var api = new FakeBarberApi();
        var nextCalls = 0;

        api.Reviews = (_, cursor, _) =>
        {
            if (cursor is null)
            {
                return Task.FromResult(Page(Reviews(10), "c1"));
            }

            nextCalls++;

            return nextCalls == 1
                ? Task.FromException<PagedResponse<ReviewResponse>>(new HttpRequestException("refused"))
                : retry.Task;
        };

        var section = new BarberReviewsSection(api, StopAfterFirstAttemptRetry());

        await InitializeSectionAsync(section);
        await section.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(NetworkText, section.PageErrorText);

        section.StartAutoRetry();

        // Тихая попытка не стирает причину, строки, курсор и кнопку: человек продолжает видеть
        // те же отзывы, пока ответа нет. Спиннер первой загрузки при этом не включается.
        Assert.Equal(1, section.ReviewsAutoRetry.Attempts);
        Assert.Equal(NetworkText, section.PageErrorText);
        Assert.Equal(10, section.Items.Count);
        Assert.True(section.CanGoNext);
        Assert.Equal("Повторить", section.NextPageButtonText);
        Assert.True(section.IsPagerVisible);
        Assert.False(section.IsFirstLoading);

        retry.SetException(ApiError(HttpStatusCode.NotFound));
        await section.ReviewsAutoRetryCompletion;

        // Неудача оставляет строки и курсор, а причину обновляет: экран говорит, чем кончилось.
        Assert.Equal(10, section.Items.Count);
        Assert.True(section.HasPageError);
        Assert.Equal(NextPageFailedText, section.PageErrorText);
        Assert.True(section.CanGoNext);
        Assert.False(section.IsAutoRetrying);
    }

    [Fact]
    public async Task StopAutoRetry_DuringSilentAttempt_KeepsReviews()
    {
        var retry = new TaskCompletionSource<PagedResponse<ReviewResponse>>();
        var api = new FakeBarberApi();
        var nextCalls = 0;

        api.Reviews = (_, cursor, _) =>
        {
            if (cursor is null)
            {
                return Task.FromResult(Page(Reviews(10), "c1"));
            }

            nextCalls++;

            return nextCalls == 1
                ? Task.FromException<PagedResponse<ReviewResponse>>(new HttpRequestException("refused"))
                : retry.Task;
        };

        var section = new BarberReviewsSection(api, StopAfterFirstAttemptRetry());

        await InitializeSectionAsync(section);
        await section.NextPageCommand.ExecuteAsync(null);

        section.StartAutoRetry();

        Assert.Equal(1, section.ReviewsAutoRetry.Attempts);
        Assert.Equal(NetworkText, section.PageErrorText);
        Assert.Equal(10, section.Items.Count);

        // Уход со страницы отменяет попытку; отменённая попытка не имеет права унести с собой
        // причину, строки и курсор — иначе вернувшийся экран остался бы без «Повторить».
        section.StopAutoRetry();
        retry.SetCanceled();
        await section.ReviewsAutoRetryCompletion;

        Assert.True(section.HasPageError);
        Assert.Equal(NetworkText, section.PageErrorText);
        Assert.Equal(10, section.Items.Count);
        Assert.True(section.CanGoNext);
        Assert.Equal("Повторить", section.NextPageButtonText);
        Assert.False(section.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_Timeout_ShowsNetworkText()
    {
        var api = new FakeBarberApi();
        var calls = 0;

        api.Reviews = (_, _, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<PagedResponse<ReviewResponse>>(new TaskCanceledException("timeout"))
                : Task.FromResult(Page(Reviews(10), NextCursor: null));
        };

        var section = new BarberReviewsSection(api, FastRetry());

        await InitializeSectionAsync(section);

        // Таймаут HttpClient приходит отменой, но токен жив: для человека это «нет связи»,
        // а не тихая отмена.
        Assert.Equal(NetworkText, section.FirstErrorText);

        section.StartAutoRetry();
        await section.ReviewsAutoRetryCompletion;

        Assert.Equal(1, section.ReviewsAutoRetry.Attempts);
        Assert.False(section.HasFirstError);
        Assert.Equal(10, section.Items.Count);
    }

    [Fact]
    public async Task AutoRetry_NextPageFailure_RetriesSameCursor()
    {
        var api = new FakeBarberApi();
        var nextCalls = 0;

        api.Reviews = (_, cursor, _) =>
        {
            if (cursor is null)
            {
                return Task.FromResult(Page(Reviews(10), "c1"));
            }

            nextCalls++;

            return nextCalls == 1
                ? Task.FromException<PagedResponse<ReviewResponse>>(new HttpRequestException("refused"))
                : Task.FromResult(Page(Reviews(2), NextCursor: null));
        };

        var section = new BarberReviewsSection(api, FastRetry());

        await InitializeSectionAsync(section);
        await section.NextPageCommand.ExecuteAsync(null);

        Assert.True(section.HasPageError);

        section.StartAutoRetry();
        await section.ReviewsAutoRetryCompletion;

        // Повтор несёт курсор упавшей страницы, а не пересобранный из текущего состояния.
        Assert.Equal("c1", api.ReviewCalls[^1].Cursor);
        Assert.Equal(10, api.ReviewCalls[^1].PageSize);

        // Успешный повтор остался на той же странице и не перескочил: показана вторая, а «Назад»
        // возвращает загруженную первую.
        Assert.Equal(2, section.Items.Count);
        Assert.Equal("11–12 из 12", section.PageRangeText);
        Assert.False(section.HasPageError);
        Assert.Equal("Дальше", section.NextPageButtonText);

        section.PreviousPageCommand.Execute(null);

        Assert.Equal(10, section.Items.Count);
        Assert.Equal("1–10 из 12", section.PageRangeText);
    }

    [Fact]
    public async Task IsAutoRetrying_TogglesWithLoopState()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, _, _) =>
                Task.FromException<PagedResponse<ReviewResponse>>(new HttpRequestException("refused")),
        };
        var section = new BarberReviewsSection(api, HangingRetry());

        await InitializeSectionAsync(section);

        section.StartAutoRetry();
        Assert.True(section.IsAutoRetrying);

        section.StopAutoRetry();
        Assert.False(section.IsAutoRetrying);

        await section.ReviewsAutoRetryCompletion;
    }

    [Fact]
    public async Task AutoRetry_TerminalAfterRetryable_DoesNotRestart()
    {
        var api = new FakeBarberApi();
        var calls = 0;

        api.Reviews = (_, _, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<PagedResponse<ReviewResponse>>(new HttpRequestException("refused"))
                : Task.FromException<PagedResponse<ReviewResponse>>(ApiError(HttpStatusCode.NotFound));
        };

        var section = new BarberReviewsSection(api, HangingRetry());

        await InitializeSectionAsync(section);

        section.StartAutoRetry();

        Assert.True(section.IsAutoRetrying);
        Assert.Equal(NetworkText, section.FirstErrorText);

        // «Повторить» останавливает цикл и читает сразу; новый окончательный сбой не имеет права
        // оживить фон памятью о прошлом Transport.
        await section.RetryFirstPageCommand.ExecuteAsync(null);

        Assert.Equal(0, section.ReviewsAutoRetry.Attempts);
        Assert.False(section.IsAutoRetrying);
        Assert.True(section.HasFirstError);
        Assert.Equal(FirstPageFailedText, section.FirstErrorText);
    }

    [Fact]
    public async Task RetryFirstPageCommand_WhenPageHidden_DoesNotStartAutoRetry()
    {
        var api = new FakeBarberApi
        {
            Reviews = (_, _, _) =>
                Task.FromException<PagedResponse<ReviewResponse>>(new HttpRequestException("refused")),
        };
        var section = new BarberReviewsSection(api, HangingRetry());

        await InitializeSectionAsync(section);

        section.StartAutoRetry();

        Assert.True(section.IsAutoRetrying);

        // Уход со страницы гасит цикл; кнопка «повторить сейчас» не имеет права включить фон
        // и оставить его работать на скрытом экране.
        section.StopAutoRetry();
        await section.ReviewsAutoRetryCompletion;

        await section.RetryFirstPageCommand.ExecuteAsync(null);

        Assert.Equal(2, api.ReviewCalls.Count);
        Assert.Equal(0, section.ReviewsAutoRetry.Attempts);
        Assert.False(section.IsAutoRetrying);
        Assert.True(section.HasFirstError);
    }

    private static Task<ApiFailureKind> InitializeSectionAsync(BarberReviewsSection section, int total = 12) =>
        section.InitializeAsync(ProfileId, "Europe/Moscow", total, SummaryText);

    /// <summary>
    /// Цикл с мгновенной паузой: секунды расписания в тесте не тратятся.
    /// </summary>
    private static AutoRetryLoop FastRetry() =>
        new(
            static (_, _) => Task.CompletedTask,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    /// <summary>
    /// Цикл, у которого первая пауза проходит сразу, а вторая висит до остановки: так видно
    /// состояние между попытками, не ожидая реального времени.
    /// </summary>
    private static AutoRetryLoop StopAfterFirstAttemptRetry()
    {
        var waits = 0;

        return new AutoRetryLoop(
            (_, cancellationToken) => Interlocked.Increment(ref waits) == 1
                ? Task.CompletedTask
                : WaitForCancellationAsync(cancellationToken),
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);
    }

    /// <summary>
    /// Цикл, у которого первая пауза висит до остановки, а следующая ждёт внешнего сигнала:
    /// так видно и работу цикла до нажатия кнопки, и его перезапуск после неё, без таймеров.
    /// </summary>
    private static AutoRetryLoop StopThenGateRetry(Task restartGate)
    {
        var waits = 0;

        return new AutoRetryLoop(
            (_, cancellationToken) =>
            {
                var wait = Interlocked.Increment(ref waits);

                return wait == 1
                    ? WaitForCancellationAsync(cancellationToken)
                    : wait == 2
                        ? restartGate
                        : Task.CompletedTask;
            },
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);
    }

    /// <summary>
    /// Цикл, который висит на первой же паузе: проверяется остановка до всякой попытки.
    /// </summary>
    private static AutoRetryLoop HangingRetry() =>
        new(
            (_, cancellationToken) => WaitForCancellationAsync(cancellationToken),
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    /// <summary>
    /// Ожидание, которое заканчивается только отменой: Stop проверяется без таймеров.
    /// </summary>
    private static Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));

        return pending.Task;
    }

    private static PagedResponse<ReviewResponse> Page(
        IReadOnlyList<ReviewResponse> items,
        string? NextCursor) =>
        new(items, NextCursor);

    private static List<ReviewResponse> Reviews(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => Review("Текст"))];

    private static ReviewResponse Review(string text) =>
        new(
            Guid.CreateVersion7(),
            ProfileId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Rating: 5,
            text,
            IsPublished: true,
            Instant,
            Instant);

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode) =>
        new(statusCode, "Request failed.", [], new Dictionary<string, string[]>());

    /// <summary>
    /// Подменённый клиент чтений: отдаёт заданную страницу и запоминает параметры запроса.
    /// </summary>
    private sealed class FakeBarberApi : IBarberSearchApi
    {
        public Func<Guid, string?, int?, Task<PagedResponse<ReviewResponse>>> Reviews { get; set; } =
            (_, _, _) => Task.FromResult(Page([], NextCursor: null));

        public List<ReviewsRequest> ReviewCalls { get; } = [];

        public Task<PagedResponse<ReviewResponse>> GetReviewsAsync(
            Guid barberProfileId,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default)
        {
            ReviewCalls.Add(new ReviewsRequest(barberProfileId, cursor, pageSize));

            return Reviews(barberProfileId, cursor, pageSize);
        }

        public Task<PagedResponse<BarberSearchItemResponse>> SearchBarbersAsync(
            BarberSearchFilters filters,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Секция отзывов не ищет мастеров.");

        public Task<IReadOnlyList<SpecializationResponse>> GetSpecializationsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Секция отзывов не читает справочник специализаций.");

        public Task<IReadOnlyList<HairServiceResponse>> GetHairServicesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Секция отзывов не читает справочник услуг.");

        public Task<IReadOnlyList<CitySuggestionResponse>> GetCitySuggestionsAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Секция отзывов не читает подсказки города.");

        public Task<BarberProfileResponse> GetBarberProfileAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Секция отзывов не читает карточку.");

        public Task<BarberSlotsResponse> GetSlotsAsync(
            Guid barberProfileId,
            DateOnly slotDate,
            Guid? hairServiceId,
            Guid? salonId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Секция отзывов не читает свободное время.");
    }

    /// <summary>
    /// Параметры запроса отзывов, чтобы проверять курсор и размер страницы.
    /// </summary>
    private sealed record ReviewsRequest(Guid BarberProfileId, string? Cursor, int? PageSize);
}
