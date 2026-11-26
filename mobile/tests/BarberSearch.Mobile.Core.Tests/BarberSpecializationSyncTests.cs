using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Domain.HairServices;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки сверки специализаций профиля с прайсом.
/// </summary>
/// <remarks>
/// Активная услуга — источник тега, скрытая — нет: строка остаётся в прайсе ради истории, но
/// клиентам её не видно. Тег без единой услуги не трогается: его могли добавить вручную в вебе.
/// </remarks>
public sealed class BarberSpecializationSyncTests
{
    private static readonly Guid Fade = Guid.Parse("9e100000-0000-4000-8000-0000000000e1");
    private static readonly Guid Beard = Guid.Parse("9e100000-0000-4000-8000-0000000000e2");
    private static readonly Guid Coloring = Guid.Parse("9e100000-0000-4000-8000-0000000000e3");

    [Fact]
    public void Compute_ActiveServiceWithoutTag_AddsTag()
    {
        var changes = BarberSpecializationSync.Compute(
            [Service(Fade)],
            []);

        Assert.Equal([Fade], changes.Add);
        Assert.Empty(changes.Remove);
        Assert.False(changes.IsEmpty);
    }

    [Fact]
    public void Compute_ActiveServiceWithTag_ChangesNothing()
    {
        var changes = BarberSpecializationSync.Compute(
            [Service(Fade)],
            [Tag(Fade)]);

        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void Compute_LastActiveServiceHidden_RemovesTag()
    {
        // Строка услуги осталась в прайсе, но выключена: специализация без активных услуг уходит.
        var changes = BarberSpecializationSync.Compute(
            [Service(Fade, isActive: false)],
            [Tag(Fade)]);

        Assert.Empty(changes.Add);
        Assert.Equal([Fade], changes.Remove);
    }

    [Fact]
    public void Compute_OtherActiveServiceOfSameSpecialization_KeepsTag()
    {
        var changes = BarberSpecializationSync.Compute(
            [Service(Fade), Service(Fade, isActive: false)],
            [Tag(Fade)]);

        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void Compute_TagWithoutServices_IsKept()
    {
        // Beard добавлен вручную в веб-кабинете: мобильная сверка не должна его стирать.
        var changes = BarberSpecializationSync.Compute(
            [Service(Fade)],
            [Tag(Fade), Tag(Beard)]);

        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void Compute_TagWithOnlyHiddenService_IsRemovedButManualTagStays()
    {
        var changes = BarberSpecializationSync.Compute(
            [Service(Fade, isActive: false), Service(Coloring)],
            [Tag(Fade), Tag(Beard), Tag(Coloring)]);

        Assert.Empty(changes.Add);
        Assert.Equal([Fade], changes.Remove);
    }

    [Fact]
    public void Compute_HiddenServiceWithoutTag_DoesNotAddTag()
    {
        var changes = BarberSpecializationSync.Compute(
            [Service(Fade, isActive: false)],
            []);

        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void Compute_EmptySpecializationId_IsIgnored()
    {
        var changes = BarberSpecializationSync.Compute(
            [Service(Guid.Empty), Service(Guid.Empty, isActive: false)],
            [Tag(Guid.Empty)]);

        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void Compute_RepeatedServices_CollapseToOneChange()
    {
        var changes = BarberSpecializationSync.Compute(
            [Service(Fade), Service(Fade), Service(Fade, isActive: false)],
            []);

        Assert.Equal([Fade], changes.Add);
    }

    [Fact]
    public void Compute_PreservesFirstAppearanceOrder()
    {
        var changes = BarberSpecializationSync.Compute(
            [Service(Coloring), Service(Fade), Service(Beard, isActive: false)],
            [Tag(Beard)]);

        // Добавление идёт в порядке услуг, удаление — в порядке тегов профиля.
        Assert.Equal([Coloring, Fade], changes.Add);
        Assert.Equal([Beard], changes.Remove);
    }

    [Fact]
    public void Compute_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(
            () => BarberSpecializationSync.Compute(null!, []));
        Assert.Throws<ArgumentNullException>(
            () => BarberSpecializationSync.Compute([], null!));
    }

    private static BarberProfileServiceResponse Service(
        Guid specializationId,
        bool isActive = true) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Стрижка",
            ServiceCategory.Haircut,
            30,
            2400m,
            60,
            isActive,
            specializationId,
            specializationId == Guid.Empty ? string.Empty : "Фейд");

    private static BarberProfileSpecializationResponse Tag(Guid specializationId) =>
        new(Guid.CreateVersion7(), specializationId, "Фейд", null);
}
