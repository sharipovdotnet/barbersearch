using BarberSearch.Application.BarberProfiles.RemoveBarberProfileService;
using BarberSearch.Application.BarberProfiles.UpdateBarberProfileService;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Правка и скрытие услуги в профиле барбера: доменная мутация, сохранение агрегата
/// и проверка владения до записи.
/// </summary>
public class BarberProfileServiceMutationTests
{
    private static BarberProfile CreateProfileWithService(
        Guid ownerId,
        Guid hairServiceId)
    {
        var profile = BarberProfile.Create(
            ownerId,
            "Master Barber",
            5,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null).Value;

        Assert.True(profile.AddService(hairServiceId, 1500m, 45).IsSuccess);

        return profile;
    }

    private static UpdateBarberProfileServiceHandler CreateUpdateHandler(
        FakeBarberProfileRepository repository,
        FakeCurrentUser currentUser)
        => new(
            repository,
            NullLogger<UpdateBarberProfileServiceHandler>.Instance,
            currentUser);

    private static RemoveBarberProfileServiceHandler CreateRemoveHandler(
        FakeBarberProfileRepository repository,
        FakeCurrentUser currentUser)
        => new(
            repository,
            NullLogger<RemoveBarberProfileServiceHandler>.Instance,
            currentUser);

    [Fact]
    public async Task UpdateService_ByOwner_ChangesPriceAndDurationAndSavesOnce()
    {
        var ownerId = Guid.CreateVersion7();
        var hairServiceId = Guid.CreateVersion7();
        var profile = CreateProfileWithService(ownerId, hairServiceId);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);

        var result = await CreateUpdateHandler(repository, new FakeCurrentUser(ownerId)).Handle(
            new UpdateBarberProfileServiceCommand(profile.Id, hairServiceId, 2000m, 60),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.UpdateCallCount);

        var service = Assert.Single(profile.Services);
        Assert.Equal(2000m, service.Price);
        Assert.Equal(60, service.DurationMinutes);
        Assert.True(service.IsActive);
    }

    [Fact]
    public async Task UpdateService_ByStranger_ReturnsForbiddenAndDoesNotWrite()
    {
        var hairServiceId = Guid.CreateVersion7();
        var profile = CreateProfileWithService(Guid.CreateVersion7(), hairServiceId);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);

        var result = await CreateUpdateHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7())).Handle(
            new UpdateBarberProfileServiceCommand(profile.Id, hairServiceId, 2000m, 60),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);

        // EnsureCanManage стоит до мутации: цена не изменилась.
        var service = Assert.Single(profile.Services);
        Assert.Equal(1500m, service.Price);
        Assert.Equal(45, service.DurationMinutes);
    }

    [Fact]
    public async Task UpdateService_WhenServiceIsMissing_ReturnsNotFound()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfileWithService(ownerId, Guid.CreateVersion7());
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);

        var result = await CreateUpdateHandler(repository, new FakeCurrentUser(ownerId)).Handle(
            new UpdateBarberProfileServiceCommand(profile.Id, Guid.CreateVersion7(), 2000m, 60),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.updateService.notFound", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task RemoveService_KeepsTheRowInactiveInTheAggregate()
    {
        // §3.4: DELETE — деактивация. Строка обязана остаться, иначе брони, сделанные на неё,
        // пропадут из списков: они строятся внутренними join'ами по barber_services.
        var ownerId = Guid.CreateVersion7();
        var hairServiceId = Guid.CreateVersion7();
        var profile = CreateProfileWithService(ownerId, hairServiceId);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);

        var result = await CreateRemoveHandler(repository, new FakeCurrentUser(ownerId)).Handle(
            new RemoveBarberProfileServiceCommand(profile.Id, hairServiceId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.UpdateCallCount);

        var service = Assert.Single(profile.Services);
        Assert.Equal(hairServiceId, service.HairServiceId);
        Assert.False(service.IsActive);
    }

    [Fact]
    public async Task RemoveService_IsIdempotent()
    {
        var ownerId = Guid.CreateVersion7();
        var hairServiceId = Guid.CreateVersion7();
        var profile = CreateProfileWithService(ownerId, hairServiceId);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);

        var handler = CreateRemoveHandler(repository, new FakeCurrentUser(ownerId));

        var first = await handler.Handle(
            new RemoveBarberProfileServiceCommand(profile.Id, hairServiceId),
            CancellationToken.None);
        var second = await handler.Handle(
            new RemoveBarberProfileServiceCommand(profile.Id, hairServiceId),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.False(Assert.Single(profile.Services).IsActive);
    }

    [Fact]
    public async Task RemoveService_ByStranger_ReturnsForbiddenAndDoesNotWrite()
    {
        var hairServiceId = Guid.CreateVersion7();
        var profile = CreateProfileWithService(Guid.CreateVersion7(), hairServiceId);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);

        var result = await CreateRemoveHandler(
            repository,
            new FakeCurrentUser(Guid.CreateVersion7())).Handle(
            new RemoveBarberProfileServiceCommand(profile.Id, hairServiceId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.True(Assert.Single(profile.Services).IsActive);
    }
}
