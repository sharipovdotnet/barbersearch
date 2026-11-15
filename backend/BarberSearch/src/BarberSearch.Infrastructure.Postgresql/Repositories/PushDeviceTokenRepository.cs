using BarberSearch.Application.Push;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Push;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий токенов устройств: upsert с передачей владения, предел устройств и уборка.
/// </summary>
/// <remarks>
/// <para>
/// Регистрация — сырой <c>INSERT … ON CONFLICT (token) DO UPDATE</c>: токен принадлежит
/// устройству, поэтому повторная регистрация под другим аккаунтом передаёт владение, а не
/// падает на уникальности. Одна транзакция с удалением самых старых устройств сверх предела
/// (<c>Push:MaxTokensPerUser</c>) не даёт двум параллельным входам оставить шесть токенов.
/// </para>
/// <para>
/// Чтение и удаление токенов не отслеживаются контекстом: они не меняются частичными
/// обновлениями, а <c>ExecuteDelete</c> идёт мимо change tracker.
/// </para>
/// <para>
/// Токены никогда не попадают в журнал: это идентификатор устройства, а не сообщение.
/// </para>
/// </remarks>
public sealed class PushDeviceTokenRepository
    : RepositoryBase<PushDeviceToken>, IPushDeviceTokenRepository
{
    public PushDeviceTokenRepository(
        ApplicationDbContext context,
        AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> RegisterAsync(
        PushDeviceToken token,
        int maxTokensPerUser,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var limit = maxTokensPerUser > 0
            ? maxTokensPerUser
            : PushOptions.DEFAULT_MAX_TOKENS_PER_USER;

        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Сериализуем регистрации одного пользователя. Без блокировки две параллельные
            // вставки под READ COMMITTED могут обе увидеть пять строк и оставить шесть:
            // DELETE одного не увидит незакоммиченную строку другого. Advisory-блокировка
            // живёт до конца транзакции и берётся по стабильному хешу user_id.
            await Context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({token.UserId}::text, 0))",
                cancellationToken);

            // ON CONFLICT (token) — передача владения: устройство вошло в другой аккаунт.
            // Прежний владелец больше не должен получать push на этот телефон.
            // GREATEST не даёт часам с разных инстансов откатить last_seen_at назад:
            // иначе устаревшая запись могла бы вытеснить самое свежее устройство.
            await Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO push_device_tokens (
                     id, user_id, token, platform, last_seen_at, created_at, updated_at)
                 VALUES (
                     {token.Id}, {token.UserId}, {token.Token}, {(short)token.Platform},
                     {token.LastSeenAt}, {token.CreatedAt}, {token.UpdatedAt})
                 ON CONFLICT (token) DO UPDATE
                 SET user_id = EXCLUDED.user_id,
                     platform = EXCLUDED.platform,
                     last_seen_at = GREATEST(push_device_tokens.last_seen_at, EXCLUDED.last_seen_at),
                     updated_at = GREATEST(push_device_tokens.updated_at, EXCLUDED.updated_at)
                 """,
                cancellationToken);

            // Предел устройств: оставляем самые свежие, остальные — самые давно не заходившие —
            // удаляем. Пользователь не должен копить неограниченное число адресов доставки.
            // Блокировка выше делает этот SELECT+DELETE безопасным при параллельных входах.
            await Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 DELETE FROM push_device_tokens
                 WHERE user_id = {token.UserId}
                   AND id NOT IN (
                       SELECT id
                       FROM push_device_tokens
                       WHERE user_id = {token.UserId}
                       ORDER BY last_seen_at DESC, id DESC
                       LIMIT {limit})
                 """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            // Сырой SQL не обновляет change tracker: снимаем возможные отслеживаемые строки,
            // чтобы следующий SaveChanges не вернул прежние значения или удалённые строки.
            DetachTracked(candidate => candidate.UserId == token.UserId);

            return UnitResult.Success<Failure>();
        }
        catch (PostgresException exception)
        {
            return MapPostgresError(exception, ApiErrorCodes.Push.DeviceTokenUpdatePrefix);
        }
        catch (NpgsqlException)
        {
            return new Failure(Error.Failure(
                ApiErrorCodes.InternalDatabaseError,
                "The push device token could not be saved."));
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PushDeviceToken>, Failure>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await Context.PushDeviceTokens
            .AsNoTracking()
            .Where(token => token.UserId == userId)
            .OrderByDescending(token => token.LastSeenAt)
            .ThenByDescending(token => token.Id)
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <inheritdoc />
    public async Task<Result<int, Failure>> DeleteAsync(
        string token,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var deleted = await Context.PushDeviceTokens
            .Where(candidate => candidate.Token == token && candidate.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        DetachTracked(candidate => candidate.Token == token);

        return deleted;
    }

    /// <inheritdoc />
    public async Task<Result<int, Failure>> DeleteByTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var deleted = await Context.PushDeviceTokens
            .Where(candidate => candidate.Token == token)
            .ExecuteDeleteAsync(cancellationToken);

        DetachTracked(candidate => candidate.Token == token);

        return deleted;
    }

    /// <inheritdoc />
    public async Task<Result<int, Failure>> DeleteStaleAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        var deleted = await Context.PushDeviceTokens
            .Where(token => token.LastSeenAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        DetachTracked(token => token.LastSeenAt < cutoff);

        return deleted;
    }
}
