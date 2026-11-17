using System.Data;
using BarberSearch.Application.Push;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Domain.Push;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий очереди push-уведомлений: вставка с дедупликацией, пачка доставки и уборка.
/// </summary>
/// <remarks>
/// <para>
/// Вставка идёт сырым <c>INSERT … ON CONFLICT DO NOTHING</c>: дедупликацию держит уникальный
/// индекс <c>ux_push_notifications_booking_id</c> по тройке
/// <c>(booking_id, kind, start_at)</c>, а EF Core не умеет выразить «не падать на конфликте».
/// Метод возвращает <c>false</c>, если такое уведомление уже есть, — по этому признаку
/// обработчик считает реально созданные строки. <c>start_at</c> в ключе позволяет повторным
/// переносам создавать новые строки, но не даёт двум событиям одного вида на одно время
/// задвоиться.
/// </para>
/// <para>
/// Пачка доставки <b>забирается</b> условным <c>UPDATE … RETURNING</c> с
/// <c>FOR UPDATE SKIP LOCKED</c>: строки получают <see cref="CLAIM_LEASE"/>, и второй
/// экземпляр приложения их уже не выберет. Долгая транзакция не подходит — репозиторий
/// не владеет scope обработчика и держал бы соединение на время сети.
/// </para>
/// <para>
/// Читаемые для изменения строки (пачка доставки) загружаются с отслеживанием: обработчик
/// меняет их и вызывает <see cref="UpdateAsync"/>, перезаписывая аренду результатом перехода.
/// </para>
/// </remarks>
public sealed class PushNotificationRepository
    : RepositoryBase<PushNotification>, IPushNotificationRepository
{
    /// <summary>
    /// Аренда захваченной строки доставки: пять минут — запас на медленную сеть и паузу GC.
    /// </summary>
    internal static readonly TimeSpan CLAIM_LEASE = TimeSpan.FromMinutes(5);

    public PushNotificationRepository(
        ApplicationDbContext context,
        AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    /// <inheritdoc />
    public async Task<Result<bool, Failure>> TryAddAsync(
        PushNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        try
        {
            var inserted = await Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO push_notifications (
                     id, user_id, kind, booking_id, start_at, salon_name, barber_name,
                     service_name, salon_time_zone_id, delivery, delivery_attempts,
                     next_attempt_at, last_error, created_at, updated_at)
                 VALUES (
                     {notification.Id}, {notification.UserId}, {(short)notification.Kind},
                     {notification.BookingId}, {notification.StartAt},
                     {notification.SalonName}, {notification.BarberName},
                     {notification.ServiceName}, {notification.SalonTimeZoneId},
                     {(short)notification.Delivery}, {notification.DeliveryAttempts},
                     {notification.NextAttemptAt}, {notification.LastError},
                     {notification.CreatedAt}, {notification.UpdatedAt})
                 ON CONFLICT DO NOTHING
                 """,
                cancellationToken);

            return inserted > 0;
        }
        catch (PostgresException exception)
        {
            return MapPostgresError(exception, ApiErrorCodes.Push.NotificationAddPrefix);
        }
        catch (NpgsqlException)
        {
            // Текст исключения Npgsql может содержать параметры команды, а в них — снимок
            // салона и мастера; наружу отдаём стабильный внутренний код.
            return new Failure(Error.Failure(
                ApiErrorCodes.InternalDatabaseError,
                "The push notification could not be saved."));
        }
    }

    /// <inheritdoc />
    public Task<UnitResult<Failure>> UpdateAsync(
        PushNotification notification,
        CancellationToken cancellationToken = default)
        => SaveGraphAsync(
            notification,
            ApiErrorCodes.Push.NotificationUpdatePrefix,
            cancellationToken);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PushNotification>, Failure>> GetPendingForDeliveryAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var limit = batchSize > 0 ? batchSize : PushOptions.DEFAULT_BATCH_SIZE;
        var leaseUntil = now.Add(CLAIM_LEASE);

        var ids = await ClaimBatchAsync(now, leaseUntil, limit, cancellationToken);

        if (ids.Count == 0)
        {
            return Array.Empty<PushNotification>();
        }

        // Строки уже захвачены, их next_attempt_at — аренда. Загружаем с отслеживанием:
        // обработчик вызовет MarkSent/MarkSkipped/MarkFailed и перезапишет аренду своим
        // значением (успех и пропуск обнуляют NextAttemptAt, ошибка ставит новую задержку).
        var rows = await Context.PushNotifications
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
        var deleted = await Context.PushNotifications
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
    /// Один SQL-запрос: подзапрос с <c>FOR UPDATE SKIP LOCKED</c> выбирает строки, не
    /// блокируясь на уже забранных другим экземпляром, и тут же ставит им аренду
    /// <c>next_attempt_at</c>. Параметры передаются через <see cref="NpgsqlParameter"/>,
    /// потому что команда возвращает набор строк (<c>RETURNING</c>).
    /// </remarks>
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
            UPDATE push_notifications
            SET next_attempt_at = @lease_until,
                updated_at = @now
            WHERE id IN (
                SELECT id
                FROM push_notifications
                WHERE delivery = @pending
                  AND delivery_attempts < @max_attempts
                  AND (next_attempt_at IS NULL OR next_attempt_at <= @now)
                ORDER BY created_at
                LIMIT @batch_size
                FOR UPDATE SKIP LOCKED)
            RETURNING id
            """;

        command.Parameters.Add(new NpgsqlParameter<short>("pending", (short)PushDelivery.Pending));
        command.Parameters.Add(new NpgsqlParameter<int>(
            "max_attempts",
            Constants.MAX_NOTIFICATION_DELIVERY_ATTEMPTS));
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
