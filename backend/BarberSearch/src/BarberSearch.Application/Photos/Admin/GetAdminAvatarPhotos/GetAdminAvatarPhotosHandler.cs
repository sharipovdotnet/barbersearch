using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Photos.Admin.GetAdminAvatarPhotos;

/// <summary>
/// Обработчик очереди «Аватары» на проверку.
/// </summary>
/// <remarks>
/// <para>
/// Очередь — только <c>Pending</c>-кадры с непустыми <c>avatar_url</c> и
/// <c>avatar_submitted_at</c>, FIFO по <c>avatar_submitted_at ASC, id ASC</c>. История
/// проверенных кадров отдельным фильтром не отдаётся: состояние видно в админских карточках
/// пользователя и барбера.
/// </para>
/// <para>
/// Доступ проверяется здесь, а не только политикой <c>AdminOnly</c> на эндпоинте: чтение
/// отдаёт email владельцев, и обработчик должен отказывать и вызванный напрямую. Порядок
/// проверок — сначала роль, затем параметры: не-администратор не получает сведений
/// о валидности запроса.
/// </para>
/// <para>
/// Репозиторий читает <c>pageSize + 1</c> строку, чтобы понять, есть ли следующая страница,
/// без отдельного <c>COUNT</c>. Курсор строится по последней строке уже урезанной страницы.
/// </para>
/// </remarks>
public sealed class GetAdminAvatarPhotosHandler
    : IQueryHandler<PagedResponse<AdminAvatarPhotoResponse>, GetAdminAvatarPhotosQuery>
{
    /// <summary>
    /// Максимальная длина поисковой строки: защита от разбора произвольно большого запроса.
    /// </summary>
    private const int MAX_SEARCH_LENGTH = 100;

    private readonly IUserRepository _userRepository;
    private readonly ICurrentUser _currentUser;

    /// <summary>
    /// Создаёт обработчик.
    /// </summary>
    /// <param name="userRepository">Пользователи: страница очереди аватаров.</param>
    /// <param name="currentUser">Текущий пользователь: проверка роли администратора.</param>
    public GetAdminAvatarPhotosHandler(
        IUserRepository userRepository,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(currentUser);

        _userRepository = userRepository;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Возвращает страницу аватаров, ждущих проверки, с курсором продолжения.
    /// </summary>
    public async Task<Result<PagedResponse<AdminAvatarPhotoResponse>, Failure>> Handle(
        GetAdminAvatarPhotosQuery query,
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

        var rowsResult = await _userRepository.GetAdminAvatarPhotosPageAsync(
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
            ? new AdminPhotoCursor(items[^1].SubmittedAt, items[^1].UserId).Encode()
            : null;

        return new PagedResponse<AdminAvatarPhotoResponse>(items, nextCursor);
    }
}
