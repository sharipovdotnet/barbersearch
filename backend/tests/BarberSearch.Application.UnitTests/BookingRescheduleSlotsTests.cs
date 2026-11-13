using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.Bookings.GetBookingRescheduleSlots;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Bookings;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Чтение слотов переноса визита: видимость, окно горизонта, длительность из брони
/// и исключение самой брони из занятости.
/// </summary>
/// <remarks>
/// Контракт — существующий <c>BarberSlotsResponse</c> (D7): отдельного ответа у переноса нет,
/// поэтому проверяется, что идентификаторы и длительность берутся из брони, а не из прайса.
/// </remarks>
public class BookingRescheduleSlotsTests
{
    private static readonly Guid BarberProfileId = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid BarberUserId = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientId = Guid.Parse("c3333333-3333-3333-3333-333333333333");
    private static readonly Guid SalonId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ServiceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly DateOnly Date = new(2026, 11, 21);

    /// <summary>11:00 по Берлину — легальный старт сетки с шагом 30 минут.</summary>
    private static readonly DateTimeOffset BookingStart = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_Anonymous_ReturnsUnauthorized()
    {
        var handler = CreateHandler(currentUser: FakeCurrentUser.Anonymous());

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(Guid.CreateVersion7(), Date),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Handle_MissingBooking_ReturnsNotFound()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(Guid.CreateVersion7(), Date),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("booking.notFound", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_ForeignCustomer_ReturnsForbidden()
    {
        var booking = CreateBooking();
        var handler = CreateHandler(bookings: AddBooking(booking), currentUser: new FakeCurrentUser(Guid.CreateVersion7()));

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, Date),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task Handle_TerminalBooking_ReturnsRescheduleStatus()
    {
        var booking = CreateBooking();
        Assert.True(booking.Cancel().IsSuccess);
        var handler = CreateHandler(bookings: AddBooking(booking));

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, Date),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.status", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_StartedBooking_ReturnsRescheduleStatusBeforeReadingTheGrid()
    {
        var booking = CreateBooking(startAt: Now.AddHours(-1));
        Assert.True(booking.Confirm().IsSuccess);
        var profiles = CreateProfiles();
        var handler = CreateHandler(bookings: AddBooking(booking), profiles: profiles);

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, Date),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.status", result.Error.Errors[0].Code);
        Assert.Empty(profiles.SlotContextLoads);
    }

    [Fact]
    public async Task Handle_WithoutDate_ReturnsRescheduleDate()
    {
        var booking = CreateBooking();
        var handler = CreateHandler(bookings: AddBooking(booking));

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.date", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_DateBeyondTheHorizon_ReturnsRescheduleDate()
    {
        var booking = CreateBooking();
        var handler = CreateHandler(bookings: AddBooking(booking));

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, Today.AddDays(91)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.date", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_UsesBookingDurationInsteadOfTheServicePriceList()
    {
        // В прайсе 30 минут, а в брони — 45: сетка переноса обязана сохранить обещанное клиенту.
        var booking = CreateBooking(startAt: new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero), durationMinutes: 45);
        var handler = CreateHandler(bookings: AddBooking(booking));

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, Date),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(45, result.Value.DurationMinutes);
        Assert.Equal(10, result.Value.Slots.Count);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero),
            result.Value.Slots[0].StartAt);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 21, 9, 45, 0, TimeSpan.Zero),
            result.Value.Slots[1].StartAt);
    }

    [Fact]
    public async Task Handle_ShowsOwnSlotAndHidesForeignBookings()
    {
        var booking = CreateBooking();
        var bookings = AddBooking(booking);

        // Чужая бронь на 12:00 по Берлину занимает слот — его видеть нельзя.
        var foreign = CreateBooking(
            startAt: new DateTimeOffset(2026, 11, 21, 11, 0, 0, TimeSpan.Zero),
            userId: Guid.CreateVersion7());
        bookings.Add(foreign);

        var handler = CreateHandler(bookings: bookings);

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, Date),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Своя бронь слот не занимает: перенести визит можно на то же время (D15).
        Assert.Contains(result.Value.Slots, slot => slot.StartAt == BookingStart);
        Assert.DoesNotContain(
            result.Value.Slots,
            slot => slot.StartAt == new DateTimeOffset(2026, 11, 21, 11, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Handle_CustomerOwner_ReadsTheVisibleSlotContext()
    {
        var booking = CreateBooking();
        var profiles = CreateProfiles();
        var handler = CreateHandler(bookings: AddBooking(booking), profiles: profiles);

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, Date),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["visible"], profiles.SlotContextLoads);
    }

    [Fact]
    public async Task Handle_ProfileBarber_ReadsTheOwnSlotContext()
    {
        var booking = CreateBooking();
        var profiles = CreateProfiles();
        var handler = CreateHandler(
            bookings: AddBooking(booking),
            profiles: profiles,
            currentUser: new FakeCurrentUser(BarberUserId));

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, Date),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["own"], profiles.SlotContextLoads);
    }

    [Fact]
    public async Task Handle_Admin_ReadsTheOwnSlotContext()
    {
        var booking = CreateBooking();
        var profiles = CreateProfiles();
        var handler = CreateHandler(
            bookings: AddBooking(booking),
            profiles: profiles,
            currentUser: new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true));

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, Date),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["own"], profiles.SlotContextLoads);
    }

    [Fact]
    public async Task Handle_TakesIdsAndDurationFromTheBooking()
    {
        var booking = CreateBooking();
        var profiles = CreateProfiles();
        var handler = CreateHandler(bookings: AddBooking(booking), profiles: profiles);

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, Date),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BarberProfileId, result.Value.BarberProfileId);
        Assert.Equal(SalonId, result.Value.SalonId);
        Assert.Equal(ServiceId, result.Value.BarberServiceId);
        Assert.Null(result.Value.HairServiceId);
        Assert.Equal(Date, result.Value.Date);
        Assert.Equal("Europe/Berlin", result.Value.TimeZoneId);

        // Длительность берётся из брони: услуга справочника в контекст не передаётся.
        Assert.Null(profiles.LastSlotContextHairServiceId);
        Assert.Equal(SalonId, profiles.LastSlotContextSalonId);
        Assert.Equal(Date, profiles.LastSlotContextDate);
    }

    [Fact]
    public async Task Handle_NonWorkingDay_ReturnsEmptySlotsButExplicitBounds()
    {
        var booking = CreateBooking();
        var handler = CreateHandler(
            bookings: AddBooking(booking),
            profiles: CreateProfiles(isWorkingDay: false));

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, Date),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsWorkingDay);
        Assert.Empty(result.Value.Slots);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 23, 0, 0, TimeSpan.Zero), result.Value.DayStart);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 23, 0, 0, TimeSpan.Zero), result.Value.DayEnd);
    }

    [Fact]
    public async Task Handle_UnresolvableTimeZone_ReturnsSlotConflict()
    {
        var booking = CreateBooking();
        var handler = CreateHandler(
            bookings: AddBooking(booking),
            profiles: CreateProfiles(timeZoneId: "Not/AZone"));

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, Date),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("booking.reschedule.slot", result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_OffersOnlyFutureSlotsWithinTheHorizon()
    {
        var booking = CreateBooking(
            startAt: new DateTimeOffset(2026, 11, 21, 13, 0, 0, TimeSpan.Zero));
        var handler = CreateHandler(
            bookings: AddBooking(booking),
            now: new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(booking.Id, Date),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 21, 12, 30, 0, TimeSpan.Zero),
            result.Value.Slots[0].StartAt);
        Assert.All(
            result.Value.Slots,
            slot => Assert.True(slot.StartAt > new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task Handle_SlotsBeyondTheBookingHorizon_AreNotOffered()
    {
        var booking = CreateBooking();
        var lastDate = Today.AddDays(90);
        var bookingOnLastDate = CreateBooking(
            startAt: new DateTimeOffset(2027, 2, 18, 9, 0, 0, TimeSpan.Zero));
        var handler = CreateHandler(
            bookings: AddBooking(bookingOnLastDate),
            profiles: CreateProfiles(date: lastDate));

        var result = await handler.Handle(
            new GetBookingRescheduleSlotsQuery(bookingOnLastDate.Id, lastDate),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Те же 09:00Z — ровно граница горизонта, поэтому она ещё допустима; всё позже — нет.
        Assert.Single(result.Value.Slots);
        Assert.Equal(
            new DateTimeOffset(2027, 2, 18, 9, 0, 0, TimeSpan.Zero),
            result.Value.Slots[0].StartAt);
        Assert.NotEqual(booking.Id, bookingOnLastDate.Id);
    }

    private static GetBookingRescheduleSlotsHandler CreateHandler(
        FakeBookingRepository? bookings = null,
        FakeBarberProfileRepository? profiles = null,
        FakeCurrentUser? currentUser = null,
        DateTimeOffset? now = null)
        => new(
            bookings ?? new FakeBookingRepository(),
            profiles ?? CreateProfiles(),
            currentUser ?? new FakeCurrentUser(ClientId),
            new FakeTimeProvider(now ?? Now),
            NullLogger<GetBookingRescheduleSlotsHandler>.Instance);

    private static FakeBookingRepository AddBooking(Booking booking)
    {
        var repository = new FakeBookingRepository();
        repository.Add(booking);

        return repository;
    }

    private static FakeBarberProfileRepository CreateProfiles(
        DateOnly? date = null,
        bool isWorkingDay = true,
        string timeZoneId = "Europe/Berlin",
        int durationMinutes = 30)
    {
        var profile = BarberProfile.Create(
            BarberUserId,
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
        Assert.True(profile.Verify().IsSuccess);

        var repository = new FakeBarberProfileRepository
        {
            SlotContext = new BarberSlotContext(
                BarberProfileId,
                SalonId,
                timeZoneId,
                date ?? Date,
                isWorkingDay,
                new TimeOnly(10, 0),
                new TimeOnly(18, 0),
                durationMinutes,
                BarberServiceId: null,
                HairServiceId: null,
                TimeOffs: [])
        };
        repository.Add(profile);
        repository.OwnershipOverrides[BarberProfileId] = BarberUserId;

        return repository;
    }

    private static Booking CreateBooking(
        DateTimeOffset? startAt = null,
        int durationMinutes = 30,
        Guid? userId = null,
        BookingStatus status = BookingStatus.Pending)
    {
        var start = startAt ?? BookingStart;
        var booking = Booking.Create(
            userId ?? ClientId,
            BarberProfileId,
            ServiceId,
            SalonId,
            start,
            start.AddMinutes(durationMinutes),
            45m,
            $"key-{Guid.NewGuid():N}").Value;

        if (status == BookingStatus.Confirmed)
        {
            Assert.True(booking.Confirm().IsSuccess);
        }

        return booking;
    }
}
