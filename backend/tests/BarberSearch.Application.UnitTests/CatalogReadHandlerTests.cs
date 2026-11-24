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
        repository.AddActive(CreateSpecialization("Alpha", "First"));
        repository.AddActive(CreateSpecialization("Zeta", null));
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
            },
            row =>
            {
                Assert.Equal("Zeta", row.Name);
                Assert.Null(row.Description);
            });
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
        var repository = new FakeHairServiceRepository();
        repository.AddActive(CreateHairService("Fade", ServiceCategory.Haircut, 60, "A fade"));
        repository.AddActive(CreateHairService("Beard Trim", ServiceCategory.Beard, 30, null));
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
                Assert.Equal(60, row.DefaultDurationMinutes);
                Assert.Equal("A fade", row.Description);
            },
            row =>
            {
                Assert.Equal("Beard Trim", row.Name);
                Assert.Equal(ServiceCategory.Beard, row.Category);
                Assert.Equal(30, row.DefaultDurationMinutes);
                Assert.Null(row.Description);
            });
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
        string? description)
    {
        var created = HairService.Create(name, category, defaultDurationMinutes, description);
        Assert.True(created.IsSuccess);

        return created.Value;
    }
}
