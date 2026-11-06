using BarberSearch.Application.BarberRequests;
using BarberSearch.Application.BarberRequests.Admin;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Infrastructure.Postgresql.Audit;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace BarberSearch.Infrastructure.Postgresql.Repositories;

/// <summary>
/// Репозиторий агрегата <see cref="BarberRequest"/>.
/// </summary>
/// <remarks>
/// <para>
/// Отдельных SQL-вставок нет: и подача, и решение идут через доменные методы и
/// <c>SaveChanges</c>, поэтому ограничения БД (частичный уникальный индекс, check длины
/// причины) превращаются в <see cref="Failure"/> силами <see cref="RepositoryBase"/>.
/// </para>
/// <para>
/// Заявка **не** входит в allowlist <c>audit_log</c> (R-34). Смена роли при одобрении
/// пишется как <c>user.updated</c> через <c>UserRepository</c>, а поля заявки содержат
/// персональные данные (город, «о себе»), которым не место в журнале. Поэтому
/// <see cref="AuditTrail"/> передаётся только ради чужих изменений в том же
/// <c>SaveChanges</c> и заявок не аудирует — расширять allowlist для неё нельзя.
/// </para>
/// </remarks>
public sealed class BarberRequestRepository
    : RepositoryBase<BarberRequest>, IBarberRequestRepository
{
    public BarberRequestRepository(ApplicationDbContext context, AuditTrail? auditTrail = null)
        : base(context, auditTrail)
    {
    }

    /// <inheritdoc />
    public async Task<Result<BarberRequest, Failure>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var request = await Context.BarberRequests
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (request is null)
        {
            return NotFound(ApiErrorCodes.BarberRequest.NotFound, nameof(BarberRequest), id);
        }

        return request;
    }

    /// <inheritdoc />
    public async Task<Result<BarberRequestResponse?, Failure>> GetLatestByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var request = await Context.BarberRequests
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return request is null
            ? Result.Success<BarberRequestResponse?, Failure>(null)
            : BarberRequestResponseMapper.Map(request);
    }

    /// <inheritdoc />
    public async Task<Result<bool, Failure>> HasPendingAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Частичный индекс ux_barber_requests_pending_user обслуживает ровно этот предикат.
        var exists = await Context.BarberRequests
            .AsNoTracking()
            .AnyAsync(
                x => x.UserId == userId && x.Status == BarberRequestStatus.Pending,
                cancellationToken);

        return exists;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AdminBarberRequestResponse>, Failure>> GetAdminPageAsync(
        AdminBarberRequestStatus status,
        AdminBarberRequestCursor? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.BarberRequest.PageLimitInvalid,
                Messages.PAGE_LIMIT_NOT_POSITIVE,
                nameof(limit)));
        }

        // Очередь решений идёт FIFO (старые сверху), список всех заявок — свежими сверху.
        var pendingOnly = status == AdminBarberRequestStatus.Pending;

        var rows = await BuildAdminRequestsQuery(
                Context,
                pendingOnly: pendingOnly,
                cursor: cursor,
                descending: !pendingOnly,
                limit: limit)
            .ToListAsync(cancellationToken);

        return rows.Select(ToAdminResponse).ToList();
    }

    /// <inheritdoc />
    public async Task<Result<AdminBarberRequestResponse, Failure>> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await BuildAdminRequestsQuery(Context, requestId: id)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return new Failure(Error.NotFound(
                ApiErrorCodes.BarberRequest.NotFound,
                $"Barber request '{id}' was not found.",
                id));
        }

        return ToAdminResponse(row);
    }

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> AddAsync(
        BarberRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await Context.BarberRequests.AddAsync(request, cancellationToken);

        // Повторная активная заявка ловится частичным индексом ux_barber_requests_pending_user:
        // обработчик переводит barberRequest.add.uniqueViolation в create.alreadyPending.
        return await SaveChangesAsync(ApiErrorCodes.BarberRequest.AddPrefix, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> UpdateAsync(
        BarberRequest request,
        CancellationToken cancellationToken = default)
        => await SaveGraphAsync(
            request,
            ApiErrorCodes.BarberRequest.UpdatePrefix,
            cancellationToken);

    /// <summary>
    /// Общая проекция администраторского чтения: заявка и её заявитель.
    /// </summary>
    /// <remarks>
    /// Join явный: навигации между агрегатами в домене нет, а каскадный FK гарантирует, что
    /// у заявки всегда есть аккаунт. Заявка материализуется сущностью, поля заявителя — плоские;
    /// вложенный <see cref="AdminBarberRequestApplicantResponse"/> собирает общий
    /// <see cref="BarberRequestResponseMapper"/>, чтобы fallback имени не жил в двух местах.
    /// </remarks>
    internal static IQueryable<AdminBarberRequestRow> BuildAdminRequestsQuery(
        ApplicationDbContext context,
        bool? pendingOnly = null,
        Guid? requestId = null,
        AdminBarberRequestCursor? cursor = null,
        bool descending = false,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requests = context.BarberRequests.AsNoTracking();

        var applicants = context.Users
            .AsNoTracking()
            .Select(user => new
            {
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email
            });

        var joined = from request in requests
                     join applicant in applicants on request.UserId equals applicant.Id
                     select new
                     {
                         request,
                         applicant
                     };

        // Фильтры и порядок применяются до проекции в запись: EF не транслирует
        // Where/OrderBy поверх конструктора AdminBarberRequestRow.
        if (requestId is { } id)
        {
            joined = joined.Where(row => row.request.Id == id);
        }

        if (pendingOnly is true)
        {
            joined = joined.Where(row =>
                row.request.Status == BarberRequestStatus.Pending);
        }

        if (cursor is { } position)
        {
            joined = descending
                ? joined.Where(row =>
                    row.request.CreatedAt < position.CreatedAt
                    || (row.request.CreatedAt == position.CreatedAt
                        && row.request.Id.CompareTo(position.Id) > 0))
                : joined.Where(row =>
                    row.request.CreatedAt > position.CreatedAt
                    || (row.request.CreatedAt == position.CreatedAt
                        && row.request.Id.CompareTo(position.Id) > 0));
        }

        var ordered = descending
            ? joined.OrderByDescending(row => row.request.CreatedAt).ThenBy(row => row.request.Id)
            : joined.OrderBy(row => row.request.CreatedAt).ThenBy(row => row.request.Id);

        var paged = limit is { } take ? ordered.Take(take) : ordered;

        return paged.Select(row => new AdminBarberRequestRow(
            row.request,
            row.applicant.Id,
            row.applicant.FirstName,
            row.applicant.LastName,
            row.applicant.Email));
    }

    private static AdminBarberRequestResponse ToAdminResponse(AdminBarberRequestRow row)
        => BarberRequestResponseMapper.MapAdmin(
            row.Request,
            row.ApplicantId,
            row.ApplicantFirstName,
            row.ApplicantLastName,
            row.ApplicantEmail);

    /// <summary>
    /// Строка администраторского чтения до сборки вложенного ответа заявителя.
    /// </summary>
    internal sealed record AdminBarberRequestRow(
        BarberRequest Request,
        Guid ApplicantId,
        string ApplicantFirstName,
        string ApplicantLastName,
        string ApplicantEmail);
}
