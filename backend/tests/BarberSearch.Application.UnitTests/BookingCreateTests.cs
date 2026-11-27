using System.Reflection;
using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.Bookings.CreateBooking;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Domain;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.Users;
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
    private static readonly Salon Salon = CreateSalon();
    private static readonly Guid SalonId = Salon.Id;

    /// <summary>
    /// «Сейчас» для тестов окна брони: фиксированные часы вместо системных.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Начало по умолчанию: неделя вперёд, 13:00 по Берлину (CET) — легальный старт
    /// получасовой сетки 09:00–18:00.
    /// </summary>
    private static readonly DateTimeOffset DefaultStartAt = Now.AddDays(7);

    /// <summary>Локальная дата салона для <see cref="DefaultStartAt"/>.</summary>
    private static readonly DateOnly DefaultStartDate = new(2026, 10, 29);

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
            StartAt = request.StartAt.AddHours(1)
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
    /// Конец визита выводит сервер из <c>BarberService.DurationMinutes</c>: клиент задаёт
    /// только начало, поэтому «тридцать минут по цене сорока пяти» не проходят.
    /// </summary>
    [Fact]
    public async Task Create_EndIsDerivedFromServiceDuration()
    {
        var context = CreateContext(durationMinutes: 45);

        // 13:30 по Берлину — легальный старт сетки с шагом 45 минут от 09:00.
        var startAt = new DateTimeOffset(2026, 10, 29, 12, 30, 0, TimeSpan.Zero);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "duration-key") with
        {
            StartAt = startAt
        };
        var handler = CreateHandler(context);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var booking = Assert.Single(context.Bookings.Bookings);
        Assert.Equal(startAt, booking.StartAt);
        Assert.Equal(45, (booking.EndAt - booking.StartAt).TotalMinutes);
        Assert.Equal(startAt.AddMinutes(45), booking.EndAt);
    }

    /// <summary>
    /// Публичный путь требует точного совпадения со стартом сетки: 13:07 помещается
    /// в рабочий день, но не является началом слота, поэтому запись отклоняется.
    /// </summary>
    [Fact]
    public async Task Create_PublicOffGridStart_ReturnsNotASlot()
    {
        var context = CreateContext();

        // 13:07 по Берлину: внутри 09:00–18:00, но между стартами 13:00 и 13:30.
        var startAt = new DateTimeOffset(2026, 10, 29, 12, 7, 0, TimeSpan.Zero);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "off-grid-key") with
        {
            StartAt = startAt
        };
        var handler = CreateHandler(context);

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.startAt.notASlot", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Bookings.AddCalls);
    }

    /// <summary>
    /// Начало внутри отпуска не «занимает» слот, а находится вне расписания: клиент
    /// получает тот же код, что и для выходного, а не «время занято».
    /// </summary>
    [Fact]
    public async Task Create_PublicStartAtInTimeOff_ReturnsOutsideSchedule()
    {
        var timeOff = new TimeOffInterval(
            new DateTimeOffset(2026, 10, 29, 11, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 29, 12, 30, 0, TimeSpan.Zero));
        var context = CreateContext(timeOffs: [timeOff]);
        var handler = CreateHandler(context);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "time-off-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.startAt.outsideSchedule", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Bookings.AddCalls);
    }

    /// <summary>
    /// Выходной барбера — тоже «вне расписания»: контекст дня говорит, что дня нет,
    /// и до сетки дело не доходит.
    /// </summary>
    [Fact]
    public async Task Create_PublicStartOnDayOff_ReturnsOutsideSchedule()
    {
        var context = CreateContext(isWorkingDay: false);
        var handler = CreateHandler(context);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "day-off-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.startAt.outsideSchedule", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Bookings.AddCalls);
    }

    /// <summary>
    /// Ручная запись барбера не обязана попадать в шаг сетки: клиент мог прийти к 13:07,
    /// и визит помещается в рабочий день. Конец по-прежнему считает сервер.
    /// </summary>
    [Fact]
    public async Task CreateByBarber_OffGridStartThatFits_CreatesBooking()
    {
        var context = CreateContext();
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var startAt = new DateTimeOffset(2026, 10, 29, 12, 7, 0, TimeSpan.Zero);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "barber-off-grid-key") with
        {
            StartAt = startAt
        };

        var result = await handler.Handle(
            new CreateBookingCommand(
                request,
                ConfirmImmediately: true,
                GuestFirstName: "Ivan",
                GuestPhone: "+7 999 123-45-67"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var booking = Assert.Single(context.Bookings.Bookings);
        Assert.Equal(startAt, booking.StartAt);
        Assert.Equal(30, (booking.EndAt - booking.StartAt).TotalMinutes);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    /// <summary>
    /// Ручная запись проверяет занятость, даже если начало не совпадает с сеткой:
    /// 13:07 пересекается с визитом 13:00–13:30 и получает отдельный код «время занято».
    /// </summary>
    [Fact]
    public async Task CreateByBarber_StartThatOverlaps_ReturnsOverlap()
    {
        var context = CreateContext();
        var existing = Booking.Create(
            context.UserId,
            context.Profile.Id,
            context.Service.Id,
            SalonId,
            DefaultStartAt,
            DefaultStartAt.AddMinutes(30),
            context.Service.Price,
            "manual-overlap-existing-key").Value;
        context.Bookings.Add(existing);
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "manual-overlap-key") with
        {
            StartAt = DefaultStartAt.AddMinutes(7)
        };

        var result = await handler.Handle(
            new CreateBookingCommand(
                request,
                ConfirmImmediately: true,
                GuestFirstName: "Ivan",
                GuestPhone: "+7 999 123-45-67"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.startAt.overlap", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Bookings.AddCalls);
    }

    /// <summary>
    /// Ни цены, ни конца визита клиент не задаёт: поле <c>Price</c> удалено в R-33, а
    /// <c>EndAt</c> — вместе с переходом на длительность услуги. Контракт не должен даже
    /// предлагать такие свойства или параметры конструктора, иначе старый клиент решит,
    /// что ими можно управлять. За цену отвечает <c>BarberService.Price</c>, за конец —
    /// <c>BarberService.DurationMinutes</c>.
    /// </summary>
    [Fact]
    public void CreateBookingRequest_HasNoPriceOrEndAtProperty()
    {
        var properties = typeof(CreateBookingRequest).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);
        Assert.DoesNotContain(properties, property => property.Name == "Price");
        Assert.DoesNotContain(properties, property => property.Name == "EndAt");

        var constructor = Assert.Single(typeof(CreateBookingRequest).GetConstructors());
        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => parameter.Name == "Price");
        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => parameter.Name == "EndAt");
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
            request.StartAt.AddMinutes(context.Service.DurationMinutes),
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
            request.StartAt.AddMinutes(context.Service.DurationMinutes),
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
            request.StartAt.AddMinutes(context.Service.DurationMinutes),
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
            StartAt = startAt
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

        // 13:30 по Берлину — легальный старт получасовой сетки. «Сейчас» ставится на пять
        // минут позже, поэтому начало ровно на границе допуска, но всё ещё не в прошлом.
        var startAt = new DateTimeOffset(2026, 10, 22, 12, 30, 0, TimeSpan.Zero);
        var handler = CreateHandler(
            context,
            now: startAt.AddMinutes(Constants.BOOKING_START_TOLERANCE_MINUTES));
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "tolerance-key") with
        {
            StartAt = startAt
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
            StartAt = startAt
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
            StartAt = startAt
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
            StartAt = startAt
        };
        var existing = Booking.Create(
            context.UserId,
            context.Profile.Id,
            context.Service.Id,
            SalonId,
            request.StartAt,
            request.StartAt.AddMinutes(context.Service.DurationMinutes),
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

    [Fact]
    public async Task CreateByBarber_ForGuest_CreatesConfirmedBookingWithGuestContact()
    {
        var context = CreateContext();
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "guest-key");

        var result = await handler.Handle(
            new CreateBookingCommand(
                request,
                ConfirmImmediately: true,
                GuestFirstName: "  Ivan  ",
                GuestLastName: " Petrov ",
                GuestPhone: " +7 999 123-45-67 "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var booking = Assert.Single(context.Bookings.Bookings);
        Assert.Null(booking.UserId);
        Assert.Equal("Ivan", booking.GuestFirstName);
        Assert.Equal("Petrov", booking.GuestLastName);
        Assert.Equal("+7 999 123-45-67", booking.GuestPhone);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task CreateByBarber_WithoutCustomerAndGuest_ReturnsCustomerRequired()
    {
        var context = CreateContext();
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "no-client-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request, ConfirmImmediately: true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.customer", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Bookings.AddCalls);
    }

    [Fact]
    public async Task CreateByBarber_WithCustomerAndGuest_ReturnsAmbiguous()
    {
        var context = CreateContext();
        var customer = CreateCustomer("ambiguous@example.com");
        context.Users.Add(customer);
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "ambiguous-key");

        var result = await handler.Handle(
            new CreateBookingCommand(
                request,
                customer.Id,
                ConfirmImmediately: true,
                GuestFirstName: "Ivan",
                GuestPhone: "+7 999 123-45-67"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.customerAmbiguous", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Bookings.AddCalls);
    }

    [Fact]
    public async Task Create_PublicPathWithGuestFields_ReturnsAmbiguous()
    {
        var context = CreateContext();
        var handler = CreateHandler(context);
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "public-guest-key");

        var result = await handler.Handle(
            new CreateBookingCommand(
                request,
                GuestFirstName: "Ivan",
                GuestPhone: "+7 999 123-45-67"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.customerAmbiguous", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Bookings.AddCalls);
    }

    [Fact]
    public async Task CreateByBarber_GuestWithBlankPhone_ReturnsGuestPhoneValidation()
    {
        var context = CreateContext();
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "blank-phone-key");

        var result = await handler.Handle(
            new CreateBookingCommand(
                request,
                ConfirmImmediately: true,
                GuestFirstName: "Ivan",
                GuestPhone: "   "),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.guestPhone", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Bookings.AddCalls);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("+7 495 123-45-67")]
    [InlineData("+49 170 1234567")]
    public async Task CreateByBarber_GuestWithInvalidPhone_ReturnsGuestPhoneValidation(string guestPhone)
    {
        var context = CreateContext();
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "invalid-phone-key");

        var result = await handler.Handle(
            new CreateBookingCommand(
                request,
                ConfirmImmediately: true,
                GuestFirstName: "Ivan",
                GuestPhone: guestPhone),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.guestPhone", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Bookings.AddCalls);
    }

    [Fact]
    public async Task CreateByBarber_ForeignProfileForGuest_ReturnsForbidden()
    {
        var context = CreateContext();
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: Guid.CreateVersion7());
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "foreign-guest-key");

        var result = await handler.Handle(
            new CreateBookingCommand(
                request,
                ConfirmImmediately: true,
                GuestFirstName: "Ivan",
                GuestPhone: "+7 999 123-45-67"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Empty(context.Bookings.Bookings);
    }

    [Fact]
    public async Task CreateByBarber_RepeatedGuestKey_ReturnsSameBooking()
    {
        var context = CreateContext();
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "guest-repeat-key");
        var command = new CreateBookingCommand(
            request,
            ConfirmImmediately: true,
            GuestFirstName: "Ivan",
            GuestPhone: "+7 999 123-45-67");

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value, second.Value);
        Assert.Single(context.Bookings.Bookings);
        Assert.Equal(1, context.Bookings.AddCalls);
    }

    [Fact]
    public async Task CreateByBarber_GuestIdempotencyKey_IsScopedByBarberProfile()
    {
        var context = CreateContext();
        var other = CreateProfileWithService(200m);
        Assert.True(other.Profile.AddSalon(SalonId).IsSuccess);
        context.Profiles.Add(other.Profile);
        context.Profiles.SlotContexts[other.Profile.Id] = CreateSlotContext(
            other.Profile.Id,
            other.Service.Id,
            other.Service.HairServiceId);

        var firstHandler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var secondHandler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: other.Profile.Id);

        // Один и тот же ключ у разных барберов не конфликтует: у гостя user_id = NULL,
        // поэтому ключ скоупится парой (barber_profile_id, idempotency_key).
        var firstRequest = CreateRequest(context.Profile.Id, context.Service.Id, "shared-guest-key");
        var secondRequest = CreateRequest(other.Profile.Id, other.Service.Id, "shared-guest-key");

        var first = await firstHandler.Handle(
            new CreateBookingCommand(
                firstRequest,
                ConfirmImmediately: true,
                GuestFirstName: "Ivan",
                GuestPhone: "+7 999 123-45-67"),
            CancellationToken.None);
        var second = await secondHandler.Handle(
            new CreateBookingCommand(
                secondRequest,
                ConfirmImmediately: true,
                GuestFirstName: "Petr",
                GuestPhone: "+7 999 765-43-21"),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value, second.Value);
        Assert.Equal(2, context.Bookings.Bookings.Count);
    }

    [Fact]
    public async Task CreateByBarber_ForCustomer_CreatesConfirmedBookingForCustomer()
    {
        var context = CreateContext();
        var customer = CreateCustomer("client@example.com");
        context.Users.Add(customer);
        var actorId = Guid.CreateVersion7();
        var handler = CreateHandler(context, actorId, context.Profile.Id);
        var request = CreateRequest(context.Profile.Id, context.Service.Id, "barber-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request, customer.Id, ConfirmImmediately: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var booking = Assert.Single(context.Bookings.Bookings);
        Assert.Equal(customer.Id, booking.UserId);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.NotEqual(actorId, booking.UserId);
    }

    [Fact]
    public async Task CreateByBarber_ForCustomer_PassesTheBarberCreatedFlag()
    {
        var context = CreateContext();
        var customer = CreateCustomer("push-barber-client@example.com");
        context.Users.Add(customer);
        var notifier = new FakeBookingCreatedPushNotifier();
        var handler = CreateHandler(
            context,
            Guid.CreateVersion7(),
            context.Profile.Id,
            notifier: notifier);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "push-barber-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request, customer.Id, ConfirmImmediately: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, notifier.Calls);
        Assert.True(notifier.LastCreatedByBarber);
        Assert.NotNull(notifier.LastBooking);
    }

    [Fact]
    public async Task Create_ByCustomer_PassesTheCustomerCreatedFlag()
    {
        var context = CreateContext();
        var notifier = new FakeBookingCreatedPushNotifier();
        var handler = CreateHandler(context, notifier: notifier);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "push-customer-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, notifier.Calls);
        Assert.False(notifier.LastCreatedByBarber);
    }

    [Fact]
    public async Task Create_WhenPushNotifierFails_StillCreatesTheBooking()
    {
        var context = CreateContext();
        var notifier = new FakeBookingCreatedPushNotifier
        {
            Failure = new Failure(Error.Failure(
                "internal.database",
                "The push notification could not be saved."))
        };
        var handler = CreateHandler(context, notifier: notifier);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "push-failure-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.Bookings.Bookings);
        Assert.Equal(1, notifier.Calls);
    }

    [Fact]
    public async Task Create_WhenPushNotifierThrows_StillCreatesTheBooking()
    {
        var context = CreateContext();
        var notifier = new FakeBookingCreatedPushNotifier
        {
            Exception = new InvalidOperationException("push failed")
        };
        var handler = CreateHandler(context, notifier: notifier);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "push-exception-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.Bookings.Bookings);
        Assert.Equal(1, notifier.Calls);
    }

    [Fact]
    public async Task CreateByBarber_ForForeignProfile_ReturnsForbiddenAndWritesNothing()
    {
        var context = CreateContext();
        var customer = CreateCustomer("client@example.com");
        context.Users.Add(customer);
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: Guid.CreateVersion7());
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "foreign-profile-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request, customer.Id, ConfirmImmediately: true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
        Assert.Empty(context.Bookings.Bookings);
    }

    [Fact]
    public async Task CreateByBarber_WithoutProfileClaim_ReturnsForbidden()
    {
        var context = CreateContext();
        var customer = CreateCustomer("client@example.com");
        context.Users.Add(customer);
        var handler = CreateHandler(context, userId: Guid.CreateVersion7());
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "no-profile-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request, customer.Id, ConfirmImmediately: true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Empty(context.Bookings.Bookings);
    }

    [Fact]
    public async Task CreateByBarber_UnknownCustomer_ReturnsNotFound()
    {
        var context = CreateContext();
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "unknown-customer-key");

        var result = await handler.Handle(
            new CreateBookingCommand(
                request,
                Guid.CreateVersion7(),
                ConfirmImmediately: true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("booking.create.customerNotFound", result.Error.Errors[0].Code);
        Assert.Equal(0, context.Bookings.AddCalls);
    }

    [Fact]
    public async Task CreateByBarber_InactiveCustomer_ReturnsNotFound()
    {
        var context = CreateContext();
        var customer = CreateCustomer("inactive@example.com");
        Assert.True(customer.Deactivate().IsSuccess);
        context.Users.Add(customer);
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "inactive-customer-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request, customer.Id, ConfirmImmediately: true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("booking.create.customerNotFound", result.Error.Errors[0].Code);
        Assert.Empty(context.Bookings.Bookings);
    }

    [Fact]
    public async Task CreateByBarber_ForAdmin_Succeeds()
    {
        var context = CreateContext();
        var admin = User.Create(
            "admin@example.com",
            "hash",
            "Admin",
            "User",
            UserRole.Admin).Value;
        context.Users.Add(admin);
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "admin-customer-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request, admin.Id, ConfirmImmediately: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var booking = Assert.Single(context.Bookings.Bookings);
        Assert.Equal(admin.Id, booking.UserId);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task CreateByBarber_ForAnotherBarber_Succeeds()
    {
        var context = CreateContext();
        var barber = User.Create(
            "barber@example.com",
            "hash",
            "Boris",
            "Barber",
            UserRole.Barber).Value;
        context.Users.Add(barber);
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "barber-customer-key");

        var result = await handler.Handle(
            new CreateBookingCommand(request, barber.Id, ConfirmImmediately: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var booking = Assert.Single(context.Bookings.Bookings);
        Assert.Equal(barber.Id, booking.UserId);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task CreateByBarber_RepeatedKey_ReturnsSameBooking()
    {
        var context = CreateContext();
        var customer = CreateCustomer("repeat-client@example.com");
        context.Users.Add(customer);
        var handler = CreateHandler(
            context,
            userId: Guid.CreateVersion7(),
            barberProfileId: context.Profile.Id);
        var request = CreateRequest(
            context.Profile.Id,
            context.Service.Id,
            "barber-repeat-key");
        var command = new CreateBookingCommand(
            request,
            customer.Id,
            ConfirmImmediately: true);

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value, second.Value);
        Assert.Single(context.Bookings.Bookings);
        Assert.Equal(1, context.Bookings.AddCalls);
    }

    private static User CreateCustomer(string email)
        => User.Create(email, "hash", "Ivan", "Petrov").Value;

    private static CreateBookingHandler CreateHandler(
        TestContext context,
        Guid? userId = null,
        Guid? barberProfileId = null,
        DateTimeOffset? now = null,
        FakeUnitOfWork? unitOfWork = null,
        FakeBookingCreatedPushNotifier? notifier = null,
        FakeBarberBookingPushNotifier? barberNotifier = null)
        => new(
            context.Bookings,
            context.Profiles,
            context.Salons,
            context.Users,
            unitOfWork ?? new FakeUnitOfWork(),
            new FakeCurrentUser(
                userId ?? context.UserId,
                barberProfileId: barberProfileId),
            notifier ?? new FakeBookingCreatedPushNotifier(),
            barberNotifier ?? new FakeBarberBookingPushNotifier(),
            new FakeTimeProvider(now ?? Now),
            NullLogger<CreateBookingHandler>.Instance);

    private static CreateBookingRequest CreateRequest(
        Guid barberProfileId,
        Guid serviceId,
        string idempotencyKey)
        => new(
            barberProfileId,
            serviceId,
            SalonId,
            DefaultStartAt,
            idempotencyKey,
            null);

    private static TestContext CreateContext(
        decimal price = 1500m,
        bool active = true,
        bool salonExists = true,
        bool worksAtSalon = true,
        int durationMinutes = 30,
        bool isWorkingDay = true,
        IReadOnlyList<TimeOffInterval>? timeOffs = null)
    {
        var profiles = new FakeBarberProfileRepository();
        var (profile, service) = CreateProfileWithService(price, active, durationMinutes);

        // Создание брони читает контекст дня: без него любой запрос отвечает 404.
        // Расписание совпадает с сеткой из документации: 09:00–18:00 по Берлину.
        profiles.SlotContext = CreateSlotContext(
            profile.Id,
            service.Id,
            service.HairServiceId,
            durationMinutes,
            isWorkingDay,
            timeOffs);
        profiles.Add(profile);

        if (worksAtSalon)
        {
            Assert.True(profile.AddSalon(SalonId).IsSuccess);
        }

        var salons = new FakeSalonRepository();

        if (salonExists)
        {
            // Создание брони читает салон целиком (нужен TimeZoneId), поэтому одного
            // признака существования мало: в фейке должна быть настоящая строка салона.
            salons.AddSalon(Salon);
        }

        return new TestContext(
            Guid.CreateVersion7(),
            new FakeUserRepository(),
            profiles,
            new FakeBookingRepository(),
            salons,
            profile,
            service);
    }

    private static Salon CreateSalon(string timeZoneId = "Europe/Berlin")
        => Salon.Create(
            name: "Booking Salon",
            address: "Teststrasse 1",
            city: "Berlin",
            country: "Germany",
            timeZoneId: timeZoneId,
            latitude: 52.5200,
            longitude: 13.4050).Value;

    private static BarberSlotContext CreateSlotContext(
        Guid barberProfileId,
        Guid barberServiceId,
        Guid hairServiceId,
        int durationMinutes = 30,
        bool isWorkingDay = true,
        IReadOnlyList<TimeOffInterval>? timeOffs = null)
        => new(
            barberProfileId,
            SalonId,
            "Europe/Berlin",
            DefaultStartDate,
            isWorkingDay,
            new TimeOnly(9, 0),
            new TimeOnly(18, 0),
            durationMinutes,
            barberServiceId,
            hairServiceId,
            timeOffs ?? []);

    private static (BarberProfile Profile, BarberService Service) CreateProfileWithService(
        decimal price,
        bool active = true,
        int durationMinutes = 30)
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

        Assert.True(profile.AddService(Guid.CreateVersion7(), price, durationMinutes).IsSuccess);

        var service = profile.Services.Single();

        if (!active)
        {
            Assert.True(service.Deactivate().IsSuccess);
        }

        return (profile, service);
    }

    private sealed record TestContext(
        Guid UserId,
        FakeUserRepository Users,
        FakeBarberProfileRepository Profiles,
        FakeBookingRepository Bookings,
        FakeSalonRepository Salons,
        BarberProfile Profile,
        BarberService Service);
}
