using BarberSearch.Application.BarberProfiles.AddBarberProfileSalon;
using BarberSearch.Application.BarberProfiles.AddBarberProfileService;
using BarberSearch.Application.BarberProfiles.AddBarberProfileSpecialization;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Ссылочная целостность между агрегатами: перед добавлением дочерней сущности
/// проверяется существование салона, услуги, специализации и профиля барбера.
/// </summary>
public class ReferenceIntegrityTests
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

    [Fact]
    public async Task AddSalon_WithUnknownSalon_ReturnsNotFoundAndDoesNotAdd()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = new AddBarberProfileSalonHandler(
            profiles,
            new FakeSalonRepository(),
            new FakeUnitOfWork(),
            NullLogger<AddBarberProfileSalonHandler>.Instance,
            new FakeCurrentUser(ownerId));
        var salonId = Guid.CreateVersion7();

        var result = await handler.Handle(
            new AddBarberProfileSalonCommand(profile.Id, salonId, false),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("barberProfile.addSalon.salonNotFound", result.Error.Errors[0].Code);
        Assert.Empty(profile.Salons);
    }

    [Fact]
    public async Task AddSalon_WithExistingSalon_AddsSalon()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var salonId = Guid.CreateVersion7();
        var salons = new FakeSalonRepository();
        salons.AddExisting(salonId);
        var handler = new AddBarberProfileSalonHandler(
            profiles,
            salons,
            new FakeUnitOfWork(),
            NullLogger<AddBarberProfileSalonHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new AddBarberProfileSalonCommand(profile.Id, salonId, false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var salon = Assert.Single(profile.Salons);
        Assert.Equal(salonId, salon.SalonId);
    }

    [Fact]
    public async Task AddService_WithUnknownHairService_ReturnsNotFoundAndDoesNotAdd()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = new AddBarberProfileServiceHandler(
            profiles,
            new FakeHairServiceRepository(),
            new FakeUnitOfWork(),
            NullLogger<AddBarberProfileServiceHandler>.Instance,
            new FakeCurrentUser(ownerId));
        var hairServiceId = Guid.CreateVersion7();

        var result = await handler.Handle(
            new AddBarberProfileServiceCommand(profile.Id, hairServiceId, 1500m, 30),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("barberProfile.addService.hairServiceNotFound", result.Error.Errors[0].Code);
        Assert.Empty(profile.Services);
    }

    [Fact]
    public async Task AddService_WithExistingHairService_AddsService()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var hairServiceId = Guid.CreateVersion7();
        var hairServices = new FakeHairServiceRepository();
        hairServices.AddExisting(hairServiceId);
        var handler = new AddBarberProfileServiceHandler(
            profiles,
            hairServices,
            new FakeUnitOfWork(),
            NullLogger<AddBarberProfileServiceHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new AddBarberProfileServiceCommand(profile.Id, hairServiceId, 1500m, 30),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var service = Assert.Single(profile.Services);
        Assert.Equal(hairServiceId, service.HairServiceId);
    }

    [Fact]
    public async Task AddSpecialization_WithUnknownSpecialization_ReturnsNotFoundAndDoesNotAdd()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var handler = new AddBarberProfileSpecializationHandler(
            profiles,
            new FakeSpecializationRepository(),
            new FakeUnitOfWork(),
            NullLogger<AddBarberProfileSpecializationHandler>.Instance,
            new FakeCurrentUser(ownerId));
        var specializationId = Guid.CreateVersion7();

        var result = await handler.Handle(
            new AddBarberProfileSpecializationCommand(profile.Id, specializationId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal(
            "barberProfile.addSpecialization.specializationNotFound",
            result.Error.Errors[0].Code);
        Assert.Empty(profile.BarberSpecializations);
    }

    [Fact]
    public async Task AddSpecialization_WithExistingSpecialization_AddsSpecialization()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var specializationId = Guid.CreateVersion7();
        var specializations = new FakeSpecializationRepository();
        specializations.AddExisting(specializationId);
        var handler = new AddBarberProfileSpecializationHandler(
            profiles,
            specializations,
            new FakeUnitOfWork(),
            NullLogger<AddBarberProfileSpecializationHandler>.Instance,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new AddBarberProfileSpecializationCommand(profile.Id, specializationId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var specialization = Assert.Single(profile.BarberSpecializations);
        Assert.Equal(specializationId, specialization.SpecializationId);
    }
}
