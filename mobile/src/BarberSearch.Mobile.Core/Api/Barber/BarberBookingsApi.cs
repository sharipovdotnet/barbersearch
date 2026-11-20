using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.Bookings.AddBookingBarberNote;
using BarberSearch.Contracts.Bookings.Barber;
using BarberSearch.Contracts.Bookings.CancelBooking;
using BarberSearch.Contracts.Bookings.ChangeBookingPrice;
using BarberSearch.Contracts.Bookings.CreateBarberBooking;
using BarberSearch.Contracts.Bookings.RescheduleBooking;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api.Barber;

/// <summary>
/// HTTP-клиент записей кабинета мастера.
/// </summary>
/// <remarks>
/// Тела собираются из контрактов бэкенда, а не из локальных копий полей: расхождение с API
/// должно ломать сборку, а не отправку. <c>X-CSRF-TOKEN</c> и <c>X-Client-Surface</c> не
/// отправляются: первый не нужен Bearer-клиенту, второй пометил бы действие мастера как
/// действие клиента и отменил бы push о нём.
/// </remarks>
public sealed class BarberBookingsApi : IBarberBookingsApi
{
    private const string BookingsPath = "api/barber/bookings";

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">
    /// Клиент с заданным <see cref="HttpClient.BaseAddress"/> и обработчиком авторизации.
    /// </param>
    public BarberBookingsApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<PagedResponse<BarberBookingResponse>> GetAsync(
        string? scope = null,
        string? cursor = null,
        int? pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildBookingsPath(scope, cursor, pageSize), cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<BarberBookingResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return page ?? throw new InvalidOperationException(
            "The API returned an empty barber bookings response.");
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(
        CreateBarberBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BookingsPath)
        {
            Content = JsonContent.Create(request, options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var created = await response.Content
            .ReadFromJsonAsync<CreatedResourceResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        // Идентификатор генерирует домен: пустой Guid в ответе означает нарушение контракта,
        // и показывать успех при этом нельзя.
        return created is null || created.Id == Guid.Empty
            ? throw new InvalidOperationException("The API returned an empty barber booking response.")
            : created.Id;
    }

    /// <inheritdoc />
    public async Task ConfirmAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        EnsureBookingId(bookingId);

        await PostWithoutBodyAsync(
                BuildBookingPath(bookingId, "confirmation"),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        EnsureBookingId(bookingId);

        await PostWithoutBodyAsync(
                BuildBookingPath(bookingId, "completion"),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CancelAsync(
        Guid bookingId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        EnsureBookingId(bookingId);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            BuildBookingPath(bookingId, "cancellation"))
        {
            // Тело отправляется и при reason = null: контракт объявляет поле, и отказ должен
            // прийти доменным кодом booking.cancel.reasonRequired, а не зависеть от разбора
            // пустого тела на сервере.
            Content = JsonContent.Create(
                new CancelBookingRequest(reason),
                options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkNoShowAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        EnsureBookingId(bookingId);

        await PostWithoutBodyAsync(
                BuildBookingPath(bookingId, "no-show"),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveNoteAsync(
        Guid bookingId,
        string note,
        CancellationToken cancellationToken = default)
    {
        EnsureBookingId(bookingId);
        ArgumentNullException.ThrowIfNull(note);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Put,
            BuildBookingPath(bookingId, "barber-note"))
        {
            Content = JsonContent.Create(
                new AddBookingBarberNoteRequest(note),
                options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ChangePriceAsync(
        Guid bookingId,
        decimal price,
        CancellationToken cancellationToken = default)
    {
        EnsureBookingId(bookingId);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Put,
            BuildBookingPath(bookingId, "price"))
        {
            Content = JsonContent.Create(
                new ChangeBookingPriceRequest(price),
                options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BarberSlotsResponse> GetRescheduleSlotsAsync(
        Guid bookingId,
        DateOnly slotDate,
        CancellationToken cancellationToken = default)
    {
        EnsureBookingId(bookingId);

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
    public async Task RescheduleAsync(
        Guid bookingId,
        DateTimeOffset startAt,
        CancellationToken cancellationToken = default)
    {
        EnsureBookingId(bookingId);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            BuildBookingPath(bookingId, "reschedule"))
        {
            Content = JsonContent.Create(
                new RescheduleBookingRequest(startAt),
                options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Отправляет действие над записью, у которого нет тела.
    /// </summary>
    /// <remarks>
    /// Успех — <c>204</c>: тело ответа не читается, иначе клиент ждал бы JSON, которого нет.
    /// </remarks>
    private async Task PostWithoutBodyAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .PostAsync(path, content: null, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Собирает адрес страницы записей.
    /// </summary>
    /// <remarks>
    /// Скоуп и курсор необязательны: отсутствие скоупа — серверный <c>upcoming</c>, отсутствие
    /// курсора — первая страница. Пустые значения в адрес не попадают, а курсор экранируется
    /// целиком: он непрозрачен и может содержать разделители query.
    /// </remarks>
    private static string BuildBookingsPath(string? scope, string? cursor, int? pageSize)
    {
        var parameters = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(scope))
        {
            parameters.Add(string.Concat("scope=", Uri.EscapeDataString(scope)));
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            parameters.Add(string.Concat("cursor=", Uri.EscapeDataString(cursor)));
        }

        if (pageSize is not null)
        {
            parameters.Add(string.Concat(
                "pageSize=",
                pageSize.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return parameters.Count == 0
            ? BookingsPath
            : string.Concat(BookingsPath, "?", string.Join('&', parameters));
    }

    /// <summary>
    /// Собирает адрес действия над записью.
    /// </summary>
    private static string BuildBookingPath(Guid bookingId, string action) => string.Concat(
        "api/bookings/",
        bookingId.ToString("D", CultureInfo.InvariantCulture),
        "/",
        action);

    /// <summary>
    /// Отсекает пустой идентификатор до запроса: такой брони не существует, и сеть для этого
    /// не нужна.
    /// </summary>
    private static void EnsureBookingId(Guid bookingId)
    {
        if (bookingId == Guid.Empty)
        {
            throw new ArgumentException("The booking identifier must not be empty.", nameof(bookingId));
        }
    }

    /// <summary>
    /// Бросает <see cref="BarberSearchApiException"/>, если ответ неуспешный.
    /// </summary>
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
}
