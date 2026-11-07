using BarberSearch.Application.Notifications;
using BarberSearch.Application.Notifications.GetBarberNotifications;
using BarberSearch.Application.Notifications.GetUnreadCount;
using BarberSearch.Application.Notifications.MarkAllNotificationsRead;
using BarberSearch.Application.Notifications.MarkNotificationRead;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Notifications;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Инбокс: страница, отметка прочтения, «прочитать все» и счётчик.
/// </summary>
public class NotificationInboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_ReturnsPageAndNextCursor()
    {
        var userId = Guid.CreateVersion7();
        var notifications = new FakeBarberNotificationRepository();

        for (var index = 0; index < 3; index++)
        {
            notifications.Seed(CreateUpcoming(userId));
        }

        var handler = CreateListHandler(notifications, new FakeCurrentUser(userId));

        var first = await handler.Handle(
            new GetBarberNotificationsQuery(Cursor: null, PageSize: 2),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(2, first.Value.Items.Count);
        Assert.NotNull(first.Value.NextCursor);

        var second = await handler.Handle(
            new GetBarberNotificationsQuery(Cursor: first.Value.NextCursor, PageSize: 2),
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Single(second.Value.Items);
        Assert.Null(second.Value.NextCursor);
    }

    [Fact]
    public async Task List_IdsAndCursor_DoNotOverlap()
    {
        var userId = Guid.CreateVersion7();
        var notifications = new FakeBarberNotificationRepository();
        var ids = new List<Guid>();

        for (var index = 0; index < 4; index++)
        {
            var notification = CreateUpcoming(userId);
            ids.Add(notification.Id);
            notifications.Seed(notification);
        }

        var handler = CreateListHandler(notifications, new FakeCurrentUser(userId));

        var page = await handler.Handle(
            new GetBarberNotificationsQuery(null, 2),
            CancellationToken.None);

        var seen = page.Value.Items.Select(item => item.Id).ToList();
        Assert.Equal(2, seen.Distinct().Count());
        Assert.All(seen, id => Assert.Contains(id, ids));
    }

    [Fact]
    public async Task List_WithInvalidPageSize_ReturnsPageSizeCode()
    {
        var userId = Guid.CreateVersion7();
        var handler = CreateListHandler(
            new FakeBarberNotificationRepository(),
            new FakeCurrentUser(userId));

        var result = await handler.Handle(
            new GetBarberNotificationsQuery(null, 0),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("notification.list.pageSize", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task List_WithBrokenCursor_ReturnsCursorCode()
    {
        var userId = Guid.CreateVersion7();
        var handler = CreateListHandler(
            new FakeBarberNotificationRepository(),
            new FakeCurrentUser(userId));

        var result = await handler.Handle(
            new GetBarberNotificationsQuery("not-a-cursor", 20),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("notification.cursor.invalid", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task List_Anonymous_ReturnsUnauthorized()
    {
        var handler = CreateListHandler(
            new FakeBarberNotificationRepository(),
            FakeCurrentUser.Anonymous());

        var result = await handler.Handle(
            new GetBarberNotificationsQuery(null, 20),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.Auth.Unauthorized, result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task MarkRead_SetsReadAtFromTheClock()
    {
        var userId = Guid.CreateVersion7();
        var notifications = new FakeBarberNotificationRepository();
        var notification = CreateUpcoming(userId);
        notifications.Seed(notification);

        var handler = new MarkNotificationReadHandler(
            notifications,
            new FakeCurrentUser(userId),
            new FakeTimeProvider(Now));

        var result = await handler.Handle(
            new MarkNotificationReadCommand(notification.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, notification.ReadAt);
    }

    [Fact]
    public async Task MarkRead_Twice_KeepsTheFirstTimestamp()
    {
        var userId = Guid.CreateVersion7();
        var notifications = new FakeBarberNotificationRepository();
        var notification = CreateUpcoming(userId);
        notifications.Seed(notification);

        var clock = new FakeTimeProvider(Now);
        var handler = new MarkNotificationReadHandler(
            notifications,
            new FakeCurrentUser(userId),
            clock);

        Assert.True((await handler.Handle(
            new MarkNotificationReadCommand(notification.Id),
            CancellationToken.None)).IsSuccess);

        clock.UtcNow = Now.AddHours(1);

        var repeated = await handler.Handle(
            new MarkNotificationReadCommand(notification.Id),
            CancellationToken.None);

        Assert.True(repeated.IsSuccess);
        Assert.Equal(Now, notification.ReadAt);
    }

    [Fact]
    public async Task MarkRead_ForAnotherBarber_ReturnsNotFound()
    {
        var ownerId = Guid.CreateVersion7();
        var strangerId = Guid.CreateVersion7();
        var notifications = new FakeBarberNotificationRepository();
        var notification = CreateUpcoming(ownerId);
        notifications.Seed(notification);

        var handler = new MarkNotificationReadHandler(
            notifications,
            new FakeCurrentUser(strangerId),
            new FakeTimeProvider(Now));

        var result = await handler.Handle(
            new MarkNotificationReadCommand(notification.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.Notification.NotFound, result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Null(notification.ReadAt);
    }

    [Fact]
    public async Task MarkRead_ForUnknownNotification_ReturnsNotFound()
    {
        var userId = Guid.CreateVersion7();
        var handler = new MarkNotificationReadHandler(
            new FakeBarberNotificationRepository(),
            new FakeCurrentUser(userId),
            new FakeTimeProvider(Now));

        var result = await handler.Handle(
            new MarkNotificationReadCommand(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.Notification.NotFound, result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task MarkAll_IsIdempotentAndKeepsReadTimestamps()
    {
        var userId = Guid.CreateVersion7();
        var notifications = new FakeBarberNotificationRepository();
        var readNotification = CreateUpcoming(userId);
        Assert.True(readNotification.MarkRead(Now.AddMinutes(-10)).IsSuccess);
        notifications.Seed(readNotification);
        notifications.Seed(CreateUpcoming(userId));
        notifications.Seed(CreateUpcoming(userId));

        var clock = new FakeTimeProvider(Now);
        var handler = new MarkAllNotificationsReadHandler(
            notifications,
            new FakeCurrentUser(userId),
            clock);

        var first = await handler.Handle(
            new MarkAllNotificationsReadCommand(),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(Now.AddMinutes(-10), readNotification.ReadAt);
        Assert.All(notifications.Items, item => Assert.NotNull(item.ReadAt));

        clock.UtcNow = Now.AddMinutes(5);

        var second = await handler.Handle(
            new MarkAllNotificationsReadCommand(),
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(Now.AddMinutes(-10), readNotification.ReadAt);
        Assert.Equal(2, notifications.MarkAllReadCalls);
    }

    [Fact]
    public async Task UnreadCount_ReturnsOnlyUnreadRows()
    {
        var userId = Guid.CreateVersion7();
        var notifications = new FakeBarberNotificationRepository();
        notifications.Seed(CreateUpcoming(userId));
        notifications.Seed(CreateUpcoming(userId));

        var read = CreateUpcoming(userId);
        Assert.True(read.MarkRead(Now).IsSuccess);
        notifications.Seed(read);

        notifications.Seed(CreateUpcoming(Guid.CreateVersion7()));

        var handler = new GetUnreadCountHandler(notifications, new FakeCurrentUser(userId));

        var result = await handler.Handle(new GetUnreadCountQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    private static GetBarberNotificationsHandler CreateListHandler(
        FakeBarberNotificationRepository notifications,
        FakeCurrentUser currentUser)
        => new(notifications, currentUser);

    private static BarberNotification CreateUpcoming(Guid userId)
        => BarberNotification.Upcoming(
            userId,
            Guid.CreateVersion7(),
            Now.AddMinutes(20),
            "стрижка",
            "Барбершоп на Ленина",
            "Asia/Yekaterinburg").Value;
}
