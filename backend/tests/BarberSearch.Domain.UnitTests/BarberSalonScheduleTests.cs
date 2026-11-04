using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.BarberSalons;
using BarberSearch.Domain.WorkingSchedules;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Гибридное расписание: недельный шаблон и исключения на конкретные даты.
/// </summary>
public class BarberSalonScheduleTests
{
    private static BarberSalon CreateSalon()
    {
        var profile = BarberProfile.Create(
            userId: Guid.CreateVersion7(),
            displayName: "Ivan the Barber",
            yearsOfExperience: 5,
            bio: null,
            languages: [Language.Ru],
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: [PaymentMethod.Cash],
            communicationFormat: null,
            goodies: null,
            latePolicy: null).Value;

        profile.AddSalon(Guid.CreateVersion7());

        return profile.Salons.Single();
    }

    private static WorkingDaySchedule Work(DayOfWeek day, int startHour, int endHour)
        => new(day, false, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    private static WorkingDaySchedule Off(DayOfWeek day)
        => new(day, true, TimeOnly.MinValue, TimeOnly.MinValue);

    private static IReadOnlyList<WorkingDaySchedule> FullWeek(
        WorkingDaySchedule monday,
        WorkingDaySchedule tuesday)
        =>
        [
            monday,
            tuesday,
            Off(DayOfWeek.Wednesday),
            Off(DayOfWeek.Thursday),
            Off(DayOfWeek.Friday),
            Off(DayOfWeek.Saturday),
            Off(DayOfWeek.Sunday)
        ];

    [Fact]
    public void ReplaceWorkingSchedules_ReplacesTheWholeWeek()
    {
        var salon = CreateSalon();
        salon.AddWorkingSchedule(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(18, 0));

        var result = salon.ReplaceWorkingSchedules(FullWeek(
            Work(DayOfWeek.Monday, 10, 20),
            Work(DayOfWeek.Tuesday, 11, 19)));

        Assert.True(result.IsSuccess);
        Assert.Equal(7, salon.WorkingSchedules.Count);

        var monday = Assert.Single(
            salon.WorkingSchedules,
            schedule => schedule.DayOfWeek == DayOfWeek.Monday);
        Assert.False(monday.IsDayOff);
        Assert.Equal(new TimeOnly(10, 0), monday.StartTime);
        Assert.Equal(new TimeOnly(20, 0), monday.EndTime);

        var wednesday = Assert.Single(
            salon.WorkingSchedules,
            schedule => schedule.DayOfWeek == DayOfWeek.Wednesday);
        Assert.True(wednesday.IsDayOff);
    }

    [Fact]
    public void ReplaceWorkingSchedules_KeepsTheExistingRowIdForTheSameDay()
    {
        var salon = CreateSalon();
        salon.AddWorkingSchedule(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(18, 0));
        var mondayId = salon.WorkingSchedules.Single().Id;

        salon.ReplaceWorkingSchedules(FullWeek(
            Work(DayOfWeek.Monday, 10, 20),
            Off(DayOfWeek.Tuesday)));

        Assert.Equal(
            mondayId,
            salon.WorkingSchedules.Single(
                schedule => schedule.DayOfWeek == DayOfWeek.Monday).Id);
    }

    [Fact]
    public void ReplaceWorkingSchedules_RequiresAllSevenDays()
    {
        var salon = CreateSalon();

        var result = salon.ReplaceWorkingSchedules(
        [
            Work(DayOfWeek.Monday, 10, 20),
            Off(DayOfWeek.Tuesday)
        ]);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "barberSalon.replaceSchedule.days");
        Assert.Empty(salon.WorkingSchedules);
    }

    [Fact]
    public void ReplaceWorkingSchedules_RejectsDuplicateDays()
    {
        var salon = CreateSalon();

        var result = salon.ReplaceWorkingSchedules(
        [
            Work(DayOfWeek.Monday, 10, 20),
            Work(DayOfWeek.Monday, 11, 19),
            Off(DayOfWeek.Tuesday),
            Off(DayOfWeek.Wednesday),
            Off(DayOfWeek.Thursday),
            Off(DayOfWeek.Friday),
            Off(DayOfWeek.Saturday)
        ]);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "barberSalon.replaceSchedule.duplicateDay");
    }

    [Fact]
    public void ReplaceWorkingSchedules_RejectsEndBeforeStart()
    {
        var salon = CreateSalon();

        var result = salon.ReplaceWorkingSchedules(FullWeek(
            Work(DayOfWeek.Monday, 18, 9),
            Off(DayOfWeek.Tuesday)));

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "barberSalon.replaceSchedule.timeRange");
        Assert.Empty(salon.WorkingSchedules);
    }

    [Fact]
    public void SetScheduleException_AddsADayOff()
    {
        var salon = CreateSalon();

        var result = salon.SetScheduleException(
            new DateOnly(2026, 11, 15),
            isDayOff: true,
            TimeOnly.MinValue,
            TimeOnly.MinValue,
            "Личные дела");

        Assert.True(result.IsSuccess);
        var exception = Assert.Single(salon.ScheduleExceptions);
        Assert.Equal(new DateOnly(2026, 11, 15), exception.Date);
        Assert.True(exception.IsDayOff);
        Assert.Equal("Личные дела", exception.Reason);
    }

    [Fact]
    public void SetScheduleException_AddsCustomHours()
    {
        var salon = CreateSalon();

        var result = salon.SetScheduleException(
            new DateOnly(2026, 11, 20),
            isDayOff: false,
            new TimeOnly(10, 0),
            new TimeOnly(14, 0),
            null);

        Assert.True(result.IsSuccess);
        var exception = Assert.Single(salon.ScheduleExceptions);
        Assert.False(exception.IsDayOff);
        Assert.Equal(new TimeOnly(10, 0), exception.StartTime);
        Assert.Equal(new TimeOnly(14, 0), exception.EndTime);
    }

    [Fact]
    public void SetScheduleException_UpdatesAnExistingDate()
    {
        var salon = CreateSalon();
        salon.SetScheduleException(
            new DateOnly(2026, 11, 20),
            isDayOff: true,
            TimeOnly.MinValue,
            TimeOnly.MinValue,
            null);

        var result = salon.SetScheduleException(
            new DateOnly(2026, 11, 20),
            isDayOff: false,
            new TimeOnly(10, 0),
            new TimeOnly(14, 0),
            "Короткий день");

        Assert.True(result.IsSuccess);
        var exception = Assert.Single(salon.ScheduleExceptions);
        Assert.False(exception.IsDayOff);
        Assert.Equal(new TimeOnly(10, 0), exception.StartTime);
        Assert.Equal(new TimeOnly(14, 0), exception.EndTime);
        Assert.Equal("Короткий день", exception.Reason);
    }

    [Fact]
    public void SetScheduleException_RejectsEndBeforeStart()
    {
        var salon = CreateSalon();

        var result = salon.SetScheduleException(
            new DateOnly(2026, 11, 20),
            isDayOff: false,
            new TimeOnly(14, 0),
            new TimeOnly(10, 0),
            null);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == "barberSalon.setScheduleException.timeRange");
        Assert.Empty(salon.ScheduleExceptions);
    }

    [Fact]
    public void RemoveScheduleException_RemovesByDate()
    {
        var salon = CreateSalon();
        salon.SetScheduleException(
            new DateOnly(2026, 11, 15),
            isDayOff: true,
            TimeOnly.MinValue,
            TimeOnly.MinValue,
            null);

        var result = salon.RemoveScheduleException(new DateOnly(2026, 11, 15));

        Assert.True(result.IsSuccess);
        Assert.Empty(salon.ScheduleExceptions);
    }

    [Fact]
    public void RemoveScheduleException_WhenMissing_ReturnsNotFound()
    {
        var salon = CreateSalon();

        var result = salon.RemoveScheduleException(new DateOnly(2026, 11, 15));

        Assert.True(result.IsFailure);
        Assert.Equal(
            "barberSalon.removeScheduleException.notFound",
            result.Error.Errors[0].Code);
    }
}
