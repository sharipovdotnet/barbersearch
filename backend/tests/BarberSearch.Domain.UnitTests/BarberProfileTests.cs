using BarberSearch.Domain;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberProfiles.Enums;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

public class BarberProfileTests
{
    private static BarberProfileCreateData CreateData(
        string displayName = "Ivan the Barber",
        int yearsOfExperience = 5)
        => new(
            Guid.CreateVersion7(),
            displayName,
            yearsOfExperience,
            "Bio",
            [Language.Ru, Language.En],
            ["Cert"],
            ["Achievement"],
            [PaymentMethod.Cash],
            ["Chatty"],
            ["Coffee"],
            "Wait 10 minutes max");

    private static BarberProfileUpdateData UpdateData(
        string displayName = "Ivan the Barber",
        string? bio = "Bio")
        => new(
            displayName,
            5,
            bio,
            [Language.Ru],
            ["Cert"],
            ["Achievement"],
            [PaymentMethod.Cash],
            ["Chatty"],
            ["Coffee"],
            "Wait 10 minutes max");

    private static BarberProfile CreateProfile()
        => BarberProfile.Create(CreateData()).Value;

    [Fact]
    public void Create_StartsWithoutRating()
    {
        // null = отзывов ещё нет; 0m в этом поле больше не пишется.
        var profile = CreateProfile();

        Assert.Null(profile.Rating);
        Assert.Equal(0, profile.ReviewsCount);
        Assert.True(profile.IsActive);
        Assert.False(profile.IsVerified);
        Assert.Equal(Grade.None, profile.Grade);
    }

    [Fact]
    public void Create_WithEmptyDisplayName_Fails()
    {
        var result = BarberProfile.Create(CreateData(displayName: "   "));

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.displayName", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithYearsOutOfRange_CollectsErrorsFromEveryField()
    {
        var result = BarberProfile.Create(CreateData(displayName: "", yearsOfExperience: 0));

        Assert.True(result.IsFailure);
        Assert.Equal(2, result.Error.Errors.Count);
    }

    [Fact]
    public void Create_UsesDefaultLanguage_WhenNoneProvided()
    {
        var data = CreateData() with { Languages = null! };

        var profile = BarberProfile.Create(data).Value;

        Assert.Single(profile.Languages);
        Assert.Equal(Language.Ru, profile.Languages.First());
    }

    [Fact]
    public void UpdateProfile_ReplacesCollectionsInsteadOfAppending()
    {
        var profile = CreateProfile();

        var first = profile.UpdateProfile(UpdateData());
        var second = profile.UpdateProfile(UpdateData());

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Single(profile.Languages);
        Assert.Single(profile.CertificatesAndTraining);
        Assert.Single(profile.Achievements);
        Assert.Single(profile.PaymentMethods);
        Assert.Single(profile.CommunicationFormat);
        Assert.Single(profile.Goodies);
    }

    [Fact]
    public void UpdateProfile_CanClearBio()
    {
        var profile = CreateProfile();

        var result = profile.UpdateProfile(UpdateData(bio: null));

        Assert.True(result.IsSuccess);
        Assert.Null(profile.Bio);
    }

    [Fact]
    public void UpdateProfile_DoesNotChangeGrade()
    {
        var profile = CreateProfile();
        profile.ChangeGrade(Grade.SeniorBarber);

        profile.UpdateProfile(UpdateData());

        Assert.Equal(Grade.SeniorBarber, profile.Grade);
    }

    [Fact]
    public void ChangeGrade_WithKnownValue_Succeeds()
    {
        var profile = CreateProfile();

        var result = profile.ChangeGrade(Grade.ArtDirector);

        Assert.True(result.IsSuccess);
        Assert.Equal(Grade.ArtDirector, profile.Grade);
    }

    [Fact]
    public void ChangeGrade_WithUnknownValue_Fails()
    {
        var profile = CreateProfile();

        var result = profile.ChangeGrade((Grade)1234);

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.changeGrade", result.Error.Errors[0].Code);
        Assert.Equal(Grade.None, profile.Grade);
    }

    [Fact]
    public void UpdateRating_WithReviews_StoresRoundedValue()
    {
        var profile = CreateProfile();

        var result = profile.UpdateRating(4.567m, 10);

        Assert.True(result.IsSuccess);
        Assert.Equal(4.57m, profile.Rating);
        Assert.Equal(10, profile.ReviewsCount);
    }

    [Fact]
    public void UpdateRating_WithZeroReviews_ClearsRating()
    {
        var profile = CreateProfile();
        profile.UpdateRating(5m, 3);

        var result = profile.UpdateRating(null, 0);

        Assert.True(result.IsSuccess);
        Assert.Null(profile.Rating);
        Assert.Equal(0, profile.ReviewsCount);
    }

    [Fact]
    public void UpdateRating_WithZeroReviewsAndValue_DropsTheValue()
    {
        var profile = CreateProfile();

        var result = profile.UpdateRating(5m, 0);

        Assert.True(result.IsSuccess);
        Assert.Null(profile.Rating);
    }

    [Fact]
    public void UpdateRating_WithoutRatingButWithReviews_Fails()
    {
        var profile = CreateProfile();

        var result = profile.UpdateRating(null, 5);

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.updateRating.rating", result.Error.Errors[0].Code);
    }

    [Fact]
    public void UpdateRating_AboveFive_Fails()
    {
        var profile = CreateProfile();

        var result = profile.UpdateRating(5.01m, 3);

        Assert.True(result.IsFailure);
        Assert.Equal(5m, BarberSearch.Domain.Constants.MAX_RATING);
    }

    [Fact]
    public void UpdateRating_BelowOne_Fails()
    {
        var profile = CreateProfile();

        var result = profile.UpdateRating(0.5m, 3);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void UpdateRating_WithNegativeReviewsCount_Fails()
    {
        var profile = CreateProfile();

        var result = profile.UpdateRating(4m, -1);

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.updateRating.reviewsCount", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Verify_InactiveProfile_Fails()
    {
        var profile = CreateProfile();
        profile.Deactivate();

        var result = profile.Verify();

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.verify.inactive", result.Error.Errors[0].Code);
        Assert.False(profile.IsVerified);
    }

    [Fact]
    public void Verify_ActiveProfile_Succeeds()
    {
        var profile = CreateProfile();

        var result = profile.Verify();

        Assert.True(result.IsSuccess);
        Assert.True(profile.IsVerified);
    }

    [Fact]
    public void AddPhoto_AssignsOwnProfileId()
    {
        var profile = CreateProfile();

        var result = profile.AddPhoto("https://cdn/1.jpg", "https://cdn/1-thumb.jpg", "Fade");

        Assert.True(result.IsSuccess);
        Assert.Single(profile.PortfolioPhotos);

        var photo = profile.PortfolioPhotos.First();

        Assert.Equal(profile.Id, photo.BarberProfileId);
        Assert.True(photo.IsPublished);
    }

    [Fact]
    public void AddPhoto_WithEmptyUrl_Fails()
    {
        var profile = CreateProfile();

        var result = profile.AddPhoto("  ");

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.addPhoto.url", result.Error.Errors[0].Code);
    }

    [Fact]
    public void AddPhoto_AboveLimit_Fails()
    {
        var profile = CreateProfile();

        for (var i = 0; i < Constants.MAX_PHOTOS_COUNT; i++)
        {
            Assert.True(profile.AddPhoto($"https://cdn/{i}.jpg").IsSuccess);
        }

        var result = profile.AddPhoto("https://cdn/extra.jpg");

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.addPhoto.limit", result.Error.Errors[0].Code);
    }

    [Fact]
    public void RemovePhoto_WhenPresent_Succeeds()
    {
        var profile = CreateProfile();
        profile.AddPhoto("https://cdn/1.jpg");
        var photoId = profile.PortfolioPhotos.First().Id;

        var result = profile.RemovePhoto(photoId);

        Assert.True(result.IsSuccess);
        Assert.Empty(profile.PortfolioPhotos);
    }

    [Fact]
    public void RemovePhoto_WhenMissing_ReturnsNotFound()
    {
        var profile = CreateProfile();

        var result = profile.RemovePhoto(Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.removePhoto.notFound", result.Error.Errors[0].Code);
    }

    [Fact]
    public void ReorderPhoto_ChangesSortOrder()
    {
        var profile = CreateProfile();
        profile.AddPhoto("https://cdn/1.jpg");
        var photoId = profile.PortfolioPhotos.First().Id;

        var result = profile.ReorderPhoto(photoId, 7);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, profile.PortfolioPhotos.First().SortOrder);
    }

    [Fact]
    public void ReorderPhoto_WhenMissing_ReturnsNotFound()
    {
        var profile = CreateProfile();

        var result = profile.ReorderPhoto(Guid.CreateVersion7(), 1);

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.reorderPhoto.notFound", result.Error.Errors[0].Code);
    }

    [Fact]
    public void AddSalon_DeduplicatesBySalonId()
    {
        var profile = CreateProfile();
        var salonId = Guid.CreateVersion7();

        var first = profile.AddSalon(salonId);
        var second = profile.AddSalon(salonId);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal("barberProfile.addSalon.duplicate", second.Error.Errors[0].Code);
        Assert.Single(profile.Salons);
    }

    [Fact]
    public void AddSalon_WithDifferentRecordIdsButSameSalon_IsStillDuplicate()
    {
        // Два Guid-идентификатора записи, но один и тот же салон.
        var profile = CreateProfile();
        var salonId = Guid.CreateVersion7();

        profile.AddSalon(salonId);
        var second = profile.AddSalon(salonId);

        Assert.True(second.IsFailure);
        Assert.Single(profile.Salons);
    }

    [Fact]
    public void AddSalon_AssignsOwnProfileId()
    {
        var profile = CreateProfile();

        profile.AddSalon(Guid.CreateVersion7());

        Assert.Equal(profile.Id, profile.Salons.First().BarberProfileId);
    }

    [Fact]
    public void AddSalon_AsPrimary_ClearsPrimaryFromOthers()
    {
        var profile = CreateProfile();
        var firstSalon = Guid.CreateVersion7();
        var secondSalon = Guid.CreateVersion7();

        profile.AddSalon(firstSalon, isPrimary: true);
        profile.AddSalon(secondSalon, isPrimary: true);

        Assert.Single(profile.Salons, s => s.IsPrimary);
        Assert.Equal(secondSalon, profile.Salons.Single(s => s.IsPrimary).SalonId);
    }

    [Fact]
    public void SetPrimarySalon_KeepsExactlyOnePrimary()
    {
        var profile = CreateProfile();
        var firstSalon = Guid.CreateVersion7();
        profile.AddSalon(firstSalon);
        profile.AddSalon(Guid.CreateVersion7());

        var result = profile.SetPrimarySalon(firstSalon);

        Assert.True(result.IsSuccess);
        Assert.Single(profile.Salons, s => s.IsPrimary);
        Assert.Equal(firstSalon, profile.Salons.Single(s => s.IsPrimary).SalonId);
    }

    [Fact]
    public void SetPrimarySalon_WhenMissing_ReturnsNotFound()
    {
        var profile = CreateProfile();

        var result = profile.SetPrimarySalon(Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.setPrimarySalon.notFound", result.Error.Errors[0].Code);
    }

    [Fact]
    public void RemoveSalon_DeduplicatesBySalonId()
    {
        var profile = CreateProfile();
        var salonId = Guid.CreateVersion7();
        profile.AddSalon(salonId);

        var result = profile.RemoveSalon(salonId);

        Assert.True(result.IsSuccess);
        Assert.Empty(profile.Salons);
    }

    [Fact]
    public void FindSalon_ReturnsSalonForScheduleWork()
    {
        var profile = CreateProfile();
        var salonId = Guid.CreateVersion7();
        profile.AddSalon(salonId);

        var found = profile.FindSalon(salonId);

        Assert.True(found.IsSuccess);
        Assert.Equal(profile.Id, found.Value.BarberProfileId);
    }

    [Fact]
    public void FindSalon_WhenMissing_ReturnsNotFound()
    {
        var profile = CreateProfile();

        var found = profile.FindSalon(Guid.CreateVersion7());

        Assert.True(found.IsFailure);
        Assert.Equal("barberProfile.findSalon.notFound", found.Error.Errors[0].Code);
    }

    [Fact]
    public void AddService_DeduplicatesByHairServiceId()
    {
        var profile = CreateProfile();
        var hairServiceId = Guid.CreateVersion7();

        var first = profile.AddService(hairServiceId, 1500m, 45);
        var second = profile.AddService(hairServiceId, 2000m, 60);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal("barberProfile.addService.duplicate", second.Error.Errors[0].Code);
        Assert.Single(profile.Services);
        Assert.Equal(profile.Id, profile.Services.First().BarberProfileId);
    }

    [Fact]
    public void AddService_WithInvalidPriceAndDuration_CollectsErrors()
    {
        var profile = CreateProfile();

        var result = profile.AddService(Guid.CreateVersion7(), -1m, 0);

        Assert.True(result.IsFailure);
        Assert.Equal(2, result.Error.Errors.Count);
        Assert.Empty(profile.Services);
    }

    [Fact]
    public void AddService_WithValidData_Succeeds()
    {
        var profile = CreateProfile();

        var result = profile.AddService(Guid.CreateVersion7(), 1500m, 45);

        Assert.True(result.IsSuccess);
        Assert.Equal(1500m, profile.Services.First().Price);
        Assert.True(profile.Services.First().IsActive);
    }

    [Fact]
    public void RemoveService_WhenMissing_ReturnsNotFound()
    {
        var profile = CreateProfile();

        var result = profile.RemoveService(Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.removeService.notFound", result.Error.Errors[0].Code);
    }

    [Fact]
    public void AddSpecialization_DeduplicatesBySpecializationId()
    {
        var profile = CreateProfile();
        var specializationId = Guid.CreateVersion7();

        var first = profile.AddSpecialization(specializationId);
        var second = profile.AddSpecialization(specializationId);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal("barberProfile.addSpecialization.duplicate", second.Error.Errors[0].Code);
        Assert.Single(profile.BarberSpecializations);
        Assert.Equal(profile.Id, profile.BarberSpecializations.First().BarberProfileId);
    }

    [Fact]
    public void RemoveSpecialization_WhenMissing_ReturnsNotFound()
    {
        var profile = CreateProfile();

        var result = profile.RemoveSpecialization(Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.removeSpecialization.notFound", result.Error.Errors[0].Code);
    }

    [Fact]
    public void AddTimeOff_RejectsOverlappingPeriods()
    {
        var profile = CreateProfile();
        var start = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        var first = profile.AddTimeOff(start, start.AddDays(3));
        var second = profile.AddTimeOff(start.AddDays(1), start.AddDays(5));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal("barberProfile.addTimeOff.overlap", second.Error.Errors[0].Code);
        Assert.Single(profile.TimeOffs);
    }

    [Fact]
    public void AddTimeOff_AllowsTouchingPeriods()
    {
        var profile = CreateProfile();
        var start = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        profile.AddTimeOff(start, start.AddDays(3));

        var second = profile.AddTimeOff(start.AddDays(3), start.AddDays(5));

        Assert.True(second.IsSuccess);
        Assert.Equal(2, profile.TimeOffs.Count);
    }

    [Fact]
    public void AddTimeOff_WithEndBeforeStart_Fails()
    {
        var profile = CreateProfile();
        var start = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        var result = profile.AddTimeOff(start, start.AddDays(-1));

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.addTimeOff.period", result.Error.Errors[0].Code);
    }

    [Fact]
    public void AddTimeOff_AssignsOwnProfileId()
    {
        var profile = CreateProfile();
        var start = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        profile.AddTimeOff(start, start.AddDays(3), "Vacation");

        var timeOff = profile.TimeOffs.First();

        Assert.Equal(profile.Id, timeOff.BarberProfileId);
        Assert.Equal("Vacation", timeOff.Reason);
    }

    [Fact]
    public void RemoveTimeOff_WhenMissing_ReturnsNotFound()
    {
        var profile = CreateProfile();

        var result = profile.RemoveTimeOff(Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.removeTimeOff.notFound", result.Error.Errors[0].Code);
    }
}
