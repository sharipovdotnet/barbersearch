using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.Bookings.RescheduleBooking;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Перенос визита: права, окно времени, сетка по длительности брони, занятость и порядок
/// «блокировка → чтение → проверки → запись».
/// </summary>
/// <remarks>
/// Фейки повторяют фильтры настоящих репозиториев: активная бронь занимает слот, отменённая —
/// нет; чтение контекста дня различает публичную и «свою» видимость. Порядок вызовов важен:
/// бронь, прочитанная до advisory-блокировки, была бы устаревшей (D8).
/// </remarks>
public class BookingRescheduleTests
{
    private static readonly Guid BarberProfileId = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid BarberUserId = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientId = Guid.Parse("c3333333-3333-3333-3333-333333333333");
    private static readonly Guid ServiceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 11, 21);

    /// <summary>11:00 по Берлину — легальный старт сетки с шагом 30 минут.</summary>
    private static readonly DateTimeOffset BookingStart = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    /// <summary>12:00 по Берлину — свободный слот того же дня.</summary>
    private static readonly DateTimeOffset FreeSlot = new(2026, 11, 21, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_ClientOwner_MovesOnlyTheTime()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        Assert.True(booking.Confirm().IsSuccess);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(bookings, salon);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FreeSlot, booking.StartAt);
        Assert.Equal(FreeSlot.AddMinutes(30), booking.EndAt);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(45m, booking.Price);
        Assert.Equal(1, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Handle_KeepsTheBookingDurationInsteadOfTheServicePriceList()
    {
        // В прайсе 30 минут, в брони — 45: шаг сетки переноса обязан быть 45.
        var salon = CreateSalon();
        var booking = CreateBooking(
            salon.Id,
            startAt: new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero),
            durationMinutes: 45);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(bookings, salon, contextDurationMinutes: 30);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, new DateTimeOffset(2026, 11, 21, 11, 15, 0, TimeSpan.Zero)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 11, 15, 0, TimeSpan.Zero), booking.StartAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero), booking.EndAt);
    }

    [Fact]
    public async Task Handle_TargetThatIsNotAGridStart_ReturnsSlotConflict()
    {
        // 11:00Z — старт сетки с шагом 30, но не с шагом 45, который задаёт длительность брони.
        var salon = CreateSalon();
        var booking = CreateBooking(
            salon.Id,
            startAt: new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero),
            durationMinutes: 45);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(bookings, salon);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.slot", result.Error.Errors[0].Code);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Handle_SameStartIsANoOpWithoutAWrite()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        var salons = CreateSalons(salon);
        var handler = CreateHandler(bookings, salon, salons);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, BookingStart),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStart, booking.StartAt);
        Assert.Equal(0, bookings.UpdateCalls);
        Assert.Equal(0, salons.GetByIdCalls);
    }

    [Fact]
    public async Task Handle_SameStartOnTerminalBooking_ReturnsRescheduleStatus()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        Assert.True(booking.Cancel().IsSuccess);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(bookings, salon);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, BookingStart),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.status", result.Error.Errors[0].Code);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Handle_Anonymous_ReturnsUnauthorized()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var handler = CreateHandler(
            AddBooking(booking),
            salon,
            currentUser: FakeCurrentUser.Anonymous());

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Handle_MissingBooking_ReturnsNotFoundBeforeTheLock()
    {
        var salon = CreateSalon();
        var handler = CreateHandler(new FakeBookingRepository(), salon);

        var result = await handler.Handle(
            new RescheduleBookingCommand(Guid.CreateVersion7(), FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("booking.notFound", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_ForeignCustomer_ReturnsForbidden()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(
            bookings,
            salon,
            currentUser: new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Handle_ProfileBarber_MovesTheBooking()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(
            bookings,
            salon,
            currentUser: new FakeCurrentUser(BarberUserId));

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FreeSlot, booking.StartAt);
    }

    [Fact]
    public async Task Handle_OtherBarber_ReturnsForbidden()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var profiles = CreateProfiles(salon.Id);
        profiles.OwnershipOverrides[BarberProfileId] = Guid.CreateVersion7();
        var bookings = AddBooking(booking);
        var handler = CreateHandler(
            bookings,
            salon,
            profiles: profiles,
            currentUser: new FakeCurrentUser(BarberUserId));

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Handle_Admin_MovesTheBookingOfAnotherCustomer()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(
            bookings,
            salon,
            currentUser: new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FreeSlot, booking.StartAt);
    }

    [Fact]
    public async Task Handle_GuestBooking_CustomerIsForbidden()
    {
        var salon = CreateSalon();
        var booking = CreateGuestBooking(salon.Id);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(bookings, salon);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Handle_GuestBooking_BarberMovesIt()
    {
        var salon = CreateSalon();
        var booking = CreateGuestBooking(salon.Id);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(
            bookings,
            salon,
            currentUser: new FakeCurrentUser(BarberUserId));

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FreeSlot, booking.StartAt);
        Assert.Null(booking.UserId);
    }

    [Fact]
    public async Task Handle_TargetInThePast_ReturnsStartAtInPast()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(bookings, salon);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, Now.AddMinutes(-1)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.startAt.inPast", result.Error.Errors[0].Code);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Handle_TargetBeyondTheHorizon_ReturnsStartAtTooFar()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(bookings, salon);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, Now.AddDays(91)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.startAt.tooFar", result.Error.Errors[0].Code);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Handle_AlreadyStartedBooking_ReturnsRescheduleStatus()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id, startAt: Now.AddHours(-1));
        Assert.True(booking.Confirm().IsSuccess);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(bookings, salon);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.status", result.Error.Errors[0].Code);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Handle_TerminalBooking_ReturnsRescheduleStatus()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        Assert.True(booking.Confirm().IsSuccess);
        Assert.True(booking.Complete().IsSuccess);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(bookings, salon);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.status", result.Error.Errors[0].Code);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Handle_DayOff_ReturnsSlotConflict()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(bookings, salon, isWorkingDay: false);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("booking.reschedule.slot", result.Error.Errors[0].Code);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Handle_TargetOutsideWorkingHours_ReturnsSlotConflict()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(bookings, salon);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("booking.reschedule.slot", result.Error.Errors[0].Code);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Handle_TargetInsideTimeOff_ReturnsSlotConflict()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        var timeOffs = new[]
        {
            new TimeOffInterval(
                new DateTimeOffset(2026, 11, 21, 10, 30, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 21, 11, 30, 0, TimeSpan.Zero))
        };
        var handler = CreateHandler(bookings, salon, timeOffs: timeOffs);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("booking.reschedule.slot", result.Error.Errors[0].Code);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Handle_TargetOccupiedByAnotherBooking_ReturnsOverlap()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        bookings.Add(CreateBooking(
            salon.Id,
            startAt: FreeSlot,
            userId: Guid.CreateVersion7()));
        var handler = CreateHandler(bookings, salon);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.overlap", result.Error.Errors[0].Code);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Handle_OwnBookingDoesNotBlockTheTargetSlot()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(bookings, salon);

        // Своя бронь не считается занятостью: соседний слот свободен.
        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, BookingStart.AddMinutes(30)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStart.AddMinutes(30), booking.StartAt);
    }

    [Fact]
    public async Task Handle_TakesTheLockBeforeReadingTheBooking()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(bookings, salon);

        await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.Equal(
            ["getBarberProfileId", "lock", "getById"],
            bookings.Operations.Take(3));
        Assert.Equal("booking.reschedule.lockTimeout", bookings.LastLockTimeoutCode);
    }

    [Fact]
    public async Task Handle_LockTimeout_ReturnsRescheduleLockTimeoutAndWritesNothing()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        bookings.OnAcquireLock = _ => UnitResult.Failure<Failure>(new Failure(Error.Conflict(
            "booking.reschedule.lockTimeout",
            "The database lock could not be acquired within the configured timeout.")));
        var handler = CreateHandler(bookings, salon);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.lockTimeout", result.Error.Errors[0].Code);
        Assert.Equal(["getBarberProfileId", "lock"], bookings.Operations);
        Assert.Equal(0, bookings.UpdateCalls);
        Assert.Equal(BookingStart, booking.StartAt);
    }

    [Fact]
    public async Task Handle_ConcurrencyConflictFromUpdate_IsPropagated()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        bookings.OnUpdate = _ => UnitResult.Failure<Failure>(new Failure(Error.Conflict(
            "booking.update.concurrencyConflict",
            "The booking was modified by another request.")));
        var handler = CreateHandler(bookings, salon);

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.update.concurrencyConflict", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_MissingSalon_ReturnsSlotConflict()
    {
        var salon = CreateSalon();
        var booking = CreateBooking(salon.Id);
        var bookings = AddBooking(booking);
        var handler = CreateHandler(
            bookings,
            salon,
            salons: new FakeSalonRepository());

        var result = await handler.Handle(
            new RescheduleBookingCommand(booking.Id, FreeSlot),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.slot", result.Error.Errors[0].Code);
        Assert.Equal(0, bookings.UpdateCalls);
    }

    private static RescheduleBookingHandler CreateHandler(
        FakeBookingRepository bookings,
        Salon salon,
        FakeSalonRepository? salons = null,
        FakeBarberProfileRepository? profiles = null,
        FakeCurrentUser? currentUser = null,
        DateTimeOffset? now = null,
        bool isWorkingDay = true,
        int contextDurationMinutes = 30,
        IReadOnlyList<TimeOffInterval>? timeOffs = null)
    {
        salons ??= CreateSalons(salon);
        profiles ??= CreateProfiles(
            salon.Id,
            isWorkingDay: isWorkingDay,
            durationMinutes: contextDurationMinutes,
            timeOffs: timeOffs);

        return new RescheduleBookingHandler(
            bookings,
            profiles,
            salons,
            new FakeUnitOfWork(salons),
            currentUser ?? new FakeCurrentUser(ClientId),
            new FakeTimeProvider(now ?? Now),
            NullLogger<RescheduleBookingHandler>.Instance);
    }

    private static FakeSalonRepository CreateSalons(Salon salon)
    {
        var repository = new FakeSalonRepository();
        repository.AddSalon(salon);

        return repository;
    }

    private static FakeBarberProfileRepository CreateProfiles(
        Guid salonId,
        bool isWorkingDay = true,
        int durationMinutes = 30,
        IReadOnlyList<TimeOffInterval>? timeOffs = null)
    {
        var repository = new FakeBarberProfileRepository
        {
            SlotContext = new BarberSlotContext(
                BarberProfileId,
                salonId,
                "Europe/Berlin",
                Date,
                isWorkingDay,
                new TimeOnly(10, 0),
                new TimeOnly(18, 0),
                durationMinutes,
                BarberServiceId: null,
                HairServiceId: null,
                TimeOffs: timeOffs ?? [])
        };
        repository.OwnershipOverrides[BarberProfileId] = BarberUserId;

        return repository;
    }

    private static Salon CreateSalon(string timeZoneId = "Europe/Berlin")
        => Salon.Create(
            name: "Reschedule Salon",
            address: "Teststrasse 1",
            city: "Berlin",
            country: "Germany",
            timeZoneId: timeZoneId,
            latitude: 52.5200,
            longitude: 13.4050).Value;

    private static FakeBookingRepository AddBooking(Booking booking)
    {
        var repository = new FakeBookingRepository();
        repository.Add(booking);

        return repository;
    }

    private static Booking CreateBooking(
        Guid salonId,
        DateTimeOffset? startAt = null,
        int durationMinutes = 30,
        Guid? userId = null)
    {
        var start = startAt ?? BookingStart;

        return Booking.Create(
            userId ?? ClientId,
            BarberProfileId,
            ServiceId,
            salonId,
            start,
            start.AddMinutes(durationMinutes),
            45m,
            $"key-{Guid.NewGuid():N}").Value;
    }

    private static Booking CreateGuestBooking(Guid salonId)
        => Booking.Create(
            userId: null,
            BarberProfileId,
            ServiceId,
            salonId,
            BookingStart,
            BookingStart.AddMinutes(30),
            45m,
            $"guest-key-{Guid.NewGuid():N}",
            guestFirstName: "Гость",
            guestPhone: "+70000000000").Value;
}
