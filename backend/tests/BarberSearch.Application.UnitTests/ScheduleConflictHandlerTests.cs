using BarberSearch.Application.BarberProfiles.AddBarberProfileTimeOff;
using BarberSearch.Application.BarberProfiles.ScheduleConflicts;
using BarberSearch.Application.BarberSalons.ReplaceWorkingSchedules;
using BarberSearch.Application.BarberSalons.SetScheduleException;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Application.WorkingSchedules.SetWorkingScheduleDayOff;
using BarberSearch.Application.WorkingSchedules.SetWorkingScheduleWorkingHours;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.WorkingSchedules;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Пишущие ручки расписания: конфликт блокирует сохранение, <c>force</c> не читает брони,
/// неизменённые дни не проверяются, таймаут блокировки не превращается в 500.
/// </summary>
public class ScheduleConflictHandlerTests
{
    [Fact]
    public async Task ReplaceWorkingSchedules_Conflict_BlocksAndDoesNotPersist()
    {
        var harness = CreateHarness();
        var bookingId = harness.AddBooking(new DateTimeOffset(2026, 11, 16, 5, 0, 0, TimeSpan.Zero));

        var result = await harness.ReplaceWeekAsync(new TimeOnly(12, 0), new TimeOnly(18, 0));

        Assert.True(result.IsSuccess);
        var conflict = Assert.Single(result.Value.Conflicts);
        Assert.Equal(bookingId, conflict.Id);
        Assert.Equal("schedule", Assert.Single(harness.Profiles.LoadCalls));
        Assert.Equal(0, harness.Profiles.UpdateCallCount);
        Assert.Equal(1, harness.Bookings.GetActiveForScheduleCalls);
        Assert.Equal(ApiErrorCodes.Schedule.LockTimeout, harness.Bookings.LastLockTimeoutCode);
    }

    [Fact]
    public async Task ReplaceWorkingSchedules_Force_SkipsBookingReadAndPersists()
    {
        var harness = CreateHarness();
        harness.AddBooking(new DateTimeOffset(2026, 11, 16, 5, 0, 0, TimeSpan.Zero));

        var result = await harness.ReplaceWeekAsync(
            new TimeOnly(12, 0),
            new TimeOnly(18, 0),
            force: true);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Conflicts);
        Assert.Equal(0, harness.Bookings.GetActiveForScheduleCalls);
        Assert.Equal(1, harness.Profiles.UpdateCallCount);
        Assert.Equal(
            new TimeOnly(12, 0),
            harness.Salon.WorkingSchedules.Single(day => day.DayOfWeek == DayOfWeek.Monday).StartTime);
    }

    [Fact]
    public async Task ReplaceWorkingSchedules_UnchangedWeek_IsNotChecked()
    {
        var harness = CreateHarness();

        // «Унаследованный» визит вне часов: повторная отправка той же недели не должна падать.
        harness.AddBooking(new DateTimeOffset(2026, 11, 16, 3, 0, 0, TimeSpan.Zero));

        var result = await harness.ReplaceWeekAsync(new TimeOnly(10, 0), new TimeOnly(20, 0));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Conflicts);
        Assert.Equal(0, harness.Bookings.GetActiveForScheduleCalls);
        Assert.Equal(1, harness.Profiles.UpdateCallCount);
    }

    [Fact]
    public async Task SetWorkingHours_Conflict_BlocksAndDoesNotPersist()
    {
        var harness = CreateHarness();
        harness.AddBooking(new DateTimeOffset(2026, 11, 16, 5, 0, 0, TimeSpan.Zero));
        var monday = harness.Salon.WorkingSchedules.Single(day => day.DayOfWeek == DayOfWeek.Monday);

        var handler = new SetWorkingScheduleWorkingHoursHandler(
            harness.Profiles,
            harness.Bookings,
            harness.Salons,
            harness.UnitOfWork,
            NullLogger<SetWorkingScheduleWorkingHoursHandler>.Instance,
            harness.CurrentUser,
            harness.Time);

        var result = await handler.Handle(
            new SetWorkingScheduleWorkingHoursCommand(
                harness.Profile.Id,
                harness.Salon.SalonId,
                monday.Id,
                new TimeOnly(12, 0),
                new TimeOnly(18, 0)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Conflicts);
        Assert.Equal(0, harness.Profiles.UpdateCallCount);
        Assert.Equal(new TimeOnly(10, 0), monday.StartTime);
    }

    [Fact]
    public async Task SetDayOff_Conflict_FlagsTheWholeDay()
    {
        var harness = CreateHarness();
        harness.AddBooking(new DateTimeOffset(2026, 11, 16, 7, 0, 0, TimeSpan.Zero));
        var monday = harness.Salon.WorkingSchedules.Single(day => day.DayOfWeek == DayOfWeek.Monday);

        var handler = new SetWorkingScheduleDayOffHandler(
            harness.Profiles,
            harness.Bookings,
            harness.Salons,
            harness.UnitOfWork,
            NullLogger<SetWorkingScheduleDayOffHandler>.Instance,
            harness.CurrentUser,
            harness.Time);

        var result = await handler.Handle(
            new SetWorkingScheduleDayOffCommand(
                harness.Profile.Id,
                harness.Salon.SalonId,
                monday.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Conflicts);
        Assert.Equal(0, harness.Profiles.UpdateCallCount);
        Assert.False(monday.IsDayOff);
    }

    [Fact]
    public async Task SetScheduleException_ExplicitDateBeyondHorizon_IsChecked()
    {
        var harness = CreateHarness();
        var date = new DateOnly(2026, 11, 10).AddDays(75);
        var bookingId = harness.AddBooking(
            new DateTimeOffset(date.ToDateTime(new TimeOnly(7, 0)), TimeSpan.Zero));

        var handler = new SetScheduleExceptionHandler(
            harness.Profiles,
            harness.Bookings,
            harness.Salons,
            harness.UnitOfWork,
            harness.CurrentUser,
            harness.Time,
            NullLogger<SetScheduleExceptionHandler>.Instance);

        var result = await handler.Handle(
            new SetScheduleExceptionCommand(
                harness.Profile.Id,
                harness.Salon.SalonId,
                date,
                IsDayOff: true,
                TimeOnly.MinValue,
                TimeOnly.MinValue,
                "Отпуск"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(bookingId, Assert.Single(result.Value.Conflicts).Id);
        Assert.Equal(0, harness.Profiles.UpdateCallCount);
    }

    [Fact]
    public async Task AddTimeOff_Conflict_BlocksAndDoesNotPersist()
    {
        var harness = CreateHarness();
        var bookingId = harness.AddBooking(new DateTimeOffset(2026, 11, 16, 5, 0, 0, TimeSpan.Zero));

        var handler = new AddBarberProfileTimeOffHandler(
            harness.Profiles,
            harness.Bookings,
            harness.UnitOfWork,
            NullLogger<AddBarberProfileTimeOffHandler>.Instance,
            harness.CurrentUser,
            harness.Time);

        var result = await handler.Handle(
            new AddBarberProfileTimeOffCommand(
                harness.Profile.Id,
                new DateTimeOffset(2026, 11, 16, 4, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero),
                "Отпуск"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(bookingId, Assert.Single(result.Value.Conflicts).Id);
        Assert.Equal(0, harness.Profiles.UpdateCallCount);
        Assert.Empty(harness.Profile.TimeOffs);
    }

    [Fact]
    public async Task AddTimeOff_Force_SkipsBookingReadAndPersists()
    {
        var harness = CreateHarness();
        harness.AddBooking(new DateTimeOffset(2026, 11, 16, 5, 0, 0, TimeSpan.Zero));

        var handler = new AddBarberProfileTimeOffHandler(
            harness.Profiles,
            harness.Bookings,
            harness.UnitOfWork,
            NullLogger<AddBarberProfileTimeOffHandler>.Instance,
            harness.CurrentUser,
            harness.Time);

        var result = await handler.Handle(
            new AddBarberProfileTimeOffCommand(
                harness.Profile.Id,
                new DateTimeOffset(2026, 11, 16, 4, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero),
                "Отпуск",
                Force: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Conflicts);
        Assert.Equal(0, harness.Bookings.GetActiveForScheduleCalls);
        Assert.Equal(1, harness.Profiles.UpdateCallCount);
        Assert.Single(harness.Profile.TimeOffs);
    }

    [Fact]
    public async Task ScheduleWrite_LockTimeout_PropagatesFailure()
    {
        var harness = CreateHarness();
        harness.Bookings.OnAcquireLock = _ => UnitResult.Failure<Failure>(new Failure(
            Error.Conflict(ApiErrorCodes.Schedule.LockTimeout, "The lock was not acquired.")));

        var result = await harness.ReplaceWeekAsync(new TimeOnly(12, 0), new TimeOnly(18, 0));

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == ApiErrorCodes.Schedule.LockTimeout);
        Assert.Empty(harness.Profiles.LoadCalls);
        Assert.Equal(0, harness.Profiles.UpdateCallCount);
    }

    private static Harness CreateHarness()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var salon = CreateSalon();
        profile.AddSalon(salon.Id, isPrimary: true);

        var salonRepository = new FakeSalonRepository();
        salonRepository.AddSalon(salon);

        var profileRepository = new FakeBarberProfileRepository();
        profileRepository.Add(profile);

        var link = profile.Salons.Single();
        link.ReplaceWorkingSchedules(Week(new TimeOnly(10, 0), new TimeOnly(20, 0)));

        var bookings = new FakeBookingRepository();
        var unitOfWork = new FakeUnitOfWork(profileRepository);

        return new Harness(
            profile,
            link,
            salon,
            profileRepository,
            bookings,
            salonRepository,
            unitOfWork,
            new FakeCurrentUser(ownerId),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero)));
    }

    private static BarberProfile CreateProfile(Guid userId)
        => BarberProfile.Create(
            userId,
            "Master Barber",
            5,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null).Value;

    private static Salon CreateSalon()
        => Salon.Create(
            "Barbershop",
            "Lenina, 1",
            "Yekaterinburg",
            "Russia",
            "Asia/Yekaterinburg",
            56.83,
            60.60).Value;

    private static IReadOnlyList<WorkingDaySchedule> Week(TimeOnly start, TimeOnly end)
        =>
        [
            new WorkingDaySchedule(DayOfWeek.Monday, false, start, end),
            new WorkingDaySchedule(DayOfWeek.Tuesday, false, start, end),
            new WorkingDaySchedule(DayOfWeek.Wednesday, false, start, end),
            new WorkingDaySchedule(DayOfWeek.Thursday, false, start, end),
            new WorkingDaySchedule(DayOfWeek.Friday, false, start, end),
            new WorkingDaySchedule(DayOfWeek.Saturday, false, start, end),
            new WorkingDaySchedule(DayOfWeek.Sunday, false, start, end)
        ];

    private sealed record Harness(
        BarberProfile Profile,
        BarberSalon Salon,
        Salon SalonEntity,
        FakeBarberProfileRepository Profiles,
        FakeBookingRepository Bookings,
        FakeSalonRepository Salons,
        FakeUnitOfWork UnitOfWork,
        FakeCurrentUser CurrentUser,
        FakeTimeProvider Time)
    {
        public Guid AddBooking(DateTimeOffset startAt, int minutes = 60)
        {
            var booking = Booking.Create(
                Guid.CreateVersion7(),
                Profile.Id,
                Guid.CreateVersion7(),
                Salon.SalonId,
                startAt,
                startAt.AddMinutes(minutes),
                1500m,
                $"key-{Guid.NewGuid():N}").Value;

            Bookings.Add(booking);

            return booking.Id;
        }

        public Task<Result<ScheduleWriteOutcome, Failure>> ReplaceWeekAsync(
            TimeOnly start,
            TimeOnly end,
            bool force = false)
        {
            var handler = new ReplaceWorkingSchedulesHandler(
                Profiles,
                Bookings,
                Salons,
                UnitOfWork,
                CurrentUser,
                Time,
                NullLogger<ReplaceWorkingSchedulesHandler>.Instance);

            return handler.Handle(
                new ReplaceWorkingSchedulesCommand(
                    Profile.Id,
                    Salon.SalonId,
                    Week(start, end),
                    force),
                CancellationToken.None);
        }
    }
}
