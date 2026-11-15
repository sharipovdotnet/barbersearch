using BarberSearch.Application.Push;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory источник визитов для push-напоминаний.
/// </summary>
/// <remarks>
/// Как и <c>FakeReminderSourceRepository</c>, фейк повторяет только те фильтры, которые
/// являются сужением выборки (подтверждённая бронь, зарегистрированный клиент, начало
/// в будущем), а окно <c>now + lead</c> и правило «бронь создана не позже
/// <c>start_at − lead</c>» оставляет обработчику. Иначе тесты не увидели бы, что обработчик
/// сам отбрасывает пограничные строки.
/// </remarks>
public sealed class FakePushSourceRepository : IPushSourceRepository
{
    private readonly List<Row> _bookings = [];

    public int GetDueCalls { get; private set; }

    public DateTimeOffset? LastFromExclusive { get; private set; }

    public DateTimeOffset? LastToInclusive { get; private set; }

    public int? LastLeadMinutes { get; private set; }

    /// <summary>
    /// Добавляет бронь зарегистрированного клиента; по умолчанию она подтверждена.
    /// </summary>
    public void AddBooking(PushReminderBooking booking, bool isConfirmed = true)
        => _bookings.Add(new Row(
            booking.BookingId,
            booking.UserId,
            booking.StartAt,
            booking.ServiceName,
            booking.SalonName,
            booking.BarberName,
            booking.SalonTimeZoneId,
            booking.BookingCreatedAt,
            isConfirmed));

    /// <summary>
    /// Добавляет гостевую бронь: у неё нет получателя, и SQL её не возвращает.
    /// </summary>
    public void AddGuestBooking(
        Guid bookingId,
        DateTimeOffset startAt,
        DateTimeOffset createdAt,
        string? salonName = "Барбершоп",
        string? barberName = "Иван",
        string? serviceName = "Стрижка",
        string? salonTimeZoneId = "Asia/Yekaterinburg")
        => _bookings.Add(new Row(
            bookingId,
            null,
            startAt,
            serviceName,
            salonName,
            barberName,
            salonTimeZoneId,
            createdAt,
            IsConfirmed: true));

    public Task<Result<IReadOnlyList<PushReminderBooking>, Failure>> GetDueBookingsAsync(
        DateTimeOffset fromExclusive,
        DateTimeOffset toInclusive,
        int leadMinutes,
        CancellationToken cancellationToken = default)
    {
        GetDueCalls++;
        LastFromExclusive = fromExclusive;
        LastToInclusive = toInclusive;
        LastLeadMinutes = leadMinutes;

        IReadOnlyList<PushReminderBooking> rows = _bookings
            .Where(row => row.IsConfirmed && row.UserId is not null)
            .Where(row => row.StartAt > fromExclusive)
            .OrderBy(row => row.StartAt)
            .ThenBy(row => row.BookingId)
            .Select(row => new PushReminderBooking(
                row.BookingId,
                row.UserId!.Value,
                row.StartAt,
                row.ServiceName,
                row.SalonName,
                row.BarberName,
                row.SalonTimeZoneId,
                row.CreatedAt))
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<PushReminderBooking>, Failure>(rows));
    }

    private sealed record Row(
        Guid BookingId,
        Guid? UserId,
        DateTimeOffset StartAt,
        string? ServiceName,
        string? SalonName,
        string? BarberName,
        string? SalonTimeZoneId,
        DateTimeOffset CreatedAt,
        bool IsConfirmed);
}
