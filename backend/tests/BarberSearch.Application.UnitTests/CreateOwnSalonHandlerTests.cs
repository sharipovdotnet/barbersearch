using BarberSearch.Application.Salons.CreateOwnSalon;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Создание салона барбером: профиль берётся из claim'а, салон уходит на модерацию
/// и привязывается к профилю в одной транзакции.
/// </summary>
/// <remarks>
/// Барбер без профиля получает <c>403 barberProfile.notFound</c> до открытия транзакции.
/// Первый салон профиля становится основным, второй — нет: от этого зависит расписание
/// и форма записи. Валидация полей остаётся доменной, поэтому обработчик возвращает
/// готовые коды <c>salon.*</c>, а не собственные сообщения.
/// </remarks>
public class CreateOwnSalonHandlerTests
{
    [Fact]
    public async Task Handle_WithoutProfileClaim_ReturnsForbiddenAndPersistsNothing()
    {
        var salons = new FakeSalonRepository();
        var profiles = new FakeBarberProfileRepository();
        var unitOfWork = new FakeUnitOfWork(salons);
        var handler = CreateHandler(
            salons,
            profiles,
            unitOfWork,
            new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(
            new CreateOwnSalonCommand(NewRequest()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.BarberProfile.NotFound, result.Error.Errors[0].Code);
        Assert.Equal(0, salons.AddCalls);
        Assert.Equal(0, profiles.UpdateCallCount);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.False(unitOfWork.IsInTransaction);
    }

    [Fact]
    public async Task Handle_FirstSalon_StartsPendingAndBecomesPrimary()
    {
        var ownerUserId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerUserId);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);

        var salons = new FakeSalonRepository();
        var unitOfWork = new FakeUnitOfWork(salons);

        // Обе записи обязаны идти внутри IUnitOfWork: иначе салон и привязка к профилю
        // могли бы сохраниться порознь.
        salons.AddBehavior = _ =>
        {
            Assert.True(unitOfWork.IsInTransaction);
            return UnitResult.Success<Failure>();
        };
        profiles.UpdateBehavior = _ =>
        {
            Assert.True(unitOfWork.IsInTransaction);
            return UnitResult.Success<Failure>();
        };

        var handler = CreateHandler(
            salons,
            profiles,
            unitOfWork,
            new FakeCurrentUser(Guid.CreateVersion7(), barberProfileId: profile.Id));

        var result = await handler.Handle(
            new CreateOwnSalonCommand(NewRequest("Owner Salon")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var salon = Assert.Single(salons.Salons);
        Assert.Equal(result.Value, salon.Id);
        Assert.Equal(SalonModerationStatus.Pending, salon.ModerationStatus);
        Assert.False(salon.IsActive);
        Assert.Equal(ownerUserId, salon.OwnerUserId);

        var link = Assert.Single(profile.Salons);
        Assert.Equal(salon.Id, link.SalonId);
        Assert.True(link.IsPrimary);

        Assert.Equal(1, salons.AddCalls);
        Assert.Equal(1, profiles.UpdateCallCount);
        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.Equal(0, unitOfWork.RollbackCount);
        Assert.False(unitOfWork.IsInTransaction);
    }

    [Fact]
    public async Task Handle_SecondSalon_IsNotPrimary()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var firstSalonId = Guid.CreateVersion7();
        Assert.True(profile.AddSalon(firstSalonId, isPrimary: true).IsSuccess);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);

        var salons = new FakeSalonRepository();
        var unitOfWork = new FakeUnitOfWork(salons);
        var handler = CreateHandler(
            salons,
            profiles,
            unitOfWork,
            new FakeCurrentUser(Guid.CreateVersion7(), barberProfileId: profile.Id));

        var result = await handler.Handle(
            new CreateOwnSalonCommand(NewRequest("Second Salon")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var added = Assert.Single(profile.Salons, link => link.SalonId == result.Value);
        Assert.False(added.IsPrimary);

        var first = Assert.Single(profile.Salons, link => link.SalonId == firstSalonId);
        Assert.True(first.IsPrimary);
    }

    [Fact]
    public async Task Handle_WithInvalidInput_ReturnsDomainCodeAndPersistsNothing()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);

        var salons = new FakeSalonRepository();
        var unitOfWork = new FakeUnitOfWork(salons);
        var handler = CreateHandler(
            salons,
            profiles,
            unitOfWork,
            new FakeCurrentUser(Guid.CreateVersion7(), barberProfileId: profile.Id));

        var result = await handler.Handle(
            new CreateOwnSalonCommand(NewRequest() with { City = "   " }),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("salon.city", result.Error.Errors[0].Code);
        Assert.Equal(0, salons.AddCalls);
        Assert.Equal(0, profiles.UpdateCallCount);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
        Assert.Empty(salons.Salons);
        Assert.Empty(profile.Salons);
    }

    private static CreateOwnSalonHandler CreateHandler(
        FakeSalonRepository salons,
        FakeBarberProfileRepository profiles,
        FakeUnitOfWork unitOfWork,
        FakeCurrentUser currentUser)
        => new(
            salons,
            profiles,
            unitOfWork,
            currentUser,
            NullLogger<CreateOwnSalonHandler>.Instance);

    private static CreateSalonRequest NewRequest(string name = "Owner Salon")
        => new(
            name,
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173,
            null,
            null,
            null);

    private static BarberProfile CreateProfile(Guid userId)
        => BarberProfile.Create(
            userId: userId,
            displayName: "Owner Barber",
            yearsOfExperience: 5,
            bio: null,
            languages: [Language.Ru],
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: null).Value;
}
