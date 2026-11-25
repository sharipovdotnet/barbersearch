using BarberSearch.Domain.HairServices;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки раскладки справочника услуг по специализациям для списка «Добавить услугу».
/// </summary>
/// <remarks>
/// Порядок групп — решение для мастера, а не алфавит веба: категория в каталожном порядке,
/// внутри категории — название специализации. Внутри группы строки остаются в порядке API,
/// а услуга с неизвестной специализацией не теряется и уходит в «Прочие услуги».
/// </remarks>
public sealed class BarberServiceCatalogGroupsTests
{
    private const string OtherServicesTitle = "Прочие услуги";

    private static readonly Guid FadeId = Guid.Parse("9e100000-0000-4000-8000-000000000101");
    private static readonly Guid BarberId = Guid.Parse("9e100000-0000-4000-8000-000000000102");
    private static readonly Guid TaperId = Guid.Parse("9e100000-0000-4000-8000-000000000103");
    private static readonly Guid StylingId = Guid.Parse("9e100000-0000-4000-8000-000000000104");
    private static readonly Guid MissingId = Guid.Parse("9e100000-0000-4000-8000-000000000105");

    private static readonly Guid FadeServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000201");
    private static readonly Guid BarberServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000202");
    private static readonly Guid TaperServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000203");
    private static readonly Guid StylingServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000204");
    private static readonly Guid LooseServiceId = Guid.Parse("9e100000-0000-4000-8000-000000000205");

    [Fact]
    public void Build_CatalogueOrderOfCategories_BeatsAlphabeticalOrderOfNames()
    {
        var groups = BarberServiceCatalogGroups.Build(
            [
                Service(StylingServiceId, "Укладка", StylingId),
                Service(FadeServiceId, "Фейд", FadeId),
            ],
            [
                Specialization(StylingId, "Укладка", ServiceCategory.Styling),
                Specialization(FadeId, "Фейд", ServiceCategory.Haircut),
            ],
            OtherServicesTitle);

        // «Укладка» стоит в ответе первой и по алфавиту идёт после «Фейда», но стрижка —
        // первая категория каталога, поэтому группа «Фейд» идёт сверху.
        Assert.Equal(["Фейд", "Укладка"], groups.Select(group => group.Title));
    }

    [Fact]
    public void Build_SameCategory_SortsSpecializationsByNameInRussianCollation()
    {
        var groups = BarberServiceCatalogGroups.Build(
            [
                Service(TaperServiceId, "Тейпер", TaperId),
                Service(BarberServiceId, "Классика", BarberId),
            ],
            [
                Specialization(TaperId, "Тейпер", ServiceCategory.Haircut),
                Specialization(BarberId, "Барбер", ServiceCategory.Haircut),
            ],
            OtherServicesTitle);

        Assert.Equal(["Барбер", "Тейпер"], groups.Select(group => group.Title));
    }

    [Fact]
    public void Build_KeepsApiOrderInsideOneGroup()
    {
        var groups = BarberServiceCatalogGroups.Build(
            [
                Service(TaperServiceId, "Тейпер", FadeId),
                Service(FadeServiceId, "Фейд", FadeId),
                Service(BarberServiceId, "Классика", FadeId),
            ],
            [Specialization(FadeId, "Фейд", ServiceCategory.Haircut)],
            OtherServicesTitle);

        var group = Assert.Single(groups);

        Assert.Equal(["Тейпер", "Фейд", "Классика"], group.Services.Select(service => service.Name));
    }

    [Fact]
    public void Build_ServiceWithoutKnownSpecialization_GoesToTrailingOtherGroup()
    {
        var groups = BarberServiceCatalogGroups.Build(
            [
                Service(LooseServiceId, "Стрижка машинкой", MissingId),
                Service(FadeServiceId, "Фейд", FadeId),
                Service(BarberServiceId, "Классика", Guid.Empty),
            ],
            [Specialization(FadeId, "Фейд", ServiceCategory.Haircut)],
            OtherServicesTitle);

        Assert.Equal(["Фейд", OtherServicesTitle], groups.Select(group => group.Title));
        Assert.Equal(
            ["Стрижка машинкой", "Классика"],
            groups[1].Services.Select(service => service.Name));
    }

    [Fact]
    public void Build_UnknownCategory_GoesAfterEveryCatalogueCategory()
    {
        var groups = BarberServiceCatalogGroups.Build(
            [
                Service(StylingServiceId, "Укладка", StylingId),
                Service(LooseServiceId, "Прочее", MissingId),
            ],
            [
                Specialization(MissingId, "Ателье", (ServiceCategory)99),
                Specialization(StylingId, "Укладка", ServiceCategory.Styling),
            ],
            OtherServicesTitle);

        Assert.Equal(["Укладка", "Ателье"], groups.Select(group => group.Title));
    }

    [Fact]
    public void Build_EqualNamesAndCategories_SortsById()
    {
        var groups = BarberServiceCatalogGroups.Build(
            [
                Service(StylingServiceId, "Укладка", StylingId),
                Service(FadeServiceId, "Фейд", FadeId),
            ],
            [
                Specialization(StylingId, "Фейд", ServiceCategory.Haircut),
                Specialization(FadeId, "Фейд", ServiceCategory.Haircut),
            ],
            OtherServicesTitle);

        Assert.Equal([FadeId, StylingId], groups.Select(group => group.Services[0].SpecializationId));
    }

    [Fact]
    public void Build_EmptyCatalogue_ReturnsNoGroups()
    {
        Assert.Empty(BarberServiceCatalogGroups.Build([], [], OtherServicesTitle));
    }

    /// <summary>
    /// Услуга справочника для раскладки.
    /// </summary>
    /// <param name="id">Идентификатор услуги.</param>
    /// <param name="name">Название.</param>
    /// <param name="specializationId">Специализация услуги.</param>
    /// <returns>Строка справочника.</returns>
    private static BarberServiceCatalogItem Service(Guid id, string name, Guid specializationId) =>
        new(BarberCabinetData.HairService(
            id,
            name,
            category: ServiceCategory.Haircut,
            specializationId: specializationId));

    /// <summary>
    /// Специализация справочника с категорией.
    /// </summary>
    /// <param name="id">Идентификатор.</param>
    /// <param name="name">Название.</param>
    /// <param name="category">Категория направления.</param>
    /// <returns>Строка справочника специализаций.</returns>
    private static BarberSearch.Contracts.Specializations.GetSpecializations.SpecializationResponse
        Specialization(Guid id, string name, ServiceCategory category) =>
        BarberCabinetData.Specialization(id, name, category: category);
}
