using System.Data;
using BarberSearch.Application.Notifications;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.Notifications;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий инбокса напоминаний: страница, счётчик, вставка с дедупликацией,
/// доставка и уборка.
/// </summary>
/// <remarks>
/// <para>
/// Вставка идёт сырым <c>INSERT ... ON CONFLICT DO NOTHING</c>: дедупликацию держат частичные
/// уникальные индексы <c>ux_barber_notifications_upcoming</c> и
/// <c>ux_barber_notifications_digest</c>, а EF Core не умеет выразить «не падать на
/// конфликте». Метод возвращает <c>false</c>, если строка уже была, — по этому признаку
/// обработчик считает реально созданные уведомления.
/// </para>
/// <para>
/// Пачка доставки <b>забирается</b> условным <c>UPDATE ... RETURNING</c> с
/// <c>FOR UPDATE SKIP LOCKED</c>: строки получают <see cref="CLAIM_LEASE"/>, и второй
/// экземпляр приложения их уже не выберет. Альтернатива — держать транзакцию с
/// <c>FOR UPDATE</c> до конца отправки — не подходит: репозиторий не владеет scope
/// обработчика, а долгая транзакция держала бы соединение и блокировки на время сети.
/// Аренда короче пяти минут: если процесс упал между захватом и сохранением результата,
/// строка вернётся в выборку после истечения аренды, а не зависнет навсегда.
/// </para>
/// <para>
/// Сущности, которые меняются (<see cref="GetByIdAsync"/> и пачка доставки), читаются
/// с отслеживанием: обработчик меняет их и вызывает <see cref="UpdateAsync"/>. Страницы
/// и счётчики — <c>AsNoTracking</c>.
/// </para>
/// </remarks>
public sealed class BarberNotificationsRepository
    : RepositoryBase<BarberNotification>, IBarberNotificationRepository
{
    /// <summary>
    /// Аренда захваченной строки доставки: сколько времени она не будет видна другим
    /// экземплярам. Отправка в Telegram укладывается в секунды; пять минут — запас на
    /// медленную сеть и паузу GC, после которого строка снова доступна.
    /// </summary>
    internal static readonly TimeSpan CLAIM_LEASE = TimeSpan.FromMinutes(5);

    public BarberNotificationsRepository(
        ApplicationDbContext context,
        AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BarberNotification>, Failure>> GetPageAsync(
        Guid userId,
        BarberNotificationCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Notification.PageSizePrefix + ".pageSize",
                "The page limit must be positive.",
                nameof(limit)));
        }

        var query = Context.BarberNotifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId);

        if (cursor is { } position)
        {
            // Порядок — created_at DESC, id DESC; тик создаёт несколько строк в одну
            // микросекунду, поэтому без второго ключа страницы теряли бы строки.
            query = query.Where(notification =>
                notification.CreatedAt < position.CreatedAt
                || (notification.CreatedAt == position.CreatedAt
                    && notification.Id.CompareTo(position.Id) < 0));
        }

        var rows = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <inheritdoc />
    public async Task<Result<int, Failure>> CountUnreadAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var count = await Context.BarberNotifications
            .AsNoTracking()
            .CountAsync(
                notification => notification.UserId == userId && notification.ReadAt == null,
                cancellationToken);

        return count;
    }

    /// <inheritdoc />
    public async Task<Result<BarberNotification, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var notification = await Context.BarberNotifications
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (notification is null)
        {
            return NotFound(ApiErrorCodes.Notification.NotFound, nameof(BarberNotification), id);
        }

        return notification;
    }

    /// <inheritdoc />
    public async Task<Result<bool, Failure>> TryAddAsync(
        BarberNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // ON CONFLICT DO NOTHING без цели срабатывает на любой из двух частичных
        // уникальных индексов; какой именно конфликт произошёл, обработчику неважно.
        try
        {
            var inserted = await Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO barber_notifications (
                     id, user_id, kind, booking_id, start_at, service_name, salon_name,
                     salon_time_zone_id, bookings_count, digest_local_date, created_at,
                     updated_at, read_at, delivery, delivery_attempts, next_attempt_at,
                     last_error)
                 VALUES (
                     {notification.Id}, {notification.UserId},
                     {(short)notification.Kind}, {notification.BookingId},
                     {notification.StartAt}, {notification.ServiceName},
                     {notification.SalonName}, {notification.SalonTimeZoneId},
                     {notification.BookingsCount}, {notification.DigestLocalDate},
                     {notification.CreatedAt}, {notification.UpdatedAt},
                     {notification.ReadAt}, {(short)notification.Delivery},
                     {notification.DeliveryAttempts}, {notification.NextAttemptAt},
                     {notification.LastError})
                 ON CONFLICT DO NOTHING
                 """,
                cancellationToken);

            return inserted > 0;
        }
        catch (PostgresException exception)
        {
            return MapPostgresError(exception, ApiErrorCodes.Notification.UpdatePrefix);
        }
        catch (NpgsqlException)
        {
            // Текст исключения Npgsql может содержать параметры команды, а в них —
            // снимок услуги и салона; наружу отдаём стабильный внутренний код.
            return new Failure(Error.Failure(
                ApiErrorCodes.InternalDatabaseError,
                "The notification could not be saved."));
        }
    }

    /// <inheritdoc />
    public Task<UnitResult<Failure>> UpdateAsync(
        BarberNotification notification,
        CancellationToken cancellationToken = default)
        => SaveGraphAsync(notification, ApiErrorCodes.Notification.UpdatePrefix, cancellationToken);

    /// <inheritdoc />
    public async Task<Result<int, Failure>> MarkAllReadAsync(
        Guid userId,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default)
    {
        var marked = await Context.BarberNotifications
            .Where(notification => notification.UserId == userId && notification.ReadAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(notification => notification.ReadAt, readAt)
                    .SetProperty(notification => notification.UpdatedAt, readAt),
                cancellationToken);

        // ExecuteUpdate идёт мимо change tracker: без отсоединения снятый ранее экземпляр
        // мог бы вернуть read_at = NULL следующим SaveChanges.
        DetachTracked(notification => notification.UserId == userId);

        return marked;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BarberNotification>, Failure>> GetPendingForDeliveryAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var limit = batchSize > 0 ? batchSize : Constants.NOTIFICATION_BATCH_SIZE;
        var leaseUntil = now.Add(CLAIM_LEASE);

        var ids = await ClaimBatchAsync(now, leaseUntil, limit, cancellationToken);

        if (ids.Count == 0)
        {
            return Array.Empty<BarberNotification>();
        }

        // Строки уже захвачены и их next_attempt_at — аренда. Загружаем их с отслеживанием:
        // обработчик вызовет MarkSent/MarkFailed и сохранит ту же сущность, перезаписав
        // аренду своим значением (MarkSent/MarkSkipped обнуляют NextAttemptAt, MarkFailed
        // ставит новую задержку). Поэтому строка не останется «захваченной» после обработки.
        var rows = await Context.BarberNotifications
            .Where(notification => ids.Contains(notification.Id))
            .OrderBy(notification => notification.CreatedAt)
            .ThenBy(notification => notification.Id)
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <inheritdoc />
    public async Task<Result<int, Failure>> DeleteOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        var deleted = await Context.BarberNotifications
            .Where(notification => notification.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // ExecuteDelete не обновляет память контекста: удалённые экземпляры снимаются,
        // иначе следующий SaveChanges попытался бы их обновить.
        DetachTracked(notification => notification.CreatedAt < cutoff);

        return deleted;
    }

    /// <summary>
    /// Захватывает пачку ожидающих строк и возвращает их идентификаторы.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Один SQL-запрос: подзапрос с <c>FOR UPDATE SKIP LOCKED</c> выбирает строки, не
    /// блокируясь на тех, что уже забрал другой экземпляр, и тут же ставит им аренду
    /// <c>next_attempt_at</c>. Строки с наступившей арендой и истёкшие повторы попадают
    /// в следующую выборку, а захваченные — нет.
    /// </para>
    /// <para>
    /// Параметры передаются через <see cref="NpgsqlParameter"/>, а не через интерполяцию EF:
    /// команда возвращает набор строк (<c>RETURNING</c>), и обычный
    /// <c>ExecuteSqlInterpolatedAsync</c> вернул бы только счётчик.
    /// </para>
    /// </summary>
    private async Task<List<Guid>> ClaimBatchAsync(
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        int limit,
        CancellationToken cancellationToken)
    {
        var connection = Context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE barber_notifications
            SET next_attempt_at = @lease_until,
                updated_at = @now
            WHERE id IN (
                SELECT id
                FROM barber_notifications
                WHERE delivery = @pending
                  AND delivery_attempts < @max_attempts
                  AND (next_attempt_at IS NULL OR next_attempt_at <= @now)
                ORDER BY created_at
                LIMIT @batch_size
                FOR UPDATE SKIP LOCKED)
            RETURNING id
            """;

        command.Parameters.Add(new NpgsqlParameter<short>("pending", (short)BarberNotificationDelivery.Pending));
        command.Parameters.Add(new NpgsqlParameter<int>("max_attempts", Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS));
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", now));
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("lease_until", leaseUntil));
        command.Parameters.Add(new NpgsqlParameter<int>("batch_size", limit));

        var ids = new List<Guid>(limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }
}
