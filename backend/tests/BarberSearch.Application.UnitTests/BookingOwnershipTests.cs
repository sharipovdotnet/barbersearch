using BarberSearch.Application.Bookings.AddBookingBarberNote;
using BarberSearch.Application.Bookings.CancelBooking;
using BarberSearch.Application.Bookings.ConfirmBooking;
using BarberSearch.Application.Bookings.MarkBookingNoShow;
using BarberSearch.Application.Push.BookingStatus;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Bookings;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

public class BookingOwnershipTests
{
    private static Booking CreateBooking(Guid clientId, Guid barberProfileId)
    {
        var startAt = DateTimeOffset.UtcNow.AddHours(1);

        return Booking.Create(
            clientId,
            barberProfileId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddMinutes(45),
            1500m,
            $"key-{Guid.NewGuid()}").Value;
    }

    private static Booking CreateGuestBooking(Guid barberProfileId)
    {
        var startAt = DateTimeOffset.UtcNow.AddHours(1);

        return Booking.Create(
            null,
            barberProfileId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddMinutes(45),
            1500m,
            $"guest-key-{Guid.NewGuid()}",
            guestFirstName: "Иван",
            guestPhone: "+7 999 123-45-67").Value;
    }

    private static BarberProfile CreateProfile(Guid userId)
        => BarberProfile.Create(
            userId,
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

    [Fact]
    public async Task Confirm_ByClient_ReturnsForbidden()
    {
        var clientId = Guid.CreateVersion7();
        var barberId = Guid.CreateVersion7();
        var booking = CreateBooking(clientId, Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(CreateProfile(barberId));
        var push = new FakeBookingStatusPushNotifier();
        var handler = new ConfirmBookingHandler(
            bookings,
            profiles,
            new FakeCurrentUser(clientId),
            push,
            NullLogger<ConfirmBookingHandler>.Instance);

        var result = await handler.Handle(
            new ConfirmBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(0, push.Calls);
    }

    [Fact]
    public async Task Confirm_ByBarberProfileOwner_Succeeds()
    {
        var barberId = Guid.CreateVersion7();
        var profile = CreateProfile(barberId);
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var push = new FakeBookingStatusPushNotifier();
        var handler = new ConfirmBookingHandler(
            bookings,
            profiles,
            new FakeCurrentUser(barberId),
            push,
            NullLogger<ConfirmBookingHandler>.Instance);

        var result = await handler.Handle(
            new ConfirmBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);

        // Барбер подтвердил ожидающую запись — клиент получает событие «подтверждена».
        Assert.Equal(1, push.Calls);
        Assert.Equal(BookingStatusChange.Confirmed, push.LastChange);
        Assert.Equal(booking.Id, push.LastBooking!.Id);
    }

    [Fact]
    public async Task Confirm_ByAdmin_Succeeds()
    {
        var booking = CreateBooking(Guid.CreateVersion7(), Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var push = new FakeBookingStatusPushNotifier();
        var handler = new ConfirmBookingHandler(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            push,
            NullLogger<ConfirmBookingHandler>.Instance);

        var result = await handler.Handle(
            new ConfirmBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Администратор действует как сотрудник салона: клиент получает то же событие.
        Assert.Equal(1, push.Calls);
        Assert.Equal(BookingStatusChange.Confirmed, push.LastChange);
    }

    [Fact]
    public async Task Confirm_WhenPushFails_StillSucceeds()
    {
        var barberId = Guid.CreateVersion7();
        var profile = CreateProfile(barberId);
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var push = new FakeBookingStatusPushNotifier
        {
            Failure = new Failure(Error.Failure(
                "internal.database",
                "The push notification could not be saved."))
        };
        var handler = new ConfirmBookingHandler(
            bookings,
            profiles,
            new FakeCurrentUser(barberId),
            push,
            NullLogger<ConfirmBookingHandler>.Instance);

        var result = await handler.Handle(
            new ConfirmBookingCommand(booking.Id),
            CancellationToken.None);

        // Бронь важнее уведомления: сбой push не откатывает подтверждение.
        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(1, bookings.UpdateCalls);
    }

    [Fact]
    public async Task Confirm_WhenPushThrows_StillSucceeds()
    {
        var barberId = Guid.CreateVersion7();
        var profile = CreateProfile(barberId);
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var push = new FakeBookingStatusPushNotifier
        {
            Exception = new InvalidOperationException("SQL parameters: secret")
        };
        var handler = new ConfirmBookingHandler(
            bookings,
            profiles,
            new FakeCurrentUser(barberId),
            push,
            NullLogger<ConfirmBookingHandler>.Instance);

        var result = await handler.Handle(
            new ConfirmBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task Cancel_ByClient_Succeeds()
    {
        var clientId = Guid.CreateVersion7();
        var booking = CreateBooking(clientId, Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var push = new FakeBookingStatusPushNotifier();
        var handler = new CancelBookingHandler(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(clientId),
            push,
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);

        // Клиент отменил сам: push ему не нужен, он уже знает о своём действии.
        Assert.Equal(0, push.Calls);
    }

    [Fact]
    public async Task Cancel_ByStranger_ReturnsForbidden()
    {
        var booking = CreateBooking(Guid.CreateVersion7(), Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var push = new FakeBookingStatusPushNotifier();
        var handler = new CancelBookingHandler(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(Guid.CreateVersion7()),
            push,
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(0, push.Calls);
    }

    [Fact]
    public async Task Cancel_GuestBookingByClient_ReturnsForbidden()
    {
        // У гостевой брони нет клиента-владельца (UserId = null): отменить её может только
        // барбер профиля или администратор.
        var booking = CreateGuestBooking(Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var push = new FakeBookingStatusPushNotifier();
        var handler = new CancelBookingHandler(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(Guid.CreateVersion7()),
            push,
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(0, bookings.UpdateCalls);
        Assert.Equal(0, push.Calls);
    }

    [Fact]
    public async Task Cancel_GuestBookingByBarberProfileOwner_Succeeds()
    {
        var barberId = Guid.CreateVersion7();
        var profile = CreateProfile(barberId);
        var booking = CreateGuestBooking(profile.Id);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        // У гостевой брони получателя нет: настоящий уведомитель вернёт успешный no-op,
        // поэтому обработчик вызывает его как обычно (см. PushBookingStatusNotifierTests).
        var handler = new CancelBookingHandler(
            bookings,
            profiles,
            new FakeCurrentUser(barberId),
            new FakeBookingStatusPushNotifier(),
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id, "Клиент не пришёл"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [Fact]
    public async Task Cancel_ByBarberProfileOwner_Succeeds()
    {
        var barberId = Guid.CreateVersion7();
        var profile = CreateProfile(barberId);
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var push = new FakeBookingStatusPushNotifier();
        var handler = new CancelBookingHandler(
            bookings,
            profiles,
            new FakeCurrentUser(barberId),
            push,
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id, "  Барбер заболел  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal("Барбер заболел", booking.CancellationReason);

        // Отменил барбер — клиент получает событие «отменена»; причина в push не попадает.
        Assert.Equal(1, push.Calls);
        Assert.Equal(BookingStatusChange.CancelledByBarber, push.LastChange);
    }

    [Fact]
    public async Task Cancel_ByBarberWithoutReason_ReturnsValidationAndKeepsPending()
    {
        var barberId = Guid.CreateVersion7();
        var profile = CreateProfile(barberId);
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var push = new FakeBookingStatusPushNotifier();
        var handler = new CancelBookingHandler(
            bookings,
            profiles,
            new FakeCurrentUser(barberId),
            push,
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("booking.cancel.reasonRequired", result.Error.Errors[0].Code);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Null(booking.CancellationReason);
        Assert.Equal(0, bookings.UpdateCalls);
        Assert.Equal(0, push.Calls);
    }

    [Fact]
    public async Task Cancel_ByBarberWithBlankReason_ReturnsValidationAndKeepsPending()
    {
        var barberId = Guid.CreateVersion7();
        var profile = CreateProfile(barberId);
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var push = new FakeBookingStatusPushNotifier();
        var handler = new CancelBookingHandler(
            bookings,
            profiles,
            new FakeCurrentUser(barberId),
            push,
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id, "   "),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("booking.cancel.reasonRequired", result.Error.Errors[0].Code);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(0, push.Calls);
    }

    [Fact]
    public async Task Cancel_ByAdminWithoutReason_Succeeds()
    {
        var booking = CreateBooking(Guid.CreateVersion7(), Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var push = new FakeBookingStatusPushNotifier();
        var handler = new CancelBookingHandler(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(Guid.CreateVersion7(), isAdmin: true),
            push,
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Null(booking.CancellationReason);

        // Администратор действует как сотрудник салона — клиент получает событие отмены.
        Assert.Equal(1, push.Calls);
        Assert.Equal(BookingStatusChange.CancelledByBarber, push.LastChange);
    }

    [Fact]
    public async Task Cancel_WhenPushFails_StillSucceeds()
    {
        var barberId = Guid.CreateVersion7();
        var profile = CreateProfile(barberId);
        var booking = CreateBooking(Guid.CreateVersion7(), profile.Id);
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var profiles = new FakeBarberProfileRepository();
        profiles.Add(profile);
        var push = new FakeBookingStatusPushNotifier
        {
            Failure = new Failure(Error.Failure(
                "internal.database",
                "The push notification could not be saved."))
        };
        var handler = new CancelBookingHandler(
            bookings,
            profiles,
            new FakeCurrentUser(barberId),
            push,
            NullLogger<CancelBookingHandler>.Instance);

        var result = await handler.Handle(
            new CancelBookingCommand(booking.Id, "Барбер заболел"),
            CancellationToken.None);

        // Отмена уже сохранена: сбой уведомления не превращает её в ошибку.
        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(1, bookings.UpdateCalls);
    }

    [Fact]
    public async Task MarkNoShow_ByStranger_ReturnsForbiddenBeforeTimeRule()
    {
        var booking = CreateBooking(Guid.CreateVersion7(), Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = new MarkBookingNoShowHandler(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(Guid.CreateVersion7()),
            // Время не влияет на сценарий: проверка владения стоит до правила «не раньше начала».
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<MarkBookingNoShowHandler>.Instance);

        var result = await handler.Handle(
            new MarkBookingNoShowCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
    }

    [Fact]
    public async Task AddBarberNote_ByClient_ReturnsForbidden()
    {
        var clientId = Guid.CreateVersion7();
        var booking = CreateBooking(clientId, Guid.CreateVersion7());
        var bookings = new FakeBookingRepository();
        bookings.Add(booking);
        var handler = new AddBookingBarberNoteHandler(
            bookings,
            new FakeBarberProfileRepository(),
            new FakeCurrentUser(clientId),
            NullLogger<AddBookingBarberNoteHandler>.Instance);

        var result = await handler.Handle(
            new AddBookingBarberNoteCommand(booking.Id, "Secret note"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Null(booking.BarberNote);
    }
}
