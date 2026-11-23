using BarberSearch.Application.HairServices.GetHairServices;
using BarberSearch.Application.Specializations.GetSpecializations;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Specializations;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Чтение публичных справочников для фильтров витрины: маппинг, порядок и проброс сбоя.
/// </summary>
public class CatalogReadHandlerTests
{
    [Fact]
    public async Task GetSpecializations_MapsRowsInRepositoryOrder()
    {
        var repository = new FakeSpecializationRepository();
        var alpha = CreateSpecialization("Alpha", "First");
        var zeta = CreateSpecialization("Zeta", null);
        repository.AddActive(alpha);
        repository.AddActive(zeta);
        repository.AddCategoryCount(alpha.Id, ServiceCategory.Haircut, 2);
        repository.AddCategoryCount(alpha.Id, ServiceCategory.Beard, 1);
        repository.AddCategoryCount(zeta.Id, ServiceCategory.Coloring, 3);
        var handler = new GetSpecializationsHandler(repository);

        var result = await handler.Handle(
            new GetSpecializationsQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value,
            row =>
            {
                Assert.Equal("Alpha", row.Name);
                Assert.Equal("First", row.Description);
                Assert.Equal(ServiceCategory.Haircut, row.Category);
            },
            row =>
            {
                Assert.Equal("Zeta", row.Name);
                Assert.Null(row.Description);
                Assert.Equal(ServiceCategory.Coloring, row.Category);
            });
    }

    [Fact]
    public async Task GetSpecializations_FallsBackToOtherWhenNoActiveServices()
    {
        var repository = new FakeSpecializationRepository();
        repository.AddActive(CreateSpecialization("Alpha", null));
        var handler = new GetSpecializationsHandler(repository);

        var result = await handler.Handle(
            new GetSpecializationsQuery(),
            CancellationToken.None);

        // Выключенные услуги в счётчики не попадают, поэтому у направления без активных
        // услуг счётчиков нет вовсе — резолвер отдаёт Other.
        var row = Assert.Single(result.Value);
        Assert.Equal(ServiceCategory.Other, row.Category);
    }

    [Fact]
    public async Task GetSpecializations_EmptyCatalogueSkipsCategoryCounts()
    {
        var repository = new FakeSpecializationRepository();
        var handler = new GetSpecializationsHandler(repository);

        var result = await handler.Handle(
            new GetSpecializationsQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        Assert.Equal(0, repository.CategoryCountRequests);
    }

    [Fact]
    public async Task GetSpecializations_PropagatesCategoryCountFailure()
    {
        var repository = new FakeSpecializationRepository
        {
            CategoryCountsFailure = new Failure(Error.Validation("catalogue.counts", "Boom."))
        };
        repository.AddActive(CreateSpecialization("Alpha", null));
        var handler = new GetSpecializationsHandler(repository);

        var result = await handler.Handle(
            new GetSpecializationsQuery(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("catalogue.counts", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetSpecializations_PropagatesRepositoryFailure()
    {
        var repository = new FakeSpecializationRepository
        {
            GetActiveFailure = new Failure(Error.Validation("catalog.test", "Boom."))
        };
        var handler = new GetSpecializationsHandler(repository);

        var result = await handler.Handle(
            new GetSpecializationsQuery(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("catalog.test", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetHairServices_MapsRowsInRepositoryOrder()
    {
        var fadeDirection = Guid.CreateVersion7();
        var beardDirection = Guid.CreateVersion7();
        var repository = new FakeHairServiceRepository();
        repository.AddActive(
            CreateHairService("Fade", ServiceCategory.Haircut, 60, "A fade", fadeDirection));
        repository.AddActive(
            CreateHairService("Beard Trim", ServiceCategory.Beard, 30, null, beardDirection));
        var handler = new GetHairServicesHandler(repository);

        var result = await handler.Handle(
            new GetHairServicesQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value,
            row =>
            {
                Assert.Equal("Fade", row.Name);
                Assert.Equal(ServiceCategory.Haircut, row.Category);
                Assert.Equal(fadeDirection, row.SpecializationId);
                Assert.Equal(60, row.DefaultDurationMinutes);
                Assert.Equal("A fade", row.Description);
            },
            row =>
            {
                Assert.Equal("Beard Trim", row.Name);
                Assert.Equal(ServiceCategory.Beard, row.Category);
                Assert.Equal(beardDirection, row.SpecializationId);
                Assert.Equal(30, row.DefaultDurationMinutes);
                Assert.Null(row.Description);
            });
    }

    [Fact]
    public async Task GetHairServices_ForwardsSpecializationFilter()
    {
        var fadeDirection = Guid.CreateVersion7();
        var beardDirection = Guid.CreateVersion7();
        var repository = new FakeHairServiceRepository();
        repository.AddActive(
            CreateHairService("Fade", ServiceCategory.Haircut, 60, null, fadeDirection));
        repository.AddActive(
            CreateHairService("Beard Trim", ServiceCategory.Beard, 30, null, beardDirection));
        var handler = new GetHairServicesHandler(repository);

        var result = await handler.Handle(
            new GetHairServicesQuery(beardDirection),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value);
        Assert.Equal("Beard Trim", row.Name);
        Assert.Equal(beardDirection, repository.LastSpecializationId);
    }

    [Fact]
    public async Task GetHairServices_TreatsEmptySpecializationAsNoFilter()
    {
        var repository = new FakeHairServiceRepository();
        repository.AddActive(CreateHairService("Fade", ServiceCategory.Haircut, 60, null));
        var handler = new GetHairServicesHandler(repository);

        var result = await handler.Handle(
            new GetHairServicesQuery(Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Null(repository.LastSpecializationId);
    }

    [Fact]
    public async Task GetHairServices_PropagatesRepositoryFailure()
    {
        var repository = new FakeHairServiceRepository
        {
            GetActiveFailure = new Failure(Error.Conflict("catalog.test", "Boom."))
        };
        var handler = new GetHairServicesHandler(repository);

        var result = await handler.Handle(
            new GetHairServicesQuery(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("catalog.test", result.Error.Errors[0].Code);
    }

    private static Specialization CreateSpecialization(string name, string? description)
    {
        var created = Specialization.Create(name, description);
        Assert.True(created.IsSuccess);

        return created.Value;
    }

    private static HairService CreateHairService(
        string name,
        ServiceCategory category,
        int defaultDurationMinutes,
        string? description,
        Guid? specializationId = null)
    {
        var created = HairService.Create(
            name,
            category,
            specializationId ?? Guid.CreateVersion7(),
            defaultDurationMinutes,
            description);
        Assert.True(created.IsSuccess);

        return created.Value;
    }
}
