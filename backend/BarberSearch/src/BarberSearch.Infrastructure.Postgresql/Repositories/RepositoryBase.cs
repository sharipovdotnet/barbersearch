using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Infrastructure.Postgresql.Audit;
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
/// Конкурентная потеря (строка изменена другим запросом между чтением и записью) — это не
/// ошибка сервера, а проигранная оптимистическая блокировка: <c>DbUpdateConcurrencyException</c>
/// превращается в <c>Error.Conflict</c> с кодом <c>{errorCode}.concurrencyConflict</c> (409).
/// </para>
/// <para>
/// Исключения чтения (нет соединения, таймаут) не перехватываются: это эксплуатационные
/// ошибки, а не бизнес-правило, их обрабатывает верхний слой.
/// </para>
/// <para>
/// Одиночные <c>GetByIdAsync</c> читаются с отслеживанием — иначе изменение агрегата требовало
/// бы повторного присоединения графа. Списки, страницы и сводки читаются <c>AsNoTracking()</c>.
/// </para>
/// <para>
/// После <see cref="Failure"/> от сохранения change tracker пуст: ни неудачная, ни прочитанная
/// ранее сущность не остаётся отслеживаемой. Иначе повторный <c>SaveChanges</c> снова наткнулся
/// бы на то же ограничение или записал часть графа, а после отката транзакции в памяти остались
/// бы копии, которых нет в базе. Агрегат, который нужно изменить после сбоя, следует перечитать.
/// </para>
/// </remarks>
public abstract class RepositoryBase<TEntity>
    where TEntity : Entity
{
    /// <summary>
    /// Верхняя граница размера страницы: защита от «выгрузи всю таблицу одним запросом».
    /// </summary>
    protected const int MAX_PAGE_SIZE = 200;

    /// <summary>
    /// Предел глубины offset-выдачи: максимальное смещение <c>page * pageSize</c>.
    /// </summary>
    internal const int MAX_PAGE_OFFSET = 10_000;

    private readonly AuditTrail? _auditTrail;

    protected RepositoryBase(ApplicationDbContext context, AuditTrail? auditTrail = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
        _auditTrail = auditTrail;
    }

    protected ApplicationDbContext Context { get; }

    /// <summary>
    /// Сохраняет изменения, накопленные контекстом.
    /// </summary>
    /// <remarks>
    /// При ошибке сохранения возвращает <see cref="Failure"/> и очищает change tracker: после
    /// сбоя в контексте не остаётся ни изменённых, ни отслеживаемых сущностей. Это важно, потому
    /// что неудачная <c>Added</c>-сущность иначе осталась бы в трекере, и повторный
    /// <c>SaveChanges</c> в том же scope снова наткнулся бы на то же ограничение или записал
    /// часть графа. После <c>Failure</c> агрегат нужно перечитать, прежде чем менять.
    /// </remarks>
    /// <param name="errorCode">Префикс кода ошибки, например <c>user.add</c>.</param>
    protected async Task<UnitResult<Failure>> SaveChangesAsync(
        string errorCode,
        CancellationToken cancellationToken)
    {
        // Аудит собирается до сохранения и попадает в тот же SaveChanges: строка audit_log
        // и само изменение либо фиксируются вместе, либо откатываются транзакцией
        // IUnitOfWork вместе (R-34).
        _auditTrail?.Capture(Context);

        try
        {
            await Context.SaveChangesAsync(cancellationToken);

            return UnitResult.Success<Failure>();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Строка изменилась между чтением и UPDATE/DELETE: xmin в WHERE не совпал.
            // Это проигранная гонка, а не сбой сервера — 409 и понятный клиенту код.
            return FailAndClearTracker(MapConcurrencyError(exception, errorCode));
        }
        catch (PostgresException exception)
        {
            // Отложенное ограничение (например, ex_barber_salons_primary_per_profile, R-06)
            // проверяется при COMMIT неявной транзакции EF Core. Npgsql бросает
            // PostgresException напрямую, а не заворачивает её в DbUpdateException,
            // поэтому маппим её тем же способом — 409/400 вместо 500.
            return FailAndClearTracker(MapPostgresError(exception, errorCode));
        }
        catch (DbUpdateException exception)
        {
            return FailAndClearTracker(MapDatabaseError(exception, errorCode));
        }
    }

    /// <summary>
    /// Возвращает <see cref="Failure"/> и снимает с change tracker все сущности.
    /// </summary>
    /// <remarks>
    /// <c>Clear()</c>, а не отцепление только изменённых: после отката транзакции недействительны
    /// и ранее прочитанные сущности, а контракт «после <c>Failure</c> контекст пуст» проще
    /// проверить и невозможно нарушить забытым состоянием <c>Added</c>/<c>Modified</c>.
    /// </remarks>
    private UnitResult<Failure> FailAndClearTracker(Failure failure)
    {
        Context.ChangeTracker.Clear();

        return UnitResult.Failure(failure);
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
    /// <para>
    /// С оптимистической блокировкой (R-05) это требование стало жёстче: исходное значение
    /// <c>xmin</c> у отсоединённой сущности неизвестно, и EF отправит <c>xmin = 0</c>.
    /// Такой UPDATE всегда проиграет гонку, поэтому обновлять агрегат можно только тем
    /// экземпляром, который прочитан в этом же <see cref="ApplicationDbContext"/>.
    /// </para>
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
    /// Снимает с change tracker отслеживаемые <typeparamref name="TEntity"/> после записи
    /// в обход EF Core (сырой SQL, <c>ExecuteUpdate</c>).
    /// </summary>
    /// <remarks>
    /// Такой <c>UPDATE</c> не обновляет память контекста: у отслеживаемой сущности остаются
    /// прежние значения, и следующий <c>SaveChanges</c> этим экземпляром мог бы вернуть колонку
    /// к ним — например, записать <c>revoked_at = NULL</c> уже отозванному refresh-токену.
    /// Отсоединённую сущность придётся перечитать, прежде чем менять.
    /// </remarks>
    /// <param name="predicate">Какие сущности снимать; <c>null</c> — все отслеживаемые.</param>
    /// <returns>Количество снятых сущностей.</returns>
    protected int DetachTracked(Func<TEntity, bool>? predicate = null)
    {
        var entries = Context.ChangeTracker
            .Entries<TEntity>()
            .Where(entry => predicate is null || predicate(entry.Entity))
            .ToList();

        foreach (var entry in entries)
        {
            entry.State = EntityState.Detached;
        }

        return entries.Count;
    }

    /// <summary>
    /// Проверяет параметры постраничной выдачи. <c>null</c> — параметры корректны.
    /// </summary>
    /// <param name="page">Номер страницы, начиная с единицы.</param>
    /// <param name="pageSize">Размер страницы, <c>1..MAX_PAGE_SIZE</c>.</param>
    /// <param name="pageCode">Публичный код ошибки номера страницы.</param>
    /// <param name="pageSizeCode">Публичный код ошибки размера страницы.</param>
    protected static Failure? ValidatePaging(
        int page,
        int pageSize,
        string pageCode,
        string pageSizeCode)
    {
        if (page < 1)
        {
            return new Failure(Error.Validation(
                pageCode,
                "Page number must be 1 or greater.",
                nameof(page)));
        }

        if (pageSize is < 1 or > MAX_PAGE_SIZE)
        {
            return new Failure(Error.Validation(
                pageSizeCode,
                Messages.PageSizeOutOfRange(MAX_PAGE_SIZE),
                nameof(pageSize)));
        }

        return null;
    }

    /// <summary>
    /// Проверяет глубину offset-выдачи: <c>page * pageSize</c> не больше
    /// <see cref="MAX_PAGE_OFFSET"/>. <c>null</c> — глубина допустима.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Skip((page - 1) * pageSize)</c> деградирует с глубиной: PostgreSQL читает и отбрасывает
    /// все пропущенные строки. Вместо перевода админских списков на keyset глубина ограничена
    /// сверху — <see cref="MAX_PAGE_OFFSET"/> строк, этого хватает экранам с фильтрами.
    /// </para>
    /// <para>
    /// Метод вызывается после <see cref="ValidatePaging"/>: при недопустимых параметрах клиент
    /// должен получить прежние коды <c>*.paging.page</c> / <c>*.paging.pageSize</c>, а не код
    /// глубины. Произведение считается в <see cref="long"/>: <paramref name="page"/> может быть
    /// <see cref="int.MaxValue"/>, и <c>int</c>-умножение переполнилось бы.
    /// </para>
    /// </remarks>
    /// <param name="page">Номер страницы, начиная с единицы.</param>
    /// <param name="pageSize">Размер страницы, <c>1..MAX_PAGE_SIZE</c>.</param>
    /// <param name="depthCode">Публичный код ошибки превышения глубины.</param>
    internal static Failure? ValidatePageOffset(int page, int pageSize, string depthCode)
    {
        if ((long)page * pageSize > MAX_PAGE_OFFSET)
        {
            return new Failure(Error.Validation(
                depthCode,
                $"Page * page size must not exceed {MAX_PAGE_OFFSET}.",
                nameof(page)));
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
    /// Переводит проигранную оптимистическую блокировку в конфликт доменного уровня.
    /// </summary>
    /// <remarks>
    /// <c>DbUpdateConcurrencyException</c> приходит после того, как EF Core отправил
    /// <c>UPDATE ... WHERE id = @id AND xmin = @original</c> и не нашёл строку. Клиенту
    /// нужен стабильный код, а не текст исключения, поэтому в сообщение попадают только
    /// имя агрегата и идентификатор.
    /// </remarks>
    private static Failure MapConcurrencyError(
        DbUpdateConcurrencyException exception,
        string errorCode)
    {
        var entry = exception.Entries.Count > 0 ? exception.Entries[0] : null;
        var entityName = entry?.Metadata.ClrType.Name ?? "Record";
        var id = entry?.Property("Id").CurrentValue;

        var subject = id is null ? entityName : $"{entityName} '{id}'";

        return new Failure(Error.Conflict(
            ApiErrorCodes.ConcurrencyConflict(errorCode),
            $"{subject} was modified or deleted by another request. Reload it and retry."));
    }

    /// <summary>
    /// Переводит ошибку ограничения PostgreSQL в доменную ошибку.
    /// </summary>
    private static Failure MapDatabaseError(DbUpdateException exception, string errorCode)
    {
        if (exception.InnerException is not PostgresException postgres)
        {
            // Внутренний диагностический код: 500 и никакой информации об операции репозитория.
            return new Failure(Error.Failure(ApiErrorCodes.InternalDatabaseError, exception.Message));
        }

        return MapPostgresError(postgres, errorCode);
    }

    /// <summary>
    /// Переводит SQLSTATE сервера в доменную ошибку.
    /// </summary>
    /// <remarks>
    /// Метод защищённый: им пользуется не только <c>SaveChanges</c>, но и сырой SQL, который
    /// ждёт блокировку (<see cref="BookingRepository.AcquireBarberLockAsync"/>). Сообщения
    /// содержат только имя ограничения или колонки — текст SQL и параметры наружу не попадают.
    /// </remarks>
    protected static Failure MapPostgresError(PostgresException postgres, string errorCode)
    {
        return postgres.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation => new Failure(Error.Conflict(
                ApiErrorCodes.UniqueViolation(errorCode),
                $"A record with the same unique values already exists ({postgres.ConstraintName}).")),

            PostgresErrorCodes.ExclusionViolation => new Failure(Error.Conflict(
                ApiErrorCodes.Overlap(errorCode),
                $"The period overlaps with an existing record ({postgres.ConstraintName}).")),

            // Fifty concurrent inserts on one slot can deadlock inside the GiST
            // exclusion-constraint check even though the constraint is doing its job.
            // It is a lost contention, not a server failure: 409, never 500.
            PostgresErrorCodes.DeadlockDetected => new Failure(Error.Conflict(
                ApiErrorCodes.Deadlock(errorCode),
                $"The operation deadlocked with a concurrent transaction ({postgres.ConstraintName}).")),

            // SET LOCAL lock_timeout истёк, пока транзакция ждала блокировку (55P03).
            // Это не сбой сервера, а осознанно ограниченное ожидание: 409 и ретрай у клиента.
            PostgresErrorCodes.LockNotAvailable => new Failure(Error.Conflict(
                ApiErrorCodes.LockTimeout(errorCode),
                "The database lock could not be acquired within the configured timeout. Retry the request.")),

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
}
