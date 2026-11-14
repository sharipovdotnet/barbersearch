using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Contracts.Bookings.RescheduleBooking;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api.Bookings;

/// <summary>
/// HTTP-клиент записей: <c>POST api/bookings</c>, список, отмена и перенос.
/// </summary>
/// <remarks>
/// Тело собирается из контракта бэкенда, а не из локальной копии полей: расхождение с API
/// должно ломать сборку, а не отправку. Время уходит тем же инстантом, что вернул сервер, —
/// пересчёта в пояс устройства здесь нет. Успешные <c>204</c> тело не читают.
/// </remarks>
public sealed class BookingsApi : IBookingsApi
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public BookingsApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<Guid> CreateBookingAsync(
        CreateBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await _httpClient
            .PostAsJsonAsync("api/bookings", request, ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var created = await response.Content
            .ReadFromJsonAsync<CreatedResourceResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        // Идентификатор генерирует домен: пустой Guid в ответе означает, что сервер
        // нарушил контракт, и показывать успех при этом нельзя.
        return created is null || created.Id == Guid.Empty
            ? throw new InvalidOperationException("The API returned an empty booking response.")
            : created.Id;
    }

    /// <inheritdoc />
    public async Task<PagedResponse<BookingResponse>> GetBookingsAsync(
        BookingScope scope,
        Guid userId,
        string? cursor = null,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The user identifier must not be empty.", nameof(userId));
        }

        using var response = await _httpClient
            .GetAsync(BuildBookingsPath(scope, userId, cursor, pageSize), cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<BookingResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return page ?? throw new InvalidOperationException("The API returned an empty bookings response.");
    }

    /// <inheritdoc />
    public async Task CancelBookingAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsync(BuildBookingPath(bookingId, "cancellation"), content: null, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BarberSlotsResponse> GetRescheduleSlotsAsync(
        Guid bookingId,
        DateOnly slotDate,
        CancellationToken cancellationToken = default)
    {
        var path = string.Concat(
            BuildBookingPath(bookingId, "reschedule-slots"),
            "?date=",
            slotDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        using var response = await _httpClient
            .GetAsync(path, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var slots = await response.Content
            .ReadFromJsonAsync<BarberSlotsResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return slots ?? throw new InvalidOperationException("The API returned an empty slots response.");
    }

    /// <inheritdoc />
    public async Task RescheduleBookingAsync(
        Guid bookingId,
        DateTimeOffset startAt,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsJsonAsync(
                BuildBookingPath(bookingId, "reschedule"),
                new RescheduleBookingRequest(startAt),
                ApiJson.Options,
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Бросает <see cref="BarberSearchApiException"/>, если ответ неуспешный.
    /// </summary>
    /// <remarks>
    /// Вынесено отдельно: у четырёх методов одна и та же проверка, а тело ошибки разбирается
    /// только в исключении.
    /// </remarks>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Собирает адрес списка записей.
    /// </summary>
    /// <remarks>
    /// Скоуп и <c>userId</c> передаются всегда: это не фильтры, а часть запроса «мои записи».
    /// Курсор добавляется только для следующей страницы — пустая строка в адрес не попадает.
    /// </remarks>
    private static string BuildBookingsPath(
        BookingScope scope,
        Guid userId,
        string? cursor,
        int pageSize)
    {
        var parameters = new List<string>(4)
        {
            string.Concat("scope=", ScopeValue(scope)),
            string.Concat("pageSize=", pageSize.ToString(CultureInfo.InvariantCulture)),
            string.Concat("userId=", userId.ToString("D", CultureInfo.InvariantCulture)),
        };

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            parameters.Add(string.Concat("cursor=", Uri.EscapeDataString(cursor)));
        }

        return string.Concat("api/bookings?", string.Join('&', parameters));
    }

    /// <summary>
    /// Собирает адрес действия над записью.
    /// </summary>
    private static string BuildBookingPath(Guid bookingId, string action) =>
        string.Concat(
            "api/bookings/",
            bookingId.ToString("D", CultureInfo.InvariantCulture),
            "/",
            action);

    /// <summary>
    /// Значение параметра <c>scope</c> на проводе.
    /// </summary>
    private static string ScopeValue(BookingScope scope) => scope switch
    {
        BookingScope.Upcoming => "upcoming",
        BookingScope.Past => "past",
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };
}
