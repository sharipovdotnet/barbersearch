using System.Reflection;
using BarberSearch.Application.Bookings.CreateBooking;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Domain;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Создание брони: идемпотентность, цена из услуги, проверка услуги, окно допустимости
/// и конфликты БД.
/// </summary>
public class BookingCreateTests
{
    private static readonly Guid SalonId = Guid.CreateVersion7();

    /// <summary>
    /// «Сейчас» для тестов окна брони: фиксированные часы вместо системных.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_BlankIdempotencyKey_ReturnsDomainValidationBeforeLookup()
    {
        var context = CreateContext();
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "   ");
        var handler = CreateHandler(context);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.idempotencyKey", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Bookings.GetByIdempotencyKeyCalls);
        Assert.Equal(0, context.Bookings.AddCalls);
        Assert.Empty(context.Bookings.Bookings);
    }

    [Fact]
    public async Task Create_TooLongIdempotencyKey_ReturnsDomainValidationBeforeLookup()
    {
        var context = CreateContext();
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            new string('k', Constants.MAX_IDEMPOTENCY_KEY_LENGTH + 1));
        var handler = CreateHandler(context);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.idempotencyKey.tooLong", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Bookings.GetByIdempotencyKeyCalls);
        Assert.Equal(0, context.Bookings.AddCalls);
        Assert.Empty(context.Bookings.Bookings);
    }

    [Fact]
    public async Task Create_PaddedIdempotencyKey_IsStoredTrimmed()
    {
        var context = CreateContext();
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "  padded-key  ");
        var handler = CreateHandler(context);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var booking = Assert.Single(context.Bookings.Bookings);
        Assert.Equal("padded-key", booking.IdempotencyKey);
    }

    [Fact]
    public async Task Create_AcquiresBarberLockBeforeInsert()
    {
        var context = CreateContext();
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "lock-key");
        var unitOfWork = new FakeUnitOfWork();
        var handler = CreateHandler(context, unitOfWork: unitOfWork);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["lock", "add"], context.Bookings.Operations);
        Assert.Equal(1, context.Bookings.LockCalls);
        Assert.Equal(context.Profile.Id, context.Bookings.LockedBarberProfileId);
        Assert.Equal(1, unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Create_LockTimeout_ReturnsConflictAndStopsBeforeInsert()
    {
        var context = CreateContext();
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "lock-timeout-key");
        context.Bookings.OnAcquireLock = _ => UnitResult.Failure<Failure>(new Failure(
            Error.Conflict(
                "booking.add.lockTimeout",
                "The database lock could not be acquired within the configured timeout.")));
        var unitOfWork = new FakeUnitOfWork();
        var handler = CreateHandler(context, unitOfWork: unitOfWork);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        // 55P03 (lock_timeout) из репозитория — это 409, а не 500. Транзакция
        // откатывается, проверки ссылок и вставка не выполняются (R-18).
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.add.lockTimeout", result.Error.Errors[0].Code);
        Assert.Equal(1, context.Bookings.LockCalls);
        Assert.Equal(0, context.Bookings.AddCalls);
        Assert.Empty(context.Bookings.Bookings);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Create_InsertRunsInsideUnitOfWorkTransaction()
    {
        var context = CreateContext();
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "inside-transaction-key");
        var unitOfWork = new FakeUnitOfWork();
        context.Bookings.OnAdd = _ =>
        {
            // Репозиторий не должен открывать собственную транзакцию: вставка идёт
            // в транзакции IUnitOfWork, иначе блокировка и проверки не атомарны (R-15).
            Assert.True(unitOfWork.IsInTransaction);

            return UnitResult.Success<Failure>();
        };
        var handler = CreateHandler(context, unitOfWork: unitOfWork);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["lock", "add"], context.Bookings.Operations);
        Assert.False(unitOfWork.IsInTransaction);
        Assert.Equal(1, unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Create_FailedReferenceCheck_AcquiresLockAndRollsBack()
    {
        var context = CreateContext(worksAtSalon: false);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "unassigned-lock-key");
        var unitOfWork = new FakeUnitOfWork();
        var handler = CreateHandler(context, unitOfWork: unitOfWork);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("booking.create.salonNotAssigned", result.Error.Errors[0].Code);

        // Блокировка берётся до проверок ссылок: иначе два запроса могли бы пройти
        // их одновременно и сериализоваться только на вставке (R-15).
        Assert.Equal(1, context.Bookings.LockCalls);
        Assert.Equal(0, context.Bookings.AddCalls);
        Assert.Equal(1, unitOfWork.RollbackCount);
    }

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

        // Повтор по ключу возвращается до транзакции: блокировка берётся только при создании.
        Assert.Equal(1, context.Bookings.LockCalls);
        Assert.Equal(1, context.Bookings.AddCalls);
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

        var firstHandler = CreateHandler(context, Guid.CreateVersion7());
        var secondHandler = CreateHandler(context, Guid.CreateVersion7());

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
    public async Task Create_PriceComesFromBarberService()
    {
        var context = CreateContext(price: 2500m);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "price-key");
        var handler = CreateHandler(context);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var booking = Assert.Single(context.Bookings.Bookings);
        Assert.Equal(2500m, booking.Price);
    }

    /// <summary>
    /// Поле <c>Price</c> удалено из контракта в R-33: чтобы не обещать клиенту неработающую цену,
    /// ни публичного свойства, ни параметра конструктора с таким именем быть не должно. За цену
    /// брони по-прежнему отвечает <c>BarberService.Price</c>.
    /// </summary>
    [Fact]
    public void CreateBookingRequest_HasNoPriceProperty()
    {
        var properties = typeof(CreateBookingRequest).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);
        Assert.DoesNotContain(properties, property => property.Name == "Price");

        var constructor = Assert.Single(typeof(CreateBookingRequest).GetConstructors());
        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => parameter.Name == "Price");
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

    [Fact]
    public async Task Create_UnknownSalon_ReturnsNotFound()
    {
        var context = CreateContext(salonExists: false);
        var handler = CreateHandler(context);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "unknown-salon-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.salonNotFound", result.Error.Errors[0].Code);
        Assert.Empty(context.Bookings.Bookings);
    }

    [Fact]
    public async Task Create_SalonWhereBarberDoesNotWork_ReturnsNotFound()
    {
        var context = CreateContext(worksAtSalon: false);
        var handler = CreateHandler(context);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "unassigned-salon-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.salonNotAssigned", result.Error.Errors[0].Code);
        Assert.Empty(context.Bookings.Bookings);
    }

    [Fact]
    public async Task Create_StartAtInThePast_ReturnsValidation()
    {
        var context = CreateContext();
        var handler = CreateHandler(context);
        var startAt = Now.AddHours(-1);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "past-key") with
        {
            StartAt = startAt,
            EndAt = startAt.AddMinutes(30)
        };

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.startAt.inPast", result.Error.Errors[0].Code);
        Assert.Empty(context.Bookings.Bookings);
        Assert.Equal(0, context.Bookings.AddCalls);
    }

    [Fact]
    public async Task Create_StartAtWithinClockTolerance_Succeeds()
    {
        var context = CreateContext();
        var handler = CreateHandler(context);
        var startAt = Now.AddMinutes(-Constants.BOOKING_START_TOLERANCE_MINUTES);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "tolerance-key") with
        {
            StartAt = startAt,
            EndAt = startAt.AddMinutes(30)
        };

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.Bookings.Bookings);
    }

    [Fact]
    public async Task Create_StartAtBeyondHorizon_ReturnsValidation()
    {
        var context = CreateContext();
        var handler = CreateHandler(context);
        var startAt = Now
            .AddDays(Constants.BOOKING_HORIZON_DAYS)
            .AddMinutes(1);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "far-key") with
        {
            StartAt = startAt,
            EndAt = startAt.AddMinutes(30)
        };

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.startAt.tooFar", result.Error.Errors[0].Code);
        Assert.Empty(context.Bookings.Bookings);
    }

    [Fact]
    public async Task Create_StartAtExactlyAtHorizon_Succeeds()
    {
        var context = CreateContext();
        var handler = CreateHandler(context);
        var startAt = Now.AddDays(Constants.BOOKING_HORIZON_DAYS);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "horizon-key") with
        {
            StartAt = startAt,
            EndAt = startAt.AddMinutes(30)
        };

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.Bookings.Bookings);
    }

    [Fact]
    public async Task Create_RepeatedKeyAfterStart_ReturnsExistingBooking()
    {
        var context = CreateContext();
        var startAt = Now.AddHours(-2);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "started-key") with
        {
            StartAt = startAt,
            EndAt = startAt.AddMinutes(30)
        };
        var existing = Booking.Create(
            context.UserId,
            context.Profile.Id,
            context.Service.Id,
            SalonId,
            request.StartAt,
            request.EndAt,
            context.Service.Price,
            request.IdempotencyKey).Value;
        context.Bookings.Add(existing);
        var handler = CreateHandler(context);

        // Повтор запроса по ключу обязан вернуть бронь, даже если её начало уже прошло:
        // идемпотентность важнее окна, иначе оборванный ретрай превратится в 400.
        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, result.Value);
        Assert.Equal(0, context.Bookings.AddCalls);
    }

    private static CreateBookingHandler CreateHandler(
        TestContext context,
        Guid? userId = null,
        DateTimeOffset? now = null,
        FakeUnitOfWork? unitOfWork = null)
        => new(
            context.Bookings,
            context.Profiles,
            context.Salons,
            unitOfWork ?? new FakeUnitOfWork(),
            new FakeCurrentUser(userId ?? context.UserId),
            new FakeTimeProvider(now ?? Now),
            NullLogger<CreateBookingHandler>.Instance);

    private static CreateBookingRequest CreateRequest(
        Guid barberProfileId,
        Guid serviceId,
        string idempotencyKey)
    {
        var startAt = Now.AddDays(7);

        return new CreateBookingRequest(
            barberProfileId,
            serviceId,
            SalonId,
            startAt,
            startAt.AddMinutes(30),
            idempotencyKey,
            null);
    }

    private static TestContext CreateContext(
        decimal price = 1500m,
        bool active = true,
        bool salonExists = true,
        bool worksAtSalon = true)
    {
        var profiles = new FakeBarberProfileRepository();
        var (profile, service) = CreateProfileWithService(price, active);
        profiles.Add(profile);

        if (worksAtSalon)
        {
            Assert.True(profile.AddSalon(SalonId).IsSuccess);
        }

        var salons = new FakeSalonRepository();

        if (salonExists)
        {
            salons.AddExisting(SalonId);
        }

        return new TestContext(
            Guid.CreateVersion7(),
            profiles,
            new FakeBookingRepository(),
            salons,
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
        FakeSalonRepository Salons,
        BarberProfile Profile,
        BarberService Service);
}
