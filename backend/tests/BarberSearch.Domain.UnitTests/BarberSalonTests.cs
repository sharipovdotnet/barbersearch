using System;
using System.Linq;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.BarberSalons;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

public class BarberSalonTests
{
    private static (BarberProfile Profile, BarberSalon Salon) CreateSalon(
        bool isPrimary = false,
        Guid? salonId = null)
    {
        var data = new BarberProfileCreateData(
            Guid.CreateVersion7(),
            "Ivan the Barber",
            5,
            null,
            [Language.Ru],
            null,
            null,
            [PaymentMethod.Cash],
            null,
            null,
            null);

        var profile = BarberProfile.Create(data).Value;
        var id = salonId ?? Guid.CreateVersion7();

        profile.AddSalon(id, isPrimary);

        return (profile, profile.Salons.Single());
    }

    [Fact]
    public void WorkingSchedule_IsOwnedByTheSalon()
    {
        var (profile, salon) = CreateSalon();

        var result = salon.AddWorkingSchedule(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(18, 0));

        Assert.True(result.IsSuccess);
        Assert.Single(salon.WorkingSchedules);
        Assert.Single(salon.WorkingSchedules, s => s.BarberSalonId == salon.Id);
        Assert.Equal(salon.Id, salon.WorkingSchedules.First().BarberSalonId);
        Assert.Equal(profile.Id, salon.BarberProfileId);
    }

    [Fact]
    public void AddWorkingSchedule_WithEndBeforeStart_Fails()
    {
        var (_, salon) = CreateSalon();

        var result = salon.AddWorkingSchedule(
            DayOfWeek.Monday,
            new TimeOnly(18, 0),
            new TimeOnly(9, 0));

        Assert.True(result.IsFailure);
        Assert.Equal("barberSalon.addWorkingSchedule.timeRange", result.Error.Errors[0].Code);
        Assert.Empty(salon.WorkingSchedules);
    }

    [Fact]
    public void AddWorkingSchedule_WithEqualTimes_Fails()
    {
        var (_, salon) = CreateSalon();
        var time = new TimeOnly(9, 0);

        var result = salon.AddWorkingSchedule(DayOfWeek.Monday, time, time);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void AddWorkingSchedule_DeduplicatesByDayOfWeek()
    {
        var (_, salon) = CreateSalon();

        var first = salon.AddWorkingSchedule(
            DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(18, 0));

        var second = salon.AddWorkingSchedule(
            DayOfWeek.Monday, new TimeOnly(10, 0), new TimeOnly(20, 0));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal("barberSalon.addWorkingSchedule.duplicateDay", second.Error.Errors[0].Code);
        Assert.Single(salon.WorkingSchedules);
    }

    [Fact]
    public void AddWorkingSchedule_AllowsAllSevenDays()
    {
        var (_, salon) = CreateSalon();

        for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
        {
            Assert.True(salon.AddWorkingSchedule(day, new TimeOnly(9, 0), new TimeOnly(18, 0)).IsSuccess);
        }

        Assert.Equal(7, salon.WorkingSchedules.Count);
    }

    [Fact]
    public void AddDayOff_MarksTheDayWithoutTimes()
    {
        var (_, salon) = CreateSalon();

        var result = salon.AddDayOff(DayOfWeek.Sunday);

        Assert.True(result.IsSuccess);
        Assert.True(salon.WorkingSchedules.First().IsDayOff);
    }

    [Fact]
    public void AddDayOff_DeduplicatesByDayOfWeek()
    {
        var (_, salon) = CreateSalon();
        salon.AddDayOff(DayOfWeek.Sunday);

        var result = salon.AddDayOff(DayOfWeek.Sunday);

        Assert.True(result.IsFailure);
        Assert.Equal("barberSalon.addWorkingSchedule.duplicateDay", result.Error.Errors[0].Code);
    }

    [Fact]
    public void RemoveWorkingSchedule_RemovesById()
    {
        var (_, salon) = CreateSalon();
        salon.AddWorkingSchedule(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(18, 0));
        var scheduleId = salon.WorkingSchedules.First().Id;

        var result = salon.RemoveWorkingSchedule(scheduleId);

        Assert.True(result.IsSuccess);
        Assert.Empty(salon.WorkingSchedules);
    }

    [Fact]
    public void RemoveWorkingSchedule_WhenMissing_ReturnsNotFound()
    {
        var (_, salon) = CreateSalon();

        var result = salon.RemoveWorkingSchedule(Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal("barberSalon.removeWorkingSchedule.notFound", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Schedule_IsNotExposedOnTheBarberProfile()
    {
        var profileType = typeof(BarberProfile);

        // Расписание принадлежит паре (барбер, салон) и не должно торчать из BarberProfile.
        Assert.Null(profileType.GetProperty("WorkingSchedules"));
        Assert.Null(profileType.GetMethod("AddWorkingSchedule"));
        Assert.Null(profileType.GetMethod("RemoveWorkingSchedule"));
    }
}
