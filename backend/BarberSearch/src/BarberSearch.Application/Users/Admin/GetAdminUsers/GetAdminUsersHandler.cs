using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.Admin;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Users.Admin.GetAdminUsers;

/// <summary>
/// Обработчик админского списка пользователей.
/// </summary>
/// <remarks>
/// Список читается свежими сверху; фильтры статуса и роли и поиск по имени, фамилии и email
/// комбинируются. Доступ проверяет контроллер политикой <c>AdminOnly</c>: обработчик
/// не знает, кто его вызвал, и не принимает решений об авторизации.
/// </remarks>
public sealed class GetAdminUsersHandler
    : IQueryHandler<PagedResponse<AdminUserResponse>, GetAdminUsersQuery>
{
    /// <summary>
    /// Максимальная длина поисковой строки: защита от разбора произвольно большого запроса.
    /// </summary>
    private const int MAX_SEARCH_LENGTH = 100;

    private static readonly string[] ROLE_NAMES = Enum.GetNames<UserRole>();

    private readonly IUserRepository _userRepository;

    public GetAdminUsersHandler(IUserRepository userRepository)
    {
        ArgumentNullException.ThrowIfNull(userRepository);

        _userRepository = userRepository;
    }

    /// <summary>
    /// Возвращает страницу пользователей выбранного статуса и роли с курсором продолжения.
    /// </summary>
    public async Task<Result<PagedResponse<AdminUserResponse>, Failure>> Handle(
        GetAdminUsersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var status = ParseStatus(query.Status);

        if (status is null)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminUser.StatusInvalid,
                "Status must be one of 'all', 'active', 'blocked' or 'unverified'.",
                nameof(query.Status)));
        }

        if (!TryParseRole(query.Role, out var role))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminUser.RoleInvalid,
                "Role must be one of 'all', 'Customer', 'Barber' or 'Admin'.",
                nameof(query.Role)));
        }

        var search = query.Search?.Trim();

        if (search is { Length: > MAX_SEARCH_LENGTH })
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminUser.SearchTooLong,
                $"Search must not be longer than {MAX_SEARCH_LENGTH} characters.",
                nameof(query.Search)));
        }

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(
                pageSize,
                ApiErrorCodes.AdminUser.PageSizePrefix) is { } pageSizeError)
        {
            return pageSizeError;
        }

        AdminUserCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = AdminUserCursor.Decode(query.Cursor);
            if (decoded.IsFailure)
            {
                return decoded.Error;
            }

            cursor = decoded.Value;
        }

        // Репозиторий читает pageSize + 1 строку, чтобы понять, есть ли следующая страница,
        // не выполняя отдельный COUNT.
        var rowsResult = await _userRepository.GetAdminPageAsync(
            status.Value,
            role,
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
            ? new AdminUserCursor(items[^1].CreatedAt, items[^1].Id).Encode()
            : null;

        return new PagedResponse<AdminUserResponse>(items, nextCursor);
    }

    private static AdminUserStatus? ParseStatus(string? status)
        => status?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => AdminUserStatus.All,
            "active" => AdminUserStatus.Active,
            "blocked" => AdminUserStatus.Blocked,
            "unverified" => AdminUserStatus.Unverified,
            _ => null
        };

    /// <summary>
    /// Разбирает фильтр роли по имени значения.
    /// </summary>
    /// <remarks>
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> принимает и числа
    /// (<c>"3"</c> — это <c>Admin</c>), а публичный контракт обещает имена. Поэтому
    /// сравнение идёт с <see cref="Enum.GetNames{TEnum}"/>: неизвестная строка и число
    /// одинаково дают <c>admin.users.role</c>.
    /// </remarks>
    private static bool TryParseRole(string? value, out UserRole? role)
    {
        role = null;

        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed)
            || trimmed.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = ROLE_NAMES.FirstOrDefault(
            candidate => candidate.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return false;
        }

        role = Enum.Parse<UserRole>(name);

        return true;
    }
}
