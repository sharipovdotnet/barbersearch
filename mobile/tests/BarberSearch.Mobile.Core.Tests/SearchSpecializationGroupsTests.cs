using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Domain.HairServices;
using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.ViewModels;
using CoreLocalization = BarberSearch.Mobile.Core.Localization.Localization;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки раскладки специализаций по категориям — зеркало <c>groupSpecializations</c> из веба.
/// </summary>
public sealed class SearchSpecializationGroupsTests
{
    [Fact]
    public void Build_OrdersGroupsByCatalogueNotByApiOrder()
    {
        var specializations = new[]
        {
            Specialization(SpecializationId(1), "Другое", ServiceCategory.Other),
            Specialization(SpecializationId(2), "Борода", ServiceCategory.Beard),
            Specialization(SpecializationId(3), "Стрижка", ServiceCategory.Haircut),
        };

        var groups = SearchSpecializationGroups.Build(specializations);

        // Порядок групп каталожный, хотя API отдал категории наоборот.
        Assert.Equal(
            [ServiceCategory.Haircut, ServiceCategory.Beard, ServiceCategory.Other],
            groups.Select(group => group.Category));
        Assert.Equal(["Стрижка", "Борода", "Другое"], groups.Select(group => group.Title));
    }

    [Fact]
    public void Build_KeepsApiOrderInsideCategory()
    {
        var specializations = new[]
        {
            Specialization(SpecializationId(1), "Барбер", ServiceCategory.Haircut),
            Specialization(SpecializationId(2), "Парикмахер", ServiceCategory.Haircut),
            Specialization(SpecializationId(3), "Стилист", ServiceCategory.Haircut),
        };

        var group = Assert.Single(SearchSpecializationGroups.Build(specializations));

        // Внутри группы — порядок ответа API, а не алфавит названий.
        Assert.Equal(
            ["Барбер", "Парикмахер", "Стилист"],
            group.Select(option => option.DisplayText));
        Assert.Equal(
            [SpecializationId(1), SpecializationId(2), SpecializationId(3)],
            group.Select(option => option.Id));
    }

    [Fact]
    public void Build_EmptyCategories_AreNotReturned()
    {
        var specializations = new[]
        {
            Specialization(SpecializationId(1), "Барбер", ServiceCategory.Haircut),
        };

        // У остальных пяти категорий нет строк — пустых заголовков в списке не будет.
        var group = Assert.Single(SearchSpecializationGroups.Build(specializations));

        Assert.Equal(ServiceCategory.Haircut, group.Category);
        Assert.Equal("Стрижка", group.Title);
    }

    [Fact]
    public void Build_AllSixCategories_UseRussianTitlesInCatalogueOrder()
    {
        // Ответ API перемешан: и категории, и строки идут не по каталогу.
        var specializations = new[]
        {
            Specialization(SpecializationId(1), "Другое", ServiceCategory.Other),
            Specialization(SpecializationId(2), "Уход", ServiceCategory.HairTreatment),
            Specialization(SpecializationId(3), "Укладка", ServiceCategory.Styling),
            Specialization(SpecializationId(4), "Окрашивание", ServiceCategory.Coloring),
            Specialization(SpecializationId(5), "Борода", ServiceCategory.Beard),
            Specialization(SpecializationId(6), "Стрижка", ServiceCategory.Haircut),
        };

        var groups = SearchSpecializationGroups.Build(specializations);

        Assert.Equal(SearchSpecializationGroups.CategoryOrder, groups.Select(group => group.Category));
        Assert.Equal(
            ["Стрижка", "Борода", "Окрашивание", "Укладка", "Уход", "Другое"],
            groups.Select(group => group.Title));
    }

    [Fact]
    public void Build_EnglishLocale_TranslatesTitles()
    {
        using var _ = UseLocale(Locale.En);

        var specializations = new[]
        {
            Specialization(SpecializationId(1), "Стрижка", ServiceCategory.Haircut),
            Specialization(SpecializationId(2), "Борода", ServiceCategory.Beard),
            Specialization(SpecializationId(3), "Окрашивание", ServiceCategory.Coloring),
            Specialization(SpecializationId(4), "Укладка", ServiceCategory.Styling),
            Specialization(SpecializationId(5), "Уход", ServiceCategory.HairTreatment),
            Specialization(SpecializationId(6), "Другое", ServiceCategory.Other),
        };

        var groups = SearchSpecializationGroups.Build(specializations);

        // Заголовки — те же ключи, что и в кабинете мастера, поэтому переводятся вместе с ним.
        Assert.Equal(
            ["Haircut", "Beard", "Colouring", "Styling", "Hair treatment", "Other"],
            groups.Select(group => group.Title));
    }

    [Fact]
    public void Build_DoesNotMutateInputList()
    {
        var specializations = new[]
        {
            Specialization(SpecializationId(1), "Стрижка", ServiceCategory.Haircut),
            Specialization(SpecializationId(2), "Борода", ServiceCategory.Beard),
        };

        _ = SearchSpecializationGroups.Build(specializations);

        // Раскладка — чистая функция: справочник принадлежит вызывающему коду и не меняется.
        Assert.Equal(
            [SpecializationId(1), SpecializationId(2)],
            specializations.Select(row => row.Id));
    }

    [Fact]
    public void Build_NullList_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => SearchSpecializationGroups.Build(null!));
    }

    private static SpecializationResponse Specialization(
        Guid id,
        string name,
        ServiceCategory category) =>
        new(id, name, null, category);

    private static Guid SpecializationId(int number) =>
        Guid.Parse($"d1000000-0000-4000-8000-{number:D12}");

    private static RestoreLocale UseLocale(Locale locale)
    {
        var previous = Loc.Current;

        var localization = new CoreLocalization(
            new InMemoryLocaleStore(locale),
            initialLocale: locale);

        localization.Initialize();
        Loc.Current = localization;

        return new RestoreLocale(previous);
    }

    private sealed class RestoreLocale(ILocalization previous) : IDisposable
    {
        public void Dispose() => Loc.Current = previous;
    }
}
