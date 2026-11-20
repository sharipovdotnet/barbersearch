using System.Net;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels.Admin;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки очереди заявок «Стать мастером»: фильтр, курсор, догрузка, отказ в доступе и
/// автоповтор чтения.
/// </summary>
/// <remarks>
/// Список — курсорная страница без общего числа; фильтр повторяет значения API. Смена фильтра
/// начинает другую выборку, поэтому проверяется, что запоздавший ответ прошлого фильтра не
/// попадает в список. Решения по заявке живут в карточке и здесь не проверяются.
/// </remarks>
public sealed class AdminRequestsViewModelTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InitializeAsync_FirstPage_LoadsRowsAndCursor()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page(
            [RequestOf(), RequestOf(displayName: "Пётр", status: BarberRequestStatus.Approved)],
            "cursor-1"));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsQueueFilter);
        Assert.True(viewModel.ShowList);
        Assert.False(viewModel.ShowEmptyState);
        Assert.Equal(2, viewModel.Items.Count);
        Assert.True(viewModel.CanLoadMore);
        Assert.True(viewModel.HasUpdatedAt);

        var first = viewModel.Items[0];

        Assert.Equal("Игорь Мастер", first.DisplayName);
        Assert.Equal("Игорь Иванов", first.ApplicantName);
        Assert.Equal("Москва", first.City);
        Assert.Equal("5 лет", first.ExperienceText);
        Assert.Equal("На рассмотрении", first.StatusText);
        Assert.True(first.IsPending);
        Assert.False(string.IsNullOrEmpty(first.SubmittedAtText));
        Assert.Contains("Москва", first.MetaText, StringComparison.Ordinal);
        Assert.Contains("5 лет", first.MetaText, StringComparison.Ordinal);

        var second = viewModel.Items[1];

        Assert.Equal("Одобрена", second.StatusText);
        Assert.True(second.IsApproved);

        // Первая страница запрашивается без курсора, с фильтром очереди и размером 20.
        Assert.Equal(AdminRequestListFilter.Pending, Assert.Single(harness.Api.Filters));
        Assert.Null(Assert.Single(harness.Api.Cursors));
        Assert.Equal(AdminRequestsViewModel.PageSize, harness.Api.PageSizes[0]);
    }

    [Fact]
    public async Task InitializeAsync_SecondAppearance_ReplacesRowsSilently()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page([RequestOf(displayName: "Старый")], null));

        await harness.ViewModel.InitializeAsync();

        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page([RequestOf(displayName: "Новый")], "cursor-2"));

        await harness.ViewModel.InitializeAsync();

        // Возврат из карточки после решения — это и есть обновление: строка заменена.
        Assert.Single(harness.ViewModel.Items);
        Assert.Equal("Новый", harness.ViewModel.Items[0].DisplayName);
        Assert.True(harness.ViewModel.CanLoadMore);
    }

    [Fact]
    public async Task InitializeAsync_EmptyQueue_ShowsQueueEmptyState()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page([], null));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowEmptyState);
        Assert.Equal("Заявок нет", harness.ViewModel.EmptyStateTitle);
        Assert.Contains("Очередь пуста", harness.ViewModel.EmptyStateDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectFilterAsync_All_LoadsAllAndUsesAllWording()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page([], null));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.SelectFilterCommand.ExecuteAsync(AdminRequestListFilter.All);

        Assert.True(harness.ViewModel.IsAllFilter);
        Assert.Equal(AdminRequestListFilter.All, harness.Api.Filters[^1]);
        Assert.Equal("Заявок с таким статусом нет", harness.ViewModel.EmptyStateTitle);
        Assert.Contains("Попробуйте очередь", harness.ViewModel.EmptyStateDescription, StringComparison.Ordinal);
        Assert.Null(harness.Api.Cursors[^1]);
    }

    [Fact]
    public async Task SelectFilterAsync_StalePendingResponse_IsDiscarded()
    {
        var harness = new Harness();
        var pending = new TaskCompletionSource<PagedResponse<AdminBarberRequestResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        harness.Api.GetPage = (filter, _, _) => filter == AdminRequestListFilter.Pending
            ? pending.Task
            : Task.FromResult(Page([RequestOf(displayName: "Из всех")], null));

        var first = harness.ViewModel.InitializeAsync();

        // Пока очередь висит, человек переключился на «Все»: ответ очереди уже не нужен.
        await harness.ViewModel.SelectFilterCommand.ExecuteAsync(AdminRequestListFilter.All);

        pending.SetResult(Page([RequestOf(displayName: "Из очереди")], null));
        await first;

        Assert.Single(harness.ViewModel.Items);
        Assert.Equal("Из всех", harness.ViewModel.Items[0].DisplayName);
        Assert.True(harness.ViewModel.IsAllFilter);
    }

    [Fact]
    public async Task LoadMoreAsync_AppendsNextPageAndStopsAtEnd()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, cursor, _) => Task.FromResult(cursor is null
            ? Page([RequestOf(), RequestOf()], "cursor-1")
            : Page([RequestOf(displayName: "Третий")], null));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(3, harness.ViewModel.Items.Count);
        Assert.Equal("Третий", harness.ViewModel.Items[2].DisplayName);
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
            ? Task.FromResult(Page([RequestOf()], "cursor-1"))
            : Task.FromException<PagedResponse<AdminBarberRequestResponse>>(
                ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Single(harness.ViewModel.Items);
        Assert.True(harness.ViewModel.ShowLoadMoreError);
        Assert.True(harness.ViewModel.CanLoadMore);
        Assert.Contains("Не удалось загрузить ещё заявки.", harness.ViewModel.LoadMoreError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryLoadMoreAsync_AfterError_AppendsPage()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, cursor, _) => cursor is null
            ? Task.FromResult(Page([RequestOf()], "cursor-1"))
            : Task.FromException<PagedResponse<AdminBarberRequestResponse>>(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal("cursor-1", harness.Api.Cursors[^1]);

        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page([RequestOf(displayName: "Догруженный")], null));
        await harness.ViewModel.RetryLoadMoreCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.ShowLoadMoreError);
        Assert.Equal(2, harness.ViewModel.Items.Count);
    }

    [Fact]
    public async Task RetryAsync_AfterFirstPageError_LoadsList()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromException<PagedResponse<AdminBarberRequestResponse>>(
            ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);
        Assert.Equal("Слишком много запросов. Подождите минуту и повторите.", harness.ViewModel.ErrorMessage);

        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page([RequestOf()], null));
        await harness.ViewModel.RetryCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.ShowError);
        Assert.Single(harness.ViewModel.Items);
    }

    [Fact]
    public async Task InitializeAsync_Forbidden_ShowsForbiddenStateAndHidesRows()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromException<PagedResponse<AdminBarberRequestResponse>>(
            ApiError(HttpStatusCode.Forbidden, "auth.forbidden"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowForbidden);
        Assert.False(harness.ViewModel.ShowList);
        Assert.False(harness.ViewModel.ShowError);
        Assert.Empty(harness.ViewModel.Items);
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_ClearsSessionAndShowsSignIn()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromException<PagedResponse<AdminBarberRequestResponse>>(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.Session.EnsureLoadedAsync();
        await harness.ViewModel.InitializeAsync();

        Assert.False(harness.Session.IsAuthenticated);
        Assert.Null(harness.Store.Tokens);
        Assert.True(harness.ViewModel.ShowSignInRequired);
        Assert.False(harness.ViewModel.ShowList);
        Assert.Empty(harness.ViewModel.Items);
    }

    [Fact]
    public async Task InitializeAsync_NetworkFailure_KeepsSessionAndShowsText()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromException<PagedResponse<AdminBarberRequestResponse>>(
            new HttpRequestException("refused"));

        await harness.Session.EnsureLoadedAsync();
        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.Session.IsAuthenticated);
        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.ErrorMessage);
        Assert.True(harness.ViewModel.ShowError);
    }

    [Fact]
    public async Task RefreshAsync_ReplacesRowsAndCursor()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page([RequestOf()], "cursor-1"));

        await harness.ViewModel.InitializeAsync();

        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page(
            [RequestOf(displayName: "Новый"), RequestOf(displayName: "Второй")],
            null));

        await harness.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.ViewModel.Items.Count);
        Assert.Equal("Новый", harness.ViewModel.Items[0].DisplayName);
        Assert.False(harness.ViewModel.CanLoadMore);
        Assert.False(harness.ViewModel.IsRefreshing);
    }

    [Fact]
    public async Task ApplySignedOutState_ClearsRowsAndShowsSignIn()
    {
        var harness = new Harness();
        harness.Api.GetPage = (_, _, _) => Task.FromResult(Page([RequestOf()], "cursor-1"));

        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.ApplySignedOutState();

        Assert.Empty(harness.ViewModel.Items);
        Assert.False(harness.ViewModel.CanLoadMore);
        Assert.False(harness.ViewModel.HasUpdatedAt);
        Assert.True(harness.ViewModel.ShowSignInRequired);
        Assert.False(harness.ViewModel.ShowList);
    }

    [Fact]
    public async Task StartAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        var harness = new Harness(autoRetry: FastRetry());
        var calls = 0;

        harness.Api.GetPage = (_, _, _) =>
        {
            calls++;

            return calls <= 3
                ? Task.FromException<PagedResponse<AdminBarberRequestResponse>>(
                    new HttpRequestException("refused"))
                : Task.FromResult(Page([RequestOf()], null));
        };

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowError);

        viewModel.StartAutoRetry();
        await viewModel.AutoRetryCompletion;

        Assert.Equal(3, viewModel.AutoRetry.Attempts);
        Assert.Single(viewModel.Items);
        Assert.False(viewModel.ShowError);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task StartAutoRetry_TerminalFailure_DoesNotStart(HttpStatusCode statusCode)
    {
        var harness = new Harness(autoRetry: FastRetry());

        harness.Api.GetPage = (_, _, _) =>
            Task.FromException<PagedResponse<AdminBarberRequestResponse>>(
                ApiError(statusCode, "error.terminal"));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    /// <summary>
    /// Цикл с мгновенной паузой: секунды расписания в тесте не тратятся.
    /// </summary>
    private static AutoRetryLoop FastRetry() =>
        new(
            static (_, _) => Task.CompletedTask,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    private static PagedResponse<AdminBarberRequestResponse> Page(
        IReadOnlyList<AdminBarberRequestResponse> items,
        string? nextCursor) =>
        new(items, nextCursor);

    private static AdminBarberRequestResponse RequestOf(
        string displayName = "Игорь Мастер",
        string applicantName = "Игорь Иванов",
        string city = "Москва",
        int yearsOfExperience = 5,
        BarberRequestStatus status = BarberRequestStatus.Pending,
        DateTimeOffset? reviewedAt = null,
        string? rejectionReason = null) =>
        new(
            Guid.CreateVersion7(),
            status,
            displayName,
            yearsOfExperience,
            city,
            "Стригу с 2018 года.",
            false,
            new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero),
            reviewedAt,
            rejectionReason,
            null,
            new AdminBarberRequestApplicantResponse(
                Guid.CreateVersion7(),
                applicantName,
                "igor@example.com"));

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, string errorCode) =>
        new(statusCode, "Request failed.", [errorCode], new Dictionary<string, string[]>());

    /// <summary>
    /// Часы с фиксированным моментом: «Обновлено в …» не зависит от времени запуска.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// Собранный экран: сессия с парой, подменённый клиент заявок и открытый ViewModel.
    /// </summary>
    private sealed class Harness
    {
        public Harness(
            bool withTokens = true,
            AutoRetryLoop? autoRetry = null,
            AutoRetryLoop? loadMoreAutoRetry = null)
        {
            Store = new FakeTokenStore
            {
                Tokens = withTokens ? new StoredTokens("access-1", "refresh-1") : null,
            };
            Session = new TokenSession(Store);

            ViewModel = new AdminRequestsViewModel(
                Api,
                Session,
                new FixedTimeProvider(Now),
                autoRetry,
                loadMoreAutoRetry);
        }

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public FakeAdminRequestsApi Api { get; } = new();

        public AdminRequestsViewModel ViewModel { get; }
    }

    /// <summary>
    /// Подменённый клиент заявок: страницы по фильтру и курсору.
    /// </summary>
    private sealed class FakeAdminRequestsApi : IAdminRequestsApi
    {
        public List<AdminRequestListFilter> Filters { get; } = [];

        public List<string?> Cursors { get; } = [];

        public List<int> PageSizes { get; } = [];

        public Func<AdminRequestListFilter, string?, int, Task<PagedResponse<AdminBarberRequestResponse>>> GetPage { get; set; } =
            (_, _, _) => Task.FromResult(Page([], null));

        public Task<PagedResponse<AdminBarberRequestResponse>> GetRequestsAsync(
            AdminRequestListFilter filter,
            string? cursor,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            Filters.Add(filter);
            Cursors.Add(cursor);
            PageSizes.Add(pageSize);

            return GetPage(filter, cursor, pageSize);
        }

        public Task<AdminBarberRequestResponse> GetRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Очередь не читает карточку.");

        public Task ApproveRequestAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Очередь не одобряет заявки.");

        public Task RejectRequestAsync(
            Guid requestId,
            string reason,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Очередь не отклоняет заявки.");
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
