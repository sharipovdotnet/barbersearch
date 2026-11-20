using System.Net;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.Admin;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels.Admin;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки очереди отзывов админки: два сегмента, курсор, догрузка и обновление.
/// </summary>
/// <remarks>
/// Очередь перечитывается при каждом появлении страницы: возврат из карточки после решения —
/// это и есть обновление. Здесь проверяется, что смена сегмента читает первую страницу заново,
/// что запоздавший ответ прошлой очереди не попадает в новую и что ошибка догрузки не стирает
/// уже показанные строки.
/// </remarks>
public sealed class AdminReviewsViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private const string NetworkErrorText =
        "Нет связи с сервером. Проверьте подключение и повторите попытку.";

    [Fact]
    public async Task InitializeAsync_PendingFirstPage_LoadsRowsAndCursor()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page(
            [
                ReviewOf(rating: 4, text: "Отличная стрижка", createdAt: Now.AddDays(-2)),
                ReviewOf(rating: 5, text: null),
            ],
            "cursor-1"));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowContent);
        Assert.Equal(2, viewModel.Items.Count);
        Assert.True(viewModel.CanLoadMore);
        Assert.Equal(AdminReviewStatusFilter.Pending, Assert.Single(harness.Api.Statuses));
        Assert.Null(harness.Api.Cursors[0]);
        Assert.Equal(AdminReviewsViewModel.PageSize, harness.Api.PageSizes[0]);

        var first = viewModel.Items[0];

        Assert.Equal("★★★★☆", first.Stars);
        Assert.Equal("4 / 5", first.RatingText);
        Assert.Equal("Дмитрий К.", first.CustomerName);
        Assert.Equal("Мастер: Иван Мастер", first.MasterLine);
        Assert.Equal("Отличная стрижка", first.TextPreview);
        Assert.Equal("ждёт 2 дня", first.WaitingText);
        Assert.Equal("На модерации", first.StatusText);
        Assert.False(first.IsPublished);

        // Оценка без комментария остаётся строкой списка с подписью, а не пустым местом.
        Assert.Equal("Без комментария.", viewModel.Items[1].TextPreview);
    }

    [Fact]
    public async Task InitializeAsync_SecondAppearance_ReloadsSilently()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page([ReviewOf()], null));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.InitializeAsync();

        // Возврат из карточки — это и есть обновление очереди, без отдельного события.
        Assert.Equal(2, harness.Api.Calls);
        Assert.Single(harness.ViewModel.Items);
        Assert.False(harness.ViewModel.IsFirstLoading);
    }

    [Fact]
    public async Task ShowPublishedAsync_SwitchingSegment_ReloadsFromFirstPage()
    {
        var harness = new Harness();
        harness.Api.GetPage = (status, _, _) => Task.FromResult(status == AdminReviewStatusFilter.Pending
            ? Page([ReviewOf(isPublished: false)], null)
            : Page([ReviewOf(isPublished: true, createdAt: Now.AddDays(-1))], null));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.ShowPublishedCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.Equal(
            new[] { AdminReviewStatusFilter.Pending, AdminReviewStatusFilter.Published },
            harness.Api.Statuses);
        Assert.Null(harness.Api.Cursors[0]);
        Assert.Null(harness.Api.Cursors[1]);
        Assert.True(viewModel.IsPublishedSelected);
        Assert.True(Assert.Single(viewModel.Items).IsPublished);
        Assert.Equal("Опубликованных отзывов пока нет", viewModel.EmptyStateTitle);
        Assert.StartsWith("опубликован", viewModel.Items[0].WaitingText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadMoreAsync_MorePages_AppendsAndStopsAtEnd()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, cursor, _) => Task.FromResult(cursor is null
            ? Page([ReviewOf(), ReviewOf()], "cursor-1")
            : Page([ReviewOf()], null));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(3, harness.ViewModel.Items.Count);
        Assert.False(harness.ViewModel.CanLoadMore);
        Assert.Equal(2, harness.Api.Cursors.Count);
        Assert.Null(harness.Api.Cursors[0]);
        Assert.Equal("cursor-1", harness.Api.Cursors[1]);
    }

    [Fact]
    public async Task LoadMoreAsync_Error_KeepsRowsAndShowsFooterError()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, cursor, _) => cursor is null
            ? Task.FromResult(Page([ReviewOf()], "cursor-1"))
            : Task.FromException<PagedResponse<AdminReviewResponse>>(
                ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Single(harness.ViewModel.Items);
        Assert.True(harness.ViewModel.ShowLoadMoreError);
        Assert.Equal("Не удалось загрузить ещё отзывы.", harness.ViewModel.LoadMoreError);
    }

    [Fact]
    public async Task RetryLoadMoreAsync_AfterNetworkError_AppendsPage()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, cursor, _) => cursor is null
            ? Task.FromResult(Page([ReviewOf()], "cursor-1"))
            : Task.FromException<PagedResponse<AdminReviewResponse>>(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page([ReviewOf()], null));

        await harness.ViewModel.RetryLoadMoreCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.ShowLoadMoreError);
        Assert.Equal(2, harness.ViewModel.Items.Count);
    }

    [Fact]
    public async Task InitializeAsync_RateLimited_ShowsRetryableText()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromException<PagedResponse<AdminReviewResponse>>(
            ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);
        Assert.Equal(
            "Слишком много запросов. Подождите минуту и повторите.",
            harness.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_NetworkFailure_ShowsNetworkText()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromException<PagedResponse<AdminReviewResponse>>(
            new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);
        Assert.Equal(NetworkErrorText, harness.ViewModel.ErrorMessage);
        Assert.True(harness.Session.IsAuthenticated);
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_ClearsSessionAndShowsSignIn()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromException<PagedResponse<AdminReviewResponse>>(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.InitializeAsync();

        Assert.False(harness.Session.IsAuthenticated);
        Assert.True(harness.ViewModel.ShowSignInRequired);
        Assert.False(harness.ViewModel.ShowContent);
        Assert.Empty(harness.ViewModel.Items);
    }

    [Fact]
    public async Task InitializeAsync_Forbidden_ShowsForbiddenState()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromException<PagedResponse<AdminReviewResponse>>(
            ApiError(HttpStatusCode.Forbidden, "auth.forbidden"));

        await harness.ViewModel.InitializeAsync();

        // Роль не появится от повтора: состояние отказа окончательное и без «Повторить».
        Assert.True(harness.ViewModel.ShowForbidden);
        Assert.False(harness.ViewModel.ShowContent);
        Assert.False(harness.ViewModel.ShowError);
    }

    [Fact]
    public async Task InitializeAsync_EmptyQueue_ShowsWebEmptyState()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page([], null));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowEmptyState);
        Assert.Equal("Очередь пуста", harness.ViewModel.EmptyStateTitle);
        Assert.Contains("Все отзывы проверены", harness.ViewModel.EmptyStateDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_ReloadsFirstPageInPlace()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page([ReviewOf(rating: 3)], null));

        await harness.ViewModel.InitializeAsync();

        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page(
            [ReviewOf(rating: 5), ReviewOf(rating: 4)],
            null));

        await harness.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.ViewModel.Items.Count);
        Assert.Equal(5, harness.ViewModel.Items[0].Rating);
        Assert.False(harness.ViewModel.IsRefreshing);
    }

    [Fact]
    public async Task RetryAsync_AfterServerError_LoadsRows()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromException<PagedResponse<AdminReviewResponse>>(
            ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);

        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page([ReviewOf()], null));

        await harness.ViewModel.RetryCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.ShowError);
        Assert.Single(harness.ViewModel.Items);
    }

    [Fact]
    public async Task StartAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        var harness = new Harness();
        var calls = 0;

        harness.Api.GetPage = (_, _, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<PagedResponse<AdminReviewResponse>>(new HttpRequestException("refused"))
                : Task.FromResult(Page([ReviewOf()], null));
        };

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);

        harness.ViewModel.StartAutoRetry();

        // Цикл останавливается сам, когда попытка удалась: ждать расписание не нужно.
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(2, calls);
        Assert.False(harness.ViewModel.ShowError);
        Assert.Single(harness.ViewModel.Items);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task ShowPublishedAsync_WhilePendingRequestInFlight_DropsStaleResponse()
    {
        var harness = new Harness();
        var pendingResponse = new TaskCompletionSource<PagedResponse<AdminReviewResponse>>();
        var pendingRequested = new TaskCompletionSource();

        harness.Api.GetPage = (status, _, _) =>
        {
            if (status == AdminReviewStatusFilter.Published)
            {
                return Task.FromResult(Page([ReviewOf(isPublished: true)], null));
            }

            pendingRequested.TrySetResult();

            return pendingResponse.Task;
        };

        var pendingLoad = harness.ViewModel.InitializeAsync();

        await pendingRequested.Task;

        // Сегмент уже переключили, и опубликованная очередь ответила первой. Запоздавший
        // ответ модерации не имеет права вернуть свои строки в чужой список.
        await harness.ViewModel.ShowPublishedCommand.ExecuteAsync(null);

        pendingResponse.SetResult(Page([ReviewOf(isPublished: false)], null));
        await pendingLoad;

        Assert.True(Assert.Single(harness.ViewModel.Items).IsPublished);
    }

    private static AdminReviewResponse ReviewOf(
        int rating = 4,
        string? text = "Отличная стрижка",
        bool isPublished = false,
        DateTimeOffset? createdAt = null,
        string customerName = "Дмитрий Крылов",
        string barberName = "Иван Мастер",
        decimal? barberRating = 4.8m,
        int reviewsCount = 12,
        bool isVerified = true,
        bool isActive = true)
    {
        var moment = createdAt ?? Now.AddDays(-2);

        return new AdminReviewResponse(
            Guid.CreateVersion7(),
            rating,
            text,
            isPublished,
            moment,
            moment,
            new AdminReviewBarberResponse(
                Guid.CreateVersion7(),
                barberName,
                barberRating,
                reviewsCount,
                isActive,
                isVerified),
            new AdminReviewCustomerResponse(
                Guid.CreateVersion7(),
                customerName,
                "dmitry@example.com"),
            new AdminReviewVisitResponse(
                Guid.CreateVersion7(),
                new DateTimeOffset(2026, 11, 14, 22, 30, 0, TimeSpan.Zero),
                2400m,
                "Стрижка",
                "Салон «Тест»",
                "Europe/Moscow"));
    }

    private static PagedResponse<AdminReviewResponse> Page(
        IReadOnlyList<AdminReviewResponse> items,
        string? nextCursor) =>
        new(items, nextCursor);

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, string errorCode) =>
        new(statusCode, "Request failed.", [errorCode], new Dictionary<string, string[]>());

    /// <summary>
    /// Цикл с мгновенной паузой: секунды расписания в тесте не тратятся.
    /// </summary>
    private static AutoRetryLoop FastRetry() =>
        new(
            static (_, _) => Task.CompletedTask,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    /// <summary>
    /// Часы, зафиксированные на моменте теста: «ждёт 2 дня» не зависит от дня запуска.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// Собранный экран: сессия с парой, фальшивый клиент и открытый ViewModel.
    /// </summary>
    private sealed class Harness
    {
        public Harness()
        {
            Store = new FakeTokenStore
            {
                Tokens = new StoredTokens("access-1", "refresh-1"),
            };
            Session = new TokenSession(Store);

            Api.GetPage = (_, _, _) => Task.FromResult(Page([], null));

            ViewModel = new AdminReviewsViewModel(
                Api,
                Session,
                new FixedTimeProvider(Now),
                FastRetry(),
                FastRetry());
        }

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public FakeAdminReviewsApi Api { get; } = new();

        public AdminReviewsViewModel ViewModel { get; }
    }

    /// <summary>
    /// Подменённый клиент админских отзывов: страницы по статусу и курсору.
    /// </summary>
    private sealed class FakeAdminReviewsApi : IAdminReviewsApi
    {
        public int Calls { get; private set; }

        public List<AdminReviewStatusFilter> Statuses { get; } = [];

        public List<string?> Cursors { get; } = [];

        public List<int> PageSizes { get; } = [];

        public Func<AdminReviewStatusFilter, string?, int, Task<PagedResponse<AdminReviewResponse>>> GetPage { get; set; } =
            (_, _, _) => Task.FromResult(Page([], null));

        public Task<PagedResponse<AdminReviewResponse>> GetReviewsAsync(
            AdminReviewStatusFilter status,
            string? cursor,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Statuses.Add(status);
            Cursors.Add(cursor);
            PageSizes.Add(pageSize);

            return GetPage(status, cursor, pageSize);
        }

        public Task<AdminReviewResponse> GetReviewAsync(
            Guid reviewId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Очередь не читает отдельную карточку.");

        public Task PublishReviewAsync(Guid reviewId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Очередь не публикует отзывы.");

        public Task HideReviewAsync(Guid reviewId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Очередь не скрывает отзывы.");
    }

    /// <summary>
    /// Подменённое хранилище пары.
    /// </summary>
    private sealed class FakeTokenStore : ITokenStore
    {
        public StoredTokens? Tokens { get; set; }

        public Task<StoredTokens?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Tokens);

        public Task SaveAsync(StoredTokens tokens, CancellationToken cancellationToken = default)
        {
            Tokens = tokens;

            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Tokens = null;

            return Task.CompletedTask;
        }
    }
}
