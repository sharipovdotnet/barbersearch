using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.HairServices;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки раскладки публичного прайса по специализациям — зеркало фильтра «Услуга».
/// </summary>
/// <remarks>
/// Функция чистая, поэтому проверяется без карточки и сети: группа — это заголовок
/// специализации и её строки, группы идут в порядке первого появления, строки внутри группы
/// сохраняют исходный порядок, а строки без специализации замыкают список «Прочими услугами».
/// </remarks>
public sealed class BarberServiceGroupsTests
{
    private const string OtherServicesTitle = "Прочие услуги";

    [Fact]
    public void Build_InterleavedSpecializations_SplitsRowsIntoOneHeadingInFirstAppearanceOrder()
    {
        // API чередует специализации: у каждой должен быть ровно один заголовок.
        var groups = BarberServiceGroups.Build(
            [
                Option("Фейд классический", "Фейд"),
                Option("Оформление бороды", "Борода"),
                Option("Фейд с выбриванием", "Фейд"),
                Option("Балаяж", "Окрашивание"),
            ],
            OtherServicesTitle);

        Assert.Equal(
            ["Фейд", "Борода", "Окрашивание"],
            groups.Select(group => group.Title));

        // Маркер «• » подсказывает, что под заголовком идёт перечисление услуг.
        Assert.Equal("• Фейд", groups[0].DisplayTitle);

        // Строки одной специализации собраны вместе и сохраняют порядок исходного списка.
        Assert.Equal(
            ["Фейд классический", "Фейд с выбриванием"],
            groups[0].Services.Select(service => service.Name));
        Assert.Equal(
            ["Оформление бороды"],
            groups[1].Services.Select(service => service.Name));
        Assert.Equal(
            ["Балаяж"],
            groups[2].Services.Select(service => service.Name));
    }

    [Fact]
    public void Build_BlankSpecialization_GoesToTrailingOtherServicesGroup()
    {
        // Пустая строка и одни пробелы — не специализация: такие строки нельзя терять.
        var groups = BarberServiceGroups.Build(
            [
                Option("Стрижка наголо", "  "),
                Option("Фейд классический", "Фейд"),
                Option("Стрижка ножницами", string.Empty),
            ],
            OtherServicesTitle);

        Assert.Equal(["Фейд", OtherServicesTitle], groups.Select(group => group.Title));
        Assert.Equal(
            ["Стрижка наголо", "Стрижка ножницами"],
            groups[1].Services.Select(service => service.Name));
    }

    [Fact]
    public void Build_NoServices_ReturnsNoGroups()
    {
        var groups = BarberServiceGroups.Build([], OtherServicesTitle);

        Assert.Empty(groups);
    }

    private static BarberServiceOption Option(string name, string specializationName) =>
        BarberServiceOption.From(Service(name, specializationName));

    private static BarberProfileServiceResponse Service(string name, string specializationName) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            name,
            ServiceCategory.Haircut,
            30,
            2400m,
            45,
            IsActive: true,
            Guid.CreateVersion7(),
            specializationName);
}
