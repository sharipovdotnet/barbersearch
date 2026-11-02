using BarberSearch.Application.Common;
using BarberSearch.Application.HairServices.Admin.GetAdminHairServices;
using BarberSearch.Application.Salons.Admin.GetAdminSalons;
using BarberSearch.Application.Specializations.Admin.GetAdminSpecializations;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.HairServices.Admin;
using BarberSearch.Contracts.Salons.Admin;
using BarberSearch.Contracts.Specializations.Admin;
using BarberSearch.Domain.HairServices;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Админское чтение каталога: разбор статуса и поиска, публичные коды ошибок, маппинг строк
/// и проброс сбоя репозитория.
/// </summary>
/// <remarks>
/// Справочники отдаются целиком, поэтому у запроса нет курсора и размера страницы —
/// проверять здесь нечего. Фейки фильтруют и сортируют по тем же правилам, что и реальные
/// репозитории, и запоминают переданные параметры: иначе «обработчик тримит поиск» нельзя
/// было бы отличить от «репозиторий тримит поиск».
/// </remarks>
public class AdminCatalogueReadHandlerTests
{
    [Theory]
    [InlineData(null, AdminCatalogueStatus.All)]
    [InlineData("", AdminCatalogueStatus.All)]
    [InlineData("all", AdminCatalogueStatus.All)]
    [InlineData(" Active ", AdminCatalogueStatus.Active)]
    [InlineData("HIDDEN", AdminCatalogueStatus.Hidden)]
    public async Task Hair_services_status_is_parsed_case_insensitively(
        string? status,
        AdminCatalogueStatus expected)
    {
        var repository = new FakeHairServiceRepository();
        var handler = new GetAdminHairServicesHandler(repository);

        var result = await handler.Handle(
            new GetAdminHairServicesQuery(status, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, repository.LastStatus);
    }

    [Fact]
    public async Task Unknown_status_is_rejected_on_every_catalogue_endpoint()
    {
        var hairServices = new FakeHairServiceRepository();
        var specializations = new FakeSpecializationRepository();
        var salons = new FakeSalonRepository();

        var hairServiceResult = await new GetAdminHairServicesHandler(hairServices).Handle(
            new GetAdminHairServicesQuery("wrong", null),
            CancellationToken.None);
        var specializationResult = await new GetAdminSpecializationsHandler(specializations).Handle(
            new GetAdminSpecializationsQuery("0", null),
            CancellationToken.None);
        var salonResult = await new GetAdminSalonsHandler(salons).Handle(
            new GetAdminSalonsQuery("up", null),
            CancellationToken.None);

        Assert.True(hairServiceResult.IsFailure);
        Assert.True(specializationResult.IsFailure);
        Assert.True(salonResult.IsFailure);

        Assert.All(
            new[]
            {
                hairServiceResult.Error,
                specializationResult.Error,
                salonResult.Error
            },
            failure => Assert.Equal("admin.catalogue.status", failure.Errors[0].Code));
    }

    [Fact]
    public async Task Search_is_trimmed_and_blank_search_is_dropped()
    {
        var repository = new FakeHairServiceRepository();
        var handler = new GetAdminHairServicesHandler(repository);

        await handler.Handle(
            new GetAdminHairServicesQuery("all", "  "),
            CancellationToken.None);

        Assert.Null(repository.LastSearch);

        await handler.Handle(
            new GetAdminHairServicesQuery("all", "  Fade  "),
            CancellationToken.None);

        Assert.Equal("Fade", repository.LastSearch);
    }

    [Fact]
    public async Task Search_longer_than_100_is_rejected_and_exactly_100_is_allowed()
    {
        var repository = new FakeSpecializationRepository();
        var handler = new GetAdminSpecializationsHandler(repository);

        var rejected = await handler.Handle(
            new GetAdminSpecializationsQuery("all", new string('a', 101)),
            CancellationToken.None);

        Assert.True(rejected.IsFailure);
        Assert.Equal("admin.catalogue.search", rejected.Error.Errors[0].Code);

        var allowed = await handler.Handle(
            new GetAdminSpecializationsQuery("all", new string('a', 100)),
            CancellationToken.None);

        Assert.True(allowed.IsSuccess);
        Assert.Equal(new string('a', 100), repository.LastSearch);
    }

    [Fact]
    public async Task Hair_service_rows_keep_is_active_and_usage_count()
    {
        var id = Guid.NewGuid();
        var repository = new FakeHairServiceRepository();
        repository.AddAdminRow(new AdminHairServiceResponse(
            id,
            "Hidden Fade",
            ServiceCategory.Beard,
            45,
            "Описание",
            IsActive: false,
            UsageCount: 7));
        var handler = new GetAdminHairServicesHandler(repository);

        var result = await handler.Handle(
            new GetAdminHairServicesQuery("all", null),
            CancellationToken.None);

        var row = Assert.Single(result.Value);
        Assert.Equal(id, row.Id);
        Assert.Equal("Hidden Fade", row.Name);
        Assert.Equal(ServiceCategory.Beard, row.Category);
        Assert.Equal(45, row.DefaultDurationMinutes);
        Assert.Equal("Описание", row.Description);
        Assert.False(row.IsActive);
        Assert.Equal(7, row.UsageCount);
    }

    [Fact]
    public async Task Salon_rows_keep_every_edit_field()
    {
        var id = Guid.NewGuid();
        var repository = new FakeSalonRepository();
        repository.AddAdminRow(new AdminSalonResponse(
            id,
            "Nord",
            "ул. Северная, 2",
            "Санкт-Петербург",
            "190000",
            "Russia",
            "Europe/Moscow",
            59.9386,
            30.3141,
            "+78120000000",
            "https://nord.example/",
            IsActive: true,
            UsageCount: 3));
        var handler = new GetAdminSalonsHandler(repository);

        var result = await handler.Handle(
            new GetAdminSalonsQuery("active", null),
            CancellationToken.None);

        var row = Assert.Single(result.Value);
        Assert.Equal("Nord", row.Name);
        Assert.Equal("Санкт-Петербург", row.City);
        Assert.Equal("190000", row.PostalCode);
        Assert.Equal("Europe/Moscow", row.TimeZoneId);
        Assert.Equal(59.9386, row.Latitude);
        Assert.Equal(30.3141, row.Longitude);
        Assert.Equal("+78120000000", row.PhoneNumber);
        Assert.Equal("https://nord.example/", row.WebsiteUrl);
        Assert.True(row.IsActive);
        Assert.Equal(3, row.UsageCount);
    }

    [Fact]
    public async Task Repository_failure_is_propagated_from_every_endpoint()
    {
        var failure = new Failure(Error.Validation("catalogue.test", "Boom."));

        var hairServiceResult = await new GetAdminHairServicesHandler(
                new FakeHairServiceRepository { GetAdminListFailure = failure })
            .Handle(new GetAdminHairServicesQuery("all", null), CancellationToken.None);
        var specializationResult = await new GetAdminSpecializationsHandler(
                new FakeSpecializationRepository { GetAdminListFailure = failure })
            .Handle(new GetAdminSpecializationsQuery("all", null), CancellationToken.None);
        var salonResult = await new GetAdminSalonsHandler(
                new FakeSalonRepository { GetAdminListFailure = failure })
            .Handle(new GetAdminSalonsQuery("all", null), CancellationToken.None);

        Assert.True(hairServiceResult.IsFailure);
        Assert.True(specializationResult.IsFailure);
        Assert.True(salonResult.IsFailure);

        Assert.All(
            new[]
            {
                hairServiceResult.Error,
                specializationResult.Error,
                salonResult.Error
            },
            result => Assert.Equal("catalogue.test", result.Errors[0].Code));
    }
}
