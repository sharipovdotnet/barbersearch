using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Bookings;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки карточки барбера: загрузка, выбор услуги/салона/даты, слоты и отправка брони.
/// </summary>
/// <remarks>
/// Тексты сверяются дословно с вебом (<c>frontend/src/hooks/use-create-booking.ts</c>,
/// <c>frontend/src/components/barber/*</c>). Время салона проверяется на московском поясе
/// и фиксированном «сейчас»: 22:00 UTC 14 ноября — это уже 15-е в Москве и ещё 14-е
/// в Лос-Анджелесе, поэтому «сегодня» нельзя взять из пояса устройства.
/// </remarks>
public sealed class BarberCardViewModelTests
{
    private static readonly Guid ProfileId = Guid.Parse("9e100000-0000-4000-8000-000000000001");
    private static readonly Guid MoscowSalonId = Guid.Parse("9e100000-0000-4000-8000-000000000002");
    private static readonly Guid LosAngelesSalonId = Guid.Parse("9e100000-0000-4000-8000-000000000003");
    private static readonly Guid HairServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000004");
    private static readonly Guid BarberServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000005");
    private static readonly Guid HiddenBarberServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000006");
    private static readonly Guid CreatedId = Guid.Parse("9e100000-0000-4000-8000-00000000000a");

    private static readonly DateTimeOffset Now = new(2026, 11, 14, 22, 0, 0, TimeSpan.Zero);

    private const string NetworkText = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string SlotsHintText = "Выберите услугу, чтобы записаться.";
    private const string SignInText = "Войдите, чтобы подтвердить запись";
    private const string SessionExpiredText = "Сессия истекла — войдите снова.";

    [Fact]
    public async Task InitializeAsync_LoadedProfile_PicksPrimarySalonAnyServiceAndSalonToday()
    {
        using var harness = new Harness();

        await harness.ViewModel.InitializeAsync(ProfileId);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.HasProfile);
        Assert.True(viewModel.IsContentVisible);
        Assert.False(viewModel.IsLoading);
        Assert.Equal("Тимур Барберов", viewModel.DisplayName);
        Assert.Equal("Старший барбер", viewModel.GradeText);
        Assert.Equal("4,8 · 12 отзывов", viewModel.RatingText);
        Assert.StartsWith("E2E Studio", viewModel.PlaceText, StringComparison.Ordinal);

        Assert.Equal(MoscowSalonId, viewModel.SelectedSalon!.SalonId);
        Assert.Null(viewModel.SelectedService!.HairServiceId);
        Assert.Equal(new DateOnly(2026, 11, 15), viewModel.SelectedDateOnly);
        Assert.Equal(new DateTime(2026, 11, 15), viewModel.MinimumDate);
        Assert.Equal(new DateTime(2026, 12, 15), viewModel.MaximumDate);

        var request = Assert.Single(harness.Api.SlotsCalls);
        Assert.Equal(new DateOnly(2026, 11, 15), request.Date);
        Assert.Equal(MoscowSalonId, request.SalonId);
        Assert.Null(request.HairServiceId);
    }

    [Fact]
    public async Task InitializeAsync_MissingId_ShowsNotFoundWithoutRequest()
    {
        using var harness = new Harness();

        await harness.ViewModel.InitializeAsync(null);

        Assert.True(harness.ViewModel.IsNotFound);
        Assert.False(harness.ViewModel.IsLoading);
        Assert.False(harness.ViewModel.HasProfile);
        Assert.Equal(0, harness.Api.ProfileCalls);
    }

    [Fact]
    public async Task InitializeAsync_RepeatedAppearing_LoadsProfileOnce()
    {
        using var harness = new Harness();

        await harness.ViewModel.InitializeAsync(ProfileId);
        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.Equal(1, harness.Api.ProfileCalls);
        Assert.Single(harness.Api.SlotsCalls);
    }

    [Fact]
    public async Task InitializeAsync_ProfileNotFound_ShowsNotFound()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromException<BarberProfileResponse>(
            ApiError(HttpStatusCode.NotFound, ApiErrorCodes.BarberProfile.NotFound));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.True(harness.ViewModel.IsNotFound);
        Assert.False(harness.ViewModel.IsContentVisible);
        Assert.False(harness.ViewModel.HasLoadError);
    }

    [Fact]
    public async Task InitializeAsync_NetworkFailure_ShowsErrorAndRetryReloads()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromException<BarberProfileResponse>(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.True(harness.ViewModel.HasLoadError);
        Assert.Equal(NetworkText, harness.ViewModel.LoadErrorText);
        Assert.False(harness.ViewModel.IsContentVisible);
        Assert.False(harness.ViewModel.IsLoading);

        harness.Api.Profile = _ => Task.FromResult(ProfileResponse());
        await harness.ViewModel.RetryLoadCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.HasLoadError);
        Assert.True(harness.ViewModel.IsContentVisible);
        Assert.Equal(2, harness.Api.ProfileCalls);
    }

    [Fact]
    public async Task InitializeAsync_RateLimited_ShowsRateLimitWording()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromException<BarberProfileResponse>(
            ApiError(HttpStatusCode.TooManyRequests, "rateLimit.exceeded"));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.Equal(
            "Слишком много запросов. Подождите минуту и повторите.",
            harness.ViewModel.LoadErrorText);
    }

    [Fact]
    public async Task InitializeAsync_InactiveServices_AreNotOffered()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(services:
        [
            Service(BarberServiceId, HairServiceId, "E2E Стрижка", 2400m, 45, isActive: true),
            Service(HiddenBarberServiceId, Guid.CreateVersion7(), "Скрытая услуга", 1000m, 30, isActive: false),
        ]));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.Single(harness.ViewModel.Services);
        Assert.Equal("E2E Стрижка", harness.ViewModel.Services[0].Name);

        // «Любая услуга» плюс одна активная услуга.
        Assert.Equal(2, harness.ViewModel.ServiceOptions.Count);
        Assert.DoesNotContain(
            harness.ViewModel.Services,
            service => service.Name == "Скрытая услуга");
    }

    [Fact]
    public async Task SelectServiceAsync_RequestsSlotsWithHairServiceId()
    {
        using var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ProfileId);

        await harness.ViewModel.SelectServiceAsync(harness.ViewModel.ServiceOptions[1]);

        var request = harness.Api.SlotsCalls[^1];
        Assert.Equal(HairServiceId, request.HairServiceId);
        Assert.True(harness.ViewModel.CanSelectSlots);
        Assert.False(harness.ViewModel.ShowSlotsHint);
    }

    [Fact]
    public async Task SelectSlot_WithoutService_DoesNothingAndKeepsHint()
    {
        using var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ProfileId);

        harness.ViewModel.SelectSlotCommand.Execute(harness.ViewModel.Slots[0]);

        Assert.False(harness.ViewModel.CanSelectSlots);
        Assert.True(harness.ViewModel.ShowSlotsHint);
        Assert.False(harness.ViewModel.IsBookingPanelVisible);
        Assert.False(harness.ViewModel.Slots[0].IsSelected);
    }

    [Fact]
    public async Task SelectSlot_WithService_ShowsSnapshotSummary()
    {
        using var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ProfileId);

        await SelectServiceAndSlotAsync(harness.ViewModel);

        Assert.True(harness.ViewModel.IsBookingPanelVisible);
        Assert.True(harness.ViewModel.IsSummaryVisible);
        Assert.Equal("воскресенье, 15 ноября, 18:30–19:15", harness.ViewModel.SummaryDateText);
        Assert.Equal("E2E Стрижка · E2E Studio", harness.ViewModel.SummaryServiceText);
        Assert.Equal("2\u00A0400\u00A0₽", harness.ViewModel.SummaryPriceText);
        Assert.True(harness.ViewModel.Slots[0].IsSelected);
    }

    [Fact]
    public async Task SelectDateAsync_AfterSelection_ResetsSelectionAndLoadsNewDate()
    {
        using var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ProfileId);
        await SelectServiceAndSlotAsync(harness.ViewModel);

        await harness.ViewModel.SelectDateAsync(new DateTime(2026, 11, 16));

        Assert.False(harness.ViewModel.IsBookingPanelVisible);
        Assert.False(harness.ViewModel.Slots[0].IsSelected);
        Assert.Equal(new DateOnly(2026, 11, 16), harness.Api.SlotsCalls[^1].Date);
        Assert.Equal(new DateOnly(2026, 11, 16), harness.ViewModel.SelectedDateOnly);
    }

    [Fact]
    public async Task SelectSalonAsync_OtherTimeZone_RecomputesTodayAndResetsSelection()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(salons:
        [
            Salon(LosAngelesSalonId, "LA Studio", "America/Los_Angeles", isPrimary: true),
            Salon(MoscowSalonId, "E2E Studio", "Europe/Moscow", isPrimary: false),
        ]));

        await harness.ViewModel.InitializeAsync(ProfileId);

        // 22:00 UTC — это ещё 14 ноября в Лос-Анджелесе.
        Assert.Equal(new DateOnly(2026, 11, 14), harness.ViewModel.SelectedDateOnly);
        Assert.Equal(new DateTime(2026, 11, 14), harness.ViewModel.MinimumDate);

        await SelectServiceAndSlotAsync(harness.ViewModel);
        await harness.ViewModel.SelectSalonAsync(harness.ViewModel.Salons[1]);

        Assert.Equal(MoscowSalonId, harness.ViewModel.SelectedSalon!.SalonId);
        Assert.Equal(new DateOnly(2026, 11, 15), harness.ViewModel.SelectedDateOnly);
        Assert.Equal(new DateTime(2026, 11, 15), harness.ViewModel.MinimumDate);
        Assert.False(harness.ViewModel.IsBookingPanelVisible);

        var request = harness.Api.SlotsCalls[^1];
        Assert.Equal(MoscowSalonId, request.SalonId);
        Assert.Equal(new DateOnly(2026, 11, 15), request.Date);
    }

    [Fact]
    public async Task LoadSlotsAsync_StaleResponse_DoesNotReplaceNewSlotList()
    {
        using var harness = new Harness();

        var stale = new TaskCompletionSource<BarberSlotsResponse>();
        var calls = 0;

        harness.Api.Slots = (date, hair, salon) =>
        {
            calls++;

            return calls switch
            {
                1 => Task.FromResult(WorkingDay(date, null, null, salon ?? MoscowSalonId)),
                2 => stale.Task,
                _ => Task.FromResult(WorkingDay(
                    date,
                    BarberServiceId,
                    hair,
                    salon ?? MoscowSalonId,
                    SlotAt(date))),
            };
        };

        await harness.ViewModel.InitializeAsync(ProfileId);

        var serviceTask = harness.ViewModel.SelectServiceAsync(harness.ViewModel.ServiceOptions[1]);
        var dateTask = harness.ViewModel.SelectDateAsync(new DateTime(2026, 11, 17));
        await dateTask;

        // Устаревший ответ приходит позже нового: он должен быть проигнорирован.
        stale.SetResult(WorkingDay(
            new DateOnly(2026, 11, 15),
            BarberServiceId,
            HairServiceId,
            MoscowSalonId,
            SlotAt(new DateOnly(2026, 11, 15))));
        await serviceTask;

        var slot = Assert.Single(harness.ViewModel.Slots);
        Assert.Equal(SlotAt(new DateOnly(2026, 11, 17)).StartAt, slot.StartAt);
    }

    [Fact]
    public async Task LoadSlotsAsync_DayOff_ShowsWebWording()
    {
        using var harness = new Harness();
        harness.Api.Slots = (date, _, salon) => Task.FromResult(DayOff(date, salon ?? MoscowSalonId));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.True(harness.ViewModel.HasEmptySlots);
        Assert.Equal("В этот день барбер не работает.", harness.ViewModel.EmptySlotsText);
        Assert.Empty(harness.ViewModel.Slots);
    }

    [Fact]
    public async Task LoadSlotsAsync_EmptyList_ShowsWebWording()
    {
        using var harness = new Harness();
        harness.Api.Slots = (date, hair, salon) => Task.FromResult(
            WorkingDay(date, null, hair, salon ?? MoscowSalonId));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.True(harness.ViewModel.HasEmptySlots);
        Assert.Equal("Свободного времени на этот день нет.", harness.ViewModel.EmptySlotsText);
        Assert.Empty(harness.ViewModel.Slots);
    }

    [Fact]
    public async Task BookAsync_WithoutSession_SendsNothingAndAsksToSignIn()
    {
        using var harness = new Harness(authenticated: false);
        await harness.ViewModel.InitializeAsync(ProfileId);
        await SelectServiceAndSlotAsync(harness.ViewModel);

        await harness.ViewModel.BookCommand.ExecuteAsync(null);

        Assert.Empty(harness.Bookings.Requests);
        Assert.True(harness.ViewModel.IsSignInVisible);
        Assert.Equal(SignInText, harness.ViewModel.SignInText);
        Assert.False(harness.ViewModel.IsBookingErrorVisible);
    }

    [Fact]
    public async Task BookAsync_Success_ShowsConfirmationAndReloadsSlots()
    {
        using var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ProfileId);
        await SelectServiceAndSlotAsync(harness.ViewModel);
        var slotsBefore = harness.Api.SlotsCalls.Count;

        await harness.ViewModel.BookCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Bookings.Requests);
        Assert.Equal(BarberServiceId, request.BarberServiceId);
        Assert.Equal(MoscowSalonId, request.SalonId);
        Assert.Null(request.CustomerNote);

        Assert.True(harness.ViewModel.IsBooked);
        Assert.True(harness.ViewModel.IsSuccessVisible);
        Assert.False(harness.ViewModel.IsSummaryVisible);
        Assert.False(harness.ViewModel.IsBooking);
        Assert.Equal(slotsBefore + 1, harness.Api.SlotsCalls.Count);
    }

    [Fact]
    public async Task BookAsync_DoubleClick_SendsOneRequest()
    {
        using var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ProfileId);
        await SelectServiceAndSlotAsync(harness.ViewModel);

        var created = new TaskCompletionSource<Guid>();
        harness.Bookings.Create = _ => created.Task;

        var first = harness.ViewModel.BookCommand.ExecuteAsync(null);
        var second = harness.ViewModel.BookCommand.ExecuteAsync(null);

        created.SetResult(CreatedId);
        await Task.WhenAll(first, second);

        Assert.Single(harness.Bookings.Requests);
    }

    [Fact]
    public async Task BookAsync_NetworkFailure_RetryReusesSameIdempotencyKey()
    {
        using var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ProfileId);
        await SelectServiceAndSlotAsync(harness.ViewModel);
        harness.Bookings.Create = _ => Task.FromException<Guid>(new HttpRequestException("refused"));

        await harness.ViewModel.BookCommand.ExecuteAsync(null);

        Assert.Equal(NetworkText, harness.ViewModel.BookingErrorText);
        Assert.Equal("Повторить", harness.ViewModel.SubmitButtonText);
        Assert.True(harness.ViewModel.IsSubmitVisible);

        harness.Bookings.Create = _ => Task.FromResult(CreatedId);
        await harness.ViewModel.BookCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.Bookings.Requests.Count);
        Assert.Equal(
            harness.Bookings.Requests[0].IdempotencyKey,
            harness.Bookings.Requests[1].IdempotencyKey);
        Assert.True(harness.ViewModel.IsBooked);
    }

    [Fact]
    public async Task BookAsync_KeyConflict_NextAttemptUsesNewKey()
    {
        using var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ProfileId);
        await SelectServiceAndSlotAsync(harness.ViewModel);
        harness.Bookings.Create = _ => Task.FromException<Guid>(
            ApiError(HttpStatusCode.Conflict, "booking.create.idempotencyKeyConflict"));

        await harness.ViewModel.BookCommand.ExecuteAsync(null);

        Assert.Equal("Попробовать снова", harness.ViewModel.SubmitButtonText);
        Assert.True(harness.ViewModel.IsSummaryVisible);

        harness.Bookings.Create = _ => Task.FromResult(CreatedId);
        await harness.ViewModel.BookCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.Bookings.Requests.Count);
        Assert.NotEqual(
            harness.Bookings.Requests[0].IdempotencyKey,
            harness.Bookings.Requests[1].IdempotencyKey);
    }

    [Fact]
    public async Task BookAsync_Overlap_ReloadsSlotsAndShowsWebWording()
    {
        using var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ProfileId);
        await SelectServiceAndSlotAsync(harness.ViewModel);
        var slotsBefore = harness.Api.SlotsCalls.Count;
        harness.Bookings.Create = _ => Task.FromException<Guid>(
            ApiError(HttpStatusCode.Conflict, ApiErrorCodes.Booking.AddOverlap));

        await harness.ViewModel.BookCommand.ExecuteAsync(null);

        Assert.Equal("Это время только что заняли. Выберите другое.", harness.ViewModel.BookingErrorText);
        Assert.False(harness.ViewModel.IsSummaryVisible);
        Assert.False(harness.ViewModel.IsSubmitVisible);
        Assert.True(harness.ViewModel.IsBookingPanelVisible);
        Assert.Equal("Выбрать другое время", harness.ViewModel.SecondaryButtonText);
        Assert.Equal(slotsBefore + 1, harness.Api.SlotsCalls.Count);
    }

    [Fact]
    public async Task BookAsync_ServiceGone_OffersSlotsRefresh()
    {
        using var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ProfileId);
        await SelectServiceAndSlotAsync(harness.ViewModel);
        harness.Bookings.Create = _ => Task.FromException<Guid>(
            ApiError(HttpStatusCode.NotFound, ApiErrorCodes.BarberProfile.NotFound));

        await harness.ViewModel.BookCommand.ExecuteAsync(null);

        Assert.Equal("Эта услуга или салон больше недоступны.", harness.ViewModel.BookingErrorText);
        Assert.Equal("Обновить свободное время", harness.ViewModel.SecondaryButtonText);
        Assert.False(harness.ViewModel.IsSummaryVisible);
    }

    [Fact]
    public async Task BookAsync_SessionExpired_ShowsSignInStateWithoutSecondMessage()
    {
        using var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ProfileId);
        await SelectServiceAndSlotAsync(harness.ViewModel);

        harness.Bookings.Create = async _ =>
        {
            // Реальный обработчик сам гасит сессию на 401; тест повторяет эту часть.
            await harness.Session.ClearAsync();

            throw ApiError(HttpStatusCode.Unauthorized, ApiErrorCodes.Auth.Unauthorized);
        };

        await harness.ViewModel.BookCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.IsAuthenticated);
        Assert.True(harness.ViewModel.IsSignInVisible);
        Assert.Equal(SessionExpiredText, harness.ViewModel.SignInText);
        Assert.False(harness.ViewModel.IsBookingErrorVisible);
        Assert.True(harness.ViewModel.IsSummaryVisible);
    }

    [Fact]
    public async Task ChangeTimeAsync_AfterOverlap_ClosesPanelAndReloadsSlots()
    {
        using var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ProfileId);
        await SelectServiceAndSlotAsync(harness.ViewModel);
        harness.Bookings.Create = _ => Task.FromException<Guid>(
            ApiError(HttpStatusCode.Conflict, ApiErrorCodes.Booking.AddOverlap));

        await harness.ViewModel.BookCommand.ExecuteAsync(null);
        var slotsBefore = harness.Api.SlotsCalls.Count;

        await harness.ViewModel.ChangeTimeCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.IsBookingPanelVisible);
        Assert.Null(harness.ViewModel.BookingErrorText);
        Assert.Equal(slotsBefore + 1, harness.Api.SlotsCalls.Count);
    }

    /// <summary>
    /// Выбирает услугу и первый слот: так выглядит экран перед записью.
    /// </summary>
    private static async Task SelectServiceAndSlotAsync(BarberCardViewModel viewModel)
    {
        await viewModel.SelectServiceAsync(viewModel.ServiceOptions[1]);
        viewModel.SelectSlotCommand.Execute(viewModel.Slots[0]);
    }

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, params string[] codes) =>
        new(statusCode, "Request failed.", codes, new Dictionary<string, string[]>());

    private static BarberProfileResponse ProfileResponse(
        IReadOnlyList<BarberProfileServiceResponse>? services = null,
        IReadOnlyList<BarberProfileSalonResponse>? salons = null) =>
        new(
            ProfileId,
            Guid.CreateVersion7(),
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
            services ?? [Service(BarberServiceId, HairServiceId, "E2E Стрижка", 2400m, 45, isActive: true)],
            salons ?? [Salon(MoscowSalonId, "E2E Studio", "Europe/Moscow", isPrimary: true)],
            [],
            [],
            []);

    private static BarberProfileServiceResponse Service(
        Guid barberServiceId,
        Guid hairServiceId,
        string name,
        decimal price,
        int durationMinutes,
        bool isActive) =>
        new(
            barberServiceId,
            hairServiceId,
            name,
            ServiceCategory.Haircut,
            40,
            price,
            durationMinutes,
            isActive);

    private static BarberProfileSalonResponse Salon(
        Guid salonId,
        string name,
        string timeZoneId,
        bool isPrimary) =>
        new(
            Guid.CreateVersion7(),
            salonId,
            name,
            "ул. Ленина, 1",
            "Москва",
            "Россия",
            timeZoneId,
            PlaceKind.Salon,
            isPrimary,
            []);

    private static BarberSlotsResponse WorkingDay(
        DateOnly date,
        Guid? barberServiceId,
        Guid? hairServiceId,
        Guid salonId,
        params BarberSlotResponse[] slots)
    {
        var offset = OffsetOf(salonId);

        return new BarberSlotsResponse(
            ProfileId,
            salonId,
            barberServiceId,
            hairServiceId,
            date,
            TimeZoneOf(salonId),
            new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), offset),
            new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), offset),
            barberServiceId is null ? 30 : 45,
            IsWorkingDay: true,
            slots);
    }

    private static BarberSlotsResponse DayOff(DateOnly date, Guid salonId) =>
        new(
            ProfileId,
            salonId,
            null,
            null,
            date,
            TimeZoneOf(salonId),
            new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), OffsetOf(salonId)),
            new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), OffsetOf(salonId)),
            30,
            IsWorkingDay: false,
            []);

    /// <summary>
    /// Слот «18:30–19:15» в поясе салона: в UTC это 15:30–16:15.
    /// </summary>
    private static BarberSlotResponse SlotAt(DateOnly date) => new(
        new DateTimeOffset(date.ToDateTime(new TimeOnly(15, 30)), TimeSpan.Zero),
        new DateTimeOffset(date.ToDateTime(new TimeOnly(16, 15)), TimeSpan.Zero));

    private static string TimeZoneOf(Guid salonId) =>
        salonId == LosAngelesSalonId ? "America/Los_Angeles" : "Europe/Moscow";

    private static TimeSpan OffsetOf(Guid salonId) =>
        salonId == LosAngelesSalonId ? TimeSpan.FromHours(-8) : TimeSpan.FromHours(3);

    /// <summary>
    /// «Сейчас» для расчёта дат: 22:00 UTC 14 ноября.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// Подменённый клиент чтений: отдаёт профиль и сетку, запоминает параметры запросов.
    /// </summary>
    private sealed class FakeBarberApi : IBarberSearchApi
    {
        public Func<Guid, Task<BarberProfileResponse>> Profile { get; set; } =
            _ => Task.FromResult(ProfileResponse());

        public Func<DateOnly, Guid?, Guid?, Task<BarberSlotsResponse>> Slots { get; set; } =
            (date, hairServiceId, salonId) => Task.FromResult(WorkingDay(
                date,
                hairServiceId is null ? null : BarberServiceId,
                hairServiceId,
                salonId ?? MoscowSalonId,
                SlotAt(date)));

        public int ProfileCalls { get; private set; }

        public List<SlotsRequest> SlotsCalls { get; } = [];

        public Task<BarberProfileResponse> GetBarberProfileAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            ProfileCalls++;

            return Profile(id);
        }

        public Task<BarberSlotsResponse> GetSlotsAsync(
            Guid barberProfileId,
            DateOnly slotDate,
            Guid? hairServiceId,
            Guid? salonId,
            CancellationToken cancellationToken = default)
        {
            SlotsCalls.Add(new SlotsRequest(slotDate, hairServiceId, salonId));

            return Slots(slotDate, hairServiceId, salonId);
        }

        public Task<PagedResponse<BarberSearchItemResponse>> SearchBarbersAsync(
            string? city,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Карточка не ищет барберов.");
    }

    /// <summary>
    /// Подменённый клиент брони: запоминает запросы и отдаёт заданный результат.
    /// </summary>
    private sealed class FakeBookingsApi : IBookingsApi
    {
        public List<CreateBookingRequest> Requests { get; } = [];

        public Func<CreateBookingRequest, Task<Guid>> Create { get; set; } =
            _ => Task.FromResult(CreatedId);

        public Task<Guid> CreateBookingAsync(
            CreateBookingRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Create(request);
        }
    }

    /// <summary>
    /// Параметры запроса сетки, чтобы проверять, что именно ушло на сервер.
    /// </summary>
    private sealed record SlotsRequest(DateOnly Date, Guid? HairServiceId, Guid? SalonId);

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

    /// <summary>
    /// Собранный экран: сессия, подменённые клиенты и ViewModel.
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
            ViewModel = new BarberCardViewModel(Api, Bookings, Session, new FixedTimeProvider(Now));
        }

        public FakeBarberApi Api { get; } = new();

        public FakeBookingsApi Bookings { get; } = new();

        public FakeTokenStore Store { get; } = new();

        public TokenSession Session { get; }

        public BarberCardViewModel ViewModel { get; }

        public void Dispose() => Session.Dispose();
    }
}
