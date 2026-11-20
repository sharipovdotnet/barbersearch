using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Перенести запись» кабинета мастера: сетка по дате, отправка выбранного
/// слота, событие успеха и перечитывание именно упавшей даты после занятого времени.
/// </summary>
/// <remarks>
/// Дата визита — 20 ноября в поясе Москвы, «сегодня» салона — 15 ноября: окно переноса
/// считается от салонного дня, а не от UTC. Отправка — запись, поэтому у экрана нет
/// автоповтора отправки; повторяется только чтение сетки, и тест ждёт цикл без пауз.
/// </remarks>
public sealed class BarberBookingRescheduleViewModelTests
{
    private static readonly DateTimeOffset VisitStart =
        new(2026, 11, 20, 10, 0, 0, TimeSpan.FromHours(3));

    private const string OverlapText = "Это время уже занято. Выберите другое.";
    private const string NoWorkingDayText = "В этот день вы не работаете.";
    private const string NoFreeTimeText = "Свободного времени на этот день нет.";

    [Fact]
    public void Initialize_SetsDateWindowAndVisitDateWithoutRequests()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.Initialize(Item());

        Assert.True(viewModel.IsInitialized);
        Assert.Equal(new DateTime(2026, 11, 15), viewModel.MinimumDate);
        Assert.Equal(new DateTime(2026, 12, 15), viewModel.MaximumDate);
        Assert.Equal(new DateTime(2026, 11, 20), viewModel.SelectedDate);
        Assert.Equal("Иван Петров", viewModel.CustomerName);
        Assert.Empty(harness.Bookings.SlotRequests);
        Assert.False(viewModel.IsDayLoaded);
    }

    [Fact]
    public async Task InitializeAsync_LoadsSlotsForVisitDate()
    {
        var harness = new Harness();
        harness.Bookings.RescheduleSlots = (_, day, _) => Task.FromResult(
            BarberCabinetFixtures.Slots(
                day,
                slots: [Slot(day, 10), Slot(day, 15)]));

        await harness.ViewModel.InitializeAsync(Item());

        var request = Assert.Single(harness.Bookings.SlotRequests);

        Assert.Equal(BarberCabinetFixtures.BookingId, request.BookingId);
        Assert.Equal(new DateOnly(2026, 11, 20), request.Date);
        Assert.Equal(new DateOnly(2026, 11, 20), harness.ViewModel.LoadedDate);
        Assert.Equal(2, harness.ViewModel.Slots.Count);
        Assert.Equal("10:00–11:00", harness.ViewModel.Slots[0].TimeText);
        Assert.True(harness.ViewModel.ShowSlots);
        Assert.False(harness.ViewModel.IsLoadingSlots);
    }

    [Fact]
    public async Task ChangeDateAsync_NewDate_ReloadsGridAndClearsSelection()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        harness.Bookings.RescheduleSlots = (_, day, _) => Task.FromResult(
            BarberCabinetFixtures.Slots(day, slots: [Slot(day, 10)]));

        await viewModel.InitializeAsync(Item());
        viewModel.SelectSlotCommand.Execute(viewModel.Slots[0]);

        Assert.NotNull(viewModel.SelectedSlot);

        await viewModel.ChangeDateAsync(new DateTime(2026, 11, 22));

        Assert.Equal(new DateTime(2026, 11, 22), viewModel.SelectedDate);
        Assert.Null(viewModel.SelectedSlot);
        Assert.Equal(new DateOnly(2026, 11, 22), viewModel.LoadedDate);
        Assert.Equal(2, harness.Bookings.SlotRequests.Count);
        Assert.Equal(new DateOnly(2026, 11, 22), harness.Bookings.SlotRequests[1].Date);
    }

    [Fact]
    public async Task SubmitAsync_SelectedSlot_SendsItsStartInstant()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync(Item());
        harness.ViewModel.SelectSlotCommand.Execute(harness.ViewModel.Slots[0]);

        var slot = harness.ViewModel.Slots[0];

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Bookings.RescheduleRequests);

        Assert.Equal(BarberCabinetFixtures.BookingId, request.BookingId);
        Assert.Equal(slot.StartAt, request.StartAt);
    }

    [Fact]
    public async Task SubmitAsync_Success_RaisesRescheduledWithSlotBounds()
    {
        var harness = new Harness();
        BarberSlotResponse? rescheduled = null;

        harness.ViewModel.Rescheduled += (_, slot) => rescheduled = slot;

        await harness.ViewModel.InitializeAsync(Item());
        harness.ViewModel.SelectSlotCommand.Execute(harness.ViewModel.Slots[0]);

        var slot = harness.ViewModel.Slots[0];

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.NotNull(rescheduled);
        Assert.Equal(slot.StartAt, rescheduled.StartAt);
        Assert.Equal(slot.EndAt, rescheduled.EndAt);
        Assert.False(harness.ViewModel.IsSubmitting);
        Assert.Null(harness.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_Overlap_ReloadsFailedDateNotNewlyPickedDate()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;
        var submitGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        harness.Bookings.RescheduleSlots = (_, day, _) => Task.FromResult(
            BarberCabinetFixtures.Slots(day, slots: [Slot(day, 10)]));
        harness.Bookings.Reschedule = (_, _, _) => submitGate.Task;

        await viewModel.InitializeAsync(Item());
        viewModel.SelectSlotCommand.Execute(viewModel.Slots[0]);

        var submit = viewModel.SubmitCommand.ExecuteAsync(null);

        // Пока перенос в полёте, человек выбирает другую дату: ответ «занято» должен
        // перечитать сетку той даты, на которую ушёл запрос.
        await viewModel.ChangeDateAsync(new DateTime(2026, 11, 22));

        submitGate.SetException(
            BarberCabinetApiError.Create(HttpStatusCode.Conflict, "booking.reschedule.overlap"));

        await submit;

        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 11, 20),
                new DateOnly(2026, 11, 22),
                new DateOnly(2026, 11, 20),
            },
            harness.Bookings.SlotRequests.Select(request => request.Date));
        Assert.Equal(new DateTime(2026, 11, 22), viewModel.SelectedDate);
        Assert.Equal(new DateOnly(2026, 11, 20), viewModel.LoadedDate);
        Assert.Equal(OverlapText, viewModel.ErrorMessage);
        Assert.Null(viewModel.SelectedSlot);
        Assert.False(viewModel.IsSubmitting);
    }

    [Fact]
    public async Task InitializeAsync_NonWorkingDay_ShowsNoWorkingDayText()
    {
        var harness = new Harness();
        harness.Bookings.RescheduleSlots = (_, day, _) => Task.FromResult(
            BarberCabinetFixtures.Slots(day, isWorkingDay: false));

        await harness.ViewModel.InitializeAsync(Item());

        Assert.True(harness.ViewModel.ShowEmptyDay);
        Assert.False(harness.ViewModel.ShowSlots);
        Assert.Equal(NoWorkingDayText, harness.ViewModel.EmptyDayText);
    }

    [Fact]
    public async Task InitializeAsync_WorkingDayWithoutSlots_ShowsNoFreeTimeText()
    {
        var harness = new Harness();
        harness.Bookings.RescheduleSlots = (_, day, _) => Task.FromResult(
            BarberCabinetFixtures.Slots(day, isWorkingDay: true));

        await harness.ViewModel.InitializeAsync(Item());

        Assert.True(harness.ViewModel.ShowEmptyDay);
        Assert.Equal(NoFreeTimeText, harness.ViewModel.EmptyDayText);
    }

    [Fact]
    public async Task SubmitAsync_DoubleTap_SendsSingleReschedule()
    {
        var harness = new Harness();
        var submitGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rescheduled = 0;

        harness.Bookings.Reschedule = (_, _, _) => submitGate.Task;
        harness.ViewModel.Rescheduled += (_, _) => rescheduled++;

        await harness.ViewModel.InitializeAsync(Item());
        harness.ViewModel.SelectSlotCommand.Execute(harness.ViewModel.Slots[0]);

        var first = harness.ViewModel.SubmitCommand.ExecuteAsync(null);
        var second = harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Single(harness.Bookings.RescheduleRequests);

        submitGate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Single(harness.Bookings.RescheduleRequests);
        Assert.Equal(1, rescheduled);
    }

    [Fact]
    public async Task LoadSlotsAsync_TransportFailure_RetriesUntilLoaded()
    {
        var harness = new Harness(BarberCabinetRetry.Fast());
        var calls = 0;

        harness.Bookings.RescheduleSlots = (_, day, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<BarberSlotsResponse>(new HttpRequestException("offline"))
                : Task.FromResult(BarberCabinetFixtures.Slots(day, slots: [Slot(day, 10)]));
        };

        harness.ViewModel.StartAutoRetry();

        await harness.ViewModel.InitializeAsync(Item());
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(2, calls);
        Assert.Single(harness.ViewModel.Slots);
        Assert.Null(harness.ViewModel.SlotsError);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    private static BarberBookingItem Item() =>
        BarberCabinetFixtures.Item(BarberCabinetFixtures.Booking(startAt: VisitStart));

    private static BarberSlotResponse Slot(DateOnly day, int hour) =>
        new(
            new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(3)),
            new DateTimeOffset(day.ToDateTime(new TimeOnly(hour + 1, 0)), TimeSpan.FromHours(3)));

    /// <summary>
    /// Собранный экран: подменённый клиент записей и часы.
    /// </summary>
    private sealed class Harness
    {
        public Harness(AutoRetryLoop? autoRetry = null) =>
            ViewModel = new BarberBookingRescheduleViewModel(
                Bookings,
                new FixedTimeProvider(BarberCabinetFixtures.Now),
                autoRetry);

        public FakeBarberBookingsApi Bookings { get; } = new();

        public BarberBookingRescheduleViewModel ViewModel { get; }
    }
}
