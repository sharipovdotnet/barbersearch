using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Domain.HairServices;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки раскладки услуг по специализациям — зеркало <c>groupHairServices</c> из веба.
/// </summary>
public sealed class SearchServiceGroupsTests
{
    private const string OtherTitle = "Прочие услуги";

    private static readonly Guid BarberId = Guid.Parse("b1000000-0000-4000-8000-000000000010");
    private static readonly Guid HairdresserId = Guid.Parse("b1000000-0000-4000-8000-000000000020");
    private static readonly Guid ColoristId = Guid.Parse("b1000000-0000-4000-8000-000000000030");
    private static readonly Guid UnknownId = Guid.Parse("b1000000-0000-4000-8000-0000000000ff");

    [Fact]
    public void Build_GroupsBySpecialization_SortsGroupsByNameAndKeepsApiOrderInside()
    {
        var hairdresser = Specialization(HairdresserId, "Парикмахер");
        var barber = Specialization(BarberId, "Барбер");
        var services = new[]
        {
            Service(ServiceId(1), "Укладка", HairdresserId),
            Service(ServiceId(2), "Стрижка", BarberId),
            Service(ServiceId(3), "Бритьё", BarberId),
        };

        var groups = SearchServiceGroups.Build(services, [hairdresser, barber], null, OtherTitle);

        // Порядок групп — по алфавиту названий, хотя API отдал специализации наоборот.
        Assert.Equal(["Барбер", "Парикмахер"], groups.Select(group => group.Title));

        // Внутри группы — порядок API, а не алфавит услуг.
        Assert.Equal(["Стрижка", "Бритьё"], groups[0].Select(option => option.DisplayText));
        Assert.Equal(["Укладка"], groups[1].Select(option => option.DisplayText));

        // Каждая строка помнит свою специализацию: по ней экран решает, совместим ли выбор.
        Assert.All(groups[0], option => Assert.Equal(BarberId, option.SpecializationId));
        Assert.All(groups[1], option => Assert.Equal(HairdresserId, option.SpecializationId));
    }

    [Fact]
    public void Build_EqualNames_SortsGroupById()
    {
        var first = Specialization(Guid.Parse("b1000000-0000-4000-8000-000000000001"), "Барбер");
        var second = Specialization(Guid.Parse("b1000000-0000-4000-8000-000000000002"), "Барбер");
        var services = new[]
        {
            Service(ServiceId(1), "Стрижка второй", second.Id),
            Service(ServiceId(2), "Стрижка первой", first.Id),
        };

        // В API вторая специализация идёт раньше: при равных именах решает Id, а не ответ.
        var groups = SearchServiceGroups.Build(services, [second, first], null, OtherTitle);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Equal("Барбер", group.Title));
        Assert.Equal("Стрижка первой", groups[0][0].DisplayText);
        Assert.Equal("Стрижка второй", groups[1][0].DisplayText);
    }

    [Fact]
    public void Build_NameCaseDiffers_SortsIgnoringCase()
    {
        var barber = Specialization(BarberId, "барбер");
        var hairdresser = Specialization(HairdresserId, "Парикмахер");
        var services = new[]
        {
            Service(ServiceId(1), "Укладка", HairdresserId),
            Service(ServiceId(2), "Стрижка", BarberId),
        };

        // Веб сравнивает имена русской локалью без учёта регистра: «барбер» раньше «Парикмахер».
        var groups = SearchServiceGroups.Build(services, [barber, hairdresser], null, OtherTitle);

        Assert.Equal(["барбер", "Парикмахер"], groups.Select(group => group.Title));
    }

    [Fact]
    public void Build_EmptyGroups_AreNotReturned()
    {
        var colorist = Specialization(ColoristId, "Колорист");
        var barber = Specialization(BarberId, "Барбер");
        var services = new[] { Service(ServiceId(1), "Стрижка", BarberId) };

        var groups = SearchServiceGroups.Build(services, [colorist, barber], null, OtherTitle);

        // Специализация без услуг — не группа: пустой заголовок в списке не нужен.
        Assert.Equal(["Барбер"], groups.Select(group => group.Title));
        Assert.Single(groups[0]);
    }

    [Fact]
    public void Build_UnknownSpecializations_GoToLastOtherGroupInInputOrder()
    {
        var barber = Specialization(BarberId, "Барбер");
        var services = new[]
        {
            Service(ServiceId(1), "Стрижка", BarberId),
            Service(ServiceId(2), "Загадочная услуга", UnknownId),
            Service(ServiceId(3), "Услуга без специализации", Guid.Empty),
        };

        var groups = SearchServiceGroups.Build(services, [barber], null, OtherTitle);

        Assert.Equal(["Барбер", OtherTitle], groups.Select(group => group.Title));
        Assert.Equal(
            ["Загадочная услуга", "Услуга без специализации"],
            groups[1].Select(option => option.DisplayText));
    }

    [Fact]
    public void Build_SpecializationSelected_ReturnsOnlyItsServicesWithoutOtherGroup()
    {
        var barber = Specialization(BarberId, "Барбер");
        var hairdresser = Specialization(HairdresserId, "Парикмахер");
        var services = new[]
        {
            Service(ServiceId(1), "Укладка", HairdresserId),
            Service(ServiceId(2), "Стрижка", BarberId),
            Service(ServiceId(3), "Загадочная услуга", UnknownId),
        };

        var groups = SearchServiceGroups.Build(services, [barber, hairdresser], BarberId, OtherTitle);

        // Чужие и «прочие» услуги в суженный список не попадают: хвостовой группы здесь нет.
        var group = Assert.Single(groups);
        Assert.Equal("Барбер", group.Title);
        Assert.Equal(["Стрижка"], group.Select(option => option.DisplayText));
    }

    [Fact]
    public void Build_SpecializationSelectedWithoutServices_ReturnsEmpty()
    {
        var barber = Specialization(BarberId, "Барбер");
        var hairdresser = Specialization(HairdresserId, "Парикмахер");
        var services = new[] { Service(ServiceId(1), "Укладка", HairdresserId) };

        var groups = SearchServiceGroups.Build(services, [barber, hairdresser], BarberId, OtherTitle);

        Assert.Empty(groups);
    }

    [Fact]
    public void Build_NoSpecializations_PutsEveryServiceIntoOtherGroup()
    {
        var services = new[]
        {
            Service(ServiceId(1), "Стрижка", BarberId),
            Service(ServiceId(2), "Укладка", HairdresserId),
        };

        var groups = SearchServiceGroups.Build(services, [], null, OtherTitle);

        // Справочник специализаций пуст — известных групп нет, но услуги не теряются.
        var group = Assert.Single(groups);
        Assert.Equal(OtherTitle, group.Title);
        Assert.Equal(["Стрижка", "Укладка"], group.Select(option => option.DisplayText));
    }

    [Fact]
    public void Build_ExplicitNullSpecialization_ReturnsAllGroupsWithOtherTail()
    {
        Guid? specializationId = null;
        var barber = Specialization(BarberId, "Барбер");
        var services = new[]
        {
            Service(ServiceId(1), "Стрижка", BarberId),
            Service(ServiceId(2), "Загадочная услуга", UnknownId),
        };

        var groups = SearchServiceGroups.Build(services, [barber], specializationId, OtherTitle);

        Assert.Equal(["Барбер", OtherTitle], groups.Select(group => group.Title));
        Assert.Single(groups[0]);
        Assert.Single(groups[1]);
    }

    [Fact]
    public void Build_DoesNotMutateInputLists()
    {
        var barber = Specialization(BarberId, "Барбер");
        var hairdresser = Specialization(HairdresserId, "Парикмахер");
        var services = new[]
        {
            Service(ServiceId(1), "Укладка", HairdresserId),
            Service(ServiceId(2), "Стрижка", BarberId),
            Service(ServiceId(3), "Загадочная услуга", UnknownId),
        };
        var specializations = new[] { hairdresser, barber };

        _ = SearchServiceGroups.Build(services, specializations, null, OtherTitle);

        // Раскладка — чистая функция: справочники принадлежат вызывающему коду и не меняются.
        Assert.Equal(
            [ServiceId(1), ServiceId(2), ServiceId(3)],
            services.Select(service => service.Id));
        Assert.Equal([hairdresser, barber], specializations);
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        var services = new[] { Service(ServiceId(1), "Стрижка", BarberId) };

        Assert.Throws<ArgumentNullException>(
            () => SearchServiceGroups.Build(null!, [], null, OtherTitle));
        Assert.Throws<ArgumentNullException>(
            () => SearchServiceGroups.Build(services, null!, null, OtherTitle));
        Assert.Throws<ArgumentNullException>(
            () => SearchServiceGroups.Build(services, [], null, null!));
    }

    private static SpecializationResponse Specialization(Guid id, string name) => new(id, name, null);

    private static HairServiceResponse Service(Guid id, string name, Guid specializationId) =>
        new(id, name, ServiceCategory.Haircut, 40, null, specializationId);

    private static Guid ServiceId(int number) =>
        Guid.Parse($"c1000000-0000-4000-8000-{number:D12}");
}
