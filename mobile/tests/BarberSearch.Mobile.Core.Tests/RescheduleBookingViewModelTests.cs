using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Bookings;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Перенести запись»: дата визита, границы, сетка слотов и отправка переноса.
/// </summary>
/// <remarks>
/// Строка списка собирается из <see cref="BookingResponse"/> и карточки мастера: без карточки
/// у неё нет пояса салона, и «сегодня» считалось бы в UTC. Время визита — 15:30 UTC, то есть
/// 18:30 в Москве; «сейчас» — 14 ноября, поэтому визит ещё впереди и перенос доступен.
/// </remarks>
public sealed class RescheduleBookingViewModelTests
{
    private static readonly Guid BookingId = Guid.Parse("9e300000-0000-4000-8000-000000000001");
    private static readonly Guid UserId = Guid.Parse("9e300000-0000-4000-8000-000000000002");
    private static readonly Guid BarberProfileId = Guid.Parse("9e300000-0000-4000-8000-000000000003");
    private static readonly Guid BarberServiceId = Guid.Parse("9e300000-0000-4000-8000-000000000004");
    private static readonly Guid HairServiceId = Guid.Parse("9e300000-0000-4000-8000-000000000005");
    private static readonly Guid SalonId = Guid.Parse("9e300000-0000-4000-8000-000000000006");

    private static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset VisitStartAt = new(2026, 11, 15, 15, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset VisitEndAt = new(2026, 11, 15, 16, 15, 0, TimeSpan.Zero);

    private static readonly DateOnly VisitDate = new(2026, 11, 15);
    private static readonly DateOnly NextDate = new(2026, 11, 16);
    private static readonly DateOnly AfterNextDate = new(2026, 11, 17);

    [Fact]
    public void Initialize_DefaultDate_IsSalonDateOfVisit()
    {
        var harness = new Harness();

        Assert.Equal(VisitDate.ToDateTime(TimeOnly.MinValue), harness.ViewModel.SelectedDate);
    }

    [Fact]
    public void Initialize_Bounds_AreTodayAndNinetyDays()
    {
        // 22:00 UTC 14 ноября — это уже 15-е в Москве: границы считаются в поясе салона.
        var harness = new Harness(new DateTimeOffset(2026, 11, 14, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTime(2026, 11, 15), harness.ViewModel.MinimumDate);
        Assert.Equal(new DateTime(2027, 2, 13), harness.ViewModel.MaximumDate);
    }

    [Fact]
    public async Task ChangeDate_NewDate_ResetsSelectionAndRefetches()
    {
        var harness = new Harness();
        await harness.ViewModel.LoadSlotsAsync();

        var previousSlot = Assert.Single(harness.ViewModel.Slots);
        harness.ViewModel.SelectSlotCommand.Execute(previousSlot);
        Assert.NotNull(harness.ViewModel.SelectedSlot);
        Assert.Single(harness.Api.SlotsCalls);

        await harness.ViewModel.ChangeDateAsync(NextDate.ToDateTime(TimeOnly.MinValue));

        Assert.Null(harness.ViewModel.SelectedSlot);
        Assert.False(harness.ViewModel.CanSubmit);

        var newSlot = Assert.Single(harness.ViewModel.Slots);
        Assert.NotSame(previousSlot, newSlot);
        Assert.Equal(SlotAt(NextDate).StartAt, newSlot.StartAt);

        Assert.Equal(2, harness.Api.SlotsCalls.Count);
        Assert.Equal(NextDate, harness.Api.SlotsCalls[^1].Date);
    }

    [Fact]
    public async Task ChangeDate_RapidSwitch_IgnoresStaleResponse()
    {
        var harness = new Harness();
        var stale = new TaskCompletionSource<BarberSlotsResponse>();

        harness.Api.Slots = date => date == NextDate
            ? stale.Task
            : Task.FromResult(WorkingDay(date, SlotAt(date)));

        await harness.ViewModel.LoadSlotsAsync();

        var staleTask = harness.ViewModel.ChangeDateAsync(NextDate.ToDateTime(TimeOnly.MinValue));
        var freshTask = harness.ViewModel.ChangeDateAsync(AfterNextDate.ToDateTime(TimeOnly.MinValue));
        await freshTask;

        // Ответ на старую дату приходит позже нового: он не должен подменить сетку.
        stale.SetResult(WorkingDay(NextDate, SlotAt(NextDate)));
        await staleTask;

        var slot = Assert.Single(harness.ViewModel.Slots);
        Assert.Equal(SlotAt(AfterNextDate).StartAt, slot.StartAt);
        Assert.Equal(AfterNextDate.ToDateTime(TimeOnly.MinValue), harness.ViewModel.SelectedDate);
    }

    [Fact]
    public async Task SubmitAsync_SendsExactlySelectedSlotStartAt()
    {
        var harness = new Harness();
        await harness.ViewModel.LoadSlotsAsync();

        var slot = Assert.Single(harness.ViewModel.Slots);
        harness.ViewModel.SelectSlotCommand.Execute(slot);

        BarberSlotResponse? confirmed = null;
        harness.ViewModel.Rescheduled += (_, raised) => confirmed = raised;

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Api.RescheduleCalls);
        Assert.Equal(BookingId, request.BookingId);
        Assert.Equal(slot.StartAt, request.StartAt);
        Assert.Null(harness.ViewModel.ErrorMessage);
        Assert.False(harness.ViewModel.IsSubmitting);

        Assert.NotNull(confirmed);
        Assert.Equal(slot.StartAt, confirmed!.StartAt);
        Assert.Equal(slot.EndAt, confirmed.EndAt);
    }

    [Fact]
    public async Task SubmitAsync_Conflict_RefetchesSlotsAndClearsSelection()
    {
        var harness = new Harness();
        await harness.ViewModel.LoadSlotsAsync();
        harness.ViewModel.SelectSlotCommand.Execute(harness.ViewModel.Slots[0]);
        var slotsBefore = harness.Api.SlotsCalls.Count;

        harness.Api.Reschedule = (_, _) => Task.FromException(
            ApiError(HttpStatusCode.Conflict, ApiErrorCodes.Booking.RescheduleOverlap));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Это время уже занято. Выберите другое.", harness.ViewModel.ErrorMessage);
        Assert.Null(harness.ViewModel.SelectedSlot);
        Assert.False(harness.ViewModel.CanSubmit);
        Assert.Equal(slotsBefore + 1, harness.Api.SlotsCalls.Count);
        Assert.Single(harness.ViewModel.Slots);
    }

    [Fact]
    public async Task SubmitAsync_TooFar_ShowsMessageWithoutSlotRefetch()
    {
        var harness = new Harness();
        await harness.ViewModel.LoadSlotsAsync();
        harness.ViewModel.SelectSlotCommand.Execute(harness.ViewModel.Slots[0]);
        var slotsBefore = harness.Api.SlotsCalls.Count;

        harness.Api.Reschedule = (_, _) => Task.FromException(
            ApiError(HttpStatusCode.BadRequest, ApiErrorCodes.Booking.RescheduleStartAtTooFar));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Слишком далеко в будущем.", harness.ViewModel.ErrorMessage);
        Assert.Equal(slotsBefore, harness.Api.SlotsCalls.Count);
        Assert.NotNull(harness.ViewModel.SelectedSlot);
    }

    [Fact]
    public async Task SubmitAsync_DoubleCall_MakesOneRequest()
    {
        var harness = new Harness();
        await harness.ViewModel.LoadSlotsAsync();
        harness.ViewModel.SelectSlotCommand.Execute(harness.ViewModel.Slots[0]);

        var pending = new TaskCompletionSource();
        harness.Api.Reschedule = (_, _) => pending.Task;

        var first = harness.ViewModel.SubmitCommand.ExecuteAsync(null);
        var second = harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        pending.SetResult();
        await Task.WhenAll(first, second);

        Assert.Single(harness.Api.RescheduleCalls);
        Assert.False(harness.ViewModel.IsSubmitting);
    }

    [Fact]
    public async Task LoadSlots_DayOff_ShowsBarberNotWorkingText()
    {
        var harness = new Harness();
        harness.Api.Slots = date => Task.FromResult(DayOff(date));

        await harness.ViewModel.LoadSlotsAsync();

        Assert.False(harness.ViewModel.IsWorkingDay);
        Assert.True(harness.ViewModel.ShowEmptyDay);
        Assert.Equal("В этот день мастер не работает.", harness.ViewModel.EmptyDayText);
        Assert.Empty(harness.ViewModel.Slots);
    }

    [Fact]
    public async Task LoadSlots_WorkingDayWithoutSlots_ShowsNoFreeTimeText()
    {
        var harness = new Harness();
        harness.Api.Slots = date => Task.FromResult(WorkingDay(date));

        await harness.ViewModel.LoadSlotsAsync();

        Assert.True(harness.ViewModel.IsWorkingDay);
        Assert.True(harness.ViewModel.ShowEmptyDay);
        Assert.Equal("Свободного времени на этот день нет.", harness.ViewModel.EmptyDayText);
        Assert.Empty(harness.ViewModel.Slots);
    }

    [Fact]
    public async Task ShowEmptyDay_PendingLoad_IsFalseUntilLoadFinishes()
    {
        var harness = new Harness();
        var pending = new TaskCompletionSource<BarberSlotsResponse>();
        harness.Api.Slots = _ => pending.Task;

        var load = harness.ViewModel.LoadSlotsAsync();

        Assert.True(harness.ViewModel.IsLoadingSlots);
        Assert.False(harness.ViewModel.IsDayLoaded);
        Assert.False(harness.ViewModel.ShowEmptyDay);

        pending.SetResult(WorkingDay(VisitDate));
        await load;

        Assert.False(harness.ViewModel.IsLoadingSlots);
        Assert.True(harness.ViewModel.IsDayLoaded);
        Assert.True(harness.ViewModel.ShowEmptyDay);
    }

    [Fact]
    public async Task LoadSlots_ApiFailure_ShowsSlotsError()
    {
        var harness = new Harness();
        harness.Api.Slots = _ => Task.FromException<BarberSlotsResponse>(
            ApiError(HttpStatusCode.InternalServerError, "common.unexpected"));

        await harness.ViewModel.LoadSlotsAsync();

        Assert.Equal("Не удалось загрузить свободное время.", harness.ViewModel.SlotsError);
        Assert.False(harness.ViewModel.ShowEmptyDay);
        Assert.False(harness.ViewModel.IsLoadingSlots);
        Assert.Empty(harness.ViewModel.Slots);
    }

    [Fact]
    public async Task CanSubmit_NoSlotSelected_IsFalseUntilSelection()
    {
        var harness = new Harness();

        Assert.False(harness.ViewModel.CanSubmit);

        await harness.ViewModel.LoadSlotsAsync();
        Assert.False(harness.ViewModel.CanSubmit);

        harness.ViewModel.SelectSlotCommand.Execute(harness.ViewModel.Slots[0]);

        Assert.NotNull(harness.ViewModel.SelectedSlot);
        Assert.True(harness.ViewModel.CanSubmit);
    }

    [Fact]
    public async Task StartAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        var harness = new Harness(autoRetry: FastRetry());
        var calls = 0;

        harness.Api.Slots = date => calls++ < 2
            ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"))
            : Task.FromResult(WorkingDay(date, SlotAt(date)));

        await harness.ViewModel.LoadSlotsAsync();

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.SlotsError);

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(2, harness.ViewModel.AutoRetry.Attempts);
        Assert.Null(harness.ViewModel.SlotsError);
        Assert.Single(harness.ViewModel.Slots);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task StartAutoRetry_TerminalFailure_DoesNotStart(HttpStatusCode statusCode)
    {
        var harness = new Harness(autoRetry: FastRetry());

        harness.Api.Slots = _ => Task.FromException<BarberSlotsResponse>(ApiError(statusCode));

        await harness.ViewModel.LoadSlotsAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
        Assert.Equal("Не удалось загрузить свободное время.", harness.ViewModel.SlotsError);
    }

    [Fact]
    public async Task StopAutoRetry_WhileWaiting_StopsFurtherAttempts()
    {
        var harness = new Harness(autoRetry: StopAfterFirstAttemptRetry());

        harness.Api.Slots = _ => Task.FromException<BarberSlotsResponse>(
            new HttpRequestException("refused"));

        await harness.ViewModel.LoadSlotsAsync();

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
        var harness = new Harness(autoRetry: HangingRetry());
        var calls = 0;

        harness.Api.Slots = date => calls++ == 0
            ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"))
            : Task.FromResult(WorkingDay(date, SlotAt(date)));

        await harness.ViewModel.LoadSlotsAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.True(harness.ViewModel.IsAutoRetrying);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);

        // Кнопка «Повторить» читает сразу, не дожидаясь паузы, и цикл после успеха не возрождается.
        await harness.ViewModel.RetrySlotsCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.Api.SlotsCalls.Count);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
        Assert.Null(harness.ViewModel.SlotsError);
        Assert.Single(harness.ViewModel.Slots);
    }

    [Fact]
    public async Task RetrySlotsCommand_WhileAutoRetrying_LoadFailsRetryable_RestartsAndKeepsRetryingUntilSuccess()
    {
        var restartGate = new TaskCompletionSource();
        var silentAttempt = new TaskCompletionSource<BarberSlotsResponse>();
        var harness = new Harness(autoRetry: StopThenGateRetry(restartGate.Task));
        var calls = 0;

        harness.Api.Slots = date => calls++ switch
        {
            0 or 1 => Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused")),
            2 => silentAttempt.Task,
            _ => Task.FromResult(WorkingDay(date, SlotAt(date))),
        };

        var viewModel = harness.ViewModel;

        await viewModel.LoadSlotsAsync();

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsAutoRetrying);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);

        // Кнопка при работающем цикле читает сразу: к возврату команды запрос уже ушёл,
        // а перезапущенный цикл ждёт паузы — второго запроса без неё нет.
        await viewModel.RetrySlotsCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.Api.SlotsCalls.Count);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.True(viewModel.IsAutoRetrying);

        // Повторяемый сбой кнопки возвращает цикл к жизни: первая же тихая попытка падает
        // снова, и цикл не сдаётся, а повторяет до успеха.
        restartGate.SetResult();
        silentAttempt.SetException(new HttpRequestException("refused"));

        await viewModel.AutoRetryCompletion;

        Assert.Equal(2, viewModel.AutoRetry.Attempts);
        Assert.Equal(4, harness.Api.SlotsCalls.Count);
        Assert.Null(viewModel.SlotsError);
        Assert.Single(viewModel.Slots);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_FailedSlotsSilentAttempt_KeepsErrorAndButton()
    {
        var harness = new Harness(autoRetry: StopAfterFirstAttemptRetry());
        var pending = new TaskCompletionSource<BarberSlotsResponse>();
        var calls = 0;

        harness.Api.Slots = _ => calls++ == 0
            ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"))
            : pending.Task;

        await harness.ViewModel.LoadSlotsAsync();

        Assert.True(harness.ViewModel.SlotsError is not null);

        harness.ViewModel.StartAutoRetry();

        // Попытка висит: тихий режим не имеет права стереть причину и включить индикатор
        // первой загрузки, иначе каждая пауза между повторами мигала бы пустым экраном.
        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.SlotsError);
        Assert.False(harness.ViewModel.IsLoadingSlots);

        pending.SetException(new HttpRequestException("refused"));

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.SlotsError);
        Assert.False(harness.ViewModel.IsLoadingSlots);

        harness.ViewModel.StopAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task StopAutoRetry_DuringSlotsSilentAttempt_KeepsError()
    {
        var harness = new Harness(autoRetry: StopAfterFirstAttemptRetry());
        var pending = new TaskCompletionSource<BarberSlotsResponse>();
        var calls = 0;

        harness.Api.Slots = _ => calls++ == 0
            ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"))
            : pending.Task;

        await harness.ViewModel.LoadSlotsAsync();

        harness.ViewModel.StartAutoRetry();

        harness.ViewModel.StopAutoRetry();

        // Отмена попытки не оставляет экран пустым: причина и кнопка «Повторить» живы.
        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.SlotsError);
        Assert.False(harness.ViewModel.IsAutoRetrying);

        pending.SetCanceled();
        await harness.ViewModel.AutoRetryCompletion;
    }

    [Fact]
    public async Task AutoRetry_Timeout_ShowsNetworkText()
    {
        var harness = new Harness(autoRetry: FastRetry());
        var calls = 0;

        harness.Api.Slots = date => calls++ == 0
            ? Task.FromException<BarberSlotsResponse>(new TaskCanceledException("timeout"))
            : Task.FromResult(WorkingDay(date, SlotAt(date)));

        await harness.ViewModel.LoadSlotsAsync();

        // Таймаут HttpClient приходит отменой, но токен жив: для человека это «нет связи».
        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.SlotsError);

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.Null(harness.ViewModel.SlotsError);
        Assert.Single(harness.ViewModel.Slots);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task IsAutoRetrying_TogglesWithLoopState()
    {
        var harness = new Harness(autoRetry: HangingRetry());

        harness.Api.Slots = _ => Task.FromException<BarberSlotsResponse>(
            new HttpRequestException("refused"));

        await harness.ViewModel.LoadSlotsAsync();

        harness.ViewModel.StartAutoRetry();
        Assert.True(harness.ViewModel.IsAutoRetrying);

        harness.ViewModel.StopAutoRetry();
        Assert.False(harness.ViewModel.IsAutoRetrying);

        await harness.ViewModel.AutoRetryCompletion;
    }

    [Fact]
    public async Task ChangeDate_WhileAutoRetrying_StopsLoopAndLoadsImmediately()
    {
        var harness = new Harness(autoRetry: HangingRetry());
        var calls = 0;

        harness.Api.Slots = date => calls++ == 0
            ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"))
            : Task.FromResult(WorkingDay(date, SlotAt(date)));

        await harness.ViewModel.LoadSlotsAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.True(harness.ViewModel.IsAutoRetrying);

        await harness.ViewModel.ChangeDateAsync(NextDate.ToDateTime(TimeOnly.MinValue));

        // Смена даты — действие человека: цикл остановлен, сетка прочитана сразу.
        Assert.False(harness.ViewModel.IsAutoRetrying);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal(2, harness.Api.SlotsCalls.Count);
        Assert.Equal(NextDate, harness.Api.SlotsCalls[^1].Date);
        Assert.Null(harness.ViewModel.SlotsError);
        Assert.Equal(SlotAt(NextDate).StartAt, Assert.Single(harness.ViewModel.Slots).StartAt);
    }

    [Fact]
    public async Task ChangeDate_WhileAutoRetrying_LoadFailsTerminal_DoesNotRestart()
    {
        var harness = new Harness(autoRetry: HangingRetry());
        var calls = 0;

        harness.Api.Slots = _ => calls++ == 0
            ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"))
            : Task.FromException<BarberSlotsResponse>(ApiError(HttpStatusCode.NotFound));

        await harness.ViewModel.LoadSlotsAsync();

        harness.ViewModel.StartAutoRetry();

        await harness.ViewModel.ChangeDateAsync(NextDate.ToDateTime(TimeOnly.MinValue));

        Assert.False(harness.ViewModel.IsAutoRetrying);
        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal("Не удалось загрузить свободное время.", harness.ViewModel.SlotsError);
    }

    [Fact]
    public async Task AutoRetry_AfterDateChange_RetriesTheFailedDate()
    {
        var harness = new Harness(autoRetry: FastRetry());
        var calls = 0;

        harness.Api.Slots = date => date == NextDate && calls++ == 0
            ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"))
            : Task.FromResult(WorkingDay(date, SlotAt(date)));

        await harness.ViewModel.LoadSlotsAsync();
        await harness.ViewModel.ChangeDateAsync(NextDate.ToDateTime(TimeOnly.MinValue));

        Assert.Equal(NextDate.ToDateTime(TimeOnly.MinValue), harness.ViewModel.SelectedDate);

        // Экран показывает другую дату (человек вернул её), но ошибка принадлежит упавшему
        // запросу: тихая попытка несёт дату снимка, а не текущий выбор календаря.
        harness.ViewModel.Initialize(harness.Item);

        Assert.Equal(VisitDate.ToDateTime(TimeOnly.MinValue), harness.ViewModel.SelectedDate);

        harness.ViewModel.StartAutoRetry();
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(1, harness.ViewModel.AutoRetry.Attempts);
        Assert.Equal(NextDate, harness.Api.SlotsCalls[^1].Date);
        Assert.Equal(VisitDate, DateOnly.FromDateTime(harness.ViewModel.SelectedDate));
        Assert.Null(harness.ViewModel.SlotsError);
    }

    [Fact]
    public async Task AutoRetry_TerminalAfterRetryable_DoesNotRestart()
    {
        var harness = new Harness(autoRetry: HangingRetry());
        var calls = 0;

        harness.Api.Slots = _ => calls++ == 0
            ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("refused"))
            : Task.FromException<BarberSlotsResponse>(ApiError(HttpStatusCode.NotFound));

        await harness.ViewModel.LoadSlotsAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.True(harness.ViewModel.IsAutoRetrying);

        // Кнопка при работающем цикле читает сразу — и получает окончательный отказ. Старый
        // «транспорт» не должен оживить цикл на отказе, который не изменится.
        await harness.ViewModel.RetrySlotsCommand.ExecuteAsync(null);

        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
        Assert.Equal("Не удалось загрузить свободное время.", harness.ViewModel.SlotsError);
    }

    private static BookingListItem CreateItem(DateTimeOffset now)
    {
        var booking = new BookingResponse(
            BookingId,
            UserId,
            BarberProfileId,
            BarberServiceId,
            SalonId,
            VisitStartAt,
            VisitEndAt,
            2400m,
            BookingStatus.Confirmed,
            "idempotency-key-1",
            null,
            null,
            null,
            now,
            false);

        var item = new BookingListItem(booking, now);
        item.ApplyProfile(ProfileResponse());

        return item;
    }

    /// <summary>
    /// Карточка мастера: московский салон и активная услуга на 45 минут.
    /// </summary>
    private static BarberProfileResponse ProfileResponse() =>
        new(
            BarberProfileId,
            UserId,
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
            [BarberService()],
            [Salon()],
            [],
            [],
            []);

    private static BarberProfileServiceResponse BarberService() =>
        new(
            BarberServiceId,
            HairServiceId,
            "E2E Стрижка",
            ServiceCategory.Haircut,
            40,
            2400m,
            45,
            true);

    private static BarberProfileSalonResponse Salon() =>
        new(
            Guid.CreateVersion7(),
            SalonId,
            "E2E Studio",
            "ул. Ленина, 1",
            "Москва",
            "Россия",
            "Europe/Moscow",
            PlaceKind.Salon,
            true,
            []);

    private static BarberSlotsResponse DayOff(DateOnly date) => SlotsResponse(date, isWorkingDay: false);

    private static BarberSlotsResponse WorkingDay(DateOnly date, params BarberSlotResponse[] slots) =>
        SlotsResponse(date, isWorkingDay: true, slots);

    private static BarberSlotsResponse SlotsResponse(
        DateOnly date,
        bool isWorkingDay,
        params BarberSlotResponse[] slots) =>
        new(
            BarberProfileId,
            SalonId,
            BarberServiceId,
            HairServiceId,
            date,
            "Europe/Moscow",
            new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(3)),
            new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(3)),
            45,
            isWorkingDay,
            slots);

    /// <summary>
    /// Слот «18:30–19:15» в поясе салона: в UTC это 15:30–16:15.
    /// </summary>
    private static BarberSlotResponse SlotAt(DateOnly date) => new(
        new DateTimeOffset(date.ToDateTime(new TimeOnly(15, 30)), TimeSpan.Zero),
        new DateTimeOffset(date.ToDateTime(new TimeOnly(16, 15)), TimeSpan.Zero));

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, params string[] codes) =>
        new(statusCode, "Request failed.", codes, new Dictionary<string, string[]>());

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
    /// Часы с заданным «сейчас»: границы дат не зависят от машины.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// Подменённый клиент записей: отдаёт сетку слотов и запоминает, что именно ушло на сервер.
    /// Отмену запроса фейк игнорирует: тест сам решает, когда завершится «медленный» ответ.
    /// </summary>
    private sealed class FakeBookingsApi : IBookingsApi
    {
        public List<SlotsRequest> SlotsCalls { get; } = [];

        public List<RescheduleRequest> RescheduleCalls { get; } = [];

        public Func<DateOnly, Task<BarberSlotsResponse>> Slots { get; set; } =
            date => Task.FromResult(WorkingDay(date, SlotAt(date)));

        public Func<Guid, DateTimeOffset, Task> Reschedule { get; set; } = (_, _) => Task.CompletedTask;

        public Task<BarberSlotsResponse> GetRescheduleSlotsAsync(
            Guid bookingId,
            DateOnly slotDate,
            CancellationToken cancellationToken = default)
        {
            SlotsCalls.Add(new SlotsRequest(bookingId, slotDate));

            return Slots(slotDate);
        }

        public Task RescheduleBookingAsync(
            Guid bookingId,
            DateTimeOffset startAt,
            CancellationToken cancellationToken = default)
        {
            RescheduleCalls.Add(new RescheduleRequest(bookingId, startAt));

            return Reschedule(bookingId, startAt);
        }

        public Task<Guid> CreateBookingAsync(
            CreateBookingRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран переноса не создаёт записи.");

        public Task<PagedResponse<BookingResponse>> GetBookingsAsync(
            BookingScope scope,
            Guid userId,
            string? cursor = null,
            int pageSize = 20,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран переноса не читает список записей.");

        public Task CancelBookingAsync(Guid bookingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран переноса не отменяет записи.");
    }

    /// <summary>
    /// Параметры запроса сетки переноса.
    /// </summary>
    private sealed record SlotsRequest(Guid BookingId, DateOnly Date);

    /// <summary>
    /// Параметры отправленного переноса: ровно тот инстант, что выбрал человек.
    /// </summary>
    private sealed record RescheduleRequest(Guid BookingId, DateTimeOffset StartAt);

    /// <summary>
    /// Собранный экран: часы, подменённый клиент и ViewModel с уже открытой записью.
    /// </summary>
    private sealed class Harness
    {
        public Harness(DateTimeOffset? now = null, AutoRetryLoop? autoRetry = null)
        {
            Clock = new FixedTimeProvider(now ?? Now);
            Item = CreateItem(Clock.Now);
            ViewModel = new RescheduleBookingViewModel(Api, Clock, autoRetry);
            ViewModel.Initialize(Item);
        }

        public FakeBookingsApi Api { get; } = new();

        public FixedTimeProvider Clock { get; }

        public BookingListItem Item { get; }

        public RescheduleBookingViewModel ViewModel { get; }
    }
}
