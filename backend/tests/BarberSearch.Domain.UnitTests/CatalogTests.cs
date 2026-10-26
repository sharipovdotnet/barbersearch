using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.Specializations;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

public class SalonTests
{
    private static Salon CreateValidSalon()
        => Salon.Create(
            "Barbershop",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173).Value;

    [Fact]
    public void Create_WithValidCoordinates_Succeeds()
    {
        var result = Salon.Create(
            "Barbershop", "Tverskaya 1", "Moscow", "RU", "Europe/Moscow", 55.7558, 37.6173);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
        Assert.Equal(55.7558, result.Value.Latitude);
    }

    [Fact]
    public void Create_WithLatitudeOutOfRange_Fails()
    {
        var result = Salon.Create(
            "Barbershop", "Tverskaya 1", "Moscow", "RU", "Europe/Moscow", 91, 37);

        Assert.True(result.IsFailure);
        Assert.Equal("salon.latitude", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithLongitudeOutOfRange_Fails()
    {
        var result = Salon.Create(
            "Barbershop", "Tverskaya 1", "Moscow", "RU", "Europe/Moscow", 55, -180.5);

        Assert.True(result.IsFailure);
        Assert.Equal("salon.longitude", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithBoundaryCoordinates_Succeeds()
    {
        var result = Salon.Create(
            "Barbershop", "Tverskaya 1", "Moscow", "RU", "Europe/Moscow", -90, 180);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_WithoutTimeZone_Fails()
    {
        var result = Salon.Create(
            "Barbershop", "Tverskaya 1", "Moscow", "RU", " ", 55, 37);

        Assert.True(result.IsFailure);
        Assert.Contains("salon.timeZoneId", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithUnknownTimeZone_Fails()
    {
        var result = Salon.Create(
            "Barbershop", "Tverskaya 1", "Moscow", "RU", "Mars/Olympus", 55, 37);

        Assert.True(result.IsFailure);
        Assert.Equal("salon.timeZoneId.unknown", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_TrimsTimeZoneId()
    {
        var result = Salon.Create(
            "Barbershop", "Tverskaya 1", "Moscow", "RU", " Europe/Moscow ", 55, 37);

        Assert.True(result.IsSuccess);
        Assert.Equal("Europe/Moscow", result.Value.TimeZoneId);
    }

    [Fact]
    public void Create_RejectsInvalidWebsiteUrls()
    {
        foreach (var websiteUrl in new[]
                 {
                     "javascript:alert(1)",
                     "data:text/html,<script>alert(1)</script>",
                     "ftp://barbershop.example.com",
                     "barbershop.example.com"
                 })
        {
            var result = Salon.Create(
                "Barbershop",
                "Tverskaya 1",
                "Moscow",
                "RU",
                "Europe/Moscow",
                55,
                37,
                websiteUrl: websiteUrl);

            Assert.True(result.IsFailure);

            var error = Assert.Single(result.Error.Errors);

            Assert.Equal("salon.websiteUrl", error.Code);
            Assert.Equal("websiteUrl", error.InvalidField);
        }
    }

    [Fact]
    public void Create_TrimsWebsiteUrl()
    {
        var result = Salon.Create(
            "Barbershop",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55,
            37,
            websiteUrl: "  https://barbershop.example.com  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("https://barbershop.example.com", result.Value.WebsiteUrl);
    }

    [Fact]
    public void Create_WithSpacesInsteadOfWebsite_SucceedsWithNull()
    {
        var result = Salon.Create(
            "Barbershop",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55,
            37,
            websiteUrl: "   ");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.WebsiteUrl);
    }

    [Fact]
    public void UpdateContactInfo_WithInvalidWebsiteUrl_FailsAndKeepsOldValues()
    {
        var salon = Salon.Create(
            "Barbershop",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55,
            37,
            phoneNumber: "+79001234567",
            websiteUrl: "https://barbershop.example.com").Value;

        var result = salon.UpdateContactInfo("+79007654321", "javascript:alert(1)");

        Assert.True(result.IsFailure);
        Assert.Equal("salon.websiteUrl", result.Error.Errors[0].Code);
        Assert.Equal("+79001234567", salon.PhoneNumber);
        Assert.Equal("https://barbershop.example.com", salon.WebsiteUrl);
    }

    [Fact]
    public void UpdateContactInfo_TrimsWebsiteUrlAndClearsItOnBlank()
    {
        var salon = CreateValidSalon();

        var updated = salon.UpdateContactInfo(
            null,
            "  https://barbershop.example.com  ");

        Assert.True(updated.IsSuccess);
        Assert.Equal("https://barbershop.example.com", salon.WebsiteUrl);

        var cleared = salon.UpdateContactInfo(null, "   ");

        Assert.True(cleared.IsSuccess);
        Assert.Null(salon.WebsiteUrl);
    }

    [Fact]
    public void Create_CollectsEveryMissingField()
    {
        var result = Salon.Create("", "", "", "", "", 55, 37);

        Assert.True(result.IsFailure);
        Assert.Equal(5, result.Error.Errors.Count);
    }

    [Fact]
    public void UpdateLocation_WithUnknownTimeZone_FailsAndKeepsOldLocation()
    {
        var salon = CreateValidSalon();

        var result = salon.UpdateLocation(
            "Arbat 5", "Moscow", "RU", "Mars/Olympus", 55.75, 37.59);

        Assert.True(result.IsFailure);
        Assert.Equal("salon.timeZoneId.unknown", result.Error.Errors[0].Code);
        Assert.Equal("Tverskaya 1", salon.Address);
        Assert.Equal("Europe/Moscow", salon.TimeZoneId);
    }

    [Fact]
    public void UpdateLocation_WithInvalidCoordinates_Fails()
    {
        var salon = CreateValidSalon();

        var result = salon.UpdateLocation("Tverskaya 1", "Moscow", "RU", "Europe/Moscow", 200, 37);

        Assert.True(result.IsFailure);
        Assert.Equal(55.7558, salon.Latitude);
    }

    [Fact]
    public void UpdateLocation_WithValidCoordinates_ChangesState()
    {
        var salon = CreateValidSalon();

        var result = salon.UpdateLocation("Arbat 5", "Moscow", "RU", "Europe/Moscow", 55.75, 37.59);

        Assert.True(result.IsSuccess);
        Assert.Equal("Arbat 5", salon.Address);
        Assert.Equal(37.59, salon.Longitude);
    }

    [Fact]
    public void DeactivateThenActivate_ChangesIsActive()
    {
        var salon = CreateValidSalon();

        salon.Deactivate();
        Assert.False(salon.IsActive);

        salon.Activate();
        Assert.True(salon.IsActive);
    }
}

public class HairServiceTests
{
    [Fact]
    public void Create_WithValidDuration_Succeeds()
    {
        var result = HairService.Create("Haircut", ServiceCategory.Haircut, 45);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public void Create_WithZeroDuration_Fails()
    {
        var result = HairService.Create("Haircut", ServiceCategory.Haircut, 0);

        Assert.True(result.IsFailure);
        Assert.Equal("hairService.defaultDurationMinutes", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithNegativeDuration_Fails()
    {
        var result = HairService.Create("Haircut", ServiceCategory.Haircut, -10);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_WithTooLongDuration_Fails()
    {
        var result = HairService.Create("Haircut", ServiceCategory.Haircut, 24 * 60 + 1);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_WithUnknownCategory_Fails()
    {
        var result = HairService.Create("Haircut", (ServiceCategory)777, 30);

        Assert.True(result.IsFailure);
        Assert.Equal("hairService.category", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Update_WithZeroDuration_FailsAndKeepsOldValue()
    {
        var service = HairService.Create("Haircut", ServiceCategory.Haircut, 30).Value;

        var result = service.Update("Haircut", null, 0);

        Assert.True(result.IsFailure);
        Assert.Equal(30, service.DefaultDurationMinutes);
    }

    [Fact]
    public void Update_WithValidData_ChangesState()
    {
        var service = HairService.Create("Haircut", ServiceCategory.Haircut, 30).Value;

        var result = service.Update("Premium haircut", "Description", 60);

        Assert.True(result.IsSuccess);
        Assert.Equal("Premium haircut", service.Name);
        Assert.Equal(60, service.DefaultDurationMinutes);
    }

    [Fact]
    public void DeactivateThenActivate_ChangesIsActive()
    {
        var service = HairService.Create("Haircut", ServiceCategory.Haircut, 30).Value;

        service.Deactivate();
        Assert.False(service.IsActive);

        service.Activate();
        Assert.True(service.IsActive);
    }
}

public class SpecializationTests
{
    [Fact]
    public void Create_WithEmptyName_Fails()
    {
        var result = Specialization.Create("   ");

        Assert.True(result.IsFailure);
        Assert.Equal("specialization.name", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithValidName_Succeeds()
    {
        var result = Specialization.Create("Fade master", "Loves fades");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public void Rename_WithEmptyName_Fails()
    {
        var specialization = Specialization.Create("Fade master").Value;

        var result = specialization.Rename("  ", null);

        Assert.True(result.IsFailure);
        Assert.Equal("Fade master", specialization.Name);
    }

    [Fact]
    public void Rename_WithValidName_ChangesState()
    {
        var specialization = Specialization.Create("Fade master").Value;

        var result = specialization.Rename("Beard master", "Beards");

        Assert.True(result.IsSuccess);
        Assert.Equal("Beard master", specialization.Name);
        Assert.Equal("Beards", specialization.Description);
    }

    [Fact]
    public void DeactivateThenActivate_ChangesIsActive()
    {
        var specialization = Specialization.Create("Fade master").Value;

        specialization.Deactivate();
        Assert.False(specialization.IsActive);

        specialization.Activate();
        Assert.True(specialization.IsActive);
    }
}
