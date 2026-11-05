using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberRequests.Admin.GetAdminBarberRequests;

/// <summary>
/// Обработчик администраторского списка заявок в барберы.
/// </summary>
/// <remarks>
/// Очередь решений (<c>pending</c>, по умолчанию) отдаётся в порядке <c>created_at ASC, id</c>
/// (FIFO: старые сверху), список <c>all</c> — в порядке <c>created_at DESC, id</c>. Курсор
/// общий: направление задаёт фильтр, а не содержимое строки. Доступ проверяет контроллер
/// политикой <c>AdminOnly</c>; в ответе есть PII заявителя, поэтому других потребителей у
/// этого списка быть не должно.
/// </remarks>
public sealed class GetAdminBarberRequestsHandler
    : IQueryHandler<PagedResponse<AdminBarberRequestResponse>, GetAdminBarberRequestsQuery>
{
    private readonly IBarberRequestRepository _repository;

    public GetAdminBarberRequestsHandler(IBarberRequestRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    /// <summary>
    /// Возвращает страницу заявок выбранного статуса с курсором продолжения.
    /// </summary>
    public async Task<Result<PagedResponse<AdminBarberRequestResponse>, Failure>> Handle(
        GetAdminBarberRequestsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var status = ParseStatus(query.Status);

        if (status is null)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.BarberRequest.StatusInvalid,
                "Status must be either 'pending' or 'all'.",
                nameof(query.Status)));
        }

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(
                pageSize,
                ApiErrorCodes.BarberRequest.PageSizePrefix) is { } pageSizeError)
        {
            return pageSizeError;
        }

        AdminBarberRequestCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = AdminBarberRequestCursor.Decode(query.Cursor);
            if (decoded.IsFailure)
            {
                return decoded.Error;
            }

            cursor = decoded.Value;
        }

        var rowsResult = await _repository.GetAdminPageAsync(
            status.Value,
            cursor,
            pageSize + 1,
            cancellationToken);
        if (rowsResult.IsFailure)
        {
            return rowsResult.Error;
        }

        var rows = rowsResult.Value;
        var hasNextPage = rows.Count > pageSize;
        var items = hasNextPage
            ? rows.Take(pageSize).ToList()
            : rows.ToList();

        var nextCursor = hasNextPage && items.Count > 0
            ? new AdminBarberRequestCursor(items[^1].CreatedAt, items[^1].Id).Encode()
            : null;

        return new PagedResponse<AdminBarberRequestResponse>(items, nextCursor);
    }

    private static AdminBarberRequestStatus? ParseStatus(string? status)
        => status?.Trim().ToLowerInvariant() switch
        {
            null or "" or "pending" => AdminBarberRequestStatus.Pending,
            "all" => AdminBarberRequestStatus.All,
            _ => null
        };
}
