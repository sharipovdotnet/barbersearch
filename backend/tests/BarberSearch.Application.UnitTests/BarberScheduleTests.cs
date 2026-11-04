using BarberSearch.Application.BarberProfiles.GetBarberSchedule;
using BarberSearch.Application.BarberSalons.RemoveScheduleException;
using BarberSearch.Application.BarberSalons.ReplaceWorkingSchedules;
using BarberSearch.Application.BarberSalons.SetScheduleException;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.WorkingSchedules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Экран расписания: чтение, замена недели и исключения на даты.
/// </summary>
public class BarberScheduleTests
{
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

    private static (BarberProfile Profile, BarberSalon Salon, FakeBarberProfileRepository Repository, FakeSalonRepository Salons, Guid OwnerId) CreateOwner()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var salon = Salon.Create(
            "Barbershop",
            "Lenina, 1",
            "Yekaterinburg",
            "Russia",
            "Asia/Yekaterinburg",
            56.83,
            60.60).Value;

        profile.AddSalon(salon.Id, isPrimary: true);

        var salonRepository = new FakeSalonRepository();
        salonRepository.AddSalon(salon);

        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);

        return (profile, profile.Salons.Single(), repository, salonRepository, ownerId);
    }

    private static IReadOnlyList<WorkingDaySchedule> Week(
        TimeOnly start,
        TimeOnly end,
        DayOfWeek? dayOff = null)
        =>
        [
            Day(DayOfWeek.Monday, start, end, dayOff),
            Day(DayOfWeek.Tuesday, start, end, dayOff),
            Day(DayOfWeek.Wednesday, start, end, dayOff),
            Day(DayOfWeek.Thursday, start, end, dayOff),
            Day(DayOfWeek.Friday, start, end, dayOff),
            Day(DayOfWeek.Saturday, start, end, dayOff),
            Day(DayOfWeek.Sunday, start, end, dayOff)
        ];

    private static WorkingDaySchedule Day(
        DayOfWeek day,
        TimeOnly start,
        TimeOnly end,
        DayOfWeek? dayOff)
        => day == dayOff
            ? new WorkingDaySchedule(day, true, TimeOnly.MinValue, TimeOnly.MinValue)
            : new WorkingDaySchedule(day, false, start, end);

    [Fact]
    public async Task GetSchedule_ReturnsSevenDaysAndUpcomingData()
    {
        var (profile, salon, repository, salons, ownerId) = CreateOwner();
        salon.AddWorkingSchedule(
            DayOfWeek.Monday,
            new TimeOnly(10, 0),
            new TimeOnly(20, 0));
        salon.SetScheduleException(
            new DateOnly(2026, 11, 15),
            isDayOff: true,
            TimeOnly.MinValue,
            TimeOnly.MinValue,
            "Личные дела");
        profile.AddTimeOff(
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 3, 0, 0, 0, TimeSpan.Zero),
            "Отпуск");

        var handler = new GetBarberScheduleHandler(
            repository,
            salons,
            new FakeCurrentUser(ownerId),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<GetBarberScheduleHandler>.Instance);

        var result = await handler.Handle(
            new GetBarberScheduleQuery(
                profile.Id,
                new DateOnly(2026, 11, 1),
                new DateOnly(2026, 11, 30)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("schedule", repository.LoadCalls);

        var response = result.Value;
        Assert.Equal(profile.Id, response.BarberProfileId);
        var responseSalon = Assert.Single(response.Salons);
        Assert.Equal(salon.SalonId, responseSalon.SalonId);
        Assert.Equal("Barbershop", responseSalon.Name);
        Assert.Equal("Asia/Yekaterinburg", responseSalon.TimeZoneId);
        Assert.True(responseSalon.IsPrimary);
        Assert.Equal(7, responseSalon.Weekly.Count);

        var monday = Assert.Single(
            responseSalon.Weekly,
            day => day.DayOfWeek == DayOfWeek.Monday);
        Assert.False(monday.IsDayOff);
        Assert.Equal(new TimeOnly(10, 0), monday.StartTime);
        Assert.Equal(new TimeOnly(20, 0), monday.EndTime);

        var tuesday = Assert.Single(
            responseSalon.Weekly,
            day => day.DayOfWeek == DayOfWeek.Tuesday);
        Assert.True(tuesday.IsDayOff);

        var exception = Assert.Single(responseSalon.Exceptions);
        Assert.Equal(new DateOnly(2026, 11, 15), exception.Date);
        Assert.True(exception.IsDayOff);

        var timeOff = Assert.Single(response.TimeOffs);
        Assert.Equal("Отпуск", timeOff.Reason);
    }

    [Fact]
    public async Task GetSchedule_ForAnotherUser_IsForbidden()
    {
        var (profile, _, repository, salons, _) = CreateOwner();
        var handler = new GetBarberScheduleHandler(
            repository,
            salons,
            new FakeCurrentUser(Guid.CreateVersion7()),
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<GetBarberScheduleHandler>.Instance);

        var result = await handler.Handle(
            new GetBarberScheduleQuery(profile.Id, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ReplaceWorkingSchedules_LoadsSalonsAndUpdatesTheWeek()
    {
        var (profile, salon, repository, _, ownerId) = CreateOwner();
        var handler = new ReplaceWorkingSchedulesHandler(
            repository,
            new FakeCurrentUser(ownerId),
            NullLogger<ReplaceWorkingSchedulesHandler>.Instance);

        var result = await handler.Handle(
            new ReplaceWorkingSchedulesCommand(
                profile.Id,
                salon.SalonId,
                Week(new TimeOnly(10, 0), new TimeOnly(20, 0), DayOfWeek.Sunday)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["salons+schedules"], repository.LoadCalls);
        Assert.Equal(7, salon.WorkingSchedules.Count);
        Assert.True(
            salon.WorkingSchedules.Single(
                schedule => schedule.DayOfWeek == DayOfWeek.Sunday).IsDayOff);
    }

    [Fact]
    public async Task SetScheduleException_LoadsTheScheduleAndUpsertsTheDate()
    {
        var (profile, salon, repository, _, ownerId) = CreateOwner();
        var handler = new SetScheduleExceptionHandler(
            repository,
            new FakeCurrentUser(ownerId),
            NullLogger<SetScheduleExceptionHandler>.Instance);

        var result = await handler.Handle(
            new SetScheduleExceptionCommand(
                profile.Id,
                salon.SalonId,
                new DateOnly(2026, 11, 20),
                IsDayOff: false,
                new TimeOnly(10, 0),
                new TimeOnly(14, 0),
                "Короткий день"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["schedule"], repository.LoadCalls);
        var exception = Assert.Single(salon.ScheduleExceptions);
        Assert.Equal(new TimeOnly(10, 0), exception.StartTime);
        Assert.Equal("Короткий день", exception.Reason);
    }

    [Fact]
    public async Task RemoveScheduleException_LoadsTheScheduleAndRemovesTheDate()
    {
        var (profile, salon, repository, _, ownerId) = CreateOwner();
        salon.SetScheduleException(
            new DateOnly(2026, 11, 20),
            isDayOff: true,
            TimeOnly.MinValue,
            TimeOnly.MinValue,
            null);
        var handler = new RemoveScheduleExceptionHandler(
            repository,
            new FakeCurrentUser(ownerId),
            NullLogger<RemoveScheduleExceptionHandler>.Instance);

        var result = await handler.Handle(
            new RemoveScheduleExceptionCommand(
                profile.Id,
                salon.SalonId,
                new DateOnly(2026, 11, 20)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["schedule"], repository.LoadCalls);
        Assert.Empty(salon.ScheduleExceptions);
    }
}
