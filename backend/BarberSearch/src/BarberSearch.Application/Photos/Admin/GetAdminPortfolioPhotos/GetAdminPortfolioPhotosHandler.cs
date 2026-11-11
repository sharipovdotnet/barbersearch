using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Photos.Admin.GetAdminPortfolioPhotos;

/// <summary>
/// Обработчик очереди «Портфолио» на проверку.
/// </summary>
/// <remarks>
/// <para>
/// Очередь — только <c>Pending</c>-фотографии, FIFO по <c>created_at ASC, id ASC</c>:
/// отдельной метки поступления у портфолио нет, кадр создаётся сразу в очередь. История
/// проверенных кадров отдельным фильтром не отдаётся.
/// </para>
/// <para>
/// Доступ проверяется здесь, а не только политикой <c>AdminOnly</c> на эндпоинте: чтение
/// отдаёт email владельца профиля. Порядок проверок — сначала роль, затем параметры.
/// </para>
/// <para>
/// Репозиторий читает <c>pageSize + 1</c> строку, чтобы понять, есть ли следующая страница,
/// без отдельного <c>COUNT</c>. Курсор строится по последней строке уже урезанной страницы.
/// </para>
/// </remarks>
public sealed class GetAdminPortfolioPhotosHandler
    : IQueryHandler<PagedResponse<AdminPortfolioPhotoResponse>, GetAdminPortfolioPhotosQuery>
{
    /// <summary>
    /// Максимальная длина поисковой строки: защита от разбора произвольно большого запроса.
    /// </summary>
    private const int MAX_SEARCH_LENGTH = 100;

    private readonly IBarberProfileRepository _barberProfileRepository;
    private readonly ICurrentUser _currentUser;

    /// <summary>
    /// Создаёт обработчик.
    /// </summary>
    /// <param name="barberProfileRepository">Профили: страница очереди портфолио.</param>
    /// <param name="currentUser">Текущий пользователь: проверка роли администратора.</param>
    public GetAdminPortfolioPhotosHandler(
        IBarberProfileRepository barberProfileRepository,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(barberProfileRepository);
        ArgumentNullException.ThrowIfNull(currentUser);

        _barberProfileRepository = barberProfileRepository;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Возвращает страницу работ портфолио, ждущих проверки, с курсором продолжения.
    /// </summary>
    public async Task<Result<PagedResponse<AdminPortfolioPhotoResponse>, Failure>> Handle(
        GetAdminPortfolioPhotosQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var allowed = _currentUser.EnsureAdmin();
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var search = query.Search?.Trim();

        if (search is { Length: > MAX_SEARCH_LENGTH })
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminPhotos.SearchTooLong,
                $"Search must not be longer than {MAX_SEARCH_LENGTH} characters.",
                nameof(query.Search)));
        }

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(
                pageSize,
                ApiErrorCodes.AdminPhotos.PageSizePrefix) is { } pageSizeError)
        {
            return pageSizeError;
        }

        AdminPhotoCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = AdminPhotoCursor.Decode(query.Cursor);
            if (decoded.IsFailure)
            {
                return decoded.Error;
            }

            cursor = decoded.Value;
        }

        var rowsResult = await _barberProfileRepository.GetAdminPortfolioPhotosPageAsync(
            cursor,
            string.IsNullOrEmpty(search) ? null : search,
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
            ? new AdminPhotoCursor(items[^1].SubmittedAt, items[^1].PhotoId).Encode()
            : null;

        return new PagedResponse<AdminPortfolioPhotoResponse>(items, nextCursor);
    }
}
