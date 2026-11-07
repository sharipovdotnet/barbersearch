using BarberSearch.Application.Notifications;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Источник данных тика напоминаний: пояс основного салона и подтверждённые визиты барбера.
/// </summary>
/// <remarks>
/// <para>
/// «Пользователь → брони» идёт через <c>barber_profiles.user_id</c>: уведомление принадлежит
/// пользователю-барберу (решение D5), а бронь хранит <c>barber_profile_id</c>. Все соединения
/// внутренние: визит без услуги, салона или профиля не даёт ни времени, ни названий, и такая
/// историческая строка выпадает — как и в чтении записей барбера (§16).
/// </para>
/// <para>
/// Возвращаются только <see cref="BookingStatus.Confirmed"/> (решение D4). Окно по
/// <c>lead_minutes</c> здесь шире максимального значения (60 минут): точное окно проверяет
/// обработчик, чтобы правило не дублировалось в SQL и оставалось проверяемым на фейках.
/// </para>
/// <para>
/// Фильтр и порядок применяются к соединённой выборке до проекции в
/// <see cref="ReminderBooking"/>: EF Core не транслирует обращение к свойству уже
/// сконструированной записи, поэтому <c>Where(row =&gt; row.StartAt &gt; …)</c> после
/// <c>select new ReminderBooking(...)</c> ушёл бы в память и упал на трансляции.
/// </para>
/// </remarks>
public sealed class ReminderSourceRepository : IReminderSourceRepository
{
    private readonly ApplicationDbContext _context;

    public ReminderSourceRepository(ApplicationDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task<Result<string?, Failure>> GetPrimarySalonTimeZoneIdAsync(
        Guid barberUserId,
        CancellationToken cancellationToken = default)
    {
        // Частичное exclusion-ограничение ex_barber_salons_primary_per_profile гарантирует
        // не больше одного основного салона на профиль, поэтому FirstOrDefault безопасен.
        var timeZoneId = await (
                from profile in _context.BarberProfiles.AsNoTracking()
                join link in _context.BarberSalons.AsNoTracking()
                    on profile.Id equals link.BarberProfileId
                join salon in _context.Salons.AsNoTracking()
                    on link.SalonId equals salon.Id
                where profile.UserId == barberUserId && link.IsPrimary
                select salon.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);

        return timeZoneId;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ReminderBooking>, Failure>> GetUpcomingBookingsAsync(
        Guid barberUserId,
        DateTimeOffset fromExclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken = default)
    {
        var rows = await BuildQuery(
                barberUserId,
                fromExclusive,
                toInclusive,
                fromExclusive: true)
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ReminderBooking>, Failure>> GetDayBookingsAsync(
        Guid barberUserId,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken = default)
    {
        var rows = await BuildQuery(
                barberUserId,
                fromInclusive,
                toExclusive,
                fromExclusive: false)
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <summary>
    /// Собирает выборку подтверждённых визитов барбера с фильтром окна и проекцией.
    /// </summary>
    /// <remarks>
    /// Фильтр и порядок накладываются на соединённую выборку до
    /// <c>select new ReminderBooking(...)</c>: EF Core не транслирует обращение к свойству
    /// уже сконструированной записи, поэтому предикат должен видеть члены сущностей.
    /// </remarks>
    /// <param name="barberUserId">Пользователь-барбер.</param>
    /// <param name="from">Нижняя граница окна.</param>
    /// <param name="to">Верхняя граница окна.</param>
    /// <param name="fromExclusive">
    /// <c>true</c> — окно <c>(from, to]</c> для напоминания; <c>false</c> — <c>[from, to)</c>
    /// для локальных суток дайджеста.
    /// </param>
    private IQueryable<ReminderBooking> BuildQuery(
        Guid barberUserId,
        DateTimeOffset from,
        DateTimeOffset to,
        bool fromExclusive)
    {
        var joined = from booking in _context.Bookings.AsNoTracking()
                     join profile in _context.BarberProfiles.AsNoTracking()
                         on booking.BarberProfileId equals profile.Id
                     join service in _context.BarberServices.AsNoTracking()
                         on booking.BarberServiceId equals service.Id
                     join hairService in _context.HairServices.AsNoTracking()
                         on service.HairServiceId equals hairService.Id
                     join salon in _context.Salons.AsNoTracking()
                         on booking.SalonId equals salon.Id
                     where profile.UserId == barberUserId
                           && booking.Status == BookingStatus.Confirmed
                     select new
                     {
                         booking,
                         hairService,
                         salon
                     };

        joined = fromExclusive
            ? joined.Where(row => row.booking.StartAt > from && row.booking.StartAt <= to)
            : joined.Where(row => row.booking.StartAt >= from && row.booking.StartAt < to);

        return joined
            .OrderBy(row => row.booking.StartAt)
            .ThenBy(row => row.booking.Id)
            .Select(row => new ReminderBooking(
                row.booking.Id,
                row.booking.StartAt,
                row.hairService.Name,
                row.salon.Name,
                row.salon.TimeZoneId));
    }
}
