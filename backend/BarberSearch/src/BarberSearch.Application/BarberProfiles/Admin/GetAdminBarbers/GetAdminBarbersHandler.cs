using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberProfiles.Admin.GetAdminBarbers;

/// <summary>
/// Обработчик админского списка барберов.
/// </summary>
/// <remarks>
/// Очередь проверки отдаётся в порядке <c>created_at ASC, id</c> (старые сверху), остальные
/// фильтры — в порядке <c>created_at DESC, id</c>. Курсор общий: направление задаёт статус,
/// а не содержимое строки. Доступ проверяет контроллер политикой <c>AdminOnly</c>.
/// </remarks>
public sealed class GetAdminBarbersHandler
    : IQueryHandler<PagedResponse<AdminBarberResponse>, GetAdminBarbersQuery>
{
    /// <summary>
    /// Максимальная длина поисковой строки: защита от разбора произвольно большого запроса.
    /// </summary>
    private const int MAX_SEARCH_LENGTH = 100;

    private readonly IBarberProfileRepository _barberProfileRepository;

    public GetAdminBarbersHandler(IBarberProfileRepository barberProfileRepository)
    {
        ArgumentNullException.ThrowIfNull(barberProfileRepository);

        _barberProfileRepository = barberProfileRepository;
    }

    /// <summary>
    /// Возвращает страницу барберов выбранного статуса с курсором продолжения.
    /// </summary>
    public async Task<Result<PagedResponse<AdminBarberResponse>, Failure>> Handle(
        GetAdminBarbersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var status = ParseStatus(query.Status);

        if (status is null)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminBarber.StatusInvalid,
                "Status must be one of 'all', 'pending' or 'hidden'.",
                nameof(query.Status)));
        }

        var search = query.Search?.Trim();

        if (search is { Length: > MAX_SEARCH_LENGTH })
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminBarber.SearchTooLong,
                $"Search must not be longer than {MAX_SEARCH_LENGTH} characters.",
                nameof(query.Search)));
        }

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(
                pageSize,
                ApiErrorCodes.AdminBarber.PageSizePrefix) is { } pageSizeError)
        {
            return pageSizeError;
        }

        AdminBarberCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = AdminBarberCursor.Decode(query.Cursor);
            if (decoded.IsFailure)
            {
                return decoded.Error;
            }

            cursor = decoded.Value;
        }

        var rowsResult = await _barberProfileRepository.GetAdminPageAsync(
            status.Value,
            string.IsNullOrEmpty(search) ? null : search,
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
            ? new AdminBarberCursor(items[^1].CreatedAt, items[^1].Id).Encode()
            : null;

        return new PagedResponse<AdminBarberResponse>(items, nextCursor);
    }

    private static AdminBarberStatus? ParseStatus(string? status)
        => status?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => AdminBarberStatus.All,
            "pending" => AdminBarberStatus.Pending,
            "hidden" => AdminBarberStatus.Hidden,
            _ => null
        };
}
