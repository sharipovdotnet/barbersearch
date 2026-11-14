using System.Net;
using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Account;
using BarberSearch.Mobile.Core.Api.Bookings;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.Formatting;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Записи»: гейт входа, два списка по месяцам, догрузка, обновление,
/// отмена, перенос, отзыв и очистка данных при выходе.
/// </summary>
/// <remarks>
/// Время салона сверяется на московском поясе и фиксированном «сейчас»: 22:00 UTC 14 ноября —
/// это уже 15-е в Москве, поэтому дата и месяц не могут браться из пояса устройства.
/// Строки списка приходят без имён, поэтому карточка барбера подменяется фейком и её вызовы
/// считаются: один барбер — один запрос за жизнь экрана.
/// </remarks>
public sealed class BookingsViewModelTests
{
    private static readonly Guid UserId = Guid.Parse("9e200000-0000-4000-8000-000000000001");
    private static readonly Guid OtherUserId = Guid.Parse("9e200000-0000-4000-8000-000000000002");
    private static readonly Guid BarberProfileId = Guid.Parse("9e200000-0000-4000-8000-000000000003");
    private static readonly Guid BarberServiceId = Guid.Parse("9e200000-0000-4000-8000-000000000004");
    private static readonly Guid HairServiceId = Guid.Parse("9e200000-0000-4000-8000-000000000005");
    private static readonly Guid SalonId = Guid.Parse("9e200000-0000-4000-8000-000000000006");
    private static readonly Guid FirstBookingId = Guid.Parse("9e200000-0000-4000-8000-000000000011");
    private static readonly Guid SecondBookingId = Guid.Parse("9e200000-0000-4000-8000-000000000012");
    private static readonly Guid ThirdBookingId = Guid.Parse("9e200000-0000-4000-8000-000000000013");
    private static readonly Guid HistoryBookingId = Guid.Parse("9e200000-0000-4000-8000-000000000014");
    private static readonly Guid RefreshedBookingId = Guid.Parse("9e200000-0000-4000-8000-000000000015");

    private static readonly DateTimeOffset Now = new(2026, 11, 14, 22, 0, 0, TimeSpan.Zero);

    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string RateLimitText = "Слишком много запросов. Подождите минуту и повторите.";

    [Fact]
    public async Task InitializeAsync_WithoutSession_ShowsGateAndMakesNoRequests()
    {
        using var harness = new Harness(authenticated: false);

        await harness.ViewModel.InitializeAsync();

        Assert.False(harness.ViewModel.IsSignedIn);
        Assert.True(harness.ViewModel.ShowGate);
        Assert.Equal(0, harness.Account.Calls);
        Assert.Empty(harness.Bookings.Requests);
        Assert.Empty(harness.Barber.ProfileCalls);
    }

    [Fact]
    public async Task InitializeAsync_WithSession_LoadsMeOnceAndSendsUserId()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page([Booking(FirstBookingId)]));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.InitializeAsync();

        Assert.Equal(1, harness.Account.Calls);
        Assert.Equal(2, harness.Bookings.Requests.Count);
        Assert.All(harness.Bookings.Requests, request => Assert.Equal(UserId, request.UserId));
    }

    [Fact]
    public async Task InitializeAsync_UpcomingFirstPage_GroupsByMonth()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
        [
            Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30)),
            Booking(SecondBookingId, startAt: At(2026, 11, 22, 9, 0)),
            Booking(ThirdBookingId, startAt: At(2026, 10, 30, 10, 0)),
        ]));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(2, viewModel.UpcomingGroups.Count);
        Assert.Equal("Ноябрь 2026", viewModel.UpcomingGroups[0].Title);
        Assert.Equal(
            new[] { FirstBookingId, SecondBookingId },
            viewModel.UpcomingGroups[0].Select(item => item.Id));
        Assert.Equal("Октябрь 2026", viewModel.UpcomingGroups[1].Title);
        Assert.Equal(ThirdBookingId, Assert.Single(viewModel.UpcomingGroups[1]).Id);
    }

    /// <summary>
    /// Пояс салона приходит только с карточкой барбера: визит в 22:30 UTC 30 ноября — это уже
    /// 1 декабря в Москве, поэтому после ответа карточки группа переезжает в «Декабрь 2026».
    /// </summary>
    [Fact]
    public async Task InitializeAsync_ProfileMovesVisitToNextMonth_RegroupsGroups()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
            [Booking(FirstBookingId, startAt: At(2026, 11, 30, 22, 30))]));

        var profile = new TaskCompletionSource<BarberProfileResponse>();
        harness.Barber.Profile = _ => profile.Task;

        var viewModel = harness.ViewModel;

        // До карточки пояс неизвестен, поэтому месяц считается по UTC — «Ноябрь 2026».
        var initialize = viewModel.InitializeAsync();

        Assert.Equal("Ноябрь 2026", Assert.Single(viewModel.UpcomingGroups).Title);
        Assert.Single(harness.Barber.ProfileCalls);

        // Ответ карточки приносит московский пояс: 22:30 UTC — это уже 1 декабря.
        profile.SetResult(ProfileResponse());
        await initialize;

        var group = Assert.Single(viewModel.UpcomingGroups);

        Assert.Equal("Декабрь 2026", group.Title);
        Assert.DoesNotContain(
            viewModel.UpcomingGroups,
            candidate => candidate.Title == "Ноябрь 2026");

        var item = Assert.Single(group);

        Assert.Equal(FirstBookingId, item.Id);
        Assert.Equal("01:30", item.TimeText);
    }

    [Fact]
    public async Task InitializeAsync_FirstRow_IsMarkedNext()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
        [
            Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30)),
            Booking(SecondBookingId, startAt: At(2026, 11, 22, 9, 0)),
        ]));

        await harness.ViewModel.InitializeAsync();

        var rows = harness.ViewModel.UpcomingGroups.SelectMany(group => group).ToList();

        Assert.True(rows[0].IsNext);
        Assert.False(rows[1].IsNext);
    }

    [Fact]
    public async Task LoadMoreCommand_NextPage_AppendsToExistingMonthGroup()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, cursor) => Task.FromResult(cursor is null
            ? Page([Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))], "c1")
            : Page([Booking(SecondBookingId, startAt: At(2026, 11, 24, 10, 0))]));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.CanLoadMore);
        Assert.Single(viewModel.UpcomingGroups);

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Single(viewModel.UpcomingGroups);
        Assert.Equal(2, viewModel.UpcomingGroups[0].Count);
        Assert.False(viewModel.CanLoadMore);
        Assert.Equal(2, harness.Bookings.Requests.Count);
        Assert.Equal("c1", harness.Bookings.Requests[1].Cursor);
    }

    [Fact]
    public async Task LoadMoreCommand_NoCursor_DoesNothing()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
            [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));

        await harness.ViewModel.InitializeAsync();

        Assert.False(harness.ViewModel.CanLoadMore);

        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Single(harness.Bookings.Requests);
    }

    [Fact]
    public async Task SelectHistoryCommand_FirstOpen_LoadsHistoryOnce()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (scope, _, _) => Task.FromResult(
            scope == BookingScope.Past
                ? Page([HistoryBooking()])
                : Page([Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.SelectHistoryCommand.ExecuteAsync(null);
        await harness.ViewModel.SelectHistoryCommand.ExecuteAsync(null);

        Assert.Equal(1, PastRequests(harness));

        await harness.ViewModel.SelectUpcomingCommand.ExecuteAsync(null);

        Assert.Equal(1, UpcomingRequests(harness));
        Assert.True(harness.ViewModel.IsUpcomingSelected);
    }

    [Fact]
    public async Task SelectHistoryCommand_DirtyAfterCancel_ReloadsHistory()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (scope, _, _) => Task.FromResult(
            scope == BookingScope.Past
                ? Page([HistoryBooking()])
                : Page([Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.SelectHistoryCommand.ExecuteAsync(null);
        await harness.ViewModel.SelectUpcomingCommand.ExecuteAsync(null);

        var item = Assert.Single(Assert.Single(harness.ViewModel.UpcomingGroups));
        await harness.ViewModel.CancelBookingAsync(item);

        Assert.Equal(1, PastRequests(harness));

        await harness.ViewModel.SelectHistoryCommand.ExecuteAsync(null);

        Assert.Equal(2, PastRequests(harness));
    }

    [Fact]
    public async Task RefreshCommand_ReplacesFirstPage()
    {
        using var harness = new Harness();
        var calls = 0;
        harness.Bookings.GetBookings = (_, _, _) =>
        {
            calls++;

            return Task.FromResult(calls == 1
                ? Page(
                [
                    Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30)),
                    Booking(SecondBookingId, startAt: At(2026, 11, 22, 9, 0)),
                ])
                : Page([Booking(RefreshedBookingId, startAt: At(2026, 11, 25, 12, 0))]));
        };

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.RefreshCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.False(viewModel.IsRefreshing);
        Assert.Single(viewModel.UpcomingGroups);
        Assert.Equal(RefreshedBookingId, Assert.Single(viewModel.UpcomingGroups[0]).Id);
        Assert.Equal(2, harness.Bookings.Requests.Count);
    }

    [Fact]
    public async Task RefreshCommand_Error_KeepsExistingRows()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
        [
            Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30)),
            Booking(SecondBookingId, startAt: At(2026, 11, 22, 9, 0)),
        ]));

        await harness.ViewModel.InitializeAsync();

        harness.Bookings.GetBookings = (_, _, _) =>
            Task.FromException<PagedResponse<BookingResponse>>(new HttpRequestException("refused"));

        await harness.ViewModel.RefreshCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.False(viewModel.IsRefreshing);
        Assert.False(viewModel.ShowError);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(2, viewModel.UpcomingGroups.SelectMany(group => group).Count());
    }

    [Fact]
    public async Task InitializeAsync_NetworkError_ShowsRetryableError()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) =>
            Task.FromException<PagedResponse<BookingResponse>>(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowError);
        Assert.Equal(NetworkText, viewModel.ErrorMessage);

        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
            [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));

        await viewModel.RetryCommand.ExecuteAsync(null);

        Assert.False(viewModel.ShowError);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Single(viewModel.UpcomingGroups);
        Assert.Equal(1, harness.Account.Calls);
    }

    [Fact]
    public async Task InitializeAsync_TooManyRequests_ShowsRateLimitText()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromException<PagedResponse<BookingResponse>>(
            ApiError(HttpStatusCode.TooManyRequests));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);
        Assert.Equal(RateLimitText, harness.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_Profiles_ResolvesNamesOncePerBarber()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
        [
            Booking(FirstBookingId, startAt: At(2026, 11, 20, 22, 30)),
            Booking(SecondBookingId, startAt: At(2026, 11, 24, 9, 0)),
        ]));

        await harness.ViewModel.InitializeAsync();

        var rows = harness.ViewModel.UpcomingGroups.SelectMany(group => group).ToList();

        Assert.All(rows, row =>
        {
            Assert.Equal("Тимур Барберов", row.BarberName);
            Assert.Equal("Стрижка", row.ServiceName);
            Assert.Equal("E2E Studio", row.SalonName);
            Assert.Equal("Москва, ул. Тверская, 1", row.CityAddress);
        });

        // 22:30 UTC — это уже 01:30 следующего дня в Москве, месяц при этом тот же.
        Assert.Equal("01:30", rows[0].TimeText);
        Assert.Equal("сб, 21 нояб.", rows[0].DateText);

        Assert.Equal(BarberProfileId, Assert.Single(harness.Barber.ProfileCalls));
    }

    [Fact]
    public async Task InitializeAsync_ProfileFailed_KeepsFallbacksAndShowsWarning()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
            [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));
        harness.Barber.Profile = _ =>
            Task.FromException<BarberProfileResponse>(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        var item = Assert.Single(Assert.Single(harness.ViewModel.UpcomingGroups));

        Assert.Equal("Барбер", item.BarberName);
        Assert.Equal("Услуга", item.ServiceName);
        Assert.Equal("Салон", item.SalonName);
        Assert.Null(item.TimeZone);
        Assert.True(harness.ViewModel.ShowProfilesWarning);
    }

    [Fact]
    public async Task CancelBookingAsync_Success_RemovesRowAndShowsBanner()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
            [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;
        var item = Assert.Single(Assert.Single(viewModel.UpcomingGroups));
        var when = item.MomentText;

        var failure = await viewModel.CancelBookingAsync(item);

        Assert.Null(failure);
        Assert.Empty(viewModel.UpcomingGroups);
        Assert.Equal($"Запись на {when} отменена.", viewModel.Announcement);
        Assert.True(viewModel.IsAnnouncementVisible);
        Assert.DoesNotContain(viewModel.ActiveGroups.SelectMany(group => group), row => row.CanCancel);
        Assert.Equal(new[] { item.Id }, harness.Bookings.CancelCalls);
    }

    [Fact]
    public async Task CancelBookingAsync_Error_KeepsRowAndReturnsText()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
            [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));
        harness.Bookings.Cancel = _ =>
            Task.FromException(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;
        var item = Assert.Single(Assert.Single(viewModel.UpcomingGroups));

        var failure = await viewModel.CancelBookingAsync(item);

        Assert.Equal(NetworkText, failure);
        Assert.Single(viewModel.UpcomingGroups);
        Assert.Equal(item.Id, Assert.Single(viewModel.UpcomingGroups[0]).Id);
        Assert.False(item.IsCancelling);
        Assert.True(viewModel.ShowList);
    }

    [Fact]
    public async Task CancelBookingAsync_DoubleCall_MakesOneRequest()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
            [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;
        var item = Assert.Single(Assert.Single(viewModel.UpcomingGroups));
        var gate = new TaskCompletionSource();
        harness.Bookings.Cancel = _ => gate.Task;

        var first = viewModel.CancelBookingAsync(item);
        var second = await viewModel.CancelBookingAsync(item);

        Assert.Null(second);
        Assert.Single(harness.Bookings.CancelCalls);

        gate.SetResult();

        Assert.Null(await first);
        Assert.Single(harness.Bookings.CancelCalls);
        Assert.Empty(viewModel.UpcomingGroups);
    }

    [Fact]
    public async Task ApplyRescheduled_ChangesTimeAndShowsBanner()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
            [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;
        var item = Assert.Single(Assert.Single(viewModel.UpcomingGroups));
        var slot = new BarberSlotResponse(
            At(2026, 11, 21, 13, 0),
            At(2026, 11, 21, 13, 45));

        Assert.Equal("18:30", item.TimeText);

        viewModel.ApplyRescheduled(item, slot);

        Assert.Equal(slot.StartAt, item.StartAt);
        Assert.Equal("16:00", item.TimeText);
        Assert.StartsWith("Визит перенесён на ", viewModel.Announcement);
        Assert.True(viewModel.IsAnnouncementVisible);
    }

    [Fact]
    public async Task ApplyReviewed_SetsHasReviewAndBanner()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (scope, _, _) => Task.FromResult(
            scope == BookingScope.Past ? Page([HistoryBooking()]) : Page([]));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.SelectHistoryCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;
        var item = Assert.Single(Assert.Single(viewModel.HistoryGroups));

        Assert.True(item.CanReview);

        viewModel.ApplyReviewed(item);

        Assert.True(item.HasReview);
        Assert.False(item.CanReview);
        Assert.True(item.ShowReviewSent);
        Assert.Equal(BookingText.ReviewSubmittedAnnouncement, viewModel.Announcement);
    }

    /// <summary>
    /// Поздний ответ «Предстоящих» после ухода на «Историю» отбрасывается: смена вкладки
    /// сдвигает поколение скрытого списка и сбрасывает его индикаторы — вкладка не «висит».
    /// </summary>
    [Fact]
    public async Task InitializeAsync_StaleResponseAfterTabSwitch_IsIgnored()
    {
        using var harness = new Harness();

        var stale = new TaskCompletionSource<PagedResponse<BookingResponse>>();
        var upcomingCalls = 0;

        harness.Bookings.GetBookings = (scope, _, _) => scope == BookingScope.Past
            ? Task.FromResult(Page([HistoryBooking()]))
            : Interlocked.Increment(ref upcomingCalls) == 1
                ? stale.Task
                : Task.FromResult(Page([Booking(SecondBookingId, startAt: At(2026, 11, 21, 9, 0))]));

        var viewModel = harness.ViewModel;

        // Первая загрузка «Предстоящих» висит в полёте: пользователь уходит на «Историю».
        var initialize = viewModel.InitializeAsync();
        await viewModel.SelectHistoryCommand.ExecuteAsync(null);

        // Ответ скрытой вкладки приходит позже: он уже не нужен, индикаторы сброшены.
        stale.SetResult(Page([Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));
        await initialize;

        Assert.Empty(viewModel.UpcomingGroups);
        Assert.False(viewModel.ShowLoading);
        Assert.False(viewModel.IsLoadingMore);

        // Возврат на вкладку снова грузит список: он не застрял в «первой загрузке».
        await viewModel.SelectUpcomingCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsUpcomingSelected);
        Assert.False(viewModel.ShowLoading);
        Assert.False(viewModel.IsLoadingMore);

        var item = Assert.Single(Assert.Single(viewModel.UpcomingGroups));

        Assert.Equal(SecondBookingId, item.Id);
        Assert.DoesNotContain(
            viewModel.UpcomingGroups.SelectMany(group => group),
            row => row.Id == FirstBookingId);
    }

    [Fact]
    public async Task ApplySignedOutState_ClearsDataAndProfiles()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
            [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.Single(viewModel.UpcomingGroups);
        Assert.Single(harness.Barber.ProfileCalls);

        viewModel.ApplySignedOutState();

        Assert.Empty(viewModel.UpcomingGroups);
        Assert.Empty(viewModel.HistoryGroups);
        Assert.False(viewModel.IsSignedIn);
        Assert.True(viewModel.ShowGate);

        harness.Account.Me = () => Task.FromResult(MeOf(OtherUserId));

        await viewModel.InitializeAsync();

        Assert.Equal(2, harness.Account.Calls);
        Assert.Equal(2, harness.Barber.ProfileCalls.Count);
        Assert.Equal(OtherUserId, harness.Bookings.Requests[^1].UserId);
        Assert.Single(viewModel.UpcomingGroups);
    }

    [Fact]
    public async Task RecomputeTimes_UpdatesCountdownAndRescheduleAvailability()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
            [Booking(FirstBookingId, startAt: Now.AddHours(2))]));

        await harness.ViewModel.InitializeAsync();

        var item = Assert.Single(Assert.Single(harness.ViewModel.UpcomingGroups));

        Assert.Equal("через 2 часа", item.CountdownText);
        Assert.True(item.CanReschedule);

        harness.Time.Now = Now.AddHours(3);
        harness.ViewModel.RecomputeTimes();

        Assert.Null(item.CountdownText);
        Assert.False(item.CanReschedule);
    }

    [Fact]
    public async Task SelectUpcomingCommand_AfterHistory_DoesNotRefetch()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (scope, _, _) => Task.FromResult(
            scope == BookingScope.Past
                ? Page([HistoryBooking()])
                : Page([Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.SelectHistoryCommand.ExecuteAsync(null);
        await harness.ViewModel.SelectUpcomingCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsUpcomingSelected);
        Assert.Single(harness.ViewModel.UpcomingGroups);
        Assert.Equal(1, UpcomingRequests(harness));
        Assert.Equal(1, PastRequests(harness));
    }

    [Fact]
    public async Task InitializeAsync_SecondAppearance_SilentlyRefreshesActiveList()
    {
        using var harness = new Harness();
        var refresh = new TaskCompletionSource<PagedResponse<BookingResponse>>();
        var calls = 0;

        harness.Bookings.GetBookings = (_, _, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromResult(Page([Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]))
                : refresh.Task;
        };

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.Single(viewModel.UpcomingGroups);

        var second = viewModel.InitializeAsync();

        // Строки остаются на месте, пока тихое обновление не ответило, и индикатора нет.
        Assert.Single(viewModel.UpcomingGroups);
        Assert.False(viewModel.ShowLoading);
        Assert.Equal(2, calls);

        refresh.SetResult(Page([Booking(SecondBookingId, startAt: At(2026, 11, 21, 9, 0))]));
        await second;

        Assert.Equal(1, harness.Account.Calls);
        Assert.Equal(2, harness.Bookings.Requests.Count);
        Assert.Equal(SecondBookingId, Assert.Single(viewModel.UpcomingGroups[0]).Id);
    }

    private static int UpcomingRequests(Harness harness) =>
        harness.Bookings.Requests.Count(request => request.Scope == BookingScope.Upcoming);

    private static int PastRequests(Harness harness) =>
        harness.Bookings.Requests.Count(request => request.Scope == BookingScope.Past);

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private static BookingResponse Booking(
        Guid id,
        DateTimeOffset? startAt = null,
        int durationMinutes = 45,
        decimal price = 2400m,
        BookingStatus status = BookingStatus.Confirmed,
        bool hasReview = false) =>
        new(
            id,
            UserId,
            BarberProfileId,
            BarberServiceId,
            SalonId,
            startAt ?? At(2026, 11, 20, 15, 30),
            (startAt ?? At(2026, 11, 20, 15, 30)).AddMinutes(durationMinutes),
            price,
            status,
            $"idempotency-{id:N}",
            null,
            null,
            null,
            (startAt ?? At(2026, 11, 20, 15, 30)).AddDays(-1),
            hasReview);

    private static BookingResponse HistoryBooking() => Booking(
        HistoryBookingId,
        startAt: At(2026, 10, 1, 9, 0),
        status: BookingStatus.Completed,
        hasReview: false);

    private static PagedResponse<BookingResponse> Page(
        IReadOnlyList<BookingResponse> items,
        string? nextCursor = null) =>
        new(items, nextCursor);

    private static MeResponse MeOf(Guid id) =>
        new(id, "mobile-test@barbersearch.test", "Customer", null, IsEmailVerified: true);

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, params string[] codes) =>
        new(statusCode, "Request failed.", codes, new Dictionary<string, string[]>());

    private static BarberProfileResponse ProfileResponse(
        IReadOnlyList<BarberProfileServiceResponse>? services = null,
        IReadOnlyList<BarberProfileSalonResponse>? salons = null) =>
        new(
            BarberProfileId,
            Guid.Parse("9e200000-0000-4000-8000-0000000000ff"),
            null,
            "Тимур Барберов",
            Grade.SeniorBarber,
            null,
            11,
            [Language.Ru],
            [],
            4.8m,
            12,
            [],
            [PaymentMethod.Cash],
            [],
            [],
            string.Empty,
            true,
            true,
            services ?? [ServiceResponse()],
            salons ?? [SalonResponse()],
            [],
            [],
            []);

    private static BarberProfileServiceResponse ServiceResponse() =>
        new(
            BarberServiceId,
            HairServiceId,
            "Стрижка",
            ServiceCategory.Haircut,
            40,
            2400m,
            45,
            IsActive: true);

    private static BarberProfileSalonResponse SalonResponse() =>
        new(
            Guid.Parse("9e200000-0000-4000-8000-0000000000fe"),
            SalonId,
            "E2E Studio",
            "ул. Тверская, 1",
            "Москва",
            "Россия",
            "Europe/Moscow",
            PlaceKind.Salon,
            IsPrimary: true,
            []);

    /// <summary>
    /// «Сейчас» с возможностью сдвинуть время: отсчёт и доступность переноса проверяются
    /// без ожидания реальных часов.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// Подменённый клиент записей: запоминает страницы, отмены и переносы, ответы задаются
    /// делегатами — так проверяются ошибки, «висящие» ответы и двойные нажатия.
    /// </summary>
    private sealed class FakeBookingsApi : IBookingsApi
    {
        public List<BookingsRequest> Requests { get; } = [];

        public List<Guid> CancelCalls { get; } = [];

        public List<RescheduleCall> RescheduleCalls { get; } = [];

        public Func<BookingScope, Guid, string?, Task<PagedResponse<BookingResponse>>> GetBookings { get; set; } =
            (_, _, _) => Task.FromResult(Page([]));

        public Func<Guid, Task> Cancel { get; set; } = _ => Task.CompletedTask;

        public Func<Guid, DateTimeOffset, Task> Reschedule { get; set; } = (_, _) => Task.CompletedTask;

        public Task<PagedResponse<BookingResponse>> GetBookingsAsync(
            BookingScope scope,
            Guid userId,
            string? cursor = null,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(new BookingsRequest(scope, userId, cursor, pageSize));

            return GetBookings(scope, userId, cursor);
        }

        public Task CancelBookingAsync(Guid bookingId, CancellationToken cancellationToken = default)
        {
            CancelCalls.Add(bookingId);

            return Cancel(bookingId);
        }

        public Task RescheduleBookingAsync(
            Guid bookingId,
            DateTimeOffset startAt,
            CancellationToken cancellationToken = default)
        {
            RescheduleCalls.Add(new RescheduleCall(bookingId, startAt));

            return Reschedule(bookingId, startAt);
        }

        public Task<Guid> CreateBookingAsync(
            CreateBookingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Записи не создают брони.");

        public Task<BarberSlotsResponse> GetRescheduleSlotsAsync(
            Guid bookingId,
            DateOnly slotDate,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не читает слоты переноса.");
    }

    /// <summary>
    /// Подменённый клиент карточки барбера: отдаёт имена, услугу, салон и пояс и считает
    /// запросы — «один барбер — одна карточка» проверяется именно по ним.
    /// </summary>
    private sealed class FakeBarberApi : IBarberSearchApi
    {
        public List<Guid> ProfileCalls { get; } = [];

        public Func<Guid, Task<BarberProfileResponse>> Profile { get; set; } =
            _ => Task.FromResult(ProfileResponse());

        public Task<BarberProfileResponse> GetBarberProfileAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            ProfileCalls.Add(id);

            return Profile(id);
        }

        public Task<PagedResponse<BarberSearchItemResponse>> SearchBarbersAsync(
            BarberSearchFilters filters,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не ищет барберов.");

        public Task<IReadOnlyList<SpecializationResponse>> GetSpecializationsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не читает справочник специализаций.");

        public Task<IReadOnlyList<HairServiceResponse>> GetHairServicesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не читает справочник услуг.");

        public Task<BarberSlotsResponse> GetSlotsAsync(
            Guid barberProfileId,
            DateOnly slotDate,
            Guid? hairServiceId,
            Guid? salonId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не читает слоты барбера.");

        public Task<PagedResponse<BarberSearch.Contracts.Reviews.GetReviews.ReviewResponse>> GetReviewsAsync(
            Guid barberProfileId,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не читает отзывы барбера.");
    }

    /// <summary>
    /// Подменённый клиент текущего пользователя: пользователя можно сменить между входами,
    /// а счётчик доказывает, что «/me» спрашивается один раз за жизнь экрана.
    /// </summary>
    private sealed class FakeAccountApi : IAccountApi
    {
        public int Calls { get; private set; }

        public Func<Task<MeResponse>> Me { get; set; } = () => Task.FromResult(MeOf(UserId));

        public Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default)
        {
            Calls++;

            return Me();
        }

        public Task<MyProfileResponse> GetMyProfileAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не читает профиль кабинета.");

        public Task UpdateProfileAsync(
            Guid userId,
            string firstName,
            string lastName,
            string? phoneNumber,
            string? avatarUrl,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не сохраняет профиль.");

        public Task ChangePasswordAsync(
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не меняет пароль.");

        public Task RequestEmailChangeAsync(
            string newEmail,
            string currentPassword,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не меняет email.");

        public Task RequestEmailVerificationAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не отправляет письмо подтверждения.");
    }

    /// <summary>
    /// Подменённое хранилище пары: без токенов экран обязан показать гейт и не идти в сеть.
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

    /// <summary>
    /// Собранный экран: сессия, подменённые клиенты, управляемые часы и ViewModel.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        public Harness(bool authenticated = true)
        {
            if (authenticated)
            {
                Store.Tokens = new StoredTokens("access-1", "refresh-1");
            }

            Session = new TokenSession(Store);
            ViewModel = new BookingsViewModel(Bookings, Barber, Account, Session, Time);
        }

        public FakeBookingsApi Bookings { get; } = new();

        public FakeBarberApi Barber { get; } = new();

        public FakeAccountApi Account { get; } = new();

        public FakeTokenStore Store { get; } = new();

        public FixedTimeProvider Time { get; } = new(Now);

        public TokenSession Session { get; }

        public BookingsViewModel ViewModel { get; }

        public void Dispose() => Session.Dispose();
    }

    /// <summary>
    /// Параметры страницы списка, чтобы проверять, что именно ушло на сервер.
    /// </summary>
    private sealed record BookingsRequest(BookingScope Scope, Guid UserId, string? Cursor, int PageSize);

    /// <summary>
    /// Параметры переноса для проверок, которым он понадобится.
    /// </summary>
    private sealed record RescheduleCall(Guid BookingId, DateTimeOffset StartAt);
}
