using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api;

/// <summary>
/// HTTP-клиент публичных чтений: поиск барберов.
/// </summary>
/// <remarks>
/// Клиент тонкий: адрес, сериализация и разбор ошибок. Никакой бизнес-логики здесь нет —
/// она живёт в ViewModel, чтобы её можно было проверить без сети.
/// </remarks>
public sealed class BarberSearchApi : IBarberSearchApi
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public BarberSearchApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<PagedResponse<BarberSearchItemResponse>> SearchBarbersAsync(
        string? city,
        string? cursor = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildSearchPath(city, cursor, pageSize), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<BarberSearchItemResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return page ?? throw new InvalidOperationException("The API returned an empty search response.");
    }

    /// <summary>
    /// Собирает относительный адрес поиска из заданных фильтров.
    /// </summary>
    /// <remarks>
    /// Пустые фильтры не передаются вовсе: сервер трактует отсутствие параметра и пустую строку
    /// одинаково, но короткий адрес читаемее в логах и кэше.
    /// </remarks>
    private static string BuildSearchPath(string? city, string? cursor, int? pageSize)
    {
        var parameters = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(city))
        {
            parameters.Add(string.Concat("city=", Uri.EscapeDataString(city)));
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
            ? "api/barbers"
            : string.Concat("api/barbers?", string.Join('&', parameters));
    }
}
