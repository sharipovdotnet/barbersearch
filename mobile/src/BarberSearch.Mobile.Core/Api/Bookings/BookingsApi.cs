using System.Net.Http.Json;
using BarberSearch.Contracts.Bookings.CreateBooking;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api.Bookings;

/// <summary>
/// HTTP-клиент создания брони: <c>POST api/bookings</c>.
/// </summary>
/// <remarks>
/// Тело собирается из контракта бэкенда, а не из локальной копии полей: расхождение с API
/// должно ломать сборку, а не отправку. Время уходит тем же инстантом, что вернул
/// <c>/slots</c>, — пересчёта в пояс устройства здесь нет.
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

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var created = await response.Content
            .ReadFromJsonAsync<CreatedResourceResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        // Идентификатор генерирует домен: пустой Guid в ответе означает, что сервер
        // нарушил контракт, и показывать успех при этом нельзя.
        return created is null || created.Id == Guid.Empty
            ? throw new InvalidOperationException("The API returned an empty booking response.")
            : created.Id;
    }
}
