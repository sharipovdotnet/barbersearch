using BarberSearch.Application.Notifications;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Notifications;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий настроек напоминаний: чтение, создание по умолчанию, поиск по коду привязки
/// и сохранение.
/// </summary>
/// <remarks>
/// <para>
/// Все чтения, кроме списка включённых настроек, идут с отслеживанием: обработчик меняет
/// найденный агрегат и вызывает <see cref="UpdateAsync"/>. Чтение без отслеживания
/// (<c>GetEnabledAsync</c>) — вход тика, там настройки только читаются.
/// </para>
/// <para>
/// Конфликт первичного ключа при создании — не <c>500</c>, а <c>409</c> с кодом
/// <c>notificationSettings.add.uniqueViolation</c>: два первых чтения одного барбера
/// могут прийти одновременно, и проигравший перечитывает уже созданную строку.
/// </para>
/// </remarks>
public sealed class BarberNotificationSettingsRepository : IBarberNotificationSettingsRepository
{
    private readonly ApplicationDbContext _context;
    private readonly AuditTrail? _auditTrail;

    public BarberNotificationSettingsRepository(
        ApplicationDbContext context,
        AuditTrail? auditTrail = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _auditTrail = auditTrail;
    }

    /// <inheritdoc />
    public async Task<Result<BarberNotificationSettings?, Failure>> FindByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var settings = await _context.BarberNotificationSettings
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        return settings;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BarberNotificationSettings>, Failure>> GetEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _context.BarberNotificationSettings
            .AsNoTracking()
            .Where(candidate => candidate.IsEnabled)
            .ToListAsync(cancellationToken);

        return settings;
    }

    /// <inheritdoc />
    public async Task<Result<BarberNotificationSettings?, Failure>> FindByTelegramLinkCodeHashAsync(
        string codeHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codeHash))
        {
            return (BarberNotificationSettings?)null;
        }

        // Срок проверяется здесь, а не в вебхуке: поиск по хешу — единственный вход, и
        // просроченный код не должен вернуть настройки, которые можно привязать.
        var settings = await _context.BarberNotificationSettings
            .FirstOrDefaultAsync(
                candidate => candidate.TelegramLinkCodeHash == codeHash
                             && candidate.TelegramLinkCodeExpiresAt != null
                             && candidate.TelegramLinkCodeExpiresAt > now,
                cancellationToken);

        return settings;
    }

    /// <inheritdoc />
    public async Task<Result<bool, Failure>> IsChatLinkedToAnotherUserAsync(
        long chatId,
        Guid exceptUserId,
        CancellationToken cancellationToken = default)
    {
        var linked = await _context.BarberNotificationSettings
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.TelegramChatId == chatId
                             && candidate.UserId != exceptUserId,
                cancellationToken);

        return linked;
    }

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> AddAsync(
        BarberNotificationSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _context.BarberNotificationSettings.AddAsync(settings, cancellationToken);

        return await SaveChangesAsync(
            ApiErrorCodes.NotificationSettings.AddPrefix,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> UpdateAsync(
        BarberNotificationSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (_context.Entry(settings).State == EntityState.Detached)
        {
            _context.BarberNotificationSettings.Update(settings);
        }

        return await SaveChangesAsync(
            ApiErrorCodes.NotificationSettings.UpdatePrefix,
            cancellationToken);
    }

    /// <summary>
    /// Сохраняет изменения и переводит ошибки ограничений в доменные коды.
    /// </summary>
    /// <remarks>
    /// Повторяет поведение <see cref="RepositoryBase{TEntity}"/>: после ошибки change tracker
    /// очищается, чтобы следующий <c>SaveChanges</c> не повторил ту же неудачную запись.
    /// Своя копия нужна потому, что настройки — агрегат с ключом <c>user_id</c>, а не
    /// <c>Entity</c>, и не наследуют базовый репозиторий.
    /// </remarks>
    private async Task<UnitResult<Failure>> SaveChangesAsync(
        string errorCode,
        CancellationToken cancellationToken)
    {
        _auditTrail?.Capture(_context);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);

            return UnitResult.Success<Failure>();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _context.ChangeTracker.Clear();

            return UnitResult.Failure(new Failure(Error.Conflict(
                ApiErrorCodes.ConcurrencyConflict(errorCode),
                exception.Message)));
        }
        catch (PostgresException exception)
        {
            _context.ChangeTracker.Clear();

            return UnitResult.Failure(MapPostgresError(exception, errorCode));
        }
        catch (DbUpdateException exception)
        {
            _context.ChangeTracker.Clear();

            return UnitResult.Failure(new Failure(Error.Failure(
                ApiErrorCodes.InternalDatabaseError,
                exception.Message)));
        }
    }

    /// <summary>
    /// Переводит SQLSTATE PostgreSQL в доменную ошибку.
    /// </summary>
    private static Failure MapPostgresError(PostgresException postgres, string errorCode)
        => postgres.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation => new Failure(Error.Conflict(
                ApiErrorCodes.UniqueViolation(errorCode),
                $"Notification settings with the same unique values already exist ({postgres.ConstraintName}).")),

            PostgresErrorCodes.ForeignKeyViolation => new Failure(Error.Conflict(
                ApiErrorCodes.ForeignKeyViolation(errorCode),
                $"A referenced record does not exist ({postgres.ConstraintName}).")),

            PostgresErrorCodes.CheckViolation => new Failure(Error.Validation(
                ApiErrorCodes.CheckViolation(errorCode),
                $"The database rejected the values ({postgres.ConstraintName}).",
                postgres.ColumnName)),

            PostgresErrorCodes.NotNullViolation => new Failure(Error.Validation(
                ApiErrorCodes.NotNullViolation(errorCode),
                $"A required value is missing ({postgres.ColumnName}).",
                postgres.ColumnName)),

            _ => new Failure(Error.Failure(ApiErrorCodes.InternalDatabaseError, postgres.MessageText))
        };
}
