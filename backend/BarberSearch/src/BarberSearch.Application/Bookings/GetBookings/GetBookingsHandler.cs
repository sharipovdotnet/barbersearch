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
/// <para>
/// Граница скоупов считается от <see cref="TimeProvider"/>, а не от системных часов:
/// «сейчас» — бизнес-решение (AGENT.md §7.6), и в тестах оно управляемое.
/// </para>
/// </remarks>
public sealed class GetBookingsHandler
    : IQueryHandler<PagedResponse<BookingResponse>, GetBookingsQuery>
{
    private const string PAGE_SIZE_ERROR_PREFIX = "booking.getBookings";

    private readonly IBookingRepository _bookingRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public GetBookingsHandler(
        IBookingRepository bookingRepository,
        IUserRepository userRepository,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(bookingRepository);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _bookingRepository = bookingRepository;
        _userRepository = userRepository;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
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

        var scope = ParseScope(query.Scope);
        if (scope is null)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.GetBookingsScope,
                "Scope must be either 'all', 'upcoming' or 'past'.",
                nameof(query.Scope)));
        }

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(pageSize, PAGE_SIZE_ERROR_PREFIX) is { } pageSizeError)
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

        var now = _timeProvider.GetUtcNow();

        var rowsResult = await _bookingRepository.GetPageAsync(
            filterUserId,
            scope.Value,
            now,
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

    /// <summary>
    /// Разбирает скоуп: пустая строка и <c>null</c> — прежнее <c>all</c>, регистр не важен.
    /// </summary>
    private static BookingScope? ParseScope(string? scope)
        => scope?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => BookingScope.All,
            "upcoming" => BookingScope.Upcoming,
            "past" => BookingScope.Past,
            _ => null
        };
}
