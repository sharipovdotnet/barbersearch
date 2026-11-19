using System.Net;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels.Admin;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки очереди мастеров: фильтры, курсор, догрузка, обновление и состояния экрана.
/// </summary>
/// <remarks>
/// Список — один запрос на появление страницы и догрузка прокруткой; возврат из карточки
/// после решения перечитывает первую страницу. Здесь проверяется и то, что решённая строка
/// уходит из очереди не локальной правкой, а новым ответом сервера.
/// </remarks>
public sealed class AdminMastersViewModelTests
{
    private const string LoadErrorText = "Не удалось загрузить мастеров.";
    private const string LoadMoreErrorText = "Не удалось загрузить ещё мастеров.";
    private const string NetworkErrorText =
        "Нет связи с сервером. Проверьте подключение и повторите попытку.";

    [Fact]
    public async Task InitializeAsync_PendingQueue_LoadsFirstPageAndRows()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page([MasterOf()], null));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsPendingSelected);
        Assert.True(viewModel.ShowList);
        Assert.False(viewModel.ShowEmptyState);
        Assert.True(viewModel.HasUpdatedAt);

        var item = Assert.Single(viewModel.Items);

        // Строка несёт ровно то, что отдаёт AdminBarberResponse: имени, email, грейда
        // и состояния достаточно, чтобы очередь читалась без карточки.
        Assert.Equal("Иван Мастер", item.DisplayName);
        Assert.Equal("ivan@example.com", item.Email);
        Assert.Equal("ИМ", item.Initials);
        Assert.Equal("Мастер", item.GradeText);
        Assert.Equal("На проверке", item.StatusText);
        Assert.Contains("4,8", item.RatingText, StringComparison.Ordinal);
        Assert.Contains("12 отзывов", item.RatingText, StringComparison.Ordinal);
        Assert.False(item.HasAvatar);
        Assert.True(item.ShowInitials);
        Assert.False(item.HasAvatarBadge);

        // Первая страница запрашивается без курсора, выбранным фильтром и размером веба.
        Assert.Equal(AdminMasterFilter.Pending, harness.MastersApi.Filters[0]);
        Assert.Null(harness.MastersApi.Cursors[0]);
        Assert.Equal(AdminMastersViewModel.PageSize, harness.MastersApi.PageSizes[0]);
    }

    [Fact]
    public async Task InitializeAsync_SecondAppearance_ReloadsQueueSilently()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page([MasterOf()], null));
        await harness.ViewModel.InitializeAsync();

        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page(
            [MasterOf(), MasterOf(displayName: "Пётр Мастер")],
            null));
        await harness.ViewModel.InitializeAsync();

        Assert.Equal(2, harness.MastersApi.Calls);
        Assert.Equal(2, harness.ViewModel.Items.Count);

        // Возврат из карточки после решения — это и есть обновление очереди.
        Assert.False(harness.ViewModel.IsFirstLoading);
    }

    [Fact]
    public async Task LoadMoreAsync_AppendsNextPageAndStopsAtEnd()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, cursor, _) => Task.FromResult(cursor is null
            ? Page([MasterOf()], "cursor-1")
            : Page([MasterOf(displayName: "Пётр Мастер")], null));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.ViewModel.Items.Count);
        Assert.False(harness.ViewModel.CanLoadMore);
        Assert.Equal("cursor-1", harness.MastersApi.Cursors[1]);
    }

    [Fact]
    public async Task RefreshAsync_ReplacesRowsAndKeepsFilter()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page([MasterOf()], null));
        await harness.ViewModel.InitializeAsync();

        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page(
            [MasterOf(displayName: "Пётр Мастер")],
            null));
        await harness.ViewModel.RefreshCommand.ExecuteAsync(null);

        var item = Assert.Single(harness.ViewModel.Items);

        Assert.Equal("Пётр Мастер", item.DisplayName);
        Assert.Equal(AdminMasterFilter.Pending, harness.MastersApi.Filters[^1]);
        Assert.False(harness.ViewModel.IsRefreshing);
    }

    [Fact]
    public async Task ShowAllAsync_ResetsRowsAndRequestsAllFilter()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page([MasterOf()], null));
        await harness.ViewModel.InitializeAsync();

        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page(
            [MasterOf(displayName: "Пётр Мастер", isVerified: true)],
            null));
        await harness.ViewModel.ShowAllCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsAllSelected);
        Assert.False(harness.ViewModel.IsPendingSelected);
        Assert.Equal(AdminMasterFilter.All, harness.MastersApi.Filters[^1]);
        Assert.Null(harness.MastersApi.Cursors[^1]);

        var item = Assert.Single(harness.ViewModel.Items);

        Assert.Equal("Подтверждён", item.StatusText);
    }

    [Fact]
    public async Task ShowPendingAsync_WhenAlreadySelected_DoesNotReload()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page([MasterOf()], null));
        await harness.ViewModel.InitializeAsync();

        await harness.ViewModel.ShowPendingCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.MastersApi.Calls);
    }

    [Fact]
    public async Task InitializeAsync_PendingEmpty_ShowsQueueEmptyState()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page([], null));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowEmptyState);
        Assert.Equal("Очередь проверки пуста", harness.ViewModel.EmptyStateTitle);
        Assert.Contains("Все профили подтверждены", harness.ViewModel.EmptyStateDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowHiddenAsync_Empty_ShowsHiddenEmptyState()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.ShowHiddenCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.ShowEmptyState);
        Assert.Equal("Скрытых профилей нет", harness.ViewModel.EmptyStateTitle);
        Assert.Contains("Никто не убран из поиска", harness.ViewModel.EmptyStateDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_FirstPageFailure_ShowsErrorThenRetryLoads()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, _, _) => Task.FromException<PagedResponse<AdminBarberResponse>>(
            ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);
        Assert.Equal(LoadErrorText, harness.ViewModel.ErrorMessage);
        Assert.False(harness.ViewModel.ShowContent);

        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page([MasterOf()], null));
        await harness.ViewModel.RetryCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.ShowError);
        Assert.Single(harness.ViewModel.Items);
    }

    [Fact]
    public async Task LoadMoreAsync_Error_KeepsRowsAndShowsFooterError()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, cursor, _) => cursor is null
            ? Task.FromResult(Page([MasterOf()], "cursor-1"))
            : Task.FromException<PagedResponse<AdminBarberResponse>>(
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
        harness.MastersApi.GetPage = (_, cursor, _) => cursor is null
            ? Task.FromResult(Page([MasterOf()], "cursor-1"))
            : Task.FromException<PagedResponse<AdminBarberResponse>>(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page(
            [MasterOf(displayName: "Пётр Мастер")],
            null));
        await harness.ViewModel.RetryLoadMoreCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.ShowLoadMoreError);
        Assert.Equal(2, harness.ViewModel.Items.Count);
        Assert.Equal("cursor-1", harness.MastersApi.Cursors[^1]);
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_ClearsSessionAndShowsSignIn()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, _, _) => Task.FromException<PagedResponse<AdminBarberResponse>>(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.InitializeAsync();

        Assert.False(harness.Session.IsAuthenticated);
        Assert.True(harness.ViewModel.ShowSignInRequired);
        Assert.False(harness.ViewModel.ShowContent);
        Assert.Empty(harness.ViewModel.Items);
    }

    [Fact]
    public async Task InitializeAsync_Forbidden_ShowsForbiddenWithoutRetry()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, _, _) => Task.FromException<PagedResponse<AdminBarberResponse>>(
            ApiError(HttpStatusCode.Forbidden, "auth.forbidden"));

        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.StartAutoRetry();

        Assert.True(harness.ViewModel.ShowForbidden);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
    }

    [Fact]
    public async Task InitializeAsync_Avatar_IsResolvedAndBadgeFollowsModeration()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page(
            [
                MasterOf(avatarUrl: "/api/media/avatars/ivan.webp", avatarStatus: PhotoModerationStatus.Pending),
                MasterOf(
                    displayName: "Пётр Мастер",
                    avatarStatus: PhotoModerationStatus.Rejected,
                    avatarBlocked: true),
            ],
            null));

        await harness.ViewModel.InitializeAsync();

        var first = harness.ViewModel.Items[0];

        Assert.True(first.HasAvatar);
        Assert.Equal(
            "http://localhost:5243/api/media/avatars/ivan.webp?size=sm",
            first.AvatarImageUrl);
        Assert.Equal("На проверке", first.AvatarBadgeText);

        // Блокировка важнее отказа: кадр скрыт администратором, и это главное состояние.
        Assert.Equal("Фото скрыто", harness.ViewModel.Items[1].AvatarBadgeText);
    }

    [Fact]
    public async Task InitializeAsync_HiddenProfile_StatusExplainsTheSearch()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page(
            [MasterOf(isActive: false)],
            null));

        await harness.ViewModel.InitializeAsync();

        Assert.Equal("Скрыт из поиска", Assert.Single(harness.ViewModel.Items).StatusText);
    }

    [Fact]
    public async Task StartAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        var harness = new Harness(autoRetry: FastRetry());
        var calls = 0;

        harness.MastersApi.GetPage = (_, _, _) =>
        {
            calls++;

            return calls <= 2
                ? Task.FromException<PagedResponse<AdminBarberResponse>>(
                    new HttpRequestException("refused"))
                : Task.FromResult(Page([MasterOf()], null));
        };

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowError);
        Assert.Equal(NetworkErrorText, viewModel.ErrorMessage);

        viewModel.StartAutoRetry();
        await viewModel.AutoRetryCompletion;

        Assert.Equal(2, viewModel.AutoRetry.Attempts);
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
        harness.MastersApi.GetPage = (_, _, _) => Task.FromException<PagedResponse<AdminBarberResponse>>(
            ApiError(statusCode, "error.terminal"));

        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.StartAutoRetry();

        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
    }

    [Fact]
    public async Task LoadMoreAutoRetry_TransportFailure_RetriesTheFailedCursor()
    {
        var harness = new Harness(loadMoreAutoRetry: FastRetry());
        var calls = 0;

        harness.MastersApi.GetPage = (_, cursor, _) =>
        {
            calls++;

            return cursor is null
                ? Task.FromResult(Page([MasterOf()], "cursor-1"))
                : calls <= 3
                    ? Task.FromException<PagedResponse<AdminBarberResponse>>(
                        new HttpRequestException("refused"))
                    : Task.FromResult(Page([MasterOf(displayName: "Пётр Мастер")], null));
        };

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();
        await viewModel.LoadMoreAutoRetryCompletion;

        Assert.Equal(2, viewModel.Items.Count);
        Assert.False(viewModel.ShowLoadMoreError);
        Assert.Equal("cursor-1", harness.MastersApi.Cursors[^1]);
    }

    [Fact]
    public async Task ApplySignedOutState_ClearsRowsAndCursor()
    {
        var harness = new Harness();
        harness.MastersApi.GetPage = (_, _, _) => Task.FromResult(Page([MasterOf()], "cursor-1"));
        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.ApplySignedOutState();

        Assert.Empty(harness.ViewModel.Items);
        Assert.True(harness.ViewModel.ShowSignInRequired);
        Assert.False(harness.ViewModel.CanLoadMore);
        Assert.Null(harness.ViewModel.UpdatedAtText);
    }

    /// <summary>
    /// Цикл с мгновенной паузой: секунды расписания в тесте не тратятся.
    /// </summary>
    private static AutoRetryLoop FastRetry() =>
        new(
            static (_, _) => Task.CompletedTask,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    private static PagedResponse<AdminBarberResponse> Page(
        IReadOnlyList<AdminBarberResponse> items,
        string? nextCursor) =>
        new(items, nextCursor);

    private static AdminBarberResponse MasterOf(
        string displayName = "Иван Мастер",
        bool isActive = true,
        bool isVerified = false,
        string? avatarUrl = null,
        bool avatarBlocked = false,
        PhotoModerationStatus avatarStatus = PhotoModerationStatus.Approved) =>
        new(
            Guid.CreateVersion7(),
            displayName,
            "ivan@example.com",
            Grade.MiddleBarber,
            4.8m,
            12,
            isActive,
            isVerified,
            new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 15, 12, 30, 0, TimeSpan.Zero),
            avatarUrl,
            avatarBlocked,
            avatarStatus);

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, string errorCode) =>
        new(statusCode, "Request failed.", [errorCode], new Dictionary<string, string[]>());

    /// <summary>
    /// Собранный экран: сессия с парой, фальшивый клиент мастеров и открытый ViewModel.
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

            MastersApi.GetPage = (_, _, _) => Task.FromResult(Page([], null));

            ViewModel = new AdminMastersViewModel(
                MastersApi,
                ApiOptions,
                Session,
                TimeProvider.System,
                autoRetry,
                loadMoreAutoRetry);
        }

        public BarberSearchApiOptions ApiOptions { get; } =
            BarberSearchApiOptions.FromBaseAddress("http://localhost:5243");

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public FakeAdminMastersApi MastersApi { get; } = new();

        public AdminMastersViewModel ViewModel { get; }
    }

    /// <summary>
    /// Подменённый клиент мастеров: страницы по фильтру и курсору, карточка и записи.
    /// </summary>
    private sealed class FakeAdminMastersApi : IAdminMastersApi
    {
        public int Calls { get; private set; }

        public List<AdminMasterFilter> Filters { get; } = [];

        public List<string?> Cursors { get; } = [];

        public List<int> PageSizes { get; } = [];

        public Func<AdminMasterFilter, string?, int, Task<PagedResponse<AdminBarberResponse>>> GetPage { get; set; } =
            (_, _, _) => Task.FromResult(Page([], null));

        public Task<PagedResponse<AdminBarberResponse>> GetMastersAsync(
            AdminMasterFilter filter,
            string? cursor,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Filters.Add(filter);
            Cursors.Add(cursor);
            PageSizes.Add(pageSize);

            return GetPage(filter, cursor, pageSize);
        }

        public Task<AdminBarberDetailResponse> GetMasterAsync(
            Guid masterId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Список не читает карточку мастера.");

        public Task VerifyMasterAsync(Guid barberProfileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Список не подтверждает профили.");

        public Task UnverifyMasterAsync(Guid barberProfileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Список не снимает подтверждение.");
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
