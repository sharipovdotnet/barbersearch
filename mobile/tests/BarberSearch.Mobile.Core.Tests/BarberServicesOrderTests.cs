using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.HairServices;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки порядка услуг в публичном прайсе — зеркало <c>sortBarberServices</c> из веба.
/// </summary>
/// <remarks>
/// Порядок задан цепочкой «категория → специализация → название → BarberServiceId»: категории
/// идут каталожно, а не по алфавиту, специализации и названия сравниваются русской локалью.
/// Функция чистая, поэтому проверяется без карточки и сети — только на строках прайса.
/// </remarks>
public sealed class BarberServicesOrderTests
{
    [Fact]
    public void Sort_AllSixCategories_OrdersRowsByCatalogueNotByApiOrder()
    {
        // Ответ API перемешан: категории идут ни по каталогу, ни по алфавиту.
        var services = new[]
        {
            Service(1, "Уход за волосами", ServiceCategory.HairTreatment, "Уход"),
            Service(2, "Укладка феном", ServiceCategory.Styling, "Укладка"),
            Service(3, "Окрашивание в блонд", ServiceCategory.Coloring, "Окрашивание"),
            Service(4, "Оформление бороды", ServiceCategory.Beard, "Борода"),
            Service(5, "Стрижка машинкой", ServiceCategory.Haircut, "Стрижка"),
            Service(6, "Восковая депиляция", ServiceCategory.Other, "Другое"),
        };

        var sorted = BarberServicesOrder.Sort(services);

        // Категории идут каталожным порядком: стрижка, борода, окрашивание, укладка, уход, другое.
        Assert.Equal(
            [
                ServiceCategory.Haircut,
                ServiceCategory.Beard,
                ServiceCategory.Coloring,
                ServiceCategory.Styling,
                ServiceCategory.HairTreatment,
                ServiceCategory.Other,
            ],
            sorted.Select(service => service.Category));
    }

    [Fact]
    public void Sort_UnknownCategory_GoesAfterOther()
    {
        // Незнакомая клиенту категория не имеет ранга и не должна вклиниваться в середину.
        var services = new[]
        {
            Service(1, "Неизвестная услуга", (ServiceCategory)999, "Неизвестная"),
            Service(2, "Восковая депиляция", ServiceCategory.Other, "Другое"),
        };

        var sorted = BarberServicesOrder.Sort(services);

        Assert.Equal(
            ["Восковая депиляция", "Неизвестная услуга"],
            sorted.Select(service => service.Name));
    }

    [Fact]
    public void Sort_SameCategory_KeepsSpecializationsTogetherAndOrdersThemAlphabetically()
    {
        // API чередует услуги разных специализаций: прайс обязан собрать их в группы.
        var services = new[]
        {
            Service(1, "Фейд классический", ServiceCategory.Haircut, "Фейд"),
            Service(2, "Стрижка наголо", ServiceCategory.Haircut, "Стрижка"),
            Service(3, "Фейд с выбриванием", ServiceCategory.Haircut, "Фейд"),
            Service(4, "Стрижка ножницами", ServiceCategory.Haircut, "Стрижка"),
        };

        var sorted = BarberServicesOrder.Sort(services);

        Assert.Equal(
            ["Стрижка наголо", "Стрижка ножницами", "Фейд классический", "Фейд с выбриванием"],
            sorted.Select(service => service.Name));

        // Внутри категории специализации идут русским алфавитом: «Стрижка» раньше «Фейда».
        Assert.Equal(
            ["Стрижка", "Стрижка", "Фейд", "Фейд"],
            sorted.Select(service => service.SpecializationName));
    }

    [Fact]
    public void Sort_SameSpecialization_OrdersNamesAlphabetically()
    {
        // Внутри одной специализации решает название, а не порядок ответа API.
        var services = new[]
        {
            Service(1, "Ягодный скраб", ServiceCategory.HairTreatment, "Уход"),
            Service(2, "Апельсиновый скраб", ServiceCategory.HairTreatment, "Уход"),
        };

        var sorted = BarberServicesOrder.Sort(services);

        Assert.Equal(
            ["Апельсиновый скраб", "Ягодный скраб"],
            sorted.Select(service => service.Name));
    }

    [Fact]
    public void Sort_FullTie_OrdersByBarberServiceId()
    {
        // Категория, специализация и название совпадают: остаётся только идентификатор строки.
        var first = Service(1, "Стрижка", ServiceCategory.Haircut, "Стрижка");
        var second = Service(2, "Стрижка", ServiceCategory.Haircut, "Стрижка");

        var sorted = BarberServicesOrder.Sort([second, first]);

        Assert.Equal(
            [first.BarberServiceId, second.BarberServiceId],
            sorted.Select(service => service.BarberServiceId));
    }

    [Fact]
    public void Sort_DoesNotMutateInputList()
    {
        var haircut = Service(1, "Стрижка машинкой", ServiceCategory.Haircut, "Стрижка");
        var beard = Service(2, "Оформление бороды", ServiceCategory.Beard, "Борода");
        var services = new List<BarberProfileServiceResponse> { beard, haircut };

        var sorted = BarberServicesOrder.Sort(services);

        // Возвращается новый список: исходный прайс остаётся в порядке ответа API.
        Assert.NotSame(services, sorted);
        Assert.Equal([beard, haircut], services);

        var array = new[] { beard, haircut };

        _ = BarberServicesOrder.Sort(array);

        Assert.Equal([beard, haircut], array);
    }

    [Fact]
    public void Compare_SameRow_ReturnsZero()
    {
        var service = Service(1, "Фейд", ServiceCategory.Haircut, "Фейд");

        // Сравнение рефлексивно: строка не меняет места относительно самой себя.
        Assert.Equal(0, BarberServicesOrder.Compare(service, service));

        var sameRow = Service(1, "Фейд", ServiceCategory.Haircut, "Фейд");

        Assert.Equal(0, BarberServicesOrder.Compare(service, sameRow));
    }

    [Fact]
    public void Compare_RowsInCatalogueOrder_IsAntisymmetric()
    {
        var haircut = Service(1, "Фейд", ServiceCategory.Haircut, "Фейд");
        var beard = Service(2, "Оформление бороды", ServiceCategory.Beard, "Борода");

        // Стрижка раньше бороды — и обратное сравнение обязано дать противоположный знак.
        Assert.True(BarberServicesOrder.Compare(haircut, beard) < 0);
        Assert.True(BarberServicesOrder.Compare(beard, haircut) > 0);

        var firstName = Service(3, "Стрижка", ServiceCategory.Haircut, "Фейд");
        var secondName = Service(4, "Фейд", ServiceCategory.Haircut, "Фейд");

        // То же правило внутри одной специализации: название решает в обе стороны.
        Assert.True(BarberServicesOrder.Compare(firstName, secondName) < 0);
        Assert.True(BarberServicesOrder.Compare(secondName, firstName) > 0);
    }

    private static BarberProfileServiceResponse Service(
        int number,
        string name,
        ServiceCategory category,
        string specializationName) =>
        new(
            ServiceId(number),
            Guid.Parse($"e2000000-0000-4000-8000-{number:D12}"),
            name,
            category,
            30,
            1000m,
            30,
            true,
            Guid.Parse($"e3000000-0000-4000-8000-{number:D12}"),
            specializationName);

    private static Guid ServiceId(int number) =>
        Guid.Parse($"e1000000-0000-4000-8000-{number:D12}");
}
