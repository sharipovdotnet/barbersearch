using System.Net;
using BarberSearch.Contracts.BarberProfiles.BarberSchedule;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки раздела «Расписание»: недельный шаблон, «заполнить будни», сохранение всей недели
/// одним <c>PUT</c>, диалог конфликта, исключения на даты и отпуска.
/// </summary>
/// <remarks>
/// «Сейчас» — 22:00 UTC 14 ноября, то есть 15 ноября в Москве: границы дат считаются в поясе
/// салона, а не телефона. Паузы <see cref="AutoRetryLoop"/> приходят делегатом, поэтому
/// повтор чтения проверяется без ожидания, а запись не повторяется вовсе.
/// </remarks>
public sealed class BarberScheduleViewModelTests
{
    private const string NetworkText =
        "Нет связи с сервером. Проверьте подключение и повторите попытку.";

    private const string SessionExpiredText = "Сессия истекла — войдите снова.";

    private const string SavedText = "Расписание сохранено.";

    private const string ConflictTitleText = "Записи вне новых часов";

    private const string MoreConflictsText =
        "Показаны не все записи: откройте раздел «Записи», чтобы увидеть остальные.";

    [Fact]
    public async Task ActivateAsync_LoadsWeekAndListsInSalonZone()
    {
        var schedule = BarberCabinetData.Schedule(
            timeOffs: [BarberCabinetData.TimeOff(reason: "Отпуск")],
            salons:
            [
                BarberCabinetData.ScheduleSalon(
                    exceptions:
                    [
                        BarberCabinetData.Exception(new DateOnly(2026, 11, 22), reason: "Съёмки"),
                        BarberCabinetData.Exception(
                            new DateOnly(2026, 11, 20),
                            isDayOff: false,
                            startHour: 10,
                            endHour: 14),
                    ]),
            ]);
        var harness = new Harness(schedule);

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(1, harness.Api.GetCalls);
        Assert.Equal(
            new[]
            {
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
                DayOfWeek.Saturday,
                DayOfWeek.Sunday,
            },
            viewModel.Days.Select(day => day.DayOfWeek));
        Assert.Equal("Понедельник", viewModel.Days[0].DayLabel);
        Assert.Equal(TimeSpan.FromHours(10), viewModel.Days[0].StartTime);
        Assert.Equal(TimeSpan.FromHours(20), viewModel.Days[0].EndTime);
        Assert.True(viewModel.Days[5].IsDayOff);
        Assert.Equal(string.Empty, viewModel.Days[5].HoursText);
        Assert.True(viewModel.ShowContent);
        Assert.False(viewModel.ShowError);
        Assert.False(viewModel.IsFirstLoading);
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.AllDaysOff);
        Assert.Single(viewModel.Salons);
        Assert.False(viewModel.HasMultipleSalons);
        Assert.Equal("Hairo · Москва", viewModel.SingleSalonText);

        // Даты раздела считаются от сегодняшнего дня в поясе салона: в Москве уже 15 ноября.
        Assert.Equal(new DateTime(2026, 11, 15), viewModel.ExceptionMinDate);
        Assert.Equal(new DateTime(2027, 2, 13), viewModel.ExceptionMaxDate);
        Assert.Equal(new DateTime(2027, 11, 15), viewModel.TimeOffMaxDate);

        Assert.True(viewModel.HasExceptions);
        Assert.Equal(
            new[] { new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 22) },
            viewModel.Exceptions.Select(exception => exception.Date));
        Assert.Equal("10:00–14:00", viewModel.Exceptions[0].HoursText);
        Assert.Equal("Выходной · Съёмки", viewModel.Exceptions[1].SummaryText);
        Assert.True(viewModel.Exceptions[1].HasReason);

        Assert.True(viewModel.HasTimeOffs);
        Assert.Equal("15–17 ноября 2026", viewModel.TimeOffs[0].RangeText);
        Assert.Equal("Отпуск", viewModel.TimeOffs[0].ReasonText);
    }

    [Fact]
    public async Task ActivateAsync_TransportFailure_RetriesUntilLoaded()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness(retry: BarberCabinetRetry.Gated(gate.Task));
        var calls = 0;

        harness.Api.Get = _ =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<BarberScheduleResponse>(new HttpRequestException("offline"))
                : Task.FromResult(BarberCabinetData.Schedule());
        };

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(NetworkText, viewModel.ErrorMessage);
        Assert.True(viewModel.ShowError);
        Assert.False(viewModel.ShowContent);
        Assert.True(viewModel.IsAutoRetrying);

        gate.SetResult();

        await viewModel.ReadRetryCompletion;

        Assert.Equal(2, calls);
        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.ShowContent);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task ActivateAsync_Unauthorized_ShowsSessionReasonWithoutRetry()
    {
        var harness = new Harness();

        harness.Api.Get = _ => Task.FromException<BarberScheduleResponse>(
            BarberCabinetApiError.Create(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(SessionExpiredText, viewModel.ErrorMessage);
        Assert.True(viewModel.ShowError);
        Assert.Equal(0, viewModel.ReadAutoRetry.Attempts);
        Assert.False(viewModel.ReadAutoRetry.IsRunning);
    }

    [Fact]
    public async Task DayEdits_MarkDirtyAndDetectInvalidHours()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;
        var monday = viewModel.Days[0];

        monday.IsWorking = false;

        Assert.True(monday.IsDayOff);
        Assert.True(monday.ShowDayOffText);
        Assert.Equal("Выходной", monday.DayOffText);
        Assert.True(viewModel.IsDirty);

        monday.IsWorking = true;
        monday.StartTime = TimeSpan.FromHours(20);
        monday.EndTime = TimeSpan.FromHours(10);

        Assert.True(monday.IsInvalid);
        Assert.True(viewModel.HasInvalidHours);
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        foreach (var day in viewModel.Days)
        {
            day.IsDayOff = true;
        }

        Assert.True(viewModel.AllDaysOff);
    }

    [Fact]
    public async Task FillWeekdays_SetsWeekdaysTenToTwentyAndKeepsWeekendsOff()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        viewModel.Days[0].IsDayOff = true;
        viewModel.Days[2].StartTime = TimeSpan.FromHours(12);
        viewModel.Days[2].EndTime = TimeSpan.FromHours(21);

        viewModel.FillWeekdaysCommand.Execute(null);

        Assert.All(
            viewModel.Days.Take(5),
            day =>
            {
                Assert.True(day.IsWorking);
                Assert.Equal(TimeSpan.FromHours(10), day.StartTime);
                Assert.Equal(TimeSpan.FromHours(20), day.EndTime);
            });
        Assert.True(viewModel.Days[5].IsDayOff);
        Assert.True(viewModel.Days[6].IsDayOff);
        Assert.False(viewModel.HasInvalidHours);
    }

    [Fact]
    public async Task SaveAsync_SendsWholeWeekInSinglePut()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        viewModel.Days[0].EndTime = TimeSpan.FromHours(18);
        viewModel.Days[6].IsWorking = true;
        viewModel.Days[6].StartTime = TimeSpan.FromHours(11);
        viewModel.Days[6].EndTime = TimeSpan.FromHours(16);

        await viewModel.SaveCommand.ExecuteAsync(null);

        var write = Assert.Single(harness.Api.WeekWrites);

        Assert.Equal(BarberCabinetFixtures.BarberProfileId, write.ProfileId);
        Assert.Equal(BarberCabinetFixtures.SalonId, write.SalonId);
        Assert.False(write.Force);
        Assert.Equal(7, write.Request.Days.Count);
        Assert.Equal(
            new[]
            {
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
                DayOfWeek.Saturday,
                DayOfWeek.Sunday,
            },
            write.Request.Days.Select(day => day.DayOfWeek));
        Assert.Equal(new TimeOnly(10, 0), write.Request.Days[0].StartTime);
        Assert.Equal(new TimeOnly(18, 0), write.Request.Days[0].EndTime);

        // Выходной уходит осмысленными 10:00–20:00, а не полуночью.
        Assert.True(write.Request.Days[5].IsDayOff);
        Assert.Equal(new TimeOnly(10, 0), write.Request.Days[5].StartTime);
        Assert.Equal(new TimeOnly(20, 0), write.Request.Days[5].EndTime);
        Assert.Equal(new TimeOnly(11, 0), write.Request.Days[6].StartTime);

        Assert.Equal(SavedText, viewModel.Announcement);
        Assert.True(viewModel.HasAnnouncement);
        Assert.False(viewModel.IsSaving);
        Assert.False(viewModel.IsDirty);
        Assert.Equal(2, harness.Api.GetCalls);
    }

    [Fact]
    public async Task SaveAsync_Conflict_OpensPromptAndForceRetriesSameRequest()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        viewModel.Days[0].EndTime = TimeSpan.FromHours(18);

        harness.Api.Replace = (_, _, _, force) => force
            ? Task.CompletedTask
            : Task.FromException(BarberCabinetData.ConflictError(
                BarberCabinetData.Conflict(
                    BarberCabinetFixtures.BookingId,
                    "Иван Петров"),
                BarberCabinetData.Conflict(
                    BarberCabinetFixtures.SecondBookingId,
                    "Пётр Сидоров")));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsConflictPromptVisible);
        Assert.True(viewModel.HasMoreConflicts == false);
        Assert.Equal(string.Empty, viewModel.MoreConflictsText);
        Assert.Equal(ConflictTitleText, viewModel.ConflictTitle);
        Assert.Equal(2, viewModel.Conflicts.Count);
        Assert.Equal("Иван Петров", viewModel.Conflicts[0].Customer);
        Assert.Equal("Стрижка", viewModel.Conflicts[0].Service);
        Assert.Equal("Подтверждена", viewModel.Conflicts[0].Status);
        Assert.Contains("2 записей", viewModel.ConflictDescription, StringComparison.Ordinal);
        Assert.Null(viewModel.SaveError);
        Assert.Single(harness.Api.WeekWrites);

        await viewModel.ConfirmForceCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.Api.WeekWrites.Count);
        Assert.True(harness.Api.WeekWrites[1].Force);
        Assert.Equal(
            harness.Api.WeekWrites[0].Request.Days.Count,
            harness.Api.WeekWrites[1].Request.Days.Count);
        Assert.False(viewModel.IsConflictPromptVisible);
        Assert.Empty(viewModel.Conflicts);
        Assert.Equal(SavedText, viewModel.Announcement);
    }

    [Fact]
    public async Task SaveAsync_ConflictBeyondLimit_CapsListAndShowsMoreText()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;
        var conflicts = Enumerable
            .Range(0, BarberScheduleViewModel.MaxConflictItems + 1)
            .Select(index => BarberCabinetData.Conflict(
                Guid.CreateVersion7(),
                string.Concat("Клиент ", index)))
            .ToArray();

        viewModel.Days[0].EndTime = TimeSpan.FromHours(18);

        harness.Api.Replace = (_, _, _, _) => Task.FromException(
            BarberCabinetData.ConflictError(conflicts));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsConflictPromptVisible);
        Assert.Equal(BarberScheduleViewModel.MaxConflictItems, viewModel.Conflicts.Count);
        Assert.True(viewModel.HasMoreConflicts);
        Assert.Equal(MoreConflictsText, viewModel.MoreConflictsText);
        Assert.Contains(
            string.Concat(BarberScheduleViewModel.MaxConflictItems + 1, " записей"),
            viewModel.ConflictDescription,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddExceptionAsync_EndBeforeStart_ShowsReasonWithoutRequest()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        viewModel.NewExceptionIsDayOff = false;
        viewModel.NewExceptionStartTime = TimeSpan.FromHours(14);
        viewModel.NewExceptionEndTime = TimeSpan.FromHours(10);

        Assert.False(viewModel.AddExceptionCommand.CanExecute(null));

        await viewModel.AddExceptionCommand.ExecuteAsync(null);

        Assert.Empty(harness.Api.ExceptionWrites);
        Assert.Equal(
            "Конец рабочего дня должен быть позже начала.",
            viewModel.ExceptionError);
    }

    [Fact]
    public async Task AddExceptionAsync_OwnHours_SendsDateAndResetsForm()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        viewModel.NewExceptionDate = new DateTime(2026, 11, 20);
        viewModel.NewExceptionIsDayOff = false;
        viewModel.NewExceptionStartTime = TimeSpan.FromHours(9);
        viewModel.NewExceptionEndTime = TimeSpan.FromHours(13);
        viewModel.NewExceptionReason = "  Съёмки  ";

        await viewModel.AddExceptionCommand.ExecuteAsync(null);

        var write = Assert.Single(harness.Api.ExceptionWrites);

        Assert.Equal(BarberCabinetFixtures.BarberProfileId, write.ProfileId);
        Assert.Equal(BarberCabinetFixtures.SalonId, write.SalonId);
        Assert.Equal(new DateOnly(2026, 11, 20), write.Date);
        Assert.False(write.Request.IsDayOff);
        Assert.Equal(new TimeOnly(9, 0), write.Request.StartTime);
        Assert.Equal(new TimeOnly(13, 0), write.Request.EndTime);
        Assert.Equal("Съёмки", write.Request.Reason);
        Assert.Equal("Исключение добавлено.", viewModel.Announcement);

        Assert.True(viewModel.NewExceptionIsDayOff);
        Assert.Equal(string.Empty, viewModel.NewExceptionReason);
        Assert.Equal(viewModel.ExceptionMinDate, viewModel.NewExceptionDate);
    }

    [Fact]
    public async Task RemoveExceptionAsync_NotFound_ShowsAlreadyDeletedAndReloads()
    {
        var schedule = BarberCabinetData.Schedule(
            salons:
            [
                BarberCabinetData.ScheduleSalon(
                    exceptions: [BarberCabinetData.Exception(new DateOnly(2026, 11, 20))]),
            ]);
        var harness = new Harness(schedule);

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        harness.Api.RemoveException = (_, _, _) => Task.FromException(
            BarberCabinetApiError.Create(
                HttpStatusCode.NotFound,
                "barberSalon.removeScheduleException.notFound"));

        await viewModel.RemoveExceptionCommand.ExecuteAsync(viewModel.Exceptions[0]);

        var removal = Assert.Single(harness.Api.ExceptionRemovals);

        Assert.Equal(new DateOnly(2026, 11, 20), removal.Date);
        Assert.Equal("Это исключение уже удалено.", viewModel.ExceptionError);
        Assert.Equal(2, harness.Api.GetCalls);
        Assert.False(viewModel.IsSaving);
    }

    [Fact]
    public async Task AddTimeOffAsync_InclusiveDates_SendsUtcIntervalWithExclusiveEnd()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        viewModel.NewTimeOffStartDate = new DateTime(2026, 11, 15);
        viewModel.NewTimeOffEndDate = new DateTime(2026, 11, 17);
        viewModel.NewTimeOffReason = " Отпуск ";

        await viewModel.AddTimeOffCommand.ExecuteAsync(null);

        var write = Assert.Single(harness.Api.TimeOffWrites);

        // 15 ноября 00:00 в Москве — это 14 ноября 21:00 UTC; конец — полночь дня после последнего.
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 21, 0, 0, TimeSpan.Zero), write.Request.StartAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 21, 0, 0, TimeSpan.Zero), write.Request.EndAt);
        Assert.Equal("Отпуск", write.Request.Reason);
        Assert.False(write.Force);
        Assert.Equal("Отпуск добавлен.", viewModel.Announcement);
        Assert.Equal(viewModel.TimeOffMinDate, viewModel.NewTimeOffStartDate);
    }

    [Fact]
    public async Task AddTimeOffAsync_Overlap_ShowsOverlapReason()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        harness.Api.AddTimeOff = (_, _, _) => Task.FromException(
            BarberCabinetApiError.Create(
                HttpStatusCode.BadRequest,
                "barberProfile.addTimeOff.overlap"));

        viewModel.NewTimeOffEndDate = new DateTime(2026, 11, 18);

        await viewModel.AddTimeOffCommand.ExecuteAsync(null);

        Assert.Equal(
            "Этот период пересекается с другим отпуском.",
            viewModel.TimeOffError);
        Assert.False(viewModel.IsSaving);
    }

    [Fact]
    public async Task SelectSalon_RebuildsWeekAndClearsDirty()
    {
        var schedule = BarberCabinetData.Schedule(
            salons:
            [
                BarberCabinetData.ScheduleSalon(),
                BarberCabinetData.ScheduleSalon(
                    BarberCabinetFixtures.OtherSalonId,
                    "Chop-Chop",
                    "Тверь",
                    isPrimary: false,
                    weekly: BarberCabinetData.OtherWeek()),
            ]);
        var harness = new Harness(schedule);

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.HasMultipleSalons);
        Assert.Equal(BarberCabinetFixtures.SalonId, viewModel.SelectedSalon?.SalonId);

        viewModel.SelectedSalon = viewModel.Salons[1];

        Assert.Equal(BarberCabinetFixtures.OtherSalonId, viewModel.SelectedSalon?.SalonId);
        Assert.Equal(TimeSpan.FromHours(12), viewModel.Days[0].StartTime);
        Assert.Equal(TimeSpan.FromHours(21), viewModel.Days[0].EndTime);
        Assert.False(viewModel.Days[6].IsDayOff);
        Assert.False(viewModel.IsDirty);
        Assert.Equal("Chop-Chop · Тверь", viewModel.SingleSalonText);
    }

    [Fact]
    public async Task DiscardChanges_RestoresLoadedWeek()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        viewModel.Days[0].EndTime = TimeSpan.FromHours(15);
        viewModel.Days[0].IsDayOff = true;

        Assert.True(viewModel.IsDirty);

        viewModel.DiscardChangesCommand.Execute(null);

        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.Days[0].IsDayOff);
        Assert.Equal(TimeSpan.FromHours(20), viewModel.Days[0].EndTime);
    }

    /// <summary>
    /// Собранный раздел: подменённый клиент, часы и цикл повтора без настоящих пауз.
    /// </summary>
    private sealed class Harness
    {
        public Harness(BarberScheduleResponse? schedule = null, AutoRetryLoop? retry = null)
        {
            if (schedule is not null)
            {
                Api.Get = _ => Task.FromResult(schedule);
            }

            ViewModel = new BarberScheduleViewModel(
                Api,
                new FixedTimeProvider(BarberCabinetFixtures.Now),
                retry);

            ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);
        }

        public FakeBarberScheduleApi Api { get; } = new();

        public BarberScheduleViewModel ViewModel { get; }
    }
}
