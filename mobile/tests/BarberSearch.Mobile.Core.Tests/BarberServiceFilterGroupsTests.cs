using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.HairServices;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки раскладки фильтра «Услуга» по специализациям — зеркало <c>groupBarberServices</c>
/// из веба.
/// </summary>
/// <remarks>
/// Функция чистая, поэтому проверяется без карточки и сети: группы идут по алфавиту названий
/// специализаций (русская локаль, без учёта регистра), при равных названиях решает
/// <c>specializationId</c>, строки внутри группы сохраняют порядок ответа API, «Любая услуга»
/// в группы не попадает, а строки без специализации замыкают список «Прочими услугами».
/// </remarks>
public sealed class BarberServiceFilterGroupsTests
{
    private const string OtherServicesTitle = "Прочие услуги";

    private static readonly Guid BeardSpecializationId = Guid.Parse("b2000000-0000-4000-8000-000000000001");
    private static readonly Guid HaircutSpecializationId = Guid.Parse("b2000000-0000-4000-8000-000000000002");
    private static readonly Guid ColoringSpecializationId = Guid.Parse("b2000000-0000-4000-8000-000000000003");

    [Fact]
    public void Build_GroupsOrderedBySpecializationName_NotInputOrder()
    {
        // API отдал «Стрижку» раньше «Бороды»: порядок групп задаёт алфавит, а не ответ.
        var services = new[]
        {
            Option("Стрижка машинкой", "Стрижка", HaircutSpecializationId),
            Option("Оформление бороды", "Борода", BeardSpecializationId),
        };

        var groups = BarberServiceFilterGroups.Build(services, OtherServicesTitle);

        Assert.Equal(["Борода", "Стрижка"], groups.Select(group => group.Title));
        Assert.Equal(["Оформление бороды"], groups[0].Select(option => option.Name));
        Assert.Equal(["Стрижка машинкой"], groups[1].Select(option => option.Name));
    }

    [Fact]
    public void Build_NamesDifferByCase_OrdersIgnoringCase()
    {
        // Веб сравнивает названия русской локалью без учёта регистра: «барбер» раньше
        // «Парикмахер», хотя посимвольно (Ordinal) было бы наоборот.
        var services = new[]
        {
            Option("Укладка", "Парикмахер", ColoringSpecializationId),
            Option("Стрижка", "барбер", HaircutSpecializationId),
        };

        var groups = BarberServiceFilterGroups.Build(services, OtherServicesTitle);

        Assert.Equal(["барбер", "Парикмахер"], groups.Select(group => group.Title));
    }

    [Fact]
    public void Build_EqualNames_SortsGroupsBySpecializationId()
    {
        var first = Guid.Parse("b2000000-0000-4000-8000-000000000001");
        var second = Guid.Parse("b2000000-0000-4000-8000-000000000002");
        var services = new[]
        {
            Option("Стрижка второй", "Барбер", second),
            Option("Стрижка первой", "Барбер", first),
        };

        // В API вторая специализация идёт раньше: при равных именах решает Id, а не ответ.
        var groups = BarberServiceFilterGroups.Build(services, OtherServicesTitle);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Equal("Барбер", group.Title));
        Assert.Equal(first, groups[0][0].SpecializationId);
        Assert.Equal("Стрижка первой", groups[0][0].Name);
        Assert.Equal(second, groups[1][0].SpecializationId);
        Assert.Equal("Стрижка второй", groups[1][0].Name);
    }

    [Fact]
    public void Build_SameNameDifferentSpecializations_RemainSeparateGroups()
    {
        var services = new[]
        {
            Option("Стрижка", "Барбер", HaircutSpecializationId),
            Option("Бритьё", "Барбер", ColoringSpecializationId),
        };

        // Ключ группы — пара «идентификатор + название»: одноимённые специализации не
        // склеиваются в одну, иначе строки чужой услуги попали бы под общий заголовок.
        var groups = BarberServiceFilterGroups.Build(services, OtherServicesTitle);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Equal("Барбер", group.Title));
        Assert.NotEqual(groups[0][0].SpecializationId, groups[1][0].SpecializationId);
        Assert.Equal(["Стрижка"], groups[0].Select(option => option.Name));
        Assert.Equal(["Бритьё"], groups[1].Select(option => option.Name));
    }

    [Fact]
    public void Build_GroupRows_KeepInputOrder()
    {
        var services = new[]
        {
            Option("Фейд с выбриванием", "Фейд", HaircutSpecializationId),
            Option("Фейд классический", "Фейд", HaircutSpecializationId),
            Option("Оформление бороды", "Борода", BeardSpecializationId),
        };

        var groups = BarberServiceFilterGroups.Build(services, OtherServicesTitle);

        // Внутри группы — порядок ответа API, а не алфавит услуг.
        Assert.Equal(["Борода", "Фейд"], groups.Select(group => group.Title));
        Assert.Equal(
            ["Фейд с выбриванием", "Фейд классический"],
            groups[1].Select(option => option.Name));
    }

    [Fact]
    public void Build_AnyServiceOption_IsNotInAnyGroup()
    {
        var any = BarberServiceOption.AnyService();
        var services = new List<BarberServiceOption>
        {
            any,
            Option("Стрижка", "Стрижка", HaircutSpecializationId),
        };

        var groups = BarberServiceFilterGroups.Build(services, OtherServicesTitle);

        // «Любая услуга» стоит над списком, а не отдельной группой и не строкой группы.
        var group = Assert.Single(groups);
        Assert.Equal("Стрижка", group.Title);
        Assert.DoesNotContain(group, option => ReferenceEquals(option, any));
        Assert.All(
            groups,
            item => Assert.DoesNotContain(item, option => option.HairServiceId is null));
    }

    [Fact]
    public void Build_BlankSpecializationName_GoesToTrailingOtherServicesGroup()
    {
        // Пустая строка и одни пробелы — не специализация: такие строки нельзя терять.
        // Вторая из них стоит в ответе последней, но заголовок группы всё равно замыкает список.
        var services = new[]
        {
            // У строк без специализации идентификатора тоже нет — как у справочника.
            Option("Стрижка наголо", "  ", Guid.Empty),
            Option("Фейд классический", "Фейд", HaircutSpecializationId),
            Option("Стрижка ножницами", string.Empty, Guid.Empty),
        };

        var groups = BarberServiceFilterGroups.Build(services, OtherServicesTitle);

        Assert.Equal(["Фейд", OtherServicesTitle], groups.Select(group => group.Title));
        Assert.Equal(
            ["Стрижка наголо", "Стрижка ножницами"],
            groups[1].Select(option => option.Name));
    }

    [Fact]
    public void Build_SpecializationNameWithSpaces_TrimsTitleAndKeepsOneGroup()
    {
        var services = new[]
        {
            Option("Фейд классический", " Фейд ", HaircutSpecializationId),
            Option("Фейд с выбриванием", "Фейд", HaircutSpecializationId),
        };

        // Заголовок обрезается по краям и служит ключом группы: один заголовок на специализацию.
        var group = Assert.Single(BarberServiceFilterGroups.Build(services, OtherServicesTitle));

        Assert.Equal("Фейд", group.Title);
        Assert.Equal(["Фейд классический", "Фейд с выбриванием"], group.Select(option => option.Name));
    }

    [Fact]
    public void Build_EmptyInput_ReturnsEmpty()
    {
        var groups = BarberServiceFilterGroups.Build([], OtherServicesTitle);

        Assert.Empty(groups);
    }

    [Fact]
    public void Build_DoesNotMutateInput_AndGroupsReferenceOriginalItems()
    {
        var coloring = Option("Балаяж", "Окрашивание", ColoringSpecializationId);
        var beard = Option("Оформление бороды", "Борода", BeardSpecializationId);
        var unknown = Option("Стрижка наголо", " ", Guid.Empty);
        var input = new List<BarberServiceOption> { coloring, beard, unknown };
        var snapshot = input.ToArray();

        // Только для чтения: попытка раскладки изменить вход сразу была бы видна.
        IReadOnlyList<BarberServiceOption> frozen = Array.AsReadOnly(snapshot);

        var groups = BarberServiceFilterGroups.Build(frozen, OtherServicesTitle);

        // Раскладка чистая: ни список, ни порядок строк не поменялись.
        Assert.Equal(snapshot, input);

        // Группы хранят ссылки на исходные пункты, а не их копии.
        Assert.Same(beard, groups[0][0]);
        Assert.Same(coloring, groups[1][0]);
        Assert.Same(unknown, groups[2][0]);
    }

    private static BarberServiceOption Option(
        string name,
        string specializationName,
        Guid specializationId,
        Guid? hairServiceId = null) =>
        BarberServiceOption.From(Service(name, specializationName, specializationId, hairServiceId));

    private static BarberProfileServiceResponse Service(
        string name,
        string specializationName,
        Guid specializationId,
        Guid? hairServiceId) =>
        new(
            Guid.CreateVersion7(),
            hairServiceId ?? Guid.CreateVersion7(),
            name,
            ServiceCategory.Haircut,
            30,
            2400m,
            45,
            IsActive: true,
            specializationId,
            specializationName);
}
