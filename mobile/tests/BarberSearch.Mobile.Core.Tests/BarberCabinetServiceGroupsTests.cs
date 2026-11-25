using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.HairServices;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки раскладки прайса кабинета по специализациям — зеркало фильтра «Услуга».
/// </summary>
/// <remarks>
/// Функция чистая: проверяется без раздела и сети. Кабинет не пересортировывает прайс,
/// поэтому группы идут в порядке первого появления в ответе API, строки внутри группы
/// сохраняют порядок API, а скрытые услуги остаются в своих группах.
/// </remarks>
public sealed class BarberCabinetServiceGroupsTests
{
    private const string OtherServicesTitle = "Прочие услуги";

    [Fact]
    public void Build_HiddenServices_StayInTheirGroupsInApiOrder()
    {
        // API отдаёт скрытую услугу первой: кабинет показывает прайс целиком и не сортирует.
        var groups = BarberCabinetServiceGroups.Build(
            [
                Item("Балаяж", "Окрашивание", isActive: false),
                Item("Фейд классический", "Фейд"),
                Item("Стрижка машинкой", "Стрижка"),
                Item("Фейд с выбриванием", "Фейд"),
            ],
            OtherServicesTitle);

        Assert.Equal(
            ["Окрашивание", "Фейд", "Стрижка"],
            groups.Select(group => group.Title));

        // Маркер «• » подсказывает, что под заголовком идёт перечисление услуг.
        Assert.Equal("• Окрашивание", groups[0].DisplayTitle);

        var coloring = Assert.Single(groups[0].Services);
        Assert.True(coloring.IsHidden);
        Assert.Equal("Балаяж", coloring.Name);

        // Чередование API не разрывает группу и не меняет порядок строк в ней.
        Assert.Equal(
            ["Фейд классический", "Фейд с выбриванием"],
            groups[1].Services.Select(service => service.Name));
    }

    [Fact]
    public void Build_BlankSpecialization_GoesToTrailingOtherServicesGroup()
    {
        var groups = BarberCabinetServiceGroups.Build(
            [
                Item("Стрижка наголо", " "),
                Item("Фейд классический", "Фейд"),
            ],
            OtherServicesTitle);

        Assert.Equal(["Фейд", OtherServicesTitle], groups.Select(group => group.Title));
        Assert.Equal(
            ["Стрижка наголо"],
            groups[1].Services.Select(service => service.Name));
    }

    [Fact]
    public void Build_NoServices_ReturnsNoGroups()
    {
        var groups = BarberCabinetServiceGroups.Build([], OtherServicesTitle);

        Assert.Empty(groups);
    }

    private static BarberServiceItem Item(
        string name,
        string specializationName,
        bool isActive = true) =>
        new(Service(name, specializationName, isActive));

    private static BarberProfileServiceResponse Service(
        string name,
        string specializationName,
        bool isActive) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            name,
            ServiceCategory.Haircut,
            30,
            2400m,
            45,
            isActive,
            Guid.CreateVersion7(),
            specializationName);
}
