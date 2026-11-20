using System.Net;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Notifications;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки инбокса уведомлений мастера: первая страница, keyset-«Дальше», отметки прочтения,
/// счётчик непрочитанных и два независимых цикла повтора чтения.
/// </summary>
/// <remarks>
/// <c>404</c> и пустая страница — не ошибка, а пустое состояние: инбокс мог быть удалён или
/// ещё не создан. Счётчик непрочитанных читается тихо: сбой бейджа не превращает рабочий
/// экран в ошибку, а нижней оценкой служат непрочитанные строки уже загруженной страницы.
/// </remarks>
public sealed class BarberNotificationsViewModelTests
{
    private const string LoadMoreFailedText = "Не удалось загрузить ещё уведомления.";

    private const string MarkAllFailedText = "Не удалось прочитать уведомления.";

    private const string AllReadText = "Все уведомления прочитаны.";

    [Fact]
    public async Task ActivateAsync_LoadsFirstPageWithCursorAndServerUnreadCount()
    {
        var harness = new Harness();

        harness.Api.Page = _ => Task.FromResult(new PagedResponse<BarberNotificationResponse>(
        [
            BarberCabinetData.Notification(),
            BarberCabinetData.Notification(BarberCabinetData.SecondNotificationId, isRead: true),
        ],
        "cursor-2"));

        harness.Api.UnreadCount = () => Task.FromResult(5);

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        var request = Assert.Single(harness.Api.PageRequests);

        Assert.Null(request.Cursor);
        Assert.Equal(BarberNotificationsViewModel.PageSize, request.PageSize);
        Assert.Equal(2, viewModel.Items.Count);
        Assert.True(viewModel.ShowContent);
        Assert.True(viewModel.HasItems);
        Assert.True(viewModel.IsAutoRetrying == false);
        Assert.True(viewModel.Items[0].IsUnread);
        Assert.False(viewModel.Items[1].IsUnread);
        Assert.Equal("Через 30 минут запись", viewModel.Items[0].Title);
        Assert.True(viewModel.HasMore);
        Assert.True(viewModel.CanLoadMore);

        Assert.Equal(5, viewModel.UnreadCount);
        Assert.Equal("5", viewModel.UnreadBadgeText);
        Assert.True(viewModel.HasUnread);
        Assert.True(viewModel.CanMarkAllRead);
    }

    [Fact]
    public async Task ActivateAsync_ServerCountOverCap_Shows99PlusBadge()
    {
        var harness = new Harness();

        harness.Api.Page = _ => Task.FromResult(new PagedResponse<BarberNotificationResponse>(
            [BarberCabinetData.Notification()],
            null));

        harness.Api.UnreadCount = () => Task.FromResult(120);

        await harness.ViewModel.ActivateAsync();

        Assert.Equal(120, harness.ViewModel.UnreadCount);
        Assert.Equal("99+", harness.ViewModel.UnreadBadgeText);
    }

    [Fact]
    public async Task ActivateAsync_UnreadCountFailure_IsSilentAndFallsBackToRows()
    {
        var harness = new Harness();

        harness.Api.Page = _ => Task.FromResult(new PagedResponse<BarberNotificationResponse>(
        [
            BarberCabinetData.Notification(),
            BarberCabinetData.Notification(BarberCabinetData.SecondNotificationId),
        ],
        null));

        harness.Api.UnreadCount = () => Task.FromException<int>(new HttpRequestException("offline"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.ShowContent);
        Assert.Equal(2, viewModel.UnreadCount);
        Assert.Equal("2", viewModel.UnreadBadgeText);
        Assert.Equal(1, harness.Api.UnreadCalls);
    }

    [Fact]
    public async Task ActivateAsync_NotFound_ShowsEmptyState()
    {
        var harness = new Harness();

        harness.Api.Page = _ => Task.FromException<PagedResponse<BarberNotificationResponse>>(
            BarberCabinetApiError.Create(HttpStatusCode.NotFound, "notification.notFound"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Empty(viewModel.Items);
        Assert.True(viewModel.HasLoaded);
        Assert.True(viewModel.ShowEmpty);
        Assert.False(viewModel.ShowError);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal("Уведомлений пока нет.", viewModel.EmptyText);
        Assert.False(viewModel.HasMore);
    }

    [Fact]
    public async Task ActivateAsync_TransportFailure_RetriesFirstPageUntilLoaded()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness(firstPageRetry: BarberCabinetRetry.Gated(gate.Task));
        var calls = 0;

        harness.Api.Page = _ =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<PagedResponse<BarberNotificationResponse>>(
                    new HttpRequestException("offline"))
                : Task.FromResult(new PagedResponse<BarberNotificationResponse>(
                    [BarberCabinetData.Notification()],
                    null));
        };

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            viewModel.ErrorMessage);
        Assert.True(viewModel.ShowError);
        Assert.True(viewModel.IsAutoRetrying);

        gate.SetResult();

        await viewModel.FirstPageRetryCompletion;

        Assert.Equal(2, calls);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Single(viewModel.Items);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task LoadMore_AppendsNextPageAndAdvancesCursor()
    {
        var harness = new Harness();
        var cursor = "cursor-1";

        harness.Api.Page = requested => Task.FromResult(
            requested is null
                ? new PagedResponse<BarberNotificationResponse>(
                    [BarberCabinetData.Notification()],
                    cursor)
                : new PagedResponse<BarberNotificationResponse>(
                    [BarberCabinetData.Notification(BarberCabinetData.SecondNotificationId)],
                    null));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.Api.PageRequests.Count);
        Assert.Equal(cursor, harness.Api.PageRequests[1].Cursor);
        Assert.Equal(2, viewModel.Items.Count);
        Assert.False(viewModel.HasMore);
        Assert.False(viewModel.CanLoadMore);
        Assert.False(viewModel.IsLoadingMore);
        Assert.Null(viewModel.LoadMoreError);
    }

    [Fact]
    public async Task LoadMore_TerminalFailure_KeepsRowsCursorAndReason()
    {
        var harness = new Harness();

        harness.Api.Page = requested => requested is null
            ? Task.FromResult(new PagedResponse<BarberNotificationResponse>(
                [BarberCabinetData.Notification()],
                "cursor-1"))
            : Task.FromException<PagedResponse<BarberNotificationResponse>>(
                BarberCabinetApiError.Create(HttpStatusCode.BadRequest, "notification.cursor"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Items);
        Assert.Equal(LoadMoreFailedText, viewModel.LoadMoreError);
        Assert.True(viewModel.HasLoadMoreError);
        Assert.True(viewModel.CanLoadMore);
        Assert.Equal(0, viewModel.NextPageAutoRetry.Attempts);
        Assert.False(viewModel.IsLoadingMore);
    }

    [Fact]
    public async Task LoadMore_TransportFailure_RetriesSameCursorUntilLoaded()
    {
        var harness = new Harness(nextPageRetry: BarberCabinetRetry.Fast());
        var nextPageCalls = 0;

        harness.Api.Page = requested =>
        {
            if (requested is null)
            {
                return Task.FromResult(new PagedResponse<BarberNotificationResponse>(
                    [BarberCabinetData.Notification()],
                    "cursor-1"));
            }

            nextPageCalls++;

            return nextPageCalls == 1
                ? Task.FromException<PagedResponse<BarberNotificationResponse>>(
                    new HttpRequestException("offline"))
                : Task.FromResult(new PagedResponse<BarberNotificationResponse>(
                    [BarberCabinetData.Notification(BarberCabinetData.SecondNotificationId)],
                    null));
        };

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.LoadMoreCommand.ExecuteAsync(null);
        await viewModel.NextPageRetryCompletion;

        Assert.Equal(2, nextPageCalls);
        Assert.Equal(2, viewModel.Items.Count);
        Assert.Null(viewModel.LoadMoreError);
        Assert.Equal("cursor-1", harness.Api.PageRequests[1].Cursor);
        Assert.Equal("cursor-1", harness.Api.PageRequests[2].Cursor);
        Assert.False(viewModel.IsLoadMoreAutoRetrying);
    }

    [Fact]
    public async Task MarkAllRead_MarksRowsAndZeroesBadge()
    {
        var harness = new Harness();

        harness.Api.Page = _ => Task.FromResult(new PagedResponse<BarberNotificationResponse>(
        [
            BarberCabinetData.Notification(),
            BarberCabinetData.Notification(BarberCabinetData.SecondNotificationId),
        ],
        null));

        harness.Api.UnreadCount = () => Task.FromResult(2);

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.MarkAllReadCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.Api.MarkAllCalls);
        Assert.All(viewModel.Items, item => Assert.False(item.IsUnread));
        Assert.Equal(0, viewModel.UnreadCount);
        Assert.False(viewModel.HasUnread);
        Assert.False(viewModel.CanMarkAllRead);
        Assert.Equal(AllReadText, viewModel.Announcement);
        Assert.Null(viewModel.MarkAllError);
        Assert.False(viewModel.IsMarkingAllRead);
    }

    [Fact]
    public async Task MarkAllRead_RateLimited_ShowsReasonAndKeepsRowsUnread()
    {
        var harness = new Harness();

        harness.Api.Page = _ => Task.FromResult(new PagedResponse<BarberNotificationResponse>(
            [BarberCabinetData.Notification()],
            null));

        harness.Api.UnreadCount = () => Task.FromResult(1);

        await harness.ViewModel.ActivateAsync();

        harness.Api.MarkAllRead = () => Task.FromException(
            BarberCabinetApiError.Create(HttpStatusCode.TooManyRequests, "rate.limited"));

        var viewModel = harness.ViewModel;

        await viewModel.MarkAllReadCommand.ExecuteAsync(null);

        Assert.Equal(
            "Слишком много запросов. Подождите минуту и повторите.",
            viewModel.MarkAllError);
        Assert.True(viewModel.HasMarkAllError);
        Assert.True(viewModel.Items[0].IsUnread);
        Assert.Equal(1, viewModel.UnreadCount);
        Assert.True(viewModel.CanMarkAllRead);
        Assert.Null(viewModel.Announcement);

        // Это запись: тихого повтора нет.
        Assert.Equal(1, harness.Api.MarkAllCalls);
    }

    [Fact]
    public async Task OpenAsync_UnreadRow_MarksItReadAndRaisesOpenRequested()
    {
        var harness = new Harness();
        var opened = 0;

        harness.Api.Page = _ => Task.FromResult(new PagedResponse<BarberNotificationResponse>(
            [BarberCabinetData.Notification()],
            null));

        harness.Api.UnreadCount = () => Task.FromResult(5);

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        viewModel.OpenRequested += (_, _) => opened++;

        await viewModel.OpenCommand.ExecuteAsync(viewModel.Items[0]);

        Assert.Equal([BarberCabinetData.NotificationId], harness.Api.ReadIds);
        Assert.False(viewModel.Items[0].IsUnread);
        Assert.Equal(4, viewModel.UnreadCount);
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task Deactivate_StopsFirstPageRetryLoop()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness(firstPageRetry: BarberCabinetRetry.Gated(gate.Task));

        harness.Api.Page = _ => Task.FromException<PagedResponse<BarberNotificationResponse>>(
            new HttpRequestException("offline"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsAutoRetrying);
        Assert.True(viewModel.FirstPageAutoRetry.IsRunning);

        viewModel.Deactivate();

        Assert.False(viewModel.FirstPageAutoRetry.IsRunning);
        Assert.False(viewModel.IsAutoRetrying);
        Assert.True(viewModel.FirstPageRetryCompletion.IsCompleted);
    }

    /// <summary>
    /// Собранный инбокс: подменённый клиент, часы и два цикла повтора чтения.
    /// </summary>
    private sealed class Harness
    {
        public Harness(
            AutoRetryLoop? firstPageRetry = null,
            AutoRetryLoop? nextPageRetry = null)
        {
            ViewModel = new BarberNotificationsViewModel(
                Api,
                new FixedTimeProvider(BarberCabinetFixtures.Now),
                firstPageRetry,
                nextPageRetry);

            ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);
        }

        public FakeBarberInboxApi Api { get; } = new();

        public BarberNotificationsViewModel ViewModel { get; }
    }
}
