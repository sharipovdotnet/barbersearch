using System.Net;
using BarberSearch.Contracts.Addresses.SuggestCities;
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
/// Строки списка приходят без имён, поэтому карточка мастера подменяется фейком и её вызовы
/// считаются: один мастер — один запрос за жизнь экрана.
/// </remarks>
public sealed class BookingsViewModelTests
{
    private static readonly Guid UserId = Guid.Parse("9e200000-0000-4000-8000-000000000001");
    private static readonly Guid OtherUserId = Guid.Parse("9e200000-0000-4000-8000-000000000002");
    private static readonly Guid BarberProfileId = Guid.Parse("9e200000-0000-4000-8000-000000000003");
    private static readonly Guid BarberServiceId = Guid.Parse("9e200000-0000-4000-8000-000000000004");
    private static readonly Guid HairServiceId = Guid.Parse("9e200000-0000-4000-8000-000000000005");
    private static readonly Guid SalonId = Guid.Parse("9e200000-0000-4000-8000-000000000006");
    private static readonly Guid SpecializationId = Guid.Parse("9e200000-0000-4000-8000-000000000007");
    private static readonly Guid FirstBookingId = Guid.Parse("9e200000-0000-4000-8000-000000000011");
    private static readonly Guid SecondBookingId = Guid.Parse("9e200000-0000-4000-8000-000000000012");
    private static readonly Guid ThirdBookingId = Guid.Parse("9e200000-0000-4000-8000-000000000013");
    private static readonly Guid HistoryBookingId = Guid.Parse("9e200000-0000-4000-8000-000000000014");
    private static readonly Guid RefreshedBookingId = Guid.Parse("9e200000-0000-4000-8000-000000000015");

    private static readonly DateTimeOffset Now = new(2026, 11, 14, 22, 0, 0, TimeSpan.Zero);

    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string RateLimitText = "Слишком много запросов. Подождите минуту и повторите.";
    private const string LoadMoreErrorText = "Не удалось загрузить ещё записи.";

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
    /// Пояс салона приходит только с карточкой мастера: визит в 22:30 UTC 30 ноября — это уже
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
    public async Task AutoRefreshAsync_LoadedList_SilentlyReplacesFirstPage()
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

        var viewModel = harness.ViewModel;

        await viewModel.AutoRefreshAsync();

        Assert.False(viewModel.IsRefreshing);
        Assert.False(viewModel.ShowLoading);
        Assert.False(viewModel.ShowError);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Single(viewModel.UpcomingGroups);
        Assert.Equal(RefreshedBookingId, Assert.Single(viewModel.UpcomingGroups[0]).Id);

        // Ровно один запрос на вызов, первая страница и без курсора; карточка мастера — из кэша.
        Assert.Equal(2, harness.Bookings.Requests.Count);
        Assert.Null(harness.Bookings.Requests[1].Cursor);
        Assert.Equal(BookingsViewModel.PageSize, harness.Bookings.Requests[1].PageSize);
        Assert.Single(harness.Barber.ProfileCalls);
    }

    [Fact]
    public async Task AutoRefreshAsync_TooManyRequests_KeepsRowsWithoutError()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
        [
            Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30)),
            Booking(SecondBookingId, startAt: At(2026, 11, 22, 9, 0)),
        ]));

        await harness.ViewModel.InitializeAsync();

        harness.Bookings.GetBookings = (_, _, _) =>
            Task.FromException<PagedResponse<BookingResponse>>(ApiError(HttpStatusCode.TooManyRequests));

        var viewModel = harness.ViewModel;

        var exception = await Record.ExceptionAsync(viewModel.AutoRefreshAsync);

        Assert.Null(exception);
        Assert.False(viewModel.IsRefreshing);
        Assert.False(viewModel.ShowError);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(2, viewModel.UpcomingGroups.SelectMany(group => group).Count());
        Assert.Equal(2, harness.Bookings.Requests.Count);
    }

    [Fact]
    public async Task AutoRefreshAsync_WithoutSession_MakesNoRequests()
    {
        using var harness = new Harness(authenticated: false);

        await harness.ViewModel.AutoRefreshAsync();

        Assert.False(harness.ViewModel.IsSignedIn);
        Assert.True(harness.ViewModel.ShowGate);
        Assert.Equal(0, harness.Account.Calls);
        Assert.Empty(harness.Bookings.Requests);
        Assert.Empty(harness.Barber.ProfileCalls);
    }

    [Fact]
    public async Task AutoRefreshAsync_SecondPageLoaded_DoesNothing()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, cursor) => Task.FromResult(cursor is null
            ? Page(
                [.. Enumerable.Range(100, BookingsViewModel.PageSize).Select(NumberedBooking)],
                "c1")
            : Page([.. Enumerable.Range(200, 5).Select(NumberedBooking)]));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        var rows = viewModel.UpcomingGroups.SelectMany(group => group).ToList();
        var requests = harness.Bookings.Requests.Count;

        Assert.Equal(BookingsViewModel.PageSize + 5, rows.Count);

        // Ответ на случай запроса: если автообновление всё же сходит в сеть, список изменится.
        harness.Bookings.GetBookings = (_, _, _) =>
            Task.FromResult(Page([Booking(RefreshedBookingId, startAt: At(2026, 11, 25, 12, 0))]));

        await viewModel.AutoRefreshAsync();

        Assert.Equal(requests, harness.Bookings.Requests.Count);
        Assert.Equal(
            rows.Select(row => row.Id),
            viewModel.UpcomingGroups.SelectMany(group => group).Select(row => row.Id));
        Assert.DoesNotContain(
            viewModel.UpcomingGroups.SelectMany(group => group),
            row => row.Id == RefreshedBookingId);
    }

    [Fact]
    public async Task AutoRefreshAsync_ConcurrentCalls_SendOneRequest()
    {
        using var harness = new Harness();
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
            [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));

        await harness.ViewModel.InitializeAsync();

        var requests = harness.Bookings.Requests.Count;

        // Ответ придержан: первый вызов висит в полёте, второй обязан выйти по флагу.
        var held = new TaskCompletionSource<PagedResponse<BookingResponse>>();
        harness.Bookings.GetBookings = (_, _, _) => held.Task;

        var first = harness.ViewModel.AutoRefreshAsync();

        await harness.ViewModel.AutoRefreshAsync();

        Assert.Equal(requests + 1, harness.Bookings.Requests.Count);

        held.SetResult(Page([Booking(RefreshedBookingId, startAt: At(2026, 11, 25, 12, 0))]));
        await first;

        Assert.Equal(requests + 1, harness.Bookings.Requests.Count);
        Assert.Equal(RefreshedBookingId, Assert.Single(Assert.Single(harness.ViewModel.UpcomingGroups)).Id);
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
            Assert.Equal("Тимур Мастеров", row.BarberName);
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

        Assert.Equal("Мастер", item.BarberName);
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

    [Fact]
    public async Task StartAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        using var harness = new Harness(autoRetry: FastRetry());
        var calls = 0;

        harness.Bookings.GetBookings = (_, _, _) =>
        {
            calls++;

            return calls <= 3
                ? Task.FromException<PagedResponse<BookingResponse>>(new HttpRequestException("refused"))
                : Task.FromResult(Page([Booking(FirstBookingId)]));
        };

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(3, harness.ViewModel.AutoRetry.Attempts);
        Assert.Single(harness.ViewModel.UpcomingGroups);
        Assert.False(harness.ViewModel.ShowError);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task StartAutoRetry_TerminalFailure_DoesNotStart(HttpStatusCode statusCode)
    {
        using var harness = new Harness(autoRetry: FastRetry());

        harness.Bookings.GetBookings = (_, _, _) =>
            Task.FromException<PagedResponse<BookingResponse>>(ApiError(statusCode));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task StopAutoRetry_WhileWaiting_StopsFurtherAttempts()
    {
        using var harness = new Harness(autoRetry: StopAfterFirstAttemptRetry());

        harness.Bookings.GetBookings = (_, _, _) =>
            Task.FromException<PagedResponse<BookingResponse>>(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.True(harness.ViewModel.IsAutoRetrying);

        harness.ViewModel.StopAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task RetryCommand_WhileAutoRetrying_LoadsImmediatelyAndSuccessDoesNotRestart()
    {
        using var harness = new Harness(autoRetry: HangingRetry());
        var calls = 0;

        harness.Bookings.GetBookings = (_, _, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<PagedResponse<BookingResponse>>(new HttpRequestException("refused"))
                : Task.FromResult(Page([Booking(FirstBookingId)]));
        };

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.True(harness.ViewModel.IsAutoRetrying);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);

        // «Повторить» — это «повторить сейчас»: цикл останавливается, чтение идёт без паузы.
        await harness.ViewModel.RetryCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.Bookings.Requests.Count);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
        Assert.Single(harness.ViewModel.UpcomingGroups);
        Assert.False(harness.ViewModel.ShowError);
    }

    [Fact]
    public async Task RetryCommand_WhileAutoRetrying_LoadFailsRetryable_RestartsAndKeepsRetryingUntilSuccess()
    {
        var restartGate = new TaskCompletionSource();
        var silentAttempt = new TaskCompletionSource<PagedResponse<BookingResponse>>();

        using var harness = new Harness(autoRetry: StopThenGateRetry(restartGate.Task));
        var calls = 0;

        harness.Bookings.GetBookings = (_, _, _) =>
        {
            calls++;

            return calls switch
            {
                1 or 2 => Task.FromException<PagedResponse<BookingResponse>>(
                    new HttpRequestException("refused")),
                3 => silentAttempt.Task,
                _ => Task.FromResult(Page([Booking(FirstBookingId)])),
            };
        };

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsAutoRetrying);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);

        // Кнопка при работающем цикле читает сразу: к возврату команды ручной запрос уже ушёл,
        // а перезапущенный цикл ждёт паузы — второго запроса без неё нет.
        await viewModel.RetryCommand.ExecuteAsync(null);

        Assert.Equal(2, calls);
        Assert.Equal(2, harness.Bookings.Requests.Count);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.True(viewModel.IsAutoRetrying);

        // Повторяемый сбой кнопки возвращает цикл к жизни: первая же тихая попытка падает
        // снова, и цикл не сдаётся, а повторяет до успеха.
        restartGate.SetResult();
        silentAttempt.SetException(new HttpRequestException("refused"));

        await viewModel.AutoRetryCompletion;

        Assert.Equal(2, viewModel.AutoRetry.Attempts);
        Assert.Equal(4, calls);
        Assert.Equal(4, harness.Bookings.Requests.Count);
        Assert.Single(viewModel.UpcomingGroups);
        Assert.False(viewModel.ShowError);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_FailedSilentAttempt_KeepsErrorMessage()
    {
        using var harness = new Harness(autoRetry: StopAfterFirstAttemptRetry());
        var calls = 0;
        var retry = new TaskCompletionSource<PagedResponse<BookingResponse>>();

        harness.Bookings.GetBookings = (_, _, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<PagedResponse<BookingResponse>>(new HttpRequestException("refused"))
                : retry.Task;
        };

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);
        Assert.NotNull(harness.ViewModel.ErrorMessage);

        harness.ViewModel.StartAutoRetry();

        // Попытка висит: повтор не стирает ошибку на старте, иначе карточка с причиной
        // мигала бы пустым экраном на каждой паузе.
        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.True(harness.ViewModel.ShowError);
        Assert.NotNull(harness.ViewModel.ErrorMessage);
        Assert.False(harness.ViewModel.ShowLoading);

        retry.SetException(new HttpRequestException("refused"));

        Assert.True(harness.ViewModel.ShowError);
        Assert.Equal(NetworkText, harness.ViewModel.ErrorMessage);

        harness.ViewModel.StopAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_Timeout_ShowsNetworkText()
    {
        using var harness = new Harness(autoRetry: FastRetry());
        var calls = 0;

        harness.Bookings.GetBookings = (_, _, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<PagedResponse<BookingResponse>>(new TaskCanceledException("timeout"))
                : Task.FromResult(Page([Booking(FirstBookingId)]));
        };

        await harness.ViewModel.InitializeAsync();

        // Таймаут HttpClient приходит отменой, но токен жив: для человека это «нет связи»,
        // а не тихая отмена. Раньше этот случай проглатывался.
        Assert.Equal(NetworkText, harness.ViewModel.ErrorMessage);

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.Single(harness.ViewModel.UpcomingGroups);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task IsAutoRetrying_TogglesWithLoopState()
    {
        using var harness = new Harness(autoRetry: HangingRetry());

        harness.Bookings.GetBookings = (_, _, _) =>
            Task.FromException<PagedResponse<BookingResponse>>(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.StartAutoRetry();
        Assert.True(harness.ViewModel.IsAutoRetrying);

        harness.ViewModel.StopAutoRetry();
        Assert.False(harness.ViewModel.IsAutoRetrying);

        await harness.ViewModel.AutoRetryCompletion;
    }

    [Fact]
    public async Task RetryCommand_WhenPageHidden_DoesNotStartAutoRetry()
    {
        using var harness = new Harness(autoRetry: FastRetry());

        harness.Bookings.GetBookings = (_, _, _) =>
            Task.FromException<PagedResponse<BookingResponse>>(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);

        // Вкладка не сообщала о видимости: кнопка «повторить сейчас» не имеет права включить
        // фон и оставить его работать на скрытом экране.
        await harness.ViewModel.RetryCommand.ExecuteAsync(null);

        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task ColdStartOffline_MeTransportFailure_Visible_RecoversThroughLoopAfterNetworkReturns()
    {
        var gate = new TaskCompletionSource();

        using var harness = new Harness(autoRetry: GatedRetry(gate.Task));

        harness.Account.Me = () =>
            Task.FromException<MeResponse>(new HttpRequestException("refused"));

        // Страница уже видна до первой загрузки — как в OnAppearing у BookingsPage.
        harness.ViewModel.StartAutoRetry();

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowError);
        Assert.Equal(NetworkText, viewModel.ErrorMessage);
        Assert.True(viewModel.IsAutoRetrying);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);

        // Сеть вернулась: /me теперь отвечает, список тоже доступен.
        harness.Account.Me = () => Task.FromResult(MeOf(UserId));
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
            [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));

        gate.SetResult();

        await viewModel.AutoRetryCompletion;

        Assert.Equal(2, harness.Account.Calls);
        var request = Assert.Single(harness.Bookings.Requests);
        Assert.Equal(UserId, request.UserId);
        Assert.Null(request.Cursor);
        Assert.Single(viewModel.UpcomingGroups);
        Assert.False(viewModel.ShowError);
        Assert.False(viewModel.IsAutoRetrying);
        Assert.Equal(1, viewModel.AutoRetry.Attempts);
    }

    [Fact]
    public async Task ColdStartOffline_MeTerminalFailure_DoesNotStartLoop()
    {
        using var harness = new Harness(autoRetry: FastRetry());

        harness.Account.Me = () =>
            Task.FromException<MeResponse>(ApiError(HttpStatusCode.NotFound));

        harness.ViewModel.StartAutoRetry();

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowError);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.False(viewModel.IsAutoRetrying);

        // Даже повторный сигнал видимости не оживляет окончательный отказ.
        viewModel.StartAutoRetry();

        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task ColdStartOffline_MeTransportFailure_WhenHidden_StartsOnlyAfterVisible()
    {
        var gate = new TaskCompletionSource();

        using var harness = new Harness(autoRetry: GatedRetry(gate.Task));

        harness.Account.Me = () =>
            Task.FromException<MeResponse>(new HttpRequestException("refused"));

        // Вкладка ещё не сообщала о видимости: цикл не имеет права ходить в сеть.
        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowError);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.False(viewModel.IsAutoRetrying);

        harness.Account.Me = () => Task.FromResult(MeOf(UserId));
        harness.Bookings.GetBookings = (_, _, _) => Task.FromResult(Page(
            [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))]));

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsAutoRetrying);

        gate.SetResult();

        await viewModel.AutoRetryCompletion;

        Assert.Single(viewModel.UpcomingGroups);
        Assert.False(viewModel.ShowError);
        Assert.False(viewModel.IsAutoRetrying);
        Assert.Equal(1, viewModel.AutoRetry.Attempts);
    }

    [Fact]
    public async Task LoadMoreAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        using var harness = new Harness(loadMoreAutoRetry: FastRetry());
        var calls = 0;

        harness.Bookings.GetBookings = (_, _, cursor) =>
        {
            calls++;

            return cursor is null
                ? Task.FromResult(Page(
                    [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))],
                    "c1"))
                : calls <= 4
                    ? Task.FromException<PagedResponse<BookingResponse>>(
                        new HttpRequestException("refused"))
                    : Task.FromResult(Page(
                        [Booking(SecondBookingId, startAt: At(2026, 11, 24, 10, 0))]));
        };

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowLoadMoreError);

        viewModel.StartAutoRetry();
        await viewModel.LoadMoreAutoRetryCompletion;

        Assert.Equal(3, viewModel.LoadMoreAutoRetry.Attempts);
        Assert.Equal(2, viewModel.UpcomingGroups.SelectMany(group => group).Count());
        Assert.False(viewModel.ShowLoadMoreError);
        Assert.False(viewModel.IsLoadMoreAutoRetrying);
        Assert.Equal("c1", harness.Bookings.Requests[^1].Cursor);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task LoadMoreAutoRetry_TerminalFailure_DoesNotStart(HttpStatusCode statusCode)
    {
        using var harness = new Harness(loadMoreAutoRetry: FastRetry());

        harness.Bookings.GetBookings = (_, _, cursor) => cursor is null
            ? Task.FromResult(Page(
                [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))],
                "c1"))
            : Task.FromException<PagedResponse<BookingResponse>>(ApiError(statusCode));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowLoadMoreError);
        Assert.Equal(LoadMoreErrorText, viewModel.LoadMoreErrorMessage);

        // Окончательный отказ не повторяется: цикл не делает ни одной попытки.
        viewModel.StartAutoRetry();

        Assert.Equal(0, viewModel.LoadMoreAutoRetry.Attempts);
        Assert.False(viewModel.IsLoadMoreAutoRetrying);
        Assert.True(viewModel.CanLoadMore);
    }

    [Fact]
    public async Task StopAutoRetry_WhileLoadMoreWaiting_StopsFurtherAttempts()
    {
        using var harness = new Harness(loadMoreAutoRetry: StopAfterFirstAttemptRetry());

        harness.Bookings.GetBookings = (_, _, cursor) => cursor is null
            ? Task.FromResult(Page(
                [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))],
                "c1"))
            : Task.FromException<PagedResponse<BookingResponse>>(
                new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();

        Assert.Equal(1, viewModel.LoadMoreAutoRetry.Attempts);
        Assert.True(viewModel.IsLoadMoreAutoRetrying);

        // Уход со страницы останавливает и цикл догрузки, а не только цикл первой страницы.
        viewModel.StopAutoRetry();
        await viewModel.LoadMoreAutoRetryCompletion;

        Assert.Equal(1, viewModel.LoadMoreAutoRetry.Attempts);
        Assert.False(viewModel.IsLoadMoreAutoRetrying);
    }

    [Fact]
    public async Task RetryLoadMoreCommand_WhileAutoRetrying_LoadsImmediatelyAndSuccessDoesNotRestart()
    {
        using var harness = new Harness(loadMoreAutoRetry: HangingRetry());
        var calls = 0;

        harness.Bookings.GetBookings = (_, _, cursor) =>
        {
            calls++;

            return cursor is null
                ? Task.FromResult(Page(
                    [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))],
                    "c1"))
                : calls == 2
                    ? Task.FromException<PagedResponse<BookingResponse>>(
                        new HttpRequestException("refused"))
                    : Task.FromResult(Page(
                        [Booking(SecondBookingId, startAt: At(2026, 11, 24, 10, 0))]));
        };

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsLoadMoreAutoRetrying);
        Assert.Equal(0, viewModel.LoadMoreAutoRetry.Attempts);

        // «Повторить» — «повторить сейчас»: цикл догрузки останавливается, чтение идёт сразу.
        await viewModel.RetryLoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(3, harness.Bookings.Requests.Count);
        Assert.Equal("c1", harness.Bookings.Requests[^1].Cursor);
        Assert.Equal(0, viewModel.LoadMoreAutoRetry.Attempts);
        Assert.False(viewModel.IsLoadMoreAutoRetrying);
        Assert.False(viewModel.ShowLoadMoreError);
        Assert.Equal(2, viewModel.UpcomingGroups.SelectMany(group => group).Count());
    }

    [Fact]
    public async Task RetryLoadMoreCommand_WhileAutoRetrying_LoadFailsRetryable_RestartsAndKeepsRetryingUntilSuccess()
    {
        var restartGate = new TaskCompletionSource();
        var silentAttempt = new TaskCompletionSource<PagedResponse<BookingResponse>>();

        using var harness = new Harness(
            loadMoreAutoRetry: StopThenGateRetry(restartGate.Task));
        var calls = 0;

        harness.Bookings.GetBookings = (_, _, cursor) =>
        {
            calls++;

            return cursor is null
                ? Task.FromResult(Page(
                    [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))],
                    "c1"))
                : calls switch
                {
                    2 or 3 => Task.FromException<PagedResponse<BookingResponse>>(
                        new HttpRequestException("refused")),
                    4 => silentAttempt.Task,
                    _ => Task.FromResult(Page(
                        [Booking(SecondBookingId, startAt: At(2026, 11, 24, 10, 0))])),
                };
        };

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsLoadMoreAutoRetrying);
        Assert.Equal(0, viewModel.LoadMoreAutoRetry.Attempts);

        // Кнопка читает сразу; сбой снова повторяемый — цикл возвращается к жизни.
        await viewModel.RetryLoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(3, calls);
        Assert.Equal(0, viewModel.LoadMoreAutoRetry.Attempts);
        Assert.True(viewModel.IsLoadMoreAutoRetrying);

        restartGate.SetResult();
        silentAttempt.SetException(new HttpRequestException("refused"));

        await viewModel.LoadMoreAutoRetryCompletion;

        Assert.Equal(2, viewModel.LoadMoreAutoRetry.Attempts);
        Assert.Equal(5, calls);
        Assert.Equal(2, viewModel.UpcomingGroups.SelectMany(group => group).Count());
        Assert.False(viewModel.ShowLoadMoreError);
        Assert.False(viewModel.IsLoadMoreAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_FailedSilentLoadMoreAttempt_KeepsLoadMoreError()
    {
        using var harness = new Harness(loadMoreAutoRetry: StopAfterFirstAttemptRetry());
        var calls = 0;
        var retry = new TaskCompletionSource<PagedResponse<BookingResponse>>();

        harness.Bookings.GetBookings = (_, _, cursor) =>
        {
            calls++;

            return cursor is null
                ? Task.FromResult(Page(
                    [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))],
                    "c1"))
                : calls == 2
                    ? Task.FromException<PagedResponse<BookingResponse>>(
                        new HttpRequestException("refused"))
                    : retry.Task;
        };

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.Equal(NetworkText, viewModel.LoadMoreErrorMessage);

        viewModel.StartAutoRetry();

        // Попытка висит: причина, строки, курсор и кнопка остаются на месте, пока ответа нет.
        Assert.Equal(1, viewModel.LoadMoreAutoRetry.Attempts);
        Assert.True(viewModel.ShowLoadMoreError);
        Assert.Equal(NetworkText, viewModel.LoadMoreErrorMessage);
        Assert.Single(viewModel.UpcomingGroups);
        Assert.True(viewModel.CanLoadMore);
        Assert.False(viewModel.ShowLoading);

        retry.SetException(new HttpRequestException("refused"));

        Assert.True(viewModel.ShowLoadMoreError);
        Assert.Equal(NetworkText, viewModel.LoadMoreErrorMessage);

        viewModel.StopAutoRetry();
        await viewModel.LoadMoreAutoRetryCompletion;

        Assert.False(viewModel.IsLoadMoreAutoRetrying);
    }

    [Fact]
    public async Task StopAutoRetry_DuringSilentLoadMoreAttempt_KeepsLoadMoreError()
    {
        using var harness = new Harness(loadMoreAutoRetry: StopAfterFirstAttemptRetry());
        var calls = 0;
        var retry = new TaskCompletionSource<PagedResponse<BookingResponse>>();

        harness.Bookings.GetBookings = (_, _, cursor) =>
        {
            calls++;

            return cursor is null
                ? Task.FromResult(Page(
                    [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))],
                    "c1"))
                : calls == 2
                    ? Task.FromException<PagedResponse<BookingResponse>>(
                        new HttpRequestException("refused"))
                    : retry.Task;
        };

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();

        Assert.Equal(NetworkText, viewModel.LoadMoreErrorMessage);

        // Отменённая попытка не имеет права унести с собой причину и строки: иначе экран
        // остался бы без догрузки и без «Повторить».
        viewModel.StopAutoRetry();
        retry.SetCanceled();
        await viewModel.LoadMoreAutoRetryCompletion;

        Assert.True(viewModel.ShowLoadMoreError);
        Assert.Equal(NetworkText, viewModel.LoadMoreErrorMessage);
        Assert.Single(viewModel.UpcomingGroups);
        Assert.True(viewModel.CanLoadMore);
        Assert.False(viewModel.IsLoadMoreAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_LoadMoreTimeout_ShowsNetworkText()
    {
        using var harness = new Harness(loadMoreAutoRetry: FastRetry());
        var calls = 0;

        harness.Bookings.GetBookings = (_, _, cursor) =>
        {
            calls++;

            return cursor is null
                ? Task.FromResult(Page(
                    [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))],
                    "c1"))
                : calls == 2
                    ? Task.FromException<PagedResponse<BookingResponse>>(
                        new TaskCanceledException("timeout"))
                    : Task.FromResult(Page(
                        [Booking(SecondBookingId, startAt: At(2026, 11, 24, 10, 0))]));
        };

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        // Таймаут HttpClient приходит отменой, но токен жив: для человека это «нет связи».
        Assert.Equal(NetworkText, viewModel.LoadMoreErrorMessage);

        viewModel.StartAutoRetry();
        await viewModel.LoadMoreAutoRetryCompletion;

        Assert.Equal(1, viewModel.LoadMoreAutoRetry.Attempts);
        Assert.Equal(2, viewModel.UpcomingGroups.SelectMany(group => group).Count());
        Assert.False(viewModel.ShowLoadMoreError);
    }

    [Fact]
    public async Task LoadMoreAutoRetry_AfterTabSwitch_RetriesTheFailedTabCursor()
    {
        var gate = new TaskCompletionSource();

        using var harness = new Harness(loadMoreAutoRetry: GatedRetry(gate.Task));
        var calls = 0;

        harness.Bookings.GetBookings = (scope, _, cursor) =>
        {
            calls++;

            if (scope == BookingScope.Past)
            {
                return Task.FromResult(Page([HistoryBooking()]));
            }

            return cursor is null
                ? Task.FromResult(Page(
                    [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))],
                    "c1"))
                : calls == 2
                    ? Task.FromException<PagedResponse<BookingResponse>>(
                        new HttpRequestException("refused"))
                    : Task.FromResult(Page(
                        [Booking(SecondBookingId, startAt: At(2026, 11, 24, 10, 0))]));
        };

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowLoadMoreError);

        viewModel.StartAutoRetry();

        // Пока попытка ждёт паузу, человек уходит на «Историю»: снимок упавшей догрузки —
        // «Предстоящие» и курсор c1, и повтор не имеет права взять текущую вкладку.
        await viewModel.SelectHistoryCommand.ExecuteAsync(null);

        gate.SetResult();
        await viewModel.LoadMoreAutoRetryCompletion;

        var request = harness.Bookings.Requests[^1];

        Assert.Equal(BookingScope.Upcoming, request.Scope);
        Assert.Equal("c1", request.Cursor);
        Assert.Equal(2, viewModel.UpcomingGroups.SelectMany(group => group).Count());
        Assert.Single(viewModel.HistoryGroups.SelectMany(group => group));
    }

    [Fact]
    public async Task LoadMoreAutoRetry_RetriedPageRepeatsItem_DoesNotDuplicate()
    {
        using var harness = new Harness(loadMoreAutoRetry: FastRetry());
        var calls = 0;
        var first = Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30));

        harness.Bookings.GetBookings = (_, _, cursor) =>
        {
            calls++;

            return cursor is null
                ? Task.FromResult(Page([first], "c1"))
                : calls == 2
                    ? Task.FromException<PagedResponse<BookingResponse>>(
                        new HttpRequestException("refused"))
                    : Task.FromResult(Page(
                        [first, Booking(SecondBookingId, startAt: At(2026, 11, 24, 10, 0))]));
        };

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();
        await viewModel.LoadMoreAutoRetryCompletion;

        var rows = viewModel.UpcomingGroups.SelectMany(group => group).ToList();

        // Сервер повторил уже показанную строку: повтор не добавляет второй экземпляр.
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, row => row.Id == FirstBookingId);
        Assert.False(viewModel.ShowLoadMoreError);
    }

    [Fact]
    public async Task LoadMoreCommand_WhileLoadMoreRetrying_DoesNotStartSecondRead()
    {
        using var harness = new Harness(loadMoreAutoRetry: FastRetry());
        var calls = 0;
        var retry = new TaskCompletionSource<PagedResponse<BookingResponse>>();

        harness.Bookings.GetBookings = (_, _, cursor) =>
        {
            calls++;

            return cursor is null
                ? Task.FromResult(Page(
                    [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))],
                    "c1"))
                : calls == 2
                    ? Task.FromException<PagedResponse<BookingResponse>>(
                        new HttpRequestException("refused"))
                    : retry.Task;
        };

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();

        // Тихая попытка уже читает ту же страницу тем же курсором: вторая догрузка по
        // порогу прокрутки добавила бы её дважды, поэтому общий флаг её не пускает.
        Assert.Equal(1, viewModel.LoadMoreAutoRetry.Attempts);
        Assert.True(viewModel.IsLoadingMore);

        var requests = harness.Bookings.Requests.Count;

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(requests, harness.Bookings.Requests.Count);

        retry.SetResult(Page(
            [Booking(SecondBookingId, startAt: At(2026, 11, 24, 10, 0))],
            null));
        await viewModel.LoadMoreAutoRetryCompletion;

        Assert.Equal(2, viewModel.UpcomingGroups.SelectMany(group => group).Count());
        Assert.False(viewModel.ShowLoadMoreError);
        Assert.False(viewModel.IsLoadingMore);
    }

    [Fact]
    public async Task LoadMoreCommand_FirstPageRefreshDuringLoad_DropsStalePageAndStopsSpinner()
    {
        using var harness = new Harness();
        var loadMorePage = new TaskCompletionSource<PagedResponse<BookingResponse>>();
        var calls = 0;

        harness.Bookings.GetBookings = (_, _, cursor) =>
        {
            calls++;

            return cursor switch
            {
                null => Task.FromResult(Page(
                    [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))],
                    "c1")),
                _ when calls == 2 => loadMorePage.Task,
                _ => Task.FromResult(Page(
                    [Booking(SecondBookingId, startAt: At(2026, 11, 24, 10, 0))],
                    null)),
            };
        };

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        // Догрузка висит, а поверх неё первую страницу перечитывает свайп: поколение
        // сдвигается, и запоздавшая страница не имеет права попасть в список.
        var loading = viewModel.LoadMoreCommand.ExecuteAsync(null);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoadingMore);

        loadMorePage.SetResult(Page(
            [Booking(ThirdBookingId, startAt: At(2026, 11, 28, 12, 0))],
            null));
        await loading;

        var rows = viewModel.UpcomingGroups.SelectMany(group => group).ToList();

        Assert.Single(rows);
        Assert.Equal(FirstBookingId, rows[0].Id);
        Assert.False(viewModel.IsLoadingMore);
        Assert.True(viewModel.CanLoadMore);

        // Флаг снят: следующая догрузка идёт и добавляет свою страницу.
        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.UpcomingGroups.SelectMany(group => group).Count());
    }

    [Fact]
    public async Task IsLoadMoreAutoRetrying_TogglesWithLoopState()
    {
        using var harness = new Harness(loadMoreAutoRetry: HangingRetry());

        harness.Bookings.GetBookings = (_, _, cursor) => cursor is null
            ? Task.FromResult(Page(
                [Booking(FirstBookingId, startAt: At(2026, 11, 20, 15, 30))],
                "c1"))
            : Task.FromException<PagedResponse<BookingResponse>>(
                new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync();
        await harness.ViewModel.LoadMoreCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();
        Assert.True(viewModel.IsLoadMoreAutoRetrying);
        Assert.False(viewModel.IsAutoRetrying);

        viewModel.StopAutoRetry();
        Assert.False(viewModel.IsLoadMoreAutoRetrying);

        await viewModel.LoadMoreAutoRetryCompletion;
    }

    [Fact]
    public async Task AutoRefreshAsync_WhileAutoRetrying_DoesNotSendSecondRequest()
    {
        using var harness = new Harness(autoRetry: HangingRetry());
        var calls = 0;

        harness.Bookings.GetBookings = (_, _, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromResult(Page([]))
                : Task.FromException<PagedResponse<BookingResponse>>(
                    new HttpRequestException("refused"));
        };

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();

        // Тик таймера падает и запускает повтор первой страницы: цикл уже читает этот
        // список, и второй такой же запрос только сдвинул бы поколение и убил попытку.
        await viewModel.AutoRefreshAsync();

        Assert.Equal(2, calls);
        Assert.True(viewModel.IsAutoRetrying);

        await viewModel.AutoRefreshAsync();

        Assert.Equal(2, calls);

        viewModel.StopAutoRetry();
        await viewModel.AutoRetryCompletion;
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

    /// <summary>
    /// Строка с уникальным id для списков в несколько страниц: номер задаёт хвост GUID
    /// и не пересекается с константами теста.
    /// </summary>
    private static BookingResponse NumberedBooking(int number) =>
        Booking(Guid.Parse($"9e200000-0000-4000-8000-{number:D12}"));

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
            "Тимур Мастеров",
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
            IsActive: true,
            SpecializationId: SpecializationId,
            SpecializationName: "Фейд");

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
    /// Подменённый клиент карточки мастера: отдаёт имена, услугу, салон и пояс и считает
    /// запросы — «один мастер — одна карточка» проверяется именно по ним.
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
            throw new NotSupportedException("Экран записей не ищет мастеров.");

        public Task<IReadOnlyList<SpecializationResponse>> GetSpecializationsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не читает справочник специализаций.");

        public Task<IReadOnlyList<HairServiceResponse>> GetHairServicesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не читает справочник услуг.");

        public Task<IReadOnlyList<CitySuggestionResponse>> GetCitySuggestionsAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не читает подсказки города.");

        public Task<BarberSlotsResponse> GetSlotsAsync(
            Guid barberProfileId,
            DateOnly slotDate,
            Guid? hairServiceId,
            Guid? salonId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не читает слоты мастера.");

        public Task<PagedResponse<BarberSearch.Contracts.Reviews.GetReviews.ReviewResponse>> GetReviewsAsync(
            Guid barberProfileId,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не читает отзывы мастера.");

        public Task<IReadOnlyList<BarberSearch.Contracts.Salons.GetSalonDirectory.SalonDirectoryItemResponse>>
            SearchSalonsAsync(
                string? city,
                string? search,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран записей не читает каталог салонов.");
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
    /// Цикл, который ждёт внешнего сигнала перед каждой попыткой: так видно снимок запроса,
    /// снятый до правки экрана, без таймеров и гонок.
    /// </summary>
    private static AutoRetryLoop GatedRetry(Task gate) =>
        new(
            (_, _) => gate,
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

    /// <summary>
    /// Собранный экран: сессия, подменённые клиенты, управляемые часы и ViewModel.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        public Harness(
            bool authenticated = true,
            AutoRetryLoop? autoRetry = null,
            AutoRetryLoop? loadMoreAutoRetry = null)
        {
            if (authenticated)
            {
                Store.Tokens = new StoredTokens("access-1", "refresh-1");
            }

            Session = new TokenSession(Store);
            ViewModel = new BookingsViewModel(
                Bookings,
                Barber,
                Account,
                Session,
                Time,
                autoRetry,
                loadMoreAutoRetry);
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
