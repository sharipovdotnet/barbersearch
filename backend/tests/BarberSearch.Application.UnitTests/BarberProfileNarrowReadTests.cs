using BarberSearch.Application.BarberProfiles.ActivateBarberProfile;
using BarberSearch.Application.BarberProfiles.AddBarberProfileSalon;
using BarberSearch.Application.BarberProfiles.AddBarberProfileService;
using BarberSearch.Application.BarberProfiles.AddBarberProfileSpecialization;
using BarberSearch.Application.BarberProfiles.AddBarberProfileTimeOff;
using BarberSearch.Application.BarberProfiles.ChangeBarberProfileGrade;
using BarberSearch.Application.BarberProfiles.FindBarberProfileSalon;
using BarberSearch.Application.BarberProfiles.RemoveBarberProfileService;
using BarberSearch.Application.BarberProfiles.ReorderBarberProfilePhoto;
using BarberSearch.Application.BarberProfiles.SetBarberProfilePrimarySalon;
using BarberSearch.Application.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Application.BarberSalons.AddBarberSalonWorkingSchedule;
using BarberSearch.Application.PortfolioPhotos.UnpublishPortfolioPhoto;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.BarberProfiles.FindBarberProfileSalon;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberProfiles.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// R-12: write-обработчики не грузят полный агрегат профиля. Каждый сценарий проверяет,
/// что репозиторий вызвал ровно одно узкое чтение, а доменный инвариант при этом сохранён.
/// </summary>
public class BarberProfileNarrowReadTests
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

    private static (BarberProfile Profile, FakeBarberProfileRepository Repository, Guid OwnerId) CreateOwner()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);

        return (profile, repository, ownerId);
    }

    [Fact]
    public async Task Activate_LoadsOnlyRoot()
    {
        var (profile, repository, ownerId) = CreateOwner();
        var handler = new ActivateBarberProfileHandler(
            repository,
            NullLogger<ActivateBarberProfileHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new ActivateBarberProfileCommand(profile.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["root"], repository.LoadCalls);
    }

    [Fact]
    public async Task UpdateProfile_LoadsOnlyRoot()
    {
        var (profile, repository, ownerId) = CreateOwner();
        var handler = new UpdateBarberProfileHandler(
            repository,
            NullLogger<UpdateBarberProfileHandler>.Instance,
            new FakeCurrentUser(ownerId));
        var request = new BarberProfileUpdateRequest(
            DisplayName: "Updated Barber",
            YearsOfExperience: 7,
            Bio: "New bio",
            Languages: [Language.Ru],
            CertificatesAndTraining: [],
            Achievements: [],
            PaymentMethods: [],
            CommunicationFormat: [],
            Goodies: [],
            LatePolicy: "10 minutes");

        var result = await handler.Handle(
            new UpdateBarberProfileCommand(profile.Id, request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["root"], repository.LoadCalls);
        Assert.Equal("Updated Barber", profile.DisplayName.Value);
    }

    [Fact]
    public async Task ChangeGrade_LoadsOnlyRoot()
    {
        var (profile, repository, _) = CreateOwner();
        var handler = new ChangeBarberProfileGradeHandler(
            repository,
            NullLogger<ChangeBarberProfileGradeHandler>.Instance);

        var result = await handler.Handle(
            new ChangeBarberProfileGradeCommand(profile.Id, Grade.SeniorBarber),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["root"], repository.LoadCalls);
        Assert.Equal(Grade.SeniorBarber, profile.Grade);
    }

    [Fact]
    public async Task AddSalon_LoadsOnlySalons()
    {
        var (profile, repository, ownerId) = CreateOwner();
        var salonId = Guid.CreateVersion7();
        var salons = new FakeSalonRepository();
        salons.AddExisting(salonId);
        var handler = new AddBarberProfileSalonHandler(
            repository,
            salons,
            new FakeUnitOfWork(),
            NullLogger<AddBarberProfileSalonHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new AddBarberProfileSalonCommand(profile.Id, salonId, IsPrimary: false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["salons"], repository.LoadCalls);
        Assert.Contains(profile.Salons, link => link.SalonId == salonId);
    }

    [Fact]
    public async Task SetPrimarySalon_LoadsOnlySalons()
    {
        var (profile, repository, ownerId) = CreateOwner();
        var firstSalonId = Guid.CreateVersion7();
        var secondSalonId = Guid.CreateVersion7();
        profile.AddSalon(firstSalonId, isPrimary: true);
        profile.AddSalon(secondSalonId);
        var handler = new SetBarberProfilePrimarySalonHandler(
            repository,
            NullLogger<SetBarberProfilePrimarySalonHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new SetBarberProfilePrimarySalonCommand(profile.Id, secondSalonId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["salons"], repository.LoadCalls);
        Assert.Equal(
            secondSalonId,
            Assert.Single(profile.Salons, link => link.IsPrimary).SalonId);
    }

    [Fact]
    public async Task AddService_LoadsOnlyServices()
    {
        var (profile, repository, ownerId) = CreateOwner();
        var hairServiceId = Guid.CreateVersion7();
        var hairServices = new FakeHairServiceRepository();
        hairServices.AddExisting(hairServiceId);
        var handler = new AddBarberProfileServiceHandler(
            repository,
            hairServices,
            new FakeUnitOfWork(),
            NullLogger<AddBarberProfileServiceHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new AddBarberProfileServiceCommand(profile.Id, hairServiceId, 1500m, 30),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["services"], repository.LoadCalls);
        Assert.Contains(profile.Services, service => service.HairServiceId == hairServiceId);
    }

    [Fact]
    public async Task RemoveService_LoadsOnlyServices()
    {
        var (profile, repository, ownerId) = CreateOwner();
        var hairServiceId = Guid.CreateVersion7();
        profile.AddService(hairServiceId, 1500m, 30);
        var handler = new RemoveBarberProfileServiceHandler(
            repository,
            NullLogger<RemoveBarberProfileServiceHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new RemoveBarberProfileServiceCommand(profile.Id, hairServiceId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["services"], repository.LoadCalls);
        Assert.Empty(profile.Services);
    }

    [Fact]
    public async Task AddSpecialization_LoadsOnlySpecializations()
    {
        var (profile, repository, ownerId) = CreateOwner();
        var specializationId = Guid.CreateVersion7();
        var specializations = new FakeSpecializationRepository();
        specializations.AddExisting(specializationId);
        var handler = new AddBarberProfileSpecializationHandler(
            repository,
            specializations,
            new FakeUnitOfWork(),
            NullLogger<AddBarberProfileSpecializationHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new AddBarberProfileSpecializationCommand(profile.Id, specializationId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["specializations"], repository.LoadCalls);
        Assert.Contains(
            profile.BarberSpecializations,
            link => link.SpecializationId == specializationId);
    }

    [Fact]
    public async Task AddTimeOff_LoadsOnlyTimeOffs()
    {
        var (profile, repository, ownerId) = CreateOwner();
        var handler = new AddBarberProfileTimeOffHandler(
            repository,
            NullLogger<AddBarberProfileTimeOffHandler>.Instance,
            new FakeCurrentUser(ownerId));
        var startAt = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

        var result = await handler.Handle(
            new AddBarberProfileTimeOffCommand(profile.Id, startAt, startAt.AddDays(2), "Trip"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["timeOffs"], repository.LoadCalls);
        Assert.Single(profile.TimeOffs);
    }

    [Fact]
    public async Task ReorderPhoto_LoadsOnlyPhotos()
    {
        var (profile, repository, ownerId) = CreateOwner();
        profile.AddPhoto("https://example.com/photo.jpg");
        var photo = Assert.Single(profile.PortfolioPhotos);
        var handler = new ReorderBarberProfilePhotoHandler(
            repository,
            NullLogger<ReorderBarberProfilePhotoHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new ReorderBarberProfilePhotoCommand(profile.Id, photo.Id, 3),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["photos"], repository.LoadCalls);
        Assert.Equal(3, photo.SortOrder);
    }

    [Fact]
    public async Task UnpublishPhoto_LoadsOnlyPhotos()
    {
        var (profile, repository, ownerId) = CreateOwner();
        profile.AddPhoto("https://example.com/photo.jpg");
        var photo = Assert.Single(profile.PortfolioPhotos);
        var handler = new UnpublishPortfolioPhotoHandler(
            repository,
            NullLogger<UnpublishPortfolioPhotoHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new UnpublishPortfolioPhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["photos"], repository.LoadCalls);
        Assert.False(photo.IsPublished);
    }

    [Fact]
    public async Task AddWorkingSchedule_LoadsSalonsAndSchedules()
    {
        var (profile, repository, ownerId) = CreateOwner();
        var salonId = Guid.CreateVersion7();
        profile.AddSalon(salonId);
        var handler = new AddBarberSalonWorkingScheduleHandler(
            repository,
            NullLogger<AddBarberSalonWorkingScheduleHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new AddBarberSalonWorkingScheduleCommand(
                profile.Id,
                salonId,
                DayOfWeek.Monday,
                new TimeOnly(9, 0),
                new TimeOnly(18, 0)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["salons+schedules"], repository.LoadCalls);
        var salon = Assert.Single(profile.Salons);
        Assert.Single(salon.WorkingSchedules);
    }

    [Fact]
    public async Task FindSalon_LoadsOwnerAndOneSalon()
    {
        var (profile, repository, ownerId) = CreateOwner();
        var salonId = Guid.CreateVersion7();
        profile.AddSalon(salonId, isPrimary: true);
        var salon = Assert.Single(profile.Salons);
        salon.AddWorkingSchedule(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(18, 0));
        salon.AddDayOff(DayOfWeek.Sunday);
        var mondaySchedule = Assert.Single(
            salon.WorkingSchedules,
            schedule => schedule.DayOfWeek == DayOfWeek.Monday);
        var handler = new FindBarberProfileSalonHandler(
            repository,
            NullLogger<FindBarberProfileSalonHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new FindBarberProfileSalonQuery(profile.Id, salonId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Доменная сущность с навигацией на профиль наружу не выходит: обработчик отвечает DTO.
        var response = Assert.IsType<BarberSalonResponse>(result.Value);
        Assert.Equal(salon.Id, response.Id);
        Assert.Equal(profile.Id, response.BarberProfileId);
        Assert.Equal(salonId, response.SalonId);
        Assert.True(response.IsPrimary);
        Assert.Equal(2, response.WorkingSchedules.Count);

        var monday = Assert.Single(
            response.WorkingSchedules,
            schedule => schedule.DayOfWeek == DayOfWeek.Monday);
        Assert.Equal(mondaySchedule.Id, monday.Id);
        Assert.Equal(new TimeOnly(9, 0), monday.StartTime);
        Assert.Equal(new TimeOnly(18, 0), monday.EndTime);
        Assert.False(monday.IsDayOff);

        var sunday = Assert.Single(
            response.WorkingSchedules,
            schedule => schedule.DayOfWeek == DayOfWeek.Sunday);
        Assert.True(sunday.IsDayOff);
        Assert.Equal(["owner", "salon"], repository.LoadCalls);
    }

    [Fact]
    public async Task AddSalon_ForStranger_StopsBeforeReferenceCheck()
    {
        var (profile, repository, _) = CreateOwner();
        var salonId = Guid.CreateVersion7();
        var salons = new FakeSalonRepository();
        salons.AddExisting(salonId);
        var handler = new AddBarberProfileSalonHandler(
            repository,
            salons,
            new FakeUnitOfWork(),
            NullLogger<AddBarberProfileSalonHandler>.Instance,
            new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(
            new AddBarberProfileSalonCommand(profile.Id, salonId, IsPrimary: false),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(["salons"], repository.LoadCalls);
        Assert.Empty(profile.Salons);
    }
}
