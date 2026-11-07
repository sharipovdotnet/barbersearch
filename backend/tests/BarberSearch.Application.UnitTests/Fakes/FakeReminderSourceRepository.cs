using BarberSearch.Application.Notifications;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.UnitTests.Fakes;

/// <summary>
/// In-memory источник для напоминаний: брони барберов и пояс основного салона.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GetUpcomingBookingsAsync"/> намеренно не фильтрует по верхней границе окна,
/// только по нижней: точное окно <c>now + lead</c> проверяет обработчик, и тест обязан
/// видеть, что бронь за <c>now + lead + 1 с</c> не создаёт уведомление. Настоящий
/// репозиторий сужает выборку сам — это оптимизация, а не правило.
/// </para>
/// <para>
/// <see cref="GetDayBookingsAsync"/>, наоборот, применяет интервал полностью: локальные сутки
/// салона считает обработчик, и фейк должен вести себя как SQL, иначе дайджест «считал» бы
/// записи соседних суток.
/// </para>
/// </remarks>
public sealed class FakeReminderSourceRepository : IReminderSourceRepository
{
    private readonly Dictionary<Guid, string?> _primaryTimeZones = [];
    private readonly List<(Guid BarberUserId, ReminderBooking Booking)> _bookings = [];

    public void SetPrimarySalonTimeZone(Guid barberUserId, string? timeZoneId)
        => _primaryTimeZones[barberUserId] = timeZoneId;

    public void AddBooking(Guid barberUserId, ReminderBooking booking)
        => _bookings.Add((barberUserId, booking));

    public Task<Result<string?, Failure>> GetPrimarySalonTimeZoneIdAsync(
        Guid barberUserId,
        CancellationToken cancellationToken = default)
    {
        _primaryTimeZones.TryGetValue(barberUserId, out var timeZoneId);

        return Task.FromResult(Result.Success<string?, Failure>(timeZoneId));
    }

    public Task<Result<IReadOnlyList<ReminderBooking>, Failure>> GetUpcomingBookingsAsync(
        Guid barberUserId,
        DateTimeOffset fromExclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReminderBooking> rows = _bookings
            .Where(entry => entry.BarberUserId == barberUserId)
            .Select(entry => entry.Booking)
            .Where(booking => booking.StartAt > fromExclusive)
            .OrderBy(booking => booking.StartAt)
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<ReminderBooking>, Failure>(rows));
    }

    public Task<Result<IReadOnlyList<ReminderBooking>, Failure>> GetDayBookingsAsync(
        Guid barberUserId,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReminderBooking> rows = _bookings
            .Where(entry => entry.BarberUserId == barberUserId)
            .Select(entry => entry.Booking)
            .Where(booking => booking.StartAt >= fromInclusive && booking.StartAt < toExclusive)
            .OrderBy(booking => booking.StartAt)
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<ReminderBooking>, Failure>(rows));
    }
}
