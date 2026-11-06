using BarberSearch.Application.Salons.AddSalonBarber;
using BarberSearch.Application.Salons.GetSalonBarbers;
using BarberSearch.Application.Salons.RemoveSalonBarber;
using BarberSearch.Application.Salons.SearchSalonBarberCandidates;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.GetSalonBarbers;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Состав салона: владелец читает прикреплённых барберов, ищет кандидатов и
/// прикрепляет/открепляет их. Чужой салон закрыт, владельца убрать нельзя.
/// </summary>
/// <remarks>
/// Салон создаётся фабрикой <see cref="Salon.SubmitByOwner"/>, поэтому владелец —
/// связь <c>OwnerUserId</c>, а не роль пользователя. Сценарии привязки проверяют
/// правило «первый салон профиля становится основным»: от него зависят слоты
/// и форма записи мастера.
/// </remarks>
public class SalonBarberManagementTests
{
    private const string DISPLAY_NAME = "Salon Barber";
    private const string OTHER_NAME = "Other Barber";

    [Fact]
    public async Task Owner_reads_barbers_of_own_salon()
    {
        var ownerId = Guid.CreateVersion7();
        var salon = CreateSalon(ownerId);
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);

        var profiles = new FakeBarberProfileRepository();
        profiles.AddSalonBarber(NewBarber(Guid.CreateVersion7(), ownerId, "Owner"));
        profiles.AddSalonBarber(NewBarber(Guid.CreateVersion7(), Guid.CreateVersion7(), "Other"));

        var handler = new GetSalonBarbersHandler(
            salons,
            profiles,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new GetSalonBarbersQuery(salon.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public async Task Reading_barbers_is_closed_for_strangers_anonymous_and_missing_salon()
    {
        var ownerId = Guid.CreateVersion7();
        var salon = CreateSalon(ownerId);
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);
        var profiles = new FakeBarberProfileRepository();
        profiles.AddSalonBarber(NewBarber(Guid.CreateVersion7(), ownerId, "Owner"));

        var stranger = await new GetSalonBarbersHandler(
                salons,
                profiles,
                new FakeCurrentUser(Guid.CreateVersion7()))
            .Handle(new GetSalonBarbersQuery(salon.Id), CancellationToken.None);

        Assert.True(stranger.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, stranger.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.Auth.Forbidden, stranger.Error.Errors[0].Code);

        var anonymous = await new GetSalonBarbersHandler(
                salons,
                profiles,
                FakeCurrentUser.Anonymous())
            .Handle(new GetSalonBarbersQuery(salon.Id), CancellationToken.None);

        Assert.True(anonymous.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, anonymous.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.Auth.Unauthorized, anonymous.Error.Errors[0].Code);

        var missing = await new GetSalonBarbersHandler(
                salons,
                profiles,
                new FakeCurrentUser(ownerId))
            .Handle(new GetSalonBarbersQuery(Guid.CreateVersion7()), CancellationToken.None);

        Assert.True(missing.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, missing.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.Salon.NotFound, missing.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Search_trims_query_trims_and_passes_the_limit()
    {
        var ownerId = Guid.CreateVersion7();
        var salon = CreateSalon(ownerId);
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);

        var profiles = new FakeBarberProfileRepository();
        profiles.AddSalonBarberCandidate(NewBarber(Guid.CreateVersion7(), Guid.CreateVersion7(), "Timur"));

        var handler = new SearchSalonBarberCandidatesHandler(
            salons,
            profiles,
            new FakeCurrentUser(ownerId));

        var result = await handler.Handle(
            new SearchSalonBarberCandidatesQuery(salon.Id, "  Timur  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("Timur", profiles.LastSalonBarberSearch);
        Assert.Equal(
            SearchSalonBarberCandidatesHandler.RESULT_LIMIT,
            profiles.LastSalonBarberLimit);
    }

    [Theory]
    [InlineData("ab", ApiErrorCodes.Salon.BarberSearchTooShort)]
    [InlineData("   ", ApiErrorCodes.Salon.BarberSearchTooShort)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", ApiErrorCodes.Salon.BarberSearchTooLong)]
    public async Task Search_rejects_too_short_and_too_long_queries(
        string search,
        string expectedCode)
    {
        var ownerId = Guid.CreateVersion7();
        var salon = CreateSalon(ownerId);
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);

        var profiles = new FakeBarberProfileRepository();

        var result = await new SearchSalonBarberCandidatesHandler(
                salons,
                profiles,
                new FakeCurrentUser(ownerId))
            .Handle(
                new SearchSalonBarberCandidatesQuery(salon.Id, search),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(expectedCode, result.Error.Errors[0].Code);
        Assert.Null(profiles.LastSalonBarberSearch);
    }

    [Fact]
    public async Task Search_is_forbidden_for_a_stranger_before_validation()
    {
        var ownerId = Guid.CreateVersion7();
        var salon = CreateSalon(ownerId);
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);
        var profiles = new FakeBarberProfileRepository();

        var result = await new SearchSalonBarberCandidatesHandler(
                salons,
                profiles,
                new FakeCurrentUser(Guid.CreateVersion7()))
            .Handle(
                new SearchSalonBarberCandidatesQuery(salon.Id, "ab"),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Null(profiles.LastSalonBarberSearch);
    }

    [Fact]
    public async Task Owner_attaches_barber_and_first_salon_becomes_primary()
    {
        var ownerId = Guid.CreateVersion7();
        var salon = CreateSalon(ownerId);
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);

        var target = CreateProfile(Guid.CreateVersion7(), OTHER_NAME);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(target);

        var unitOfWork = new FakeUnitOfWork(salons, profiles);
        var handler = new AddSalonBarberHandler(
            salons,
            profiles,
            unitOfWork,
            new FakeCurrentUser(ownerId),
            NullLogger<AddSalonBarberHandler>.Instance);

        var result = await handler.Handle(
            new AddSalonBarberCommand(salon.Id, target.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var link = Assert.Single(target.Salons);
        Assert.Equal(salon.Id, link.SalonId);
        Assert.True(link.IsPrimary);
        Assert.Equal(1, profiles.UpdateCallCount);
        Assert.Equal(1, unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Attaching_a_second_salon_does_not_steal_primary()
    {
        var ownerId = Guid.CreateVersion7();
        var salon = CreateSalon(ownerId);
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);

        var target = CreateProfile(Guid.CreateVersion7(), OTHER_NAME);
        var existingSalonId = Guid.CreateVersion7();
        Assert.True(target.AddSalon(existingSalonId, isPrimary: true).IsSuccess);

        var profiles = new FakeBarberProfileRepository();
        profiles.Add(target);

        var handler = new AddSalonBarberHandler(
            salons,
            profiles,
            new FakeUnitOfWork(salons, profiles),
            new FakeCurrentUser(ownerId),
            NullLogger<AddSalonBarberHandler>.Instance);

        var result = await handler.Handle(
            new AddSalonBarberCommand(salon.Id, target.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var added = Assert.Single(target.Salons, link => link.SalonId == salon.Id);
        Assert.False(added.IsPrimary);
        Assert.True(
            Assert.Single(
                target.Salons,
                link => link.SalonId == existingSalonId).IsPrimary);
    }

    [Fact]
    public async Task Attaching_rejects_inactive_profiles_duplicates_and_strangers()
    {
        var ownerId = Guid.CreateVersion7();
        var salon = CreateSalon(ownerId);
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);

        var inactive = CreateProfile(Guid.CreateVersion7(), "Inactive");
        Assert.True(inactive.Deactivate().IsSuccess);

        var profiles = new FakeBarberProfileRepository();
        profiles.Add(inactive);

        var unitOfWork = new FakeUnitOfWork(salons, profiles);
        var handler = new AddSalonBarberHandler(
            salons,
            profiles,
            unitOfWork,
            new FakeCurrentUser(ownerId),
            NullLogger<AddSalonBarberHandler>.Instance);

        var inactiveResult = await handler.Handle(
            new AddSalonBarberCommand(salon.Id, inactive.Id),
            CancellationToken.None);

        Assert.True(inactiveResult.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, inactiveResult.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.Salon.BarberInactive, inactiveResult.Error.Errors[0].Code);
        Assert.Empty(inactive.Salons);
        Assert.Equal(1, unitOfWork.RollbackCount);

        var duplicate = CreateProfile(Guid.CreateVersion7(), "Duplicate");
        Assert.True(duplicate.AddSalon(salon.Id, isPrimary: true).IsSuccess);
        profiles.Add(duplicate);

        var duplicateResult = await handler.Handle(
            new AddSalonBarberCommand(salon.Id, duplicate.Id),
            CancellationToken.None);

        Assert.True(duplicateResult.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, duplicateResult.Error.Errors[0].Type);
        Assert.Equal(
            "barberProfile.addSalon.duplicate",
            duplicateResult.Error.Errors[0].Code);

        var strangerHandler = new AddSalonBarberHandler(
            salons,
            profiles,
            new FakeUnitOfWork(salons, profiles),
            new FakeCurrentUser(Guid.CreateVersion7()),
            NullLogger<AddSalonBarberHandler>.Instance);

        var strangerResult = await strangerHandler.Handle(
            new AddSalonBarberCommand(salon.Id, duplicate.Id),
            CancellationToken.None);

        Assert.True(strangerResult.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, strangerResult.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Attaching_reports_a_missing_profile()
    {
        var ownerId = Guid.CreateVersion7();
        var salon = CreateSalon(ownerId);
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);

        var profiles = new FakeBarberProfileRepository();
        var handler = new AddSalonBarberHandler(
            salons,
            profiles,
            new FakeUnitOfWork(salons, profiles),
            new FakeCurrentUser(ownerId),
            NullLogger<AddSalonBarberHandler>.Instance);

        var result = await handler.Handle(
            new AddSalonBarberCommand(salon.Id, Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.BarberProfile.NotFound, result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Owner_detaches_barber_but_not_the_salon_owner()
    {
        var ownerId = Guid.CreateVersion7();
        var salon = CreateSalon(ownerId);
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);

        var barber = CreateProfile(Guid.CreateVersion7(), OTHER_NAME);
        Assert.True(barber.AddSalon(salon.Id).IsSuccess);

        var salonOwner = CreateProfile(ownerId, DISPLAY_NAME);
        Assert.True(salonOwner.AddSalon(salon.Id, isPrimary: true).IsSuccess);

        var profiles = new FakeBarberProfileRepository();
        profiles.Add(barber);
        profiles.Add(salonOwner);

        var unitOfWork = new FakeUnitOfWork(salons, profiles);
        var handler = new RemoveSalonBarberHandler(
            salons,
            profiles,
            unitOfWork,
            new FakeCurrentUser(ownerId),
            NullLogger<RemoveSalonBarberHandler>.Instance);

        var removed = await handler.Handle(
            new RemoveSalonBarberCommand(salon.Id, barber.Id),
            CancellationToken.None);

        Assert.True(removed.IsSuccess);
        Assert.Empty(barber.Salons);

        var ownerRemoval = await handler.Handle(
            new RemoveSalonBarberCommand(salon.Id, salonOwner.Id),
            CancellationToken.None);

        Assert.True(ownerRemoval.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, ownerRemoval.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.Salon.BarberOwnerImmutable,
            ownerRemoval.Error.Errors[0].Code);
        Assert.Single(salonOwner.Salons);

        var stranger = new RemoveSalonBarberHandler(
            salons,
            profiles,
            new FakeUnitOfWork(salons, profiles),
            new FakeCurrentUser(Guid.CreateVersion7()),
            NullLogger<RemoveSalonBarberHandler>.Instance);

        var strangerResult = await stranger.Handle(
            new RemoveSalonBarberCommand(salon.Id, salonOwner.Id),
            CancellationToken.None);

        Assert.True(strangerResult.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, strangerResult.Error.Errors[0].Type);
    }

    private static Salon CreateSalon(Guid ownerId)
        => Salon.SubmitByOwner(
            ownerId,
            "Owner Salon",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173).Value;

    private static BarberProfile CreateProfile(Guid userId, string displayName)
        => BarberProfile.Create(
            userId: userId,
            displayName: displayName,
            yearsOfExperience: 5,
            bio: null,
            languages: [Language.Ru],
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: null).Value;

    private static SalonBarberResponse NewBarber(
        Guid barberProfileId,
        Guid userId,
        string displayName)
        => new(
            barberProfileId,
            userId,
            displayName,
            $"{displayName.Replace(' ', '-').ToLowerInvariant()}@barbersearch.test",
            IsActive: true,
            IsVerified: true);
}
