using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Bookings.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Bookings;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Bookings.Admin.GetAdminBookings;

/// <summary>
/// Обработчик админского списка броней.
/// </summary>
/// <remarks>
/// <para>
/// Список читается свежими сверху: поддержке нужен последний визит, а не вся история
/// клиента. Фильтр статуса и поиск комбинируются; поиск — подстрока email клиента или
/// точный идентификатор брони.
/// </para>
/// <para>
/// Доступ проверяет контроллер политикой <c>AdminOnly</c>: обработчик не знает, кто его
/// вызвал, и не принимает решений об авторизации.
/// </para>
/// </remarks>
public sealed class GetAdminBookingsHandler
    : IQueryHandler<PagedResponse<AdminBookingResponse>, GetAdminBookingsQuery>
{
    /// <summary>
    /// Максимальная длина поисковой строки: защита от разбора произвольно большого запроса.
    /// </summary>
    private const int MAX_SEARCH_LENGTH = 100;

    private static readonly string[] STATUS_NAMES = Enum.GetNames<BookingStatus>();

    private readonly IBookingRepository _bookingRepository;

    public GetAdminBookingsHandler(IBookingRepository bookingRepository)
    {
        ArgumentNullException.ThrowIfNull(bookingRepository);

        _bookingRepository = bookingRepository;
    }

    /// <summary>
    /// Возвращает страницу броней выбранного статуса с поиском и курсором продолжения.
    /// </summary>
    public async Task<Result<PagedResponse<AdminBookingResponse>, Failure>> Handle(
        GetAdminBookingsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TryParseStatus(query.Status, out var status))
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminBooking.StatusInvalid,
                "Status must be one of 'all', 'Pending', 'Confirmed', 'Completed', 'Cancelled' or 'NoShow'.",
                nameof(query.Status)));
        }

        var search = query.Search?.Trim();

        if (search is { Length: > MAX_SEARCH_LENGTH })
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminBooking.SearchTooLong,
                $"Search must not be longer than {MAX_SEARCH_LENGTH} characters.",
                nameof(query.Search)));
        }

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(
                pageSize,
                ApiErrorCodes.AdminBooking.PageSizePrefix) is { } pageSizeError)
        {
            return pageSizeError;
        }

        AdminBookingCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = AdminBookingCursor.Decode(query.Cursor);
            if (decoded.IsFailure)
            {
                return decoded.Error;
            }

            cursor = decoded.Value;
        }

        var filters = new AdminBookingFilters(
            status,
            string.IsNullOrEmpty(search) ? null : search);

        // Репозиторий читает pageSize + 1 строку, чтобы понять, есть ли следующая страница,
        // не выполняя отдельный COUNT.
        var rowsResult = await _bookingRepository.GetAdminPageAsync(
            filters,
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
            ? new AdminBookingCursor(items[^1].StartAt, items[^1].Id).Encode()
            : null;

        return new PagedResponse<AdminBookingResponse>(items, nextCursor);
    }

    /// <summary>
    /// Разбирает фильтр статуса по имени значения.
    /// </summary>
    /// <remarks>
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> принимает и числа
    /// (<c>"0"</c> — это <c>Pending</c>), а публичный контракт обещает имена. Поэтому
    /// сравнение идёт с <see cref="Enum.GetNames{TEnum}"/>: неизвестная строка и число
    /// одинаково дают <c>admin.bookings.status</c>. <c>null</c>, пустая строка и <c>all</c>
    /// означают «без фильтра» — это значение по умолчанию списка.
    /// </remarks>
    private static bool TryParseStatus(string? value, out BookingStatus? status)
    {
        status = null;

        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed)
            || trimmed.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = STATUS_NAMES.FirstOrDefault(
            candidate => candidate.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return false;
        }

        status = Enum.Parse<BookingStatus>(name);

        return true;
    }
}
