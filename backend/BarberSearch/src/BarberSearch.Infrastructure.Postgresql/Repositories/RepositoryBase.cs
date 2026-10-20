using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shared;
using Entity = BarberSearch.Domain.Common.Entity;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Общая часть репозиториев: контекст, сохранение изменений и перевод ошибок PostgreSQL
/// в <see cref="Failure"/>.
/// </summary>
/// <remarks>
/// <para>
/// Ограничения БД (уникальность, внешний ключ, check, пересечение периодов) не должны
/// выходить наружу исключением: они превращаются в доменные ошибки — <c>Error.Conflict</c>
/// или <c>Error.Validation</c> — с кодом вида <c>booking.add.uniqueViolation</c>.
/// </para>
/// <para>
/// Исключения чтения (нет соединения, таймаут) не перехватываются: это эксплуатационные
/// ошибки, а не бизнес-правило, их обрабатывает верхний слой.
/// </para>
/// <para>
/// Одиночные <c>GetByIdAsync</c> читаются с отслеживанием — иначе изменение агрегата требовало
/// бы повторного присоединения графа. Списки, страницы и сводки читаются <c>AsNoTracking()</c>.
/// </para>
/// </remarks>
public abstract class RepositoryBase<TEntity>
    where TEntity : Entity
{
    /// <summary>
    /// Верхняя граница размера страницы: защита от «выгрузи всю таблицу одним запросом».
    /// </summary>
    protected const int MAX_PAGE_SIZE = 200;

    protected RepositoryBase(ApplicationDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
    }

    protected ApplicationDbContext Context { get; }

    /// <summary>
    /// Сохраняет изменения, накопленные контекстом.
    /// </summary>
    /// <param name="errorCode">Префикс кода ошибки, например <c>user.add</c>.</param>
    protected async Task<UnitResult<Failure>> SaveChangesAsync(
        string errorCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await Context.SaveChangesAsync(cancellationToken);

            return UnitResult.Success<Failure>();
        }
        catch (DbUpdateException exception)
        {
            return UnitResult.Failure(MapDatabaseError(exception, errorCode));
        }
    }

    /// <summary>
    /// Сохраняет граф сущности: отсоединённая сущность сначала присоединяется как изменённая.
    /// </summary>
    /// <remarks>
    /// Если сущность уже отслеживается контекстом (её вернул <c>GetByIdAsync</c> в этом же
    /// запросе), состояние не трогается — EF сам видит новые дочерние сущности как <c>Added</c>,
    /// а удалённые как <c>Deleted</c>. У отсоединённого графа отличить новую дочернюю сущность
    /// от существующей нельзя (идентификаторы генерирует домен), поэтому он помечается
    /// изменённым целиком: сначала загружайте агрегат в тот же контекст.
    /// </remarks>
    protected async Task<UnitResult<Failure>> SaveGraphAsync(
        TEntity entity,
        string errorCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (Context.Entry(entity).State == EntityState.Detached)
        {
            Context.Update(entity);
        }

        return await SaveChangesAsync(errorCode, cancellationToken);
    }

    /// <summary>
    /// Проверяет параметры постраничной выдачи. <c>null</c> — параметры корректны.
    /// </summary>
    protected static Failure? ValidatePaging(int page, int pageSize, string errorCode)
    {
        if (page < 1)
        {
            return new Failure(Error.Validation(
                $"{errorCode}.page",
                "Page number must be 1 or greater.",
                nameof(page)));
        }

        if (pageSize is < 1 or > MAX_PAGE_SIZE)
        {
            return new Failure(Error.Validation(
                $"{errorCode}.pageSize",
                $"Page size must be between 1 and {MAX_PAGE_SIZE}.",
                nameof(pageSize)));
        }

        return null;
    }

    /// <summary>
    /// Ошибка «запись не найдена» в едином для всех репозиториев виде.
    /// </summary>
    protected static Failure NotFound(string errorCode, string entityName, Guid id)
        => new(Error.NotFound(
            errorCode,
            $"{entityName} with identifier '{id}' was not found.",
            id));

    /// <summary>
    /// Переводит ошибку ограничения PostgreSQL в доменную ошибку.
    /// </summary>
    private static Failure MapDatabaseError(DbUpdateException exception, string errorCode)
    {
        if (exception.InnerException is not PostgresException postgres)
        {
            return new Failure(Error.Failure($"{errorCode}.database", exception.Message));
        }

        return postgres.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation => new Failure(Error.Conflict(
                $"{errorCode}.uniqueViolation",
                $"A record with the same unique values already exists ({postgres.ConstraintName}).")),

            PostgresErrorCodes.ExclusionViolation => new Failure(Error.Conflict(
                $"{errorCode}.overlap",
                $"The period overlaps with an existing record ({postgres.ConstraintName}).")),

            // Fifty concurrent inserts on one slot can deadlock inside the GiST
            // exclusion-constraint check even though the constraint is doing its job.
            // It is a lost contention, not a server failure: 409, never 500.
            PostgresErrorCodes.DeadlockDetected => new Failure(Error.Conflict(
                $"{errorCode}.deadlock",
                $"The operation deadlocked with a concurrent transaction ({postgres.ConstraintName}).")),

            PostgresErrorCodes.ForeignKeyViolation => new Failure(Error.Conflict(
                $"{errorCode}.foreignKeyViolation",
                $"A referenced record does not exist ({postgres.ConstraintName}).")),

            PostgresErrorCodes.CheckViolation => new Failure(Error.Validation(
                $"{errorCode}.checkViolation",
                $"The database rejected the values ({postgres.ConstraintName}).",
                postgres.ColumnName)),

            PostgresErrorCodes.NotNullViolation => new Failure(Error.Validation(
                $"{errorCode}.notNullViolation",
                $"A required value is missing ({postgres.ColumnName}).",
                postgres.ColumnName)),

            _ => new Failure(Error.Failure($"{errorCode}.database", postgres.MessageText))
        };
    }
}
