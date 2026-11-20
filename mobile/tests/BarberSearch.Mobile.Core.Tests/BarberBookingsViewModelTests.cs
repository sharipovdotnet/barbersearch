using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки раздела «Записи» кабинета мастера: слияние дня, три вида, пейджинг, действия
/// над визитом и тихое автообновление, которое уступает другим операциям.
/// </summary>
/// <remarks>
/// «Сегодня» считается по календарю устройства, поэтому моменты визитов строятся от
/// локальной даты <see cref="BarberCabinetFixtures.TodayAt"/>, а не от UTC: тест не зависит
/// от пояса машины. Первые страницы обоих списков приходят из подменённого клиента; паузы
/// циклов повтора подменяются задачами, чтобы «Пробуем снова…» проверялось без времени.
/// </remarks>
public sealed class BarberBookingsViewModelTests
{
    private const string ConfirmFailedText = "Не удалось подтвердить запись.";

    [Fact]
    public async Task ActivateAsync_FirstLoad_BuildsTodayFromBothScopes()
    {
        var harness = new Harness();
        var first = Booking(BarberCabinetFixtures.BookingId, start: BarberCabinetFixtures.TodayAt(10));
        var second = Booking(BarberCabinetFixtures.SecondBookingId, start: BarberCabinetFixtures.TodayAt(14));
        var history = Booking(
            Guid.Parse("9e100000-0000-4000-8000-0000000000b3"),
            BookingStatus.Completed,
            BarberCabinetFixtures.TodayAt(12));

        harness.Bookings.Page = (scope, cursor, _, _) => Task.FromResult(cursor is not null
            ? EmptyPage()
            : scope == "past"
                ? Page([history])
                : Page([first, second]));

        harness.ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.False(viewModel.IsFirstLoading);
        Assert.True(viewModel.IsToday);
        Assert.True(viewModel.ShowList);
        Assert.Equal(
            [BarberCabinetFixtures.BookingId, history.Id, second.Id],
            viewModel.Items.Where(item => !item.IsGap).Select(item => item.Id));
        Assert.Equal(2, viewModel.Items.Count(item => item.IsGap));
        Assert.Contains("3 визита", viewModel.DaySubtitleText, StringComparison.Ordinal);
        Assert.Contains("2 окна", viewModel.DaySubtitleText, StringComparison.Ordinal);
        Assert.NotNull(viewModel.UpdatedAt);
        Assert.Equal(
            ["upcoming", "past"],
            harness.Bookings.PageRequests.Select(request => request.Scope));
    }

    [Fact]
    public async Task SelectUpcomingCommand_ShowsOnlyUpcomingFirstPage()
    {
        var harness = new Harness();
        var first = Booking(BarberCabinetFixtures.BookingId, start: BarberCabinetFixtures.TodayAt(10));
        var second = Booking(BarberCabinetFixtures.SecondBookingId, start: BarberCabinetFixtures.TodayAt(14));

        harness.Bookings.Page = (scope, _, _, _) => Task.FromResult(
            scope == "past"
                ? Page([Booking(Guid.NewGuid(), BookingStatus.Completed, BarberCabinetFixtures.TodayAt(12))])
                : Page([first, second]));

        harness.ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);
        await harness.ViewModel.ActivateAsync();

        await harness.ViewModel.SelectUpcomingCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsUpcoming);
        Assert.False(viewModel.ShowDayHeader);
        Assert.Equal(
            [BarberCabinetFixtures.BookingId, second.Id],
            viewModel.Items.Select(item => item.Id));
        Assert.Equal(2, harness.Bookings.PageRequests.Count);
    }

    [Fact]
    public async Task SelectPastCommand_ShowsHistoryRows()
    {
        var harness = new Harness();
        var history = Booking(
            BarberCabinetFixtures.SecondBookingId,
            BookingStatus.Completed,
            BarberCabinetFixtures.TodayAt(9));

        harness.Bookings.Page = (scope, _, _, _) => Task.FromResult(
            scope == "past"
                ? Page([history])
                : Page([Booking(BarberCabinetFixtures.BookingId, start: BarberCabinetFixtures.TodayAt(10))]));

        harness.ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);
        await harness.ViewModel.ActivateAsync();

        await harness.ViewModel.SelectPastCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsPast);
        Assert.Equal([BarberCabinetFixtures.SecondBookingId], viewModel.Items.Select(item => item.Id));
        Assert.Equal(2, harness.Bookings.PageRequests.Count);
    }

    [Fact]
    public async Task Pagination_GoNextThenGoPrev_LoadsSecondPageOnceAndReturnsCache()
    {
        var harness = new Harness();
        var first = Booking(BarberCabinetFixtures.BookingId, start: BarberCabinetFixtures.TodayAt(10));
        var second = Booking(BarberCabinetFixtures.SecondBookingId, start: BarberCabinetFixtures.TodayAt(14));

        harness.Bookings.Page = (scope, cursor, _, _) => Task.FromResult(
            scope == "past"
                ? EmptyPage()
                : cursor is null
                    ? Page([first], "cursor-1")
                    : Page([second]));

        harness.ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);
        await harness.ViewModel.ActivateAsync();
        await harness.ViewModel.SelectUpcomingCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.HasNext);
        Assert.Equal(1, viewModel.PageNumber);

        await harness.ViewModel.GoNextCommand.ExecuteAsync(null);

        Assert.Equal("cursor-1", harness.Bookings.PageRequests[^1].Cursor);
        Assert.Equal(2, viewModel.PageNumber);
        Assert.Equal(20, viewModel.Offset);
        Assert.True(viewModel.HasPrev);
        Assert.Equal([BarberCabinetFixtures.SecondBookingId], viewModel.Items.Select(item => item.Id));

        var requestsAfterNext = harness.Bookings.PageRequests.Count;

        harness.ViewModel.GoPrevCommand.Execute(null);

        Assert.Equal(1, viewModel.PageNumber);
        Assert.Equal(0, viewModel.Offset);
        Assert.Equal([BarberCabinetFixtures.BookingId], viewModel.Items.Select(item => item.Id));
        Assert.Equal(requestsAfterNext, harness.Bookings.PageRequests.Count);
    }

    [Fact]
    public async Task ApplyNote_SetsNoteAndAnnouncement()
    {
        var harness = new Harness();
        var item = await harness.LoadSingleUpcomingAsync();

        harness.ViewModel.ApplyNote(item, "  Покороче сзади  ");

        Assert.Equal("Покороче сзади", item.Booking?.BarberNote);
        Assert.True(item.HasBarberNote);
        Assert.Contains("Покороче сзади", item.BarberNoteText, StringComparison.Ordinal);
        Assert.Equal("Заметка сохранена.", harness.ViewModel.Announcement);
        Assert.True(harness.ViewModel.HasAnnouncement);
    }

    [Fact]
    public async Task ApplyRescheduled_SetsBoundsAndAnnouncement()
    {
        var harness = new Harness();
        var item = await harness.LoadSingleUpcomingAsync();
        var startAt = BarberCabinetFixtures.TodayAt(16);
        var slot = new BarberSlotResponse(startAt, startAt.AddHours(1));

        harness.ViewModel.ApplyRescheduled(item, slot);

        Assert.Equal(startAt, item.StartAt);
        Assert.Equal(slot.EndAt, item.EndAt);
        Assert.Equal(
            string.Concat("Визит перенесён на ", item.MomentText, "."),
            harness.ViewModel.Announcement);
    }

    [Fact]
    public async Task OnBookingCreated_FromPast_SwitchesToTodayAndReloadsBothScopes()
    {
        var harness = new Harness();
        var row = Booking(BarberCabinetFixtures.BookingId, start: BarberCabinetFixtures.TodayAt(10));

        harness.Bookings.Page = (scope, _, _, _) => Task.FromResult(
            scope == "past" ? EmptyPage() : Page([row]));

        harness.ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);
        await harness.ViewModel.ActivateAsync();
        await harness.ViewModel.SelectPastCommand.ExecuteAsync(null);

        var requestsBefore = harness.Bookings.PageRequests.Count;

        await harness.ViewModel.OnBookingCreated("Иван Петров");

        Assert.True(harness.ViewModel.IsToday);
        Assert.Equal("Клиент записан: Иван Петров.", harness.ViewModel.Announcement);
        Assert.Equal(requestsBefore + 2, harness.Bookings.PageRequests.Count);
        Assert.Contains(harness.ViewModel.Items, item => item.Id == BarberCabinetFixtures.BookingId);
    }

    [Fact]
    public async Task ConfirmAsync_PendingRow_AppliesConfirmation()
    {
        var harness = new Harness();
        var item = await harness.LoadSingleUpcomingAsync(BookingStatus.Pending);

        var confirmed = await harness.ViewModel.ConfirmAsync(item);

        Assert.True(confirmed);
        Assert.Equal(BookingStatus.Confirmed, item.Status);
        Assert.Equal([item.Id], harness.Bookings.ConfirmRequests);
        Assert.Equal("Запись подтверждена.", harness.ViewModel.Announcement);
        Assert.False(item.IsBusy);
    }

    [Fact]
    public async Task ConfirmAsync_NotFound_ShowsNotFoundText()
    {
        var harness = new Harness();
        var item = await harness.LoadSingleUpcomingAsync(BookingStatus.Pending);

        harness.Bookings.Confirm = (_, _) => Task.FromException(
            BarberCabinetApiError.Create(HttpStatusCode.NotFound, "booking.notFound"));

        var confirmed = await harness.ViewModel.ConfirmAsync(item);

        Assert.False(confirmed);
        Assert.Equal("Запись не найдена.", item.ActionError);
        Assert.True(item.HasActionError);
        Assert.False(item.CanReloadAfterError);
        Assert.Null(harness.ViewModel.Announcement);
        Assert.False(item.IsBusy);
        Assert.NotEqual(ConfirmFailedText, item.ActionError);
    }

    [Fact]
    public async Task CancelAsync_Conflict_ShowsConflictTextAndAsksToReload()
    {
        var harness = new Harness();
        var item = await harness.LoadSingleUpcomingAsync(BookingStatus.Pending);

        harness.Bookings.Cancel = (_, _, _) => Task.FromException(
            BarberCabinetApiError.Create(HttpStatusCode.Conflict, "booking.update.concurrencyConflict"));

        var cancelled = await harness.ViewModel.CancelAsync(item, "Клиент не пришёл");

        Assert.False(cancelled);
        Assert.Equal("Запись уже изменилась — обновите список.", item.ActionError);
        Assert.True(item.CanReloadAfterError);
        Assert.Null(harness.ViewModel.Announcement);
    }

    [Fact]
    public async Task AutoRefreshAsync_WhenNotVisible_DoesNothing()
    {
        var harness = new Harness();

        harness.ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);

        await harness.ViewModel.AutoRefreshAsync();

        Assert.Empty(harness.Bookings.PageRequests);
        Assert.Empty(harness.ViewModel.Items);
    }

    [Fact]
    public async Task AutoRefreshAsync_SecondPage_DoesNothing()
    {
        var harness = new Harness();
        var first = Booking(BarberCabinetFixtures.BookingId, start: BarberCabinetFixtures.TodayAt(10));
        var second = Booking(BarberCabinetFixtures.SecondBookingId, start: BarberCabinetFixtures.TodayAt(14));

        harness.Bookings.Page = (scope, cursor, _, _) => Task.FromResult(
            scope == "past"
                ? EmptyPage()
                : cursor is null
                    ? Page([first], "cursor-1")
                    : Page([second]));

        harness.ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);
        await harness.ViewModel.ActivateAsync();
        await harness.ViewModel.SelectUpcomingCommand.ExecuteAsync(null);
        await harness.ViewModel.GoNextCommand.ExecuteAsync(null);

        var requestsBefore = harness.Bookings.PageRequests.Count;

        await harness.ViewModel.AutoRefreshAsync();

        Assert.Equal(requestsBefore, harness.Bookings.PageRequests.Count);
    }

    [Fact]
    public async Task AutoRefreshAsync_SilentFailure_KeepsRowsAndHidesReason()
    {
        var harness = new Harness();
        await harness.LoadSingleUpcomingAsync();

        var itemsBefore = harness.ViewModel.Items.Count;

        harness.Bookings.Page = (_, _, _, _) => Task.FromException<PagedResponse<BarberBookingResponse>>(
            new HttpRequestException("offline"));

        await harness.ViewModel.AutoRefreshAsync();

        Assert.Equal(itemsBefore, harness.ViewModel.Items.Count);
        Assert.Null(harness.ViewModel.ErrorMessage);
        Assert.False(harness.ViewModel.HasError);
        Assert.True(harness.ViewModel.ShowList);
    }

    [Fact]
    public async Task AutoRefreshAsync_Today_ReplacesRowsSilently()
    {
        var harness = new Harness();
        var first = Booking(BarberCabinetFixtures.BookingId, start: BarberCabinetFixtures.TodayAt(10));
        var replacement = Booking(
            BarberCabinetFixtures.SecondBookingId,
            start: BarberCabinetFixtures.TodayAt(11));

        harness.Bookings.Page = (scope, _, _, _) => Task.FromResult(
            scope == "past" ? EmptyPage() : Page([first]));

        harness.ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);
        await harness.ViewModel.ActivateAsync();

        harness.Bookings.Page = (scope, _, _, _) => Task.FromResult(
            scope == "past" ? EmptyPage() : Page([replacement]));

        await harness.ViewModel.AutoRefreshAsync();

        Assert.Equal(
            [BarberCabinetFixtures.SecondBookingId],
            harness.ViewModel.Items.Select(item => item.Id));
        Assert.Null(harness.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task AutoRefreshAsync_DuringAction_DoesNothing()
    {
        var harness = new Harness();
        var item = await harness.LoadSingleUpcomingAsync(BookingStatus.Pending);
        var confirmGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        harness.Bookings.Confirm = (_, _) => confirmGate.Task;

        var confirm = harness.ViewModel.ConfirmAsync(item);
        var requestsBefore = harness.Bookings.PageRequests.Count;

        await harness.ViewModel.AutoRefreshAsync();

        Assert.Equal(requestsBefore, harness.Bookings.PageRequests.Count);
        Assert.True(item.IsBusy);

        confirmGate.SetResult();
        await confirm;
    }

    [Fact]
    public async Task FirstPageRetry_KeepsRowsAndReasonUntilAnswer()
    {
        var retryGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCall = new TaskCompletionSource<PagedResponse<BarberBookingResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var history = Booking(
            BarberCabinetFixtures.SecondBookingId,
            BookingStatus.Completed,
            BarberCabinetFixtures.TodayAt(9));
        var upcoming = Booking(BarberCabinetFixtures.BookingId, start: BarberCabinetFixtures.TodayAt(10));
        var harness = new Harness(BarberCabinetRetry.Gated(retryGate.Task));
        var calls = 0;

        harness.Bookings.Page = (scope, _, _, _) =>
        {
            if (scope == "past")
            {
                return Task.FromResult(Page([history]));
            }

            calls++;

            if (calls == 1)
            {
                return Task.FromException<PagedResponse<BarberBookingResponse>>(
                    new HttpRequestException("offline"));
            }

            secondCallStarted.TrySetResult();

            return secondCall.Task;
        };

        harness.ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);

        await harness.ViewModel.ActivateAsync();

        // День частичный: строка истории осталась, причина «Ближайших» ждёт ответа.
        Assert.Single(harness.ViewModel.Items);
        Assert.NotNull(harness.ViewModel.UpcomingError);
        Assert.True(harness.ViewModel.IsAutoRetrying);

        retryGate.SetResult();

        await secondCallStarted.Task;

        // Тихая попытка не стирает ни строки, ни причину и не показывает индикатор загрузки.
        Assert.Single(harness.ViewModel.Items);
        Assert.NotNull(harness.ViewModel.UpcomingError);
        Assert.False(harness.ViewModel.IsFirstLoading);
        Assert.True(harness.ViewModel.ShowList);

        secondCall.SetResult(Page([upcoming]));

        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(
            [BarberCabinetFixtures.SecondBookingId, BarberCabinetFixtures.BookingId],
            harness.ViewModel.Items.Select(item => item.Id));
        Assert.Null(harness.ViewModel.UpcomingError);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task ActivateAsync_CancelledLaterToday_IsNotNextClient()
    {
        var harness = new Harness();

        // «Сегодня» считается по календарю устройства, поэтому моменты строятся от
        // локального «сейчас»: оба визита ещё впереди и в этой же календарной дате.
        var localNow = TimeZoneInfo.ConvertTime(BarberCabinetFixtures.Now, TimeZoneInfo.Local);
        var cancelled = Booking(
            BarberCabinetFixtures.BookingId,
            BookingStatus.Cancelled,
            localNow.AddMinutes(10));
        var confirmed = Booking(
            BarberCabinetFixtures.SecondBookingId,
            BookingStatus.Confirmed,
            localNow.AddMinutes(40));

        harness.Bookings.Page = (scope, _, _, _) => Task.FromResult(
            scope == "past" ? EmptyPage() : Page([cancelled, confirmed]));

        harness.ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);

        await harness.ViewModel.ActivateAsync();

        var next = harness.ViewModel.Items.Single(item => !item.IsGap && item.Id == confirmed.Id);

        Assert.Equal(
            BarberCabinetText.NextClient(
                next.StartAt,
                next.EndAt,
                BarberCabinetFixtures.Now,
                next.TimeZone),
            harness.ViewModel.NextClientText);
    }

    private static BarberBookingResponse Booking(
        Guid id,
        BookingStatus status = BookingStatus.Confirmed,
        DateTimeOffset? start = null,
        string? barberNote = null)
    {
        var value = start ?? BarberCabinetFixtures.TodayAt(10);

        return BarberCabinetFixtures.Booking(
            id: id,
            status: status,
            startAt: value,
            endAt: value.AddHours(1),
            barberNote: barberNote);
    }

    private static PagedResponse<BarberBookingResponse> Page(
        IReadOnlyList<BarberBookingResponse> items,
        string? nextCursor = null) =>
        new(items, nextCursor);

    private static PagedResponse<BarberBookingResponse> EmptyPage() => new([], null);

    /// <summary>
    /// Собранный раздел: подменённый клиент записей и часы.
    /// </summary>
    private sealed class Harness
    {
        public Harness(AutoRetryLoop? upcomingRetry = null, AutoRetryLoop? pastRetry = null) =>
            ViewModel = new BarberBookingsViewModel(
                Bookings,
                new FixedTimeProvider(BarberCabinetFixtures.Now),
                upcomingRetry,
                pastRetry);

        public FakeBarberBookingsApi Bookings { get; } = new();

        public BarberBookingsViewModel ViewModel { get; }

        /// <summary>
        /// Готовит раздел с одной сегодняшней записью в «Ближайших».
        /// </summary>
        /// <param name="status">Статус записи.</param>
        /// <returns>Строка списка.</returns>
        public async Task<BarberBookingItem> LoadSingleUpcomingAsync(
            BookingStatus status = BookingStatus.Confirmed)
        {
            var row = Booking(
                BarberCabinetFixtures.BookingId,
                status,
                BarberCabinetFixtures.TodayAt(10));

            Bookings.Page = (scope, _, _, _) => Task.FromResult(
                scope == "past" ? EmptyPage() : Page([row]));

            ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);
            await ViewModel.ActivateAsync();

            return ViewModel.Items.First(item => !item.IsGap);
        }
    }
}
