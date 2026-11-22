using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.Addresses.SuggestAddresses;

namespace BarberSearch.Mobile.Core.Api.Addresses;

/// <summary>
/// HTTP-клиент подсказок адреса внутри города.
/// </summary>
/// <remarks>
/// Адрес повторяет веб (<c>addressSuggestionsUrl</c>): <c>query</c>, затем
/// <c>cityProviderId</c> или <c>city</c>, затем <c>limit</c>. Лимит передаётся явно, чтобы
/// клиент не зависел от серверного значения по умолчанию, если оно изменится. Оба значения
/// тримятся и экранируются: город и улица могут содержать пробел, дефис или точку.
/// </remarks>
public sealed class AddressesApi : IAddressesApi
{
    /// <summary>
    /// Сколько подсказок адреса просить: восемь — серверный максимум и значение по умолчанию.
    /// </summary>
    private const int AddressSuggestionsLimit = 8;

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">
    /// Клиент с заданным <see cref="HttpClient.BaseAddress"/> и обработчиком авторизации.
    /// </param>
    public AddressesApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AddressSuggestionResponse>> GetAddressSuggestionsAsync(
        string query,
        string? city,
        string? cityProviderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        using var response = await _httpClient
            .GetAsync(BuildPath(query, city, cityProviderId), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var items = await response.Content
            .ReadFromJsonAsync<List<AddressSuggestionResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        // Пустой массив — законное «ничего не найдено», а null — потерянное тело контракта.
        return items ?? throw new InvalidOperationException(
            "The API returned an empty address suggestions response.");
    }

    /// <summary>
    /// Собирает адрес подсказок адреса.
    /// </summary>
    /// <remarks>
    /// Пустые <paramref name="city"/> и <paramref name="cityProviderId"/> не попадают в адрес:
    /// сервер трактует отсутствие параметра и пустую строку одинаково, но лишний <c>city=</c>
    /// только засоряет адрес и кэш. Провайдерский токен важнее свободного текста — то же
    /// правило у веба.
    /// </remarks>
    private static string BuildPath(string query, string? city, string? cityProviderId)
    {
        var parameters = new List<string>(3)
        {
            string.Concat("query=", Uri.EscapeDataString(query.Trim())),
        };

        var providerId = cityProviderId?.Trim();

        if (!string.IsNullOrEmpty(providerId))
        {
            parameters.Add(string.Concat(
                "cityProviderId=",
                Uri.EscapeDataString(providerId)));
        }
        else
        {
            var cityText = city?.Trim();

            if (!string.IsNullOrEmpty(cityText))
            {
                parameters.Add(string.Concat("city=", Uri.EscapeDataString(cityText)));
            }
        }

        parameters.Add(string.Concat(
            "limit=",
            AddressSuggestionsLimit.ToString(CultureInfo.InvariantCulture)));

        return string.Concat("api/addresses/suggest?", string.Join('&', parameters));
    }
}
