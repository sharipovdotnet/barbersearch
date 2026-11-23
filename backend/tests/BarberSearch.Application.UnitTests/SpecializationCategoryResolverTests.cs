using BarberSearch.Application.Specializations;
using BarberSearch.Domain.HairServices;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Правило категории направления: большинство активных услуг, ничья по меньшему коду enum,
/// отсутствие услуг — <c>Other</c>.
/// </summary>
public class SpecializationCategoryResolverTests
{
    private static readonly Guid Direction = Guid.NewGuid();

    [Fact]
    public void Majority_wins()
    {
        var category = SpecializationCategoryResolver.Resolve(
        [
            Count(ServiceCategory.Coloring, 2),
            Count(ServiceCategory.HairTreatment, 1)
        ]);

        Assert.Equal(ServiceCategory.Coloring, category);
    }

    [Theory]
    [InlineData(ServiceCategory.Coloring, ServiceCategory.Haircut, ServiceCategory.Haircut)]
    [InlineData(ServiceCategory.Other, ServiceCategory.Styling, ServiceCategory.Styling)]
    [InlineData(ServiceCategory.HairTreatment, ServiceCategory.Beard, ServiceCategory.Beard)]
    public void Tie_goes_to_the_lower_enum_value(
        ServiceCategory first,
        ServiceCategory second,
        ServiceCategory expected)
    {
        var category = SpecializationCategoryResolver.Resolve([
            Count(first, 1),
            Count(second, 1)
        ]);

        Assert.Equal(expected, category);
    }

    [Fact]
    public void No_services_fall_back_to_other()
    {
        var category = SpecializationCategoryResolver.Resolve([]);

        Assert.Equal(ServiceCategory.Other, category);
    }

    [Fact]
    public void Result_does_not_depend_on_count_order()
    {
        ServiceCategoryCount[] counts =
        [
            Count(ServiceCategory.HairTreatment, 1),
            Count(ServiceCategory.Coloring, 2)
        ];

        var forward = SpecializationCategoryResolver.Resolve(counts);
        var backward = SpecializationCategoryResolver.Resolve(counts.AsEnumerable().Reverse());

        Assert.Equal(ServiceCategory.Coloring, forward);
        Assert.Equal(forward, backward);
    }

    [Fact]
    public void ResolveAll_marks_direction_without_counts_as_other()
    {
        var withServices = Guid.NewGuid();
        var withoutServices = Guid.NewGuid();

        var categories = SpecializationCategoryResolver.ResolveAll(
            [withServices, withoutServices],
            [new ServiceCategoryCount(withServices, ServiceCategory.Beard, 4)]);

        Assert.Equal(ServiceCategory.Beard, categories[withServices]);
        Assert.Equal(ServiceCategory.Other, categories[withoutServices]);
    }

    [Fact]
    public void ResolveAll_returns_nothing_for_an_empty_catalogue()
    {
        var categories = SpecializationCategoryResolver.ResolveAll(
            [],
            [Count(ServiceCategory.Haircut, 3)]);

        Assert.Empty(categories);
    }

    private static ServiceCategoryCount Count(ServiceCategory category, int count)
        => new(Direction, category, count);
}
