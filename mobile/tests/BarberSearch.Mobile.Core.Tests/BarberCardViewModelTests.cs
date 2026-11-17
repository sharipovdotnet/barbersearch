using System.Net;
using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Bookings;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки карточки мастера: загрузка, выбор услуги/салона/даты, слоты и отправка брони.
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

    /// <summary>
    /// 14 ноября 2026, 22:30 UTC — это уже 15-е в Москве и ещё 14-е в Лос-Анджелесе:
    /// по этой строке видно, чей пояс взяли для даты отзыва.
    /// </summary>
    private static readonly DateTimeOffset ReviewInstant = new(2026, 11, 14, 22, 30, 0, TimeSpan.Zero);

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
        Assert.Equal("Тимур Мастеров", viewModel.DisplayName);
        Assert.Equal("Старший мастер", viewModel.GradeText);
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
        Assert.Equal("В этот день мастер не работает.", harness.ViewModel.EmptySlotsText);
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

    [Fact]
    public async Task InitializeAsync_PhotosWithoutThumbnail_UseMdVariantAndAbsoluteUrls()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(photos:
        [
            Photo("/api/media/portfolio/photo-1.webp"),
        ]));

        await harness.ViewModel.InitializeAsync(ProfileId);

        var photo = Assert.Single(harness.ViewModel.Photos);

        Assert.True(harness.ViewModel.HasPhotos);
        // Своя раздача без готовой миниатюры просит у сервера вариант md, а не исходник:
        // плитка сетки маленькая, и качать полноразмерный кадр ради неё незачем.
        Assert.Equal("http://localhost:5243/api/media/portfolio/photo-1.webp?size=md", photo.TileImageUrl);
        Assert.Equal("http://localhost:5243/api/media/portfolio/photo-1.webp", photo.ImageUrl);
        Assert.Equal("Работа мастера Тимур Мастеров, фото 1", photo.AltText);
    }

    [Fact]
    public async Task InitializeAsync_PhotoWithThumbnail_PrefersThumbnailAndTrimsDescription()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(photos:
        [
            Photo(
                "/api/media/portfolio/photo-2.webp",
                thumbnailUrl: "/api/media/portfolio/photo-2-thumb.webp",
                description: "  Текстурный затылок  "),
        ]));

        await harness.ViewModel.InitializeAsync(ProfileId);

        var photo = Assert.Single(harness.ViewModel.Photos);

        // Миниатюра уже нарезана сервером: повторно просить size=md нельзя, иначе
        // получился бы чужой query поверх готового адреса.
        Assert.Equal("http://localhost:5243/api/media/portfolio/photo-2-thumb.webp", photo.TileImageUrl);
        Assert.Equal("http://localhost:5243/api/media/portfolio/photo-2.webp", photo.ImageUrl);
        Assert.Equal("Текстурный затылок", photo.AltText);
        Assert.True(photo.HasDescription);
    }

    [Fact]
    public async Task InitializeAsync_AvatarUrl_ResolvesMdVariantAndHidesInitials()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(avatarUrl: "/api/media/avatars/avatar.webp"));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.True(harness.ViewModel.HasAvatar);
        Assert.False(harness.ViewModel.ShowInitials);
        // 192 px на 64 pt при тройной плотности: тот же вариант, что в вебе.
        Assert.Equal("http://localhost:5243/api/media/avatars/avatar.webp?size=md", harness.ViewModel.AvatarImageUrl);
        Assert.Equal("ТМ", harness.ViewModel.Initials);
    }

    [Fact]
    public async Task InitializeAsync_NullAvatar_ShowsInitialsFallback()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(avatarUrl: null));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.False(harness.ViewModel.HasAvatar);
        Assert.True(harness.ViewModel.ShowInitials);
        Assert.Null(harness.ViewModel.AvatarImageUrl);
        Assert.Equal("ТМ", harness.ViewModel.Initials);
    }

    [Fact]
    public async Task InitializeAsync_VerifiedProfile_ShowsVerificationAndExperience()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(
            yearsOfExperience: 8,
            isVerified: true));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.True(harness.ViewModel.IsVerified);
        // Форма слова зависит от числа: «8 лет», а не «8 года».
        Assert.Equal("Стаж: 8 лет", harness.ViewModel.ExperienceText);
    }

    [Fact]
    public async Task InitializeAsync_Languages_FillChipsInServerOrder()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(
            languages: [Language.Ru, Language.En, Language.Kg]));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.True(harness.ViewModel.HasLanguages);
        Assert.Equal(3, harness.ViewModel.LanguageChips.Count);
        // Самоназвания, как в вебе: English, а не «Английский».
        Assert.Equal("Русский", harness.ViewModel.LanguageChips[0]);
        Assert.Equal("English", harness.ViewModel.LanguageChips[1]);
        Assert.Equal("Кыргызча", harness.ViewModel.LanguageChips[2]);
    }

    [Fact]
    public async Task InitializeAsync_AboutLists_KeepWebOrderAndSkipEmptyLists()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(
            bio: "  Стригу с 2015 года.  ",
            additionalServices: ["  Борода  ", "   "],
            certificatesAndTraining: [],
            achievements: ["Победа на чемпионате"],
            goodies: [],
            communicationFormat: ["Дружелюбно"]));

        await harness.ViewModel.InitializeAsync(ProfileId);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.HasAbout);
        Assert.True(viewModel.HasBio);
        Assert.Equal("Стригу с 2015 года.", viewModel.Bio);

        // Заголовки идут веб-порядком, а списки без непустых строк не создаются вовсе:
        // иначе на экране появился бы заголовок без содержимого.
        Assert.Equal(3, viewModel.AboutLists.Count);
        Assert.Equal("Дополнительные услуги", viewModel.AboutLists[0].Title);
        Assert.Equal("Достижения", viewModel.AboutLists[1].Title);
        Assert.Equal("Формат общения", viewModel.AboutLists[2].Title);

        Assert.Equal("• Борода", viewModel.AboutLists[0].Items[0].Text);
        Assert.Equal("• Победа на чемпионате", viewModel.AboutLists[1].Items[0].Text);
        Assert.Equal("• Дружелюбно", viewModel.AboutLists[2].Items[0].Text);
    }

    [Fact]
    public async Task InitializeAsync_EmptyAboutData_HidesAboutSection()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(
            bio: "   ",
            languages: [],
            additionalServices: [],
            certificatesAndTraining: [],
            achievements: [],
            goodies: [],
            communicationFormat: [],
            paymentMethods: []));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.False(harness.ViewModel.HasAbout);
        Assert.False(harness.ViewModel.HasBio);
        Assert.False(harness.ViewModel.HasLanguages);
        Assert.False(harness.ViewModel.HasPayments);
        Assert.Empty(harness.ViewModel.AboutLists);
    }

    [Fact]
    public async Task InitializeAsync_Specializations_FillNameAndDescription()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(specializations:
        [
            Specialization("Фейд", "Короткая стрижка"),
            Specialization("Борода", null),
        ]));

        await harness.ViewModel.InitializeAsync(ProfileId);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.HasSpecializations);
        Assert.Equal(2, viewModel.Specializations.Count);
        Assert.Equal("Фейд", viewModel.Specializations[0].Name);
        Assert.True(viewModel.Specializations[0].HasDescription);
        Assert.False(viewModel.Specializations[1].HasDescription);
        Assert.Null(viewModel.Specializations[1].Description);
    }

    [Fact]
    public async Task InitializeAsync_PaymentMethods_FillChipsInServerOrder()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(
            paymentMethods: [PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.Sbp]));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.True(harness.ViewModel.HasPayments);
        Assert.Equal(3, harness.ViewModel.PaymentChips.Count);
        Assert.Equal("Наличные", harness.ViewModel.PaymentChips[0]);
        Assert.Equal("Банковская карта", harness.ViewModel.PaymentChips[1]);
        Assert.Equal("СБП", harness.ViewModel.PaymentChips[2]);
    }

    [Fact]
    public async Task InitializeAsync_LatePolicy_TrimsTextAndShowsSection()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(latePolicy: "  До 10 минут.  "));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.True(harness.ViewModel.HasLatePolicy);
        Assert.Equal("Опоздания: До 10 минут.", harness.ViewModel.LatePolicyText);
    }

    [Fact]
    public async Task InitializeAsync_BlankLatePolicy_HidesSection()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(latePolicy: "   "));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.False(harness.ViewModel.HasLatePolicy);
        Assert.Equal(string.Empty, harness.ViewModel.LatePolicyText);
    }

    [Fact]
    public async Task InitializeAsync_WithReviews_LoadsFirstPageInPrimarySalonTimeZone()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(
            salons:
            [
                // Неосновной салон стоит первым: пояс отзывов обязан браться у основного,
                // и разница поясов делает подмену заметной.
                Salon(LosAngelesSalonId, "LA Studio", "America/Los_Angeles", isPrimary: false),
                Salon(MoscowSalonId, "E2E Studio", "Europe/Moscow", isPrimary: true),
            ],
            rating: 4.8m,
            reviewsCount: 12));
        harness.Api.Reviews = (_, _, _) => Task.FromResult(new PagedResponse<ReviewResponse>(
            [Review(text: "Отлично", createdAt: ReviewInstant)],
            NextCursor: null));

        await harness.ViewModel.InitializeAsync(ProfileId);

        var request = Assert.Single(harness.Api.ReviewCalls);

        Assert.Equal(ProfileId, request.BarberProfileId);
        Assert.Null(request.Cursor);
        Assert.Equal(10, request.PageSize);
        Assert.Equal("4,8 · 12 отзывов", harness.ViewModel.Reviews.SummaryText);

        var item = Assert.Single(harness.ViewModel.Reviews.Items);

        // 22:30 UTC — это 01:30 15 ноября в Москве; в Лос-Анджелесе было бы ещё 14-е.
        Assert.Equal("15 ноября 2026", item.DateText);
        Assert.Equal("Отлично", item.BodyText);
    }

    [Fact]
    public async Task InitializeAsync_ZeroReviews_DoesNotRequestReviews()
    {
        using var harness = new Harness();
        harness.Api.Profile = _ => Task.FromResult(ProfileResponse(
            rating: null,
            reviewsCount: 0));

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.Empty(harness.Api.ReviewCalls);
        Assert.True(harness.ViewModel.Reviews.IsEmpty);
        Assert.False(harness.ViewModel.Reviews.HasSummary);
    }

    [Fact]
    public async Task InitializeAsync_RepeatedAppearing_DoesNotRequestReviewsAgain()
    {
        using var harness = new Harness();

        await harness.ViewModel.InitializeAsync(ProfileId);
        await harness.ViewModel.InitializeAsync(ProfileId);

        // Профиль уже загружен, поэтому секция отзывов не перезагружается: иначе каждый
        // возврат на экран стоил бы лишнего запроса и сбрасывал страницу.
        Assert.Single(harness.Api.ReviewCalls);
    }

    [Fact]
    public async Task InitializeAsync_ProfileWithoutPhotos_HidesWorksSection()
    {
        using var harness = new Harness();

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.False(harness.ViewModel.HasPhotos);
        Assert.Empty(harness.ViewModel.Photos);
    }

    [Fact]
    public async Task StartAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        using var harness = new Harness(autoRetry: FastRetry());
        var slots = harness.Api.Slots;
        var calls = 0;

        harness.Api.Slots = (date, hairServiceId, salonId) =>
        {
            calls++;

            return calls <= 3
                ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"))
                : slots(date, hairServiceId, salonId);
        };

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.True(harness.ViewModel.HasSlotsError);

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(3, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.HasSlotsError);
        Assert.Single(harness.ViewModel.Slots);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task StartAutoRetry_TerminalFailure_DoesNotStart(HttpStatusCode statusCode)
    {
        using var harness = new Harness(autoRetry: FastRetry());

        harness.Api.Slots = (_, _, _) =>
            Task.FromException<BarberSlotsResponse>(ApiError(statusCode));

        await harness.ViewModel.InitializeAsync(ProfileId);

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task StopAutoRetry_WhileWaiting_StopsFurtherAttempts()
    {
        using var harness = new Harness(autoRetry: StopAfterFirstAttemptRetry());

        harness.Api.Slots = (_, _, _) =>
            Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync(ProfileId);

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.True(harness.ViewModel.IsAutoRetrying);

        harness.ViewModel.StopAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task RetrySlotsCommand_WhileAutoRetrying_LoadsImmediatelyAndSuccessDoesNotRestart()
    {
        using var harness = new Harness(autoRetry: HangingRetry());
        var slots = harness.Api.Slots;
        var calls = 0;

        harness.Api.Slots = (date, hairServiceId, salonId) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"))
                : slots(date, hairServiceId, salonId);
        };

        await harness.ViewModel.InitializeAsync(ProfileId);

        harness.ViewModel.StartAutoRetry();

        Assert.True(harness.ViewModel.IsAutoRetrying);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);

        // «Повторить» у сетки — это «повторить сейчас»: цикл останавливается, чтение идёт сразу.
        await harness.ViewModel.RetrySlotsCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.Api.SlotsCalls.Count);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
        Assert.False(harness.ViewModel.HasSlotsError);
        Assert.Single(harness.ViewModel.Slots);
    }

    [Fact]
    public async Task RetrySlotsCommand_WhileAutoRetrying_LoadFailsRetryable_RestartsAndKeepsRetryingUntilSuccess()
    {
        var restartGate = new TaskCompletionSource();
        var silentAttempt = new TaskCompletionSource<BarberSlotsResponse>();

        using var harness = new Harness(autoRetry: StopThenGateRetry(restartGate.Task));
        var slots = harness.Api.Slots;
        var calls = 0;

        harness.Api.Slots = (date, hairServiceId, salonId) =>
        {
            calls++;

            return calls switch
            {
                1 or 2 => Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused")),
                3 => silentAttempt.Task,
                _ => slots(date, hairServiceId, salonId),
            };
        };

        await harness.ViewModel.InitializeAsync(ProfileId);

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsAutoRetrying);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);

        // Кнопка при работающем цикле читает сразу: к возврату команды ручной запрос уже ушёл,
        // а перезапущенный цикл ждёт паузы — второго запроса без неё нет.
        await viewModel.RetrySlotsCommand.ExecuteAsync(null);

        Assert.Equal(2, calls);
        Assert.Equal(2, harness.Api.SlotsCalls.Count);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.True(viewModel.IsAutoRetrying);

        // Повторяемый сбой кнопки возвращает цикл к жизни: первая же тихая попытка падает
        // снова, и цикл не сдаётся, а повторяет до успеха.
        restartGate.SetResult();
        silentAttempt.SetException(new HttpRequestException("refused"));

        await viewModel.AutoRetryCompletion;

        Assert.Equal(2, viewModel.AutoRetry.Attempts);
        Assert.Equal(4, calls);
        Assert.Single(viewModel.Slots);
        Assert.False(viewModel.HasSlotsError);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_FailedSilentAttempt_KeepsErrorMessage()
    {
        using var harness = new Harness(autoRetry: StopAfterFirstAttemptRetry());
        var calls = 0;
        var retry = new TaskCompletionSource<BarberSlotsResponse>();

        harness.Api.Slots = (_, _, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"))
                : retry.Task;
        };

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.Equal(NetworkText, harness.ViewModel.SlotsErrorText);

        harness.ViewModel.StartAutoRetry();

        // Тихая попытка не стирает причину на старте: карточка ошибки и «Повторить» остаются
        // на экране, пока ответ не пришёл, иначе экран мигал бы пустотой на каждой паузе.
        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.True(harness.ViewModel.HasSlotsError);
        Assert.Equal(NetworkText, harness.ViewModel.SlotsErrorText);
        Assert.False(harness.ViewModel.IsSlotsLoading);

        retry.SetException(ApiError(HttpStatusCode.BadRequest));

        await harness.ViewModel.AutoRetryCompletion;

        Assert.False(harness.ViewModel.IsAutoRetrying);
        Assert.True(harness.ViewModel.HasSlotsError);
        Assert.Equal("Не удалось загрузить свободное время.", harness.ViewModel.SlotsErrorText);
    }

    [Fact]
    public async Task StopAutoRetry_DuringSilentAttempt_KeepsSlotsError()
    {
        using var harness = new Harness(autoRetry: StopAfterFirstAttemptRetry());
        var calls = 0;
        var retry = new TaskCompletionSource<BarberSlotsResponse>();

        harness.Api.Slots = (_, _, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"))
                : retry.Task;
        };

        await harness.ViewModel.InitializeAsync(ProfileId);

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(NetworkText, harness.ViewModel.SlotsErrorText);

        // Уход со страницы отменяет попытку; отменённая попытка не имеет права унести с собой
        // причину — иначе вернувшийся экран остался бы без сетки и без «Повторить».
        harness.ViewModel.StopAutoRetry();
        retry.SetCanceled();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.True(harness.ViewModel.HasSlotsError);
        Assert.Equal(NetworkText, harness.ViewModel.SlotsErrorText);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task StopAutoRetry_DuringSilentProfileAttempt_KeepsLoadError()
    {
        using var harness = new Harness(autoRetry: StopAfterFirstAttemptRetry());
        var calls = 0;
        var retry = new TaskCompletionSource<BarberProfileResponse>();

        harness.Api.Profile = _ => ++calls == 1
            ? Task.FromException<BarberProfileResponse>(new HttpRequestException("refused"))
            : retry.Task;

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.Equal(NetworkText, harness.ViewModel.LoadErrorText);

        harness.ViewModel.StartAutoRetry();

        // Тихий повтор профиля не стирает причину на старте: пока ответа нет, на экране
        // остаются «Повторить» и объяснение, а не пустая карточка с индикатором.
        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal(NetworkText, harness.ViewModel.LoadErrorText);
        Assert.False(harness.ViewModel.IsLoading);

        harness.ViewModel.StopAutoRetry();
        retry.SetCanceled();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(NetworkText, harness.ViewModel.LoadErrorText);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_ProfileRecoversButSlotsFail_KeepsRetrying()
    {
        using var harness = new Harness(autoRetry: FastRetry());
        var profile = harness.Api.Profile;
        var slots = harness.Api.Slots;
        var profileCalls = 0;
        var slotCalls = 0;

        harness.Api.Profile = id => ++profileCalls == 1
            ? Task.FromException<BarberProfileResponse>(new HttpRequestException("refused"))
            : profile(id);

        harness.Api.Slots = (date, hairServiceId, salonId) => ++slotCalls == 1
            ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"))
            : slots(date, hairServiceId, salonId);

        await harness.ViewModel.InitializeAsync(ProfileId);

        Assert.Equal(NetworkText, harness.ViewModel.LoadErrorText);

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        // Профиль загрузился на первой попытке, а сетка упала — цикл обязан продолжить сетку,
        // а не остановиться вместе с успехом профиля.
        Assert.Equal(2, harness.ViewModel.AutoRetry.Attempts);
        Assert.True(harness.ViewModel.IsContentVisible);
        Assert.False(harness.ViewModel.HasSlotsError);
        Assert.Single(harness.ViewModel.Slots);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task RetryLoadCommand_TerminalProfileFailure_DoesNotStartAutoRetry()
    {
        using var harness = new Harness(autoRetry: HangingRetry());

        harness.Api.Profile = _ => Task.FromException<BarberProfileResponse>(new HttpRequestException("refused"));

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.InitializeAsync(ProfileId);

        // Сетевой сбой на видимой странице — как раз повод для цикла.
        Assert.Equal(NetworkText, harness.ViewModel.LoadErrorText);
        Assert.True(harness.ViewModel.IsAutoRetrying);

        harness.Api.Profile = _ => Task.FromException<BarberProfileResponse>(ApiError(HttpStatusCode.BadRequest));

        // Кнопка останавливает фон и читает сразу; окончательный отказ не должен включать его
        // снова, иначе 400 повторялся бы без конца пауз и без единого шанса на успех.
        await harness.ViewModel.RetryLoadCommand.ExecuteAsync(null);

        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
        Assert.Equal("Не удалось загрузить профиль мастера.", harness.ViewModel.LoadErrorText);
    }

    [Fact]
    public async Task RetrySlotsCommand_WhenPageHidden_DoesNotStartAutoRetry()
    {
        using var harness = new Harness(autoRetry: FastRetry());

        harness.Api.Slots = (_, _, _) =>
            Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync(ProfileId);

        // Страница не сообщала о видимости: кнопка «повторить сейчас» не имеет права
        // включить фон и оставить его работать на скрытом экране.
        await harness.ViewModel.RetrySlotsCommand.ExecuteAsync(null);

        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_Timeout_ShowsNetworkText()
    {
        using var harness = new Harness(autoRetry: FastRetry());
        var slots = harness.Api.Slots;
        var calls = 0;

        harness.Api.Slots = (date, hairServiceId, salonId) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<BarberSlotsResponse>(new TaskCanceledException("timeout"))
                : slots(date, hairServiceId, salonId);
        };

        await harness.ViewModel.InitializeAsync(ProfileId);

        // Таймаут HttpClient приходит отменой, но токен жив: для человека это «нет связи»,
        // а не тихая отмена. Раньше этот случай проглатывался.
        Assert.Equal(NetworkText, harness.ViewModel.SlotsErrorText);

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.HasSlotsError);
        Assert.Single(harness.ViewModel.Slots);
    }

    [Fact]
    public async Task IsAutoRetrying_TogglesWithLoopState()
    {
        using var harness = new Harness(autoRetry: HangingRetry());

        harness.Api.Slots = (_, _, _) =>
            Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync(ProfileId);

        harness.ViewModel.StartAutoRetry();
        Assert.True(harness.ViewModel.IsAutoRetrying);

        harness.ViewModel.StopAutoRetry();
        Assert.False(harness.ViewModel.IsAutoRetrying);

        await harness.ViewModel.AutoRetryCompletion;
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
    /// Ожидание, которое заканчивается только отменой: Stop проверяется без таймеров.
    /// </summary>
    private static Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));

        return pending.Task;
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
        IReadOnlyList<BarberProfileSalonResponse>? salons = null,
        string? avatarUrl = null,
        string displayName = "Тимур Мастеров",
        string? bio = null,
        int yearsOfExperience = 11,
        IReadOnlyList<Language>? languages = null,
        IReadOnlyList<string>? certificatesAndTraining = null,
        decimal? rating = 4.8m,
        int reviewsCount = 12,
        IReadOnlyList<string>? achievements = null,
        IReadOnlyList<PaymentMethod>? paymentMethods = null,
        IReadOnlyList<string>? communicationFormat = null,
        IReadOnlyList<string>? goodies = null,
        string latePolicy = "",
        bool isVerified = true,
        IReadOnlyList<BarberProfileSpecializationResponse>? specializations = null,
        IReadOnlyList<string>? additionalServices = null,
        IReadOnlyList<BarberProfilePhotoResponse>? photos = null) =>
        new(
            ProfileId,
            Guid.CreateVersion7(),
            avatarUrl,
            displayName,
            Grade.SeniorBarber,
            bio,
            yearsOfExperience,
            languages ?? [Language.Ru],
            certificatesAndTraining ?? [],
            rating,
            reviewsCount,
            achievements ?? [],
            paymentMethods ?? [PaymentMethod.Cash],
            communicationFormat ?? [],
            goodies ?? [],
            latePolicy,
            true,
            isVerified,
            services ?? [Service(BarberServiceId, HairServiceId, "E2E Стрижка", 2400m, 45, isActive: true)],
            salons ?? [Salon(MoscowSalonId, "E2E Studio", "Europe/Moscow", isPrimary: true)],
            specializations ?? [],
            additionalServices ?? [],
            photos ?? []);

    private static BarberProfilePhotoResponse Photo(
        string url,
        string? thumbnailUrl = null,
        string? description = null) =>
        new(
            Guid.CreateVersion7(),
            url,
            thumbnailUrl,
            description,
            SortOrder: 0,
            IsPublished: true,
            IsBlocked: false,
            PhotoModerationStatus.Approved,
            RejectionReason: null);

    private static BarberProfileSpecializationResponse Specialization(string name, string? description) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            name,
            description);

    private static ReviewResponse Review(string? text, DateTimeOffset createdAt) =>
        new(
            Guid.CreateVersion7(),
            ProfileId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Rating: 5,
            text,
            IsPublished: true,
            createdAt,
            createdAt);

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

        public Func<Guid, string?, int?, Task<PagedResponse<ReviewResponse>>> Reviews { get; set; } =
            (_, _, _) => Task.FromResult(new PagedResponse<ReviewResponse>([], NextCursor: null));

        public int ProfileCalls { get; private set; }

        public List<SlotsRequest> SlotsCalls { get; } = [];

        public List<ReviewsRequest> ReviewCalls { get; } = [];

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

        public Task<PagedResponse<ReviewResponse>> GetReviewsAsync(
            Guid barberProfileId,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default)
        {
            ReviewCalls.Add(new ReviewsRequest(barberProfileId, cursor, pageSize));

            return Reviews(barberProfileId, cursor, pageSize);
        }

        public Task<PagedResponse<BarberSearchItemResponse>> SearchBarbersAsync(
            BarberSearchFilters filters,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Карточка не ищет мастеров.");

        public Task<IReadOnlyList<SpecializationResponse>> GetSpecializationsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Карточка не читает справочник специализаций.");

        public Task<IReadOnlyList<HairServiceResponse>> GetHairServicesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Карточка не читает справочник услуг.");

        public Task<IReadOnlyList<CitySuggestionResponse>> GetCitySuggestionsAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Карточка не читает подсказки города.");
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

        public Task<PagedResponse<BookingResponse>> GetBookingsAsync(
            BookingScope scope,
            Guid userId,
            string? cursor = null,
            int pageSize = 20,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Карточка не читает список записей.");

        public Task CancelBookingAsync(Guid bookingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Карточка не отменяет записи.");

        public Task<BarberSlotsResponse> GetRescheduleSlotsAsync(
            Guid bookingId,
            DateOnly slotDate,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Карточка не переносит записи.");

        public Task RescheduleBookingAsync(
            Guid bookingId,
            DateTimeOffset startAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Карточка не переносит записи.");
    }

    /// <summary>
    /// Параметры запроса сетки, чтобы проверять, что именно ушло на сервер.
    /// </summary>
    private sealed record SlotsRequest(DateOnly Date, Guid? HairServiceId, Guid? SalonId);

    /// <summary>
    /// Параметры запроса отзывов: профиль, курсор и размер страницы.
    /// </summary>
    private sealed record ReviewsRequest(Guid BarberProfileId, string? Cursor, int? PageSize);

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
        public Harness(bool authenticated = true, AutoRetryLoop? autoRetry = null)
        {
            if (authenticated)
            {
                Store.Tokens = new StoredTokens("access-1", "refresh-1");
            }

            Session = new TokenSession(Store);
            ViewModel = new BarberCardViewModel(
                Api,
                BarberSearchApiOptions.FromBaseAddress("http://localhost:5243"),
                Bookings,
                Session,
                new FixedTimeProvider(Now),
                autoRetry);
        }

        public FakeBarberApi Api { get; } = new();

        public FakeBookingsApi Bookings { get; } = new();

        public FakeTokenStore Store { get; } = new();

        public TokenSession Session { get; }

        public BarberCardViewModel ViewModel { get; }

        public void Dispose() => Session.Dispose();
    }
}
