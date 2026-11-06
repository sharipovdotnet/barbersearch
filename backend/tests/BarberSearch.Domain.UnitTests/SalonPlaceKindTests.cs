using BarberSearch.Domain.Salons;
using Shared;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Тип места приёма (<see cref="PlaceKind"/>) и флаг каталога (<see cref="Salon.IsListed"/>):
/// значения по умолчанию, скрытое место без модерации и запрет публиковать дом и выезд.
/// </summary>
/// <remarks>
/// Флаг не выводится из типа: кабинет одного мастера бывает и публичным, и скрытым.
/// Скрытое место не публикуется, поэтому не проходит модерацию: оно сразу активно и видно
/// только с карточки барбера. Барбершоп и студия аренды кресел обязаны быть в каталоге.
/// Существующие инварианты (адрес, часовой пояс, координаты) от типа не зависят.
/// </remarks>
public class SalonPlaceKindTests
{
    [Fact]
    public void Create_WithoutKindAndListed_DefaultsToSalonListedAndActive()
    {
        var salon = CreateAdminSalon();

        Assert.Equal(PlaceKind.Salon, salon.Kind);
        Assert.True(salon.IsListed);
        Assert.Equal(SalonModerationStatus.Active, salon.ModerationStatus);
        Assert.True(salon.IsActive);
    }

    [Fact]
    public void Create_UnlistedHome_StaysActiveButOutOfCatalogue()
    {
        var salon = CreateAdminSalon(kind: PlaceKind.Home, isListed: false);

        Assert.Equal(PlaceKind.Home, salon.Kind);
        Assert.False(salon.IsListed);
        Assert.Equal(SalonModerationStatus.Active, salon.ModerationStatus);
        Assert.True(salon.IsActive);
    }

    [Fact]
    public void SubmitByOwner_WithoutKindAndListed_StartsPendingListedSalon()
    {
        var ownerUserId = Guid.CreateVersion7();

        var salon = CreateOwnerSalon(ownerUserId);

        Assert.Equal(PlaceKind.Salon, salon.Kind);
        Assert.True(salon.IsListed);
        Assert.Equal(SalonModerationStatus.Pending, salon.ModerationStatus);
        Assert.False(salon.IsActive);
        Assert.Equal(ownerUserId, salon.OwnerUserId);
    }

    [Fact]
    public void SubmitByOwner_UnlistedPlace_StartsActiveWithoutReview()
    {
        var ownerUserId = Guid.CreateVersion7();

        var salon = CreateOwnerSalon(
            ownerUserId,
            kind: PlaceKind.SoloStudio,
            isListed: false);

        Assert.Equal(PlaceKind.SoloStudio, salon.Kind);
        Assert.False(salon.IsListed);
        Assert.Equal(SalonModerationStatus.Active, salon.ModerationStatus);
        Assert.True(salon.IsActive);
        Assert.Null(salon.ReviewedAt);
        Assert.Null(salon.ReviewedByUserId);
        Assert.Equal(ownerUserId, salon.OwnerUserId);
    }

    [Fact]
    public void SubmitByOwner_ListedSoloStudio_StartsPending()
    {
        var salon = CreateOwnerSalon(kind: PlaceKind.SoloStudio, isListed: true);

        Assert.Equal(PlaceKind.SoloStudio, salon.Kind);
        Assert.True(salon.IsListed);
        Assert.Equal(SalonModerationStatus.Pending, salon.ModerationStatus);
        Assert.False(salon.IsActive);
    }

    [Theory]
    [InlineData(PlaceKind.Home)]
    [InlineData(PlaceKind.Mobile)]
    public void SubmitByOwner_ListedHomeOrMobile_ReturnsListedKindError(PlaceKind kind)
    {
        var result = Salon.SubmitByOwner(
            Guid.CreateVersion7(),
            "Private Place",
            "Teststrasse 1",
            "Berlin",
            "Germany",
            "Europe/Berlin",
            52.5200,
            13.4050,
            kind: kind,
            isListed: true);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("salon.isListed.kind", result.Error.Errors[0].Code);
        Assert.Equal("isListed", result.Error.Errors[0].InvalidField);
    }

    [Theory]
    [InlineData(PlaceKind.Salon)]
    [InlineData(PlaceKind.RentalStudio)]
    public void SubmitByOwner_UnlistedSalonOrRentalStudio_ReturnsListedKindError(PlaceKind kind)
    {
        var result = Salon.SubmitByOwner(
            Guid.CreateVersion7(),
            "Catalogue Place",
            "Teststrasse 1",
            "Berlin",
            "Germany",
            "Europe/Berlin",
            52.5200,
            13.4050,
            kind: kind,
            isListed: false);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("salon.isListed.kind", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_ListedHome_ReturnsListedKindError()
    {
        var result = Salon.Create(
            "Home Salon",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173,
            kind: PlaceKind.Home,
            isListed: true);

        Assert.True(result.IsFailure);
        Assert.Equal("salon.isListed.kind", result.Error.Errors[0].Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void Create_WithUndefinedKind_ReturnsKindUnknown(int value)
    {
        var result = Salon.Create(
            "Unknown Kind Salon",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173,
            kind: (PlaceKind)value);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("salon.kind.unknown", result.Error.Errors[0].Code);
    }

    [Fact]
    public void SubmitByOwner_WithUndefinedKind_ReturnsKindUnknown()
    {
        var result = Salon.SubmitByOwner(
            Guid.CreateVersion7(),
            "Unknown Kind Salon",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173,
            kind: (PlaceKind)99,
            isListed: false);

        Assert.True(result.IsFailure);
        Assert.Equal("salon.kind.unknown", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_HomeStillRequiresAddressAndValidTimeZone()
    {
        var noAddress = Salon.Create(
            "Home Salon",
            "   ",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173,
            kind: PlaceKind.Home,
            isListed: false);

        Assert.True(noAddress.IsFailure);
        Assert.Equal("salon.address", noAddress.Error.Errors[0].Code);

        var badTimeZone = Salon.Create(
            "Mobile Zone",
            "Выезд по городу",
            "Moscow",
            "RU",
            "Nowhere/Invalid",
            55.7558,
            37.6173,
            kind: PlaceKind.Mobile,
            isListed: false);

        Assert.True(badTimeZone.IsFailure);
        Assert.Equal("salon.timeZoneId.unknown", badTimeZone.Error.Errors[0].Code);
    }

    [Fact]
    public void KindAndIsListed_HaveNoPublicSetter()
    {
        foreach (var name in new[] { nameof(Salon.Kind), nameof(Salon.IsListed) })
        {
            var property = typeof(Salon).GetProperty(name);

            Assert.NotNull(property);
            Assert.Null(property!.GetSetMethod(nonPublic: false));
            Assert.NotNull(property.GetSetMethod(nonPublic: true));
        }
    }

    private static Salon CreateAdminSalon(
        PlaceKind kind = PlaceKind.Salon,
        bool isListed = true)
        => Salon.Create(
            "Admin Salon",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173,
            kind: kind,
            isListed: isListed).Value;

    private static Salon CreateOwnerSalon(
        Guid? ownerUserId = null,
        PlaceKind kind = PlaceKind.Salon,
        bool isListed = true)
        => Salon.SubmitByOwner(
            ownerUserId ?? Guid.CreateVersion7(),
            "Owner Salon",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173,
            kind: kind,
            isListed: isListed).Value;
}
