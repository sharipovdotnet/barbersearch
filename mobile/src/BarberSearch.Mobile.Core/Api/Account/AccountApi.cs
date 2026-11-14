using System.Net.Http.Json;
using BarberSearch.Contracts.Auth.Me;

namespace BarberSearch.Mobile.Core.Api.Account;

/// <summary>
/// HTTP-клиент данных пользователя; сейчас это одно чтение <c>GET api/auth/me</c>.
/// </summary>
/// <remarks>
/// Ответ строится сервером из claims, а не из базы: он показывает ровно то, что видит сервер
/// в текущем токене. Роль и подтверждение email ничего не кэшируют — после смены роли старый
/// токен отдаёт старое значение до истечения срока.
/// </remarks>
public sealed class AccountApi : IAccountApi
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public AccountApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync("api/auth/me", cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var me = await response.Content
            .ReadFromJsonAsync<MeResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return me ?? throw new InvalidOperationException("The API returned an empty current-user response.");
    }
}
