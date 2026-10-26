using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Bookings.GetBookings;

/// <summary>
/// Обработчик списка броней.
/// </summary>
/// <remarks>
/// Владение проверяется здесь, а не в контроллере: чужой <c>userId</c> — это 403,
/// а не пустой список, чтобы клиент не путал «нет прав» с «нет броней». Администратор
/// обходит проверку; для несуществующего клиента он получает 404.
/// </remarks>
public sealed class GetBookingsHandler
    : IQueryHandler<PagedResponse<BookingResponse>, GetBookingsQuery>
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUser _currentUser;

    public GetBookingsHandler(
        IBookingRepository bookingRepository,
        IUserRepository userRepository,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(bookingRepository);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(currentUser);

        _bookingRepository = bookingRepository;
        _userRepository = userRepository;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Возвращает страницу броней с курсором продолжения.
    /// </summary>
    public async Task<Result<PagedResponse<BookingResponse>, Failure>> Handle(
        GetBookingsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userIdResult = _currentUser.RequireUserId();
        if (userIdResult.IsFailure)
        {
            return userIdResult.Error;
        }

        var currentUserId = userIdResult.Value;

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(pageSize, "booking.getBookings") is { } pageSizeError)
        {
            return pageSizeError;
        }

        var requestedUserId = query.UserId == Guid.Empty ? null : query.UserId;

        if (requestedUserId is { } foreignUserId
            && foreignUserId != currentUserId
            && !_currentUser.IsAdmin)
        {
            return new Failure(Error.Forbidden(
                ApiErrorCodes.Auth.Forbidden,
                "Bookings of another user are not available."));
        }

        Guid? filterUserId;

        if (_currentUser.IsAdmin)
        {
            filterUserId = requestedUserId;

            if (requestedUserId is { } adminFilterUserId)
            {
                var userExists = await _userRepository.ExistsAsync(
                    adminFilterUserId,
                    cancellationToken);
                if (userExists.IsFailure)
                {
                    return userExists.Error;
                }

                if (!userExists.Value)
                {
                    return new Failure(Error.NotFound(
                        "booking.getBookings.userNotFound",
                        $"User '{adminFilterUserId}' was not found.",
                        adminFilterUserId));
                }
            }
        }
        else
        {
            filterUserId = currentUserId;
        }

        BookingCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = BookingCursor.Decode(query.Cursor);
            if (decoded.IsFailure)
            {
                return decoded.Error;
            }

            cursor = decoded.Value;
        }

        var rowsResult = await _bookingRepository.GetPageAsync(
            filterUserId,
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
            ? new BookingCursor(items[^1].StartAt, items[^1].Id).Encode()
            : null;

        return new PagedResponse<BookingResponse>(items, nextCursor);
    }
}
