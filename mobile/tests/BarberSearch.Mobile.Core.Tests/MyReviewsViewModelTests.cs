using System.Net;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetMyReviews;
using BarberSearch.Contracts.Reviews.UpdateReview;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Reviews;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Мои отзывы»: курсор, догрузка, обновление, правка и удаление строк.
/// </summary>
/// <remarks>
/// Список — один запрос за визит страницы и догрузка прокруткой: полный <c>GET</c> после
/// правки или удаления сдвинул бы страницы, поэтому здесь проверяется, что строка меняется
/// на месте и что ответ, пришедший после завершения сессии, отбрасывается.
/// </remarks>
public sealed class MyReviewsViewModelTests
{
    private const string LoadMoreErrorText = "Не удалось загрузить следующие отзывы.";
    private const string NetworkErrorText =
        "Нет связи с сервером. Проверьте подключение и повторите попытку.";

    [Fact]
    public async Task InitializeAsync_FirstPage_LoadsRowsAndCursor()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page(
            [ReviewOf(rating: 5, text: "Отлично"), ReviewOf(rating: 3, text: null, isPublished: false)],
            "cursor-1"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.IsAuthenticated);
        Assert.True(harness.ViewModel.ShowList);
        Assert.Equal(2, harness.ViewModel.Items.Count);
        Assert.True(harness.ViewModel.CanLoadMore);

        var first = harness.ViewModel.Items[0];
        Assert.Equal("★★★★★", first.Stars);
        Assert.Equal("5 / 5", first.RatingText);
        Assert.Equal("Опубликован", first.StatusText);
        Assert.Equal("Отлично", first.BodyText);
        Assert.Equal("15 ноября 2026", first.ReviewDateText);
        Assert.Contains("2\u00A0400\u00A0₽", first.VisitText, StringComparison.Ordinal);

        var second = harness.ViewModel.Items[1];
        Assert.Equal("На модерации", second.StatusText);
        Assert.Equal("Без комментария.", second.BodyText);

        // Первая страница запрашивается без курсора и с размером 10.
        Assert.Null(Assert.Single(harness.ReviewsApi.Cursors));
        Assert.Equal(MyReviewsViewModel.PageSize, harness.ReviewsApi.PageSizes[0]);
    }

    [Fact]
    public async Task InitializeAsync_FirstLoadRequested_OnlyOncePerVisit()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.InitializeAsync();

        Assert.Equal(1, harness.ReviewsApi.Calls);
    }

    [Fact]
    public async Task LoadMoreAsync_AppendsNextPageAndStopsAtEnd()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (cursor, _) => Task.FromResult(cursor is null
            ? Page([ReviewOf(), ReviewOf()], "cursor-1")
            : Page([ReviewOf()], null));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(3, harness.ViewModel.Items.Count);
        Assert.False(harness.ViewModel.CanLoadMore);
        Assert.Equal(2, harness.ReviewsApi.Cursors.Count);
        Assert.Null(harness.ReviewsApi.Cursors[0]);
        Assert.Equal("cursor-1", harness.ReviewsApi.Cursors[1]);
    }

    [Fact]
    public async Task RefreshAsync_ReplacesRowsAndCursor()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([ReviewOf()], "cursor-1"));
        await harness.ViewModel.InitializeAsync();

        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([ReviewOf(rating: 4), ReviewOf(), ReviewOf()], null));
        await harness.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(3, harness.ViewModel.Items.Count);
        Assert.False(harness.ViewModel.CanLoadMore);
        Assert.Equal(4, harness.ViewModel.Items[0].Rating);
        Assert.False(harness.ViewModel.IsRefreshing);
    }

    [Fact]
    public async Task RetryAsync_AfterFirstPageError_LoadsList()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromException<PagedResponse<MyReviewResponse>>(
            ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);
        Assert.Equal("Слишком много запросов. Подождите минуту и повторите.", harness.ViewModel.ErrorMessage);

        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([ReviewOf()], null));
        await harness.ViewModel.RetryCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.ShowError);
        Assert.Single(harness.ViewModel.Items);
    }

    [Fact]
    public async Task LoadMoreAsync_Error_KeepsRowsAndShowsFooterError()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (cursor, _) => cursor is null
            ? Task.FromResult(Page([ReviewOf()], "cursor-1"))
            : Task.FromException<PagedResponse<MyReviewResponse>>(
                ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Single(harness.ViewModel.Items);
        Assert.True(harness.ViewModel.ShowLoadMoreError);
        Assert.Equal(LoadMoreErrorText, harness.ViewModel.LoadMoreError);
    }

    [Fact]
    public async Task RetryLoadMoreAsync_AfterError_AppendsPage()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (cursor, _) => cursor is null
            ? Task.FromResult(Page([ReviewOf()], "cursor-1"))
            : Task.FromException<PagedResponse<MyReviewResponse>>(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([ReviewOf()], null));
        await harness.ViewModel.RetryLoadMoreCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.ShowLoadMoreError);
        Assert.Equal(2, harness.ViewModel.Items.Count);
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_ClearsSession()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromException<PagedResponse<MyReviewResponse>>(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.InitializeAsync();

        Assert.False(harness.Session.IsAuthenticated);
        Assert.False(harness.ViewModel.IsAuthenticated);
        Assert.Empty(harness.ViewModel.Items);
    }

    [Fact]
    public async Task InitializeAsync_NetworkFailure_KeepsSessionAndShowsText()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromException<PagedResponse<MyReviewResponse>>(
            new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.Session.IsAuthenticated);
        Assert.Equal(NetworkErrorText, harness.ViewModel.ErrorMessage);
        Assert.True(harness.ViewModel.ShowError);
    }

    [Fact]
    public async Task InitializeAsync_EmptyList_ShowsEmptyState()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([], null));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowEmptyState);
        Assert.Equal("Пока нет отзывов.", harness.ViewModel.EmptyStateTitle);
        Assert.Contains("«Моих записях»", harness.ViewModel.EmptyStateDescription);
    }

    [Fact]
    public async Task DeleteAsync_Success_RemovesRowAndShowsBanner()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([ReviewOf(), ReviewOf()], null));
        await harness.ViewModel.InitializeAsync();

        var error = await harness.ViewModel.DeleteAsync(harness.ViewModel.Items[0]);

        Assert.Null(error);
        Assert.Single(harness.ViewModel.Items);
        Assert.Equal("Отзыв удалён.", harness.ViewModel.Announcement);
        Assert.True(harness.ViewModel.HasAnnouncement);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_RemovesRowWithAlreadyDeletedBanner()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([ReviewOf(), ReviewOf()], null));
        await harness.ViewModel.InitializeAsync();
        harness.ReviewsApi.Delete = _ => Task.FromException(
            ApiError(HttpStatusCode.NotFound, "review.notFound"));

        var error = await harness.ViewModel.DeleteAsync(harness.ViewModel.Items[0]);

        // Отзыв уже удалён — например, с другого устройства: для списка это тот же успех.
        Assert.Null(error);
        Assert.Single(harness.ViewModel.Items);
        Assert.Equal("Этот отзыв уже удалён.", harness.ViewModel.Announcement);
    }

    [Fact]
    public async Task DeleteAsync_Forbidden_ReturnsTextAndKeepsRow()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([ReviewOf()], null));
        await harness.ViewModel.InitializeAsync();
        harness.ReviewsApi.Delete = _ => Task.FromException(
            ApiError(HttpStatusCode.Forbidden, "auth.forbidden"));

        var error = await harness.ViewModel.DeleteAsync(harness.ViewModel.Items[0]);

        Assert.Equal("Удалить отзыв может только его автор.", error);
        Assert.Single(harness.ViewModel.Items);
    }

    [Fact]
    public async Task DeleteAsync_RateLimited_ReturnsRateLimitText()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([ReviewOf()], null));
        await harness.ViewModel.InitializeAsync();
        harness.ReviewsApi.Delete = _ => Task.FromException(
            ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit"));

        var error = await harness.ViewModel.DeleteAsync(harness.ViewModel.Items[0]);

        Assert.Equal("Слишком много запросов. Подождите минуту и повторите.", error);
    }

    [Fact]
    public async Task DeleteAsync_NetworkFailure_ReturnsNetworkText()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([ReviewOf()], null));
        await harness.ViewModel.InitializeAsync();
        harness.ReviewsApi.Delete = _ => Task.FromException(new HttpRequestException("refused"));

        var error = await harness.ViewModel.DeleteAsync(harness.ViewModel.Items[0]);

        Assert.Equal(NetworkErrorText, error);
        Assert.Single(harness.ViewModel.Items);
    }

    [Fact]
    public async Task DeleteAsync_LastRowWithCursor_LoadsNextPage()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (cursor, _) => Task.FromResult(cursor is null
            ? Page([ReviewOf()], "cursor-1")
            : Page([ReviewOf(rating: 4)], null));
        await harness.ViewModel.InitializeAsync();

        await harness.ViewModel.DeleteAsync(harness.ViewModel.Items[0]);

        // Опустевшая страница при наличии курсора — не конец списка: следующая догружается.
        Assert.Single(harness.ViewModel.Items);
        Assert.Equal(4, harness.ViewModel.Items[0].Rating);
        Assert.False(harness.ViewModel.CanLoadMore);
    }

    [Fact]
    public async Task DeleteAsync_DoubleTap_DeletesOnce()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([ReviewOf()], null));
        await harness.ViewModel.InitializeAsync();

        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.ReviewsApi.Delete = async _ => await pending.Task;

        var item = harness.ViewModel.Items[0];
        var first = harness.ViewModel.DeleteAsync(item);
        var second = harness.ViewModel.DeleteAsync(item);

        pending.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, harness.ReviewsApi.DeleteCalls);
    }

    [Fact]
    public async Task ApplyUpdated_ChangesStatusOnTheRowAndShowsBanner()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([ReviewOf(text: "Старый текст")], null));
        await harness.ViewModel.InitializeAsync();

        var item = harness.ViewModel.Items[0];
        var updatedAt = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        harness.ViewModel.ApplyUpdated(item, new UpdateReviewResponse(false, updatedAt), 4, "Новый текст");

        Assert.Equal(4, item.Rating);
        Assert.Equal("Новый текст", item.BodyText);
        Assert.Equal("На модерации", item.StatusText);
        Assert.Equal(updatedAt, item.UpdatedAt);
        Assert.Equal("Отзыв обновлён.", harness.ViewModel.Announcement);

        // Строка осталась на своём месте: порядок задаётся датой создания, а не правкой.
        Assert.Same(item, harness.ViewModel.Items[0]);
    }

    [Fact]
    public async Task ApplySignedOutState_ClearsRowsAndCursor()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([ReviewOf()], "cursor-1"));
        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.ApplySignedOutState();

        Assert.Empty(harness.ViewModel.Items);
        Assert.False(harness.ViewModel.IsAuthenticated);
        Assert.False(harness.ViewModel.CanLoadMore);
        Assert.Null(harness.ViewModel.Announcement);
        Assert.False(harness.ViewModel.ShowList);
    }

    [Fact]
    public async Task InitializeAsync_StaleResponseAfterSignOut_IsDiscarded()
    {
        var harness = new Harness();
        var pending = new TaskCompletionSource<PagedResponse<MyReviewResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.ReviewsApi.GetMy = (_, _) => pending.Task;

        var loading = harness.ViewModel.InitializeAsync();
        harness.ViewModel.ApplySignedOutState();

        pending.SetResult(Page([ReviewOf()], null));
        await loading;

        // Ответ пришёл после завершения сессии — данные прошлого пользователя не всплывают.
        Assert.Empty(harness.ViewModel.Items);
        Assert.False(harness.ViewModel.IsAuthenticated);
    }

    [Fact]
    public async Task EditCommand_RaisesEditRequested()
    {
        var harness = new Harness();
        harness.ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([ReviewOf()], null));
        await harness.ViewModel.InitializeAsync();

        MyReviewListItem? requested = null;
        harness.ViewModel.EditRequested += (_, item) => requested = item;

        harness.ViewModel.EditCommand.Execute(harness.ViewModel.Items[0]);

        Assert.Same(harness.ViewModel.Items[0], requested);
    }

    private static PagedResponse<MyReviewResponse> Page(
        IReadOnlyList<MyReviewResponse> items,
        string? nextCursor) =>
        new(items, nextCursor);

    private static MyReviewResponse ReviewOf(
        int rating = 5,
        string? text = "Отлично",
        bool isPublished = true,
        string barberName = "Игорь Барбер") =>
        new(
            Guid.CreateVersion7(),
            rating,
            text,
            isPublished,
            new DateTimeOffset(2026, 11, 15, 10, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 15, 10, 30, 0, TimeSpan.Zero),
            new MyReviewBarberResponse(Guid.CreateVersion7(), barberName),
            new MyReviewVisitResponse(
                Guid.CreateVersion7(),
                new DateTimeOffset(2026, 11, 15, 10, 30, 0, TimeSpan.Zero),
                "UTC",
                2400m,
                "Стрижка",
                "Барбершоп «Тест»"));

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, string errorCode) =>
        new(statusCode, "Request failed.", [errorCode], new Dictionary<string, string[]>());

    /// <summary>
    /// Собранный экран: сессия с парой, фальшивый клиент отзывов и открытый ViewModel.
    /// </summary>
    private sealed class Harness
    {
        public Harness(bool withTokens = true)
        {
            Store = new FakeTokenStore
            {
                Tokens = withTokens ? new StoredTokens("access-1", "refresh-1") : null,
            };
            Session = new TokenSession(Store);

            ReviewsApi.GetMy = (_, _) => Task.FromResult(Page([], null));

            ViewModel = new MyReviewsViewModel(ReviewsApi, Session);
        }

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public FakeReviewsApi ReviewsApi { get; } = new();

        public MyReviewsViewModel ViewModel { get; }
    }

    /// <summary>
    /// Подменённый клиент отзывов: страницы по курсору, правка и удаление.
    /// </summary>
    private sealed class FakeReviewsApi : IReviewsApi
    {
        public int Calls { get; private set; }

        public int DeleteCalls { get; private set; }

        public List<string?> Cursors { get; } = [];

        public List<int> PageSizes { get; } = [];

        public Func<string?, int, Task<PagedResponse<MyReviewResponse>>> GetMy { get; set; } =
            (_, _) => Task.FromResult(Page([], null));

        public Func<Guid, Task> Delete { get; set; } = _ => Task.CompletedTask;

        public Func<Guid, int, string?, Task<UpdateReviewResponse>> Update { get; set; } =
            (_, rating, text) => Task.FromResult(new UpdateReviewResponse(true, DateTimeOffset.UtcNow));

        public Task<Guid> CreateReviewAsync(
            Guid bookingId,
            int rating,
            string? text,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран «Мои отзывы» не создаёт отзывы.");

        public Task<PagedResponse<MyReviewResponse>> GetMyReviewsAsync(
            string? cursor,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Cursors.Add(cursor);
            PageSizes.Add(pageSize);

            return GetMy(cursor, pageSize);
        }

        public Task<UpdateReviewResponse> UpdateReviewAsync(
            Guid reviewId,
            int rating,
            string? text,
            CancellationToken cancellationToken = default) =>
            Update(reviewId, rating, text);

        public Task DeleteReviewAsync(Guid reviewId, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;

            return Delete(reviewId);
        }
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
