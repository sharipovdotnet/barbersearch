using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Bookings.GetBarberBookings;

/// <summary>
/// Обработчик списка записей барбера.
/// </summary>
/// <remarks>
/// Профиль берётся из claim'а <c>barber_profile_id</c> текущей сессии: query-параметра с
/// профилем нет, поэтому один барбер не может прочитать записи другого. Политика
/// <c>BarberOnly</c> уже отсекла клиента и администратора; здесь закрывается случай
/// «роль есть, профиль не привязан» — <c>403 auth.forbidden</c>.
/// <para>
/// Граница скоупов считается от <see cref="TimeProvider"/>, а не от системных часов:
/// «сейчас» — бизнес-решение (AGENT.md §7.6), и в тестах оно управляемое.
/// </para>
/// </remarks>
public sealed class GetBarberBookingsHandler
    : IQueryHandler<PagedResponse<BarberBookingResponse>, GetBarberBookingsQuery>
{
    /// <summary>
    /// Префикс кода ошибки размера страницы; полный код — <c>booking.getBarberBookings.pageSize</c>.
    /// </summary>
    private const string PAGE_SIZE_ERROR_PREFIX = "booking.getBarberBookings";

    private readonly IBookingRepository _bookingRepository;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public GetBarberBookingsHandler(
        IBookingRepository bookingRepository,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(bookingRepository);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _bookingRepository = bookingRepository;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Возвращает страницу записей барбера выбранного скоупа.
    /// </summary>
    public async Task<Result<PagedResponse<BarberBookingResponse>, Failure>> Handle(
        GetBarberBookingsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!_currentUser.TryGetBarberProfileId(out var barberProfileId))
        {
            return new Failure(Error.Forbidden(
                ApiErrorCodes.Auth.Forbidden,
                "The current user has no barber profile linked to the account."));
        }

        var scope = ParseScope(query.Scope);

        if (scope is null)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.Booking.GetBarberBookingsScope,
                "Scope must be either 'upcoming' or 'past'.",
                nameof(query.Scope)));
        }

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(pageSize, PAGE_SIZE_ERROR_PREFIX) is { } pageSizeError)
        {
            return pageSizeError;
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

        var rowsResult = await _bookingRepository.GetBarberPageAsync(
            barberProfileId,
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

        return new PagedResponse<BarberBookingResponse>(items, nextCursor);
    }

    private static BarberBookingScope? ParseScope(string? scope)
        => scope?.Trim().ToLowerInvariant() switch
        {
            null or "" or "upcoming" => BarberBookingScope.Upcoming,
            "past" => BarberBookingScope.Past,
            _ => null
        };
}
