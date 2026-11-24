using BarberSearch.Application.HairServices.CreateHairService;
using BarberSearch.Application.HairServices.UpdateHairService;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.HairServices;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Запись услуги каталога: существование специализации проверяется до сохранения,
/// а смена специализации — обычная правка, категория услуги выводится из неё.
/// </summary>
public class HairServiceWriteHandlerTests
{
    [Fact]
    public async Task Create_WithUnknownSpecialization_ReturnsNotFoundAndDoesNotAdd()
    {
        var repository = new FakeHairServiceRepository();
        var handler = new CreateHairServiceHandler(
            repository,
            new FakeSpecializationRepository(),
            NullLogger<CreateHairServiceHandler>.Instance);

        var result = await handler.Handle(
            new CreateHairServiceCommand("Fade", Guid.CreateVersion7(), 45, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("hairService.specialization.notFound", result.Error.Errors[0].Code);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task Create_WithEmptySpecialization_ReturnsValidationAndDoesNotAdd()
    {
        var repository = new FakeHairServiceRepository();
        var handler = new CreateHairServiceHandler(
            repository,
            new FakeSpecializationRepository(),
            NullLogger<CreateHairServiceHandler>.Instance);

        var result = await handler.Handle(
            new CreateHairServiceCommand("Fade", Guid.Empty, 45, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("hairService.specializationId", result.Error.Errors[0].Code);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task Create_WithExistingSpecialization_AddsServiceWithIt()
    {
        var specializationId = Guid.CreateVersion7();
        var specializations = new FakeSpecializationRepository();
        specializations.AddExisting(specializationId);
        var repository = new FakeHairServiceRepository();
        var handler = new CreateHairServiceHandler(
            repository,
            specializations,
            NullLogger<CreateHairServiceHandler>.Instance);

        var result = await handler.Handle(
            new CreateHairServiceCommand("Fade", specializationId, 45, "A fade"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var added = Assert.Single(repository.Added);
        Assert.Equal(result.Value, added.Id);
        Assert.Equal(specializationId, added.SpecializationId);
    }

    [Fact]
    public async Task Update_WithUnknownSpecialization_ReturnsNotFoundAndDoesNotSave()
    {
        var repository = new FakeHairServiceRepository();
        var hairService = CreateHairService(repository);
        var handler = new UpdateHairServiceHandler(
            repository,
            new FakeSpecializationRepository(),
            NullLogger<UpdateHairServiceHandler>.Instance);

        var result = await handler.Handle(
            new UpdateHairServiceCommand(
                hairService.Id,
                hairService.Name,
                null,
                45,
                Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("hairService.specialization.notFound", result.Error.Errors[0].Code);
        Assert.Empty(repository.Updated);
    }

    [Fact]
    public async Task Update_WithExistingSpecialization_SwitchesSpecialization()
    {
        var repository = new FakeHairServiceRepository();
        var hairService = CreateHairService(repository);
        var newSpecializationId = Guid.CreateVersion7();
        var specializations = new FakeSpecializationRepository();
        specializations.AddExisting(newSpecializationId);
        var handler = new UpdateHairServiceHandler(
            repository,
            specializations,
            NullLogger<UpdateHairServiceHandler>.Instance);

        var result = await handler.Handle(
            new UpdateHairServiceCommand(
                hairService.Id,
                "Renamed Fade",
                "New description",
                60,
                newSpecializationId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = Assert.Single(repository.Updated);
        Assert.Equal("Renamed Fade", updated.Name);
        Assert.Equal(60, updated.DefaultDurationMinutes);
        Assert.Equal(newSpecializationId, updated.SpecializationId);
    }

    private static HairService CreateHairService(FakeHairServiceRepository repository)
    {
        var hairService = HairService.Create(
            "Fade",
            Guid.CreateVersion7(),
            45,
            null).Value;

        repository.Store(hairService);

        return hairService;
    }
}
