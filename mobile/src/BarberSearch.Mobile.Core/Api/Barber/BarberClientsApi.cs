using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.BarberClients;
using BarberSearch.Contracts.BarberProfiles.GetBarberBookingOptions;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;

namespace BarberSearch.Mobile.Core.Api.Barber;

/// <summary>
/// HTTP-клиент данных формы ручной записи мастера.
/// </summary>
/// <remarks>
/// Слоты читаются из <c>api/barber/slots</c>, а не из публичного <c>api/barbers/{id}/slots</c>:
/// у кабинета нет идентификатора профиля в запросе, и профиль может быть ещё не подтверждён.
/// Переиспользовать публичный клиент нельзя — он ходит на другую ручку и на другой
/// (анонимный) <see cref="HttpClient"/>.
/// </remarks>
public sealed class BarberClientsApi : IBarberClientsApi
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">
    /// Клиент с заданным <see cref="HttpClient.BaseAddress"/> и обработчиком авторизации.
    /// </param>
    public BarberClientsApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BarberClientResponse>> SearchClientsAsync(
        string search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);

        var path = string.Concat(
            "api/barber/clients?search=",
            Uri.EscapeDataString(search.Trim()));

        using var response = await _httpClient
            .GetAsync(path, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var clients = await response.Content
            .ReadFromJsonAsync<List<BarberClientResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        // Пустой массив — «никого не нашли», а не ошибка; null — потеря контракта.
        return clients ?? throw new InvalidOperationException(
            "The API returned an empty barber clients response.");
    }

    /// <inheritdoc />
    public async Task<BarberBookingOptionsResponse> GetBookingOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync("api/barber/booking-options", cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var options = await response.Content
            .ReadFromJsonAsync<BarberBookingOptionsResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return options ?? throw new InvalidOperationException(
            "The API returned an empty barber booking options response.");
    }

    /// <inheritdoc />
    public async Task<BarberSlotsResponse> GetSlotsAsync(
        DateOnly slotDate,
        Guid? hairServiceId,
        Guid? salonId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildSlotsPath(slotDate, hairServiceId, salonId), cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var slots = await response.Content
            .ReadFromJsonAsync<BarberSlotsResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return slots ?? throw new InvalidOperationException("The API returned an empty slots response.");
    }

    /// <summary>
    /// Собирает адрес сетки слотов собственного профиля.
    /// </summary>
    /// <remarks>
    /// Дата — календарная строка салона в инвариантной культуре, без пересчёта в UTC.
    /// Необязательные услуга и салон не передаются вовсе, когда не выбраны: сервер сам
    /// подставит шаг сетки по умолчанию и основной салон.
    /// </remarks>
    private static string BuildSlotsPath(DateOnly slotDate, Guid? hairServiceId, Guid? salonId)
    {
        var parameters = new List<string>(3)
        {
            string.Concat("date=", slotDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        };

        if (hairServiceId is { } serviceId)
        {
            parameters.Add(string.Concat(
                "hairServiceId=",
                serviceId.ToString("D", CultureInfo.InvariantCulture)));
        }

        if (salonId is { } salon)
        {
            parameters.Add(string.Concat(
                "salonId=",
                salon.ToString("D", CultureInfo.InvariantCulture)));
        }

        return string.Concat("api/barber/slots?", string.Join('&', parameters));
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
