using BarberSearch.Application.Bookings.CreateBooking;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Создание брони: идемпотентность, цена из услуги, проверка услуги и конфликты БД.
/// </summary>
public class BookingCreateTests
{
    private static readonly Guid SalonId = Guid.CreateVersion7();

    [Fact]
    public async Task Create_RepeatedIdempotencyKey_ReturnsSameBooking()
    {
        var context = CreateContext();
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "repeat-key");
        var handler = CreateHandler(context);

        var first = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);
        var second = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value, second.Value);
        Assert.Single(context.Bookings.Bookings);
    }

    [Fact]
    public async Task Create_SameKeyForDifferentUsers_CreatesTwoBookings()
    {
        var context = CreateContext();
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "shared-key");
        var secondRequest = request with
        {
            StartAt = request.StartAt.AddHours(1),
            EndAt = request.EndAt.AddHours(1)
        };

        var firstHandler = new CreateBookingHandler(
            context.Bookings,
            context.Profiles,
            new FakeCurrentUser(Guid.CreateVersion7()),
            NullLogger<CreateBookingHandler>.Instance);
        var secondHandler = new CreateBookingHandler(
            context.Bookings,
            context.Profiles,
            new FakeCurrentUser(Guid.CreateVersion7()),
            NullLogger<CreateBookingHandler>.Instance);

        var first = await firstHandler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);
        var second = await secondHandler.Handle(
            new CreateBookingCommand(secondRequest),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value, second.Value);
        Assert.Equal(2, context.Bookings.Bookings.Count);
    }

    [Fact]
    public async Task Create_PriceComesFromBarberService_RequestPriceIsIgnored()
    {
        var context = CreateContext(price: 2500m);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "price-key") with { Price = 1m };
        var handler = CreateHandler(context);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var booking = Assert.Single(context.Bookings.Bookings);
        Assert.Equal(2500m, booking.Price);
    }

    [Fact]
    public async Task Create_UniqueViolation_ReReadsWinnerAndReturnsItsId()
    {
        var context = CreateContext();
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "race-key");
        var winner = Booking.Create(
            context.UserId,
            context.Profile.Id,
            context.Service.Id,
            SalonId,
            request.StartAt,
            request.EndAt,
            context.Service.Price,
            request.IdempotencyKey).Value;

        // Обработчик уже проверил ключ и не нашёл брони; вставка проиграла гонку,
        // но победившая строка уже закоммичена и читается повторно.
        context.Bookings.OnAdd = _ =>
        {
            context.Bookings.Add(winner);

            return UnitResult.Failure<Failure>(new Failure(Error.Conflict(
                "booking.add.uniqueViolation",
                "A record with the same unique values already exists.")));
        };
        var handler = CreateHandler(context);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(winner.Id, result.Value);
        Assert.Single(context.Bookings.Bookings);
    }

    [Fact]
    public async Task Create_UniqueViolationWithoutWinner_ReturnsConflict()
    {
        var context = CreateContext();
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "lost-key");
        context.Bookings.OnAdd = _ => UnitResult.Failure<Failure>(new Failure(Error.Conflict(
            "booking.add.uniqueViolation",
            "A record with the same unique values already exists.")));
        var handler = CreateHandler(context);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.idempotencyKeyConflict", result.Error.Errors[0].Code);
        Assert.Empty(context.Bookings.Bookings);
    }

    [Fact]
    public async Task Create_ForeignService_ReturnsNotFound()
    {
        var context = CreateContext();
        var other = CreateProfileWithService(100m);
        context.Profiles.Add(other.Profile);
        var handler = CreateHandler(context);

        // Услуга принадлежит другому барберу.
        var request = CreateRequest(
            context.Profile.Id,
            other.Service.Id,
            "foreign-service-key");
        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Empty(context.Bookings.Bookings);
    }

    [Fact]
    public async Task Create_InactiveService_ReturnsNotFound()
    {
        var context = CreateContext(active: false);
        var handler = CreateHandler(context);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "inactive-service-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.serviceInactive", result.Error.Errors[0].Code);
        Assert.Empty(context.Bookings.Bookings);
    }

    [Fact]
    public async Task Create_OverlappingActiveBooking_ReturnsConflict()
    {
        var context = CreateContext();
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "new-key");
        var existing = Booking.Create(
            context.UserId,
            context.Profile.Id,
            context.Service.Id,
            SalonId,
            request.StartAt,
            request.EndAt,
            context.Service.Price,
            "existing-key").Value;
        context.Bookings.Add(existing);
        var handler = CreateHandler(context);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.add.overlap", result.Error.Errors[0].Code);
        Assert.Single(context.Bookings.Bookings);
    }

    [Fact]
    public async Task Create_OverlappingCancelledBooking_Succeeds()
    {
        var context = CreateContext();
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "after-cancel-key");
        var cancelled = Booking.Create(
            context.UserId,
            context.Profile.Id,
            context.Service.Id,
            SalonId,
            request.StartAt,
            request.EndAt,
            context.Service.Price,
            "cancelled-key").Value;
        Assert.True(cancelled.Cancel().IsSuccess);
        context.Bookings.Add(cancelled);
        var handler = CreateHandler(context);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, context.Bookings.Bookings.Count);
    }

    private static CreateBookingHandler CreateHandler(TestContext context)
        => new(
            context.Bookings,
            context.Profiles,
            new FakeCurrentUser(context.UserId),
            NullLogger<CreateBookingHandler>.Instance);

    private static CreateBookingRequest CreateRequest(
        Guid barberProfileId,
        Guid serviceId,
        string idempotencyKey)
    {
        var startAt = DateTimeOffset.UtcNow.AddDays(7);

        return new CreateBookingRequest(
            barberProfileId,
            serviceId,
            SalonId,
            startAt,
            startAt.AddMinutes(30),
            0m,
            idempotencyKey,
            null);
    }

    private static TestContext CreateContext(
        decimal price = 1500m,
        bool active = true)
    {
        var profiles = new FakeBarberProfileRepository();
        var (profile, service) = CreateProfileWithService(price, active);
        profiles.Add(profile);

        return new TestContext(
            Guid.CreateVersion7(),
            profiles,
            new FakeBookingRepository(),
            profile,
            service);
    }

    private static (BarberProfile Profile, BarberService Service) CreateProfileWithService(
        decimal price,
        bool active = true)
    {
        var profile = BarberProfile.Create(
            Guid.CreateVersion7(),
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

        Assert.True(profile.AddService(Guid.CreateVersion7(), price, 30).IsSuccess);

        var service = profile.Services.Single();

        if (!active)
        {
            Assert.True(service.Deactivate().IsSuccess);
        }

        return (profile, service);
    }

    private sealed record TestContext(
        Guid UserId,
        FakeBarberProfileRepository Profiles,
        FakeBookingRepository Bookings,
        BarberProfile Profile,
        BarberService Service);
}
