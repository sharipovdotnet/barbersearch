using BarberSearch.Application.Push;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Источник данных для push-уведомлений клиенту: подтверждённые брони в окне напоминания
/// и снимок каталога для события создания брони.
/// </summary>
/// <remarks>
/// <para>
/// Все соединения внутренние: визит без профиля мастера, услуги или салона не даёт имени
/// и названий для текста, и такая историческая строка выпадает — как в напоминании барберу.
/// </para>
/// <para>
/// Три условия окна закреплены в SQL: <c>status = Confirmed</c>, <c>user_id IS NOT NULL</c>
/// (гостю push отправить некуда), <c>start_at</c> в <c>(now, now + lead]</c> и
/// <c>created_at &lt;= start_at − lead</c> (бронь, оформленная внутри окна, не получает
/// «через час»). Обработчик повторяет окно в коде, поэтому правило остаётся проверяемым
/// на фейках, а запрос — лишь сужением выборки.
/// </para>
/// <para>
/// <see cref="GetBookingSnapshotAsync"/> читает те же каталоги по идентификаторам из брони:
/// он вызывается в транзакции создания брони, поэтому это один индексированный запрос без
/// сети. Отсутствующее звено даёт <c>null</c>, а не ошибку.
/// </para>
/// </remarks>
public sealed class PushSourceRepository : IPushSourceRepository
{
    private readonly ApplicationDbContext _context;

    public PushSourceRepository(ApplicationDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PushReminderBooking>, Failure>> GetDueBookingsAsync(
        DateTimeOffset fromExclusive,
        DateTimeOffset toInclusive,
        int leadMinutes,
        CancellationToken cancellationToken = default)
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
                     where booking.Status == BookingStatus.Confirmed
                           && booking.UserId != null
                           && booking.StartAt > fromExclusive
                           && booking.StartAt <= toInclusive
                           && booking.CreatedAt <= booking.StartAt.AddMinutes(-leadMinutes)
                     select new
                     {
                         booking,
                         profile,
                         hairService,
                         salon
                     };

        var rows = await joined
            .OrderBy(row => row.booking.StartAt)
            .ThenBy(row => row.booking.Id)
            .Select(row => new PushReminderBooking(
                row.booking.Id,
                row.booking.UserId!.Value,
                row.booking.StartAt,
                row.hairService.Name,
                row.salon.Name,
                row.profile.DisplayName.Value,
                row.salon.TimeZoneId,
                row.booking.CreatedAt))
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <inheritdoc />
    public async Task<Result<PushBookingSnapshot?, Failure>> GetBookingSnapshotAsync(
        Guid barberProfileId,
        Guid barberServiceId,
        Guid salonId,
        CancellationToken cancellationToken = default)
    {
        // Внутренние join'ы: если услуги, её строки справочника, профиля или салона уже нет,
        // строка не возвращается — текст деградирует, а бронь создаётся.
        var rows = await (from profile in _context.BarberProfiles.AsNoTracking()
                          join service in _context.BarberServices.AsNoTracking()
                              on profile.Id equals service.BarberProfileId
                          join hairService in _context.HairServices.AsNoTracking()
                              on service.HairServiceId equals hairService.Id
                          join salon in _context.Salons.AsNoTracking()
                              on salonId equals salon.Id
                          where profile.Id == barberProfileId
                                && service.Id == barberServiceId
                          select new PushBookingSnapshot(
                              salon.Name,
                              profile.DisplayName.Value,
                              hairService.Name,
                              salon.TimeZoneId))
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success<PushBookingSnapshot?, Failure>(rows);
    }
}
