using System.Net.Http.Json;
using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.BarberRequests.CreateBarberRequest;
using BarberSearch.Contracts.BarberRequests.GetMyBarberRequest;

namespace BarberSearch.Mobile.Core.Api.BarberRequests;

/// <summary>
/// HTTP-клиент заявки на роль мастера.
/// </summary>
/// <remarks>
/// Тело запроса собирается из контрактов бэкенда, чтобы расхождение ломало сборку, а не
/// отправку. Ошибки не переводятся и не ветвятся здесь: <c>detail</c> английский и
/// диагностический, поэтому решение о тексте принимает <c>BarberRequestViewModel</c> по
/// <c>StatusCode</c> и <c>ErrorCodes</c>.
/// </remarks>
public sealed class BarberRequestsApi : IBarberRequestsApi
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public BarberRequestsApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<MyBarberRequestResponse> GetMineAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync("api/barber-requests/mine", cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var mine = await response.Content
            .ReadFromJsonAsync<MyBarberRequestResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return mine ?? throw new InvalidOperationException("The API returned an empty barber request response.");
    }

    /// <inheritdoc />
    public async Task<BarberRequestResponse> CreateAsync(
        CreateBarberRequestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await _httpClient
            .PostAsJsonAsync("api/barber-requests", request, ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var created = await response.Content
            .ReadFromJsonAsync<BarberRequestResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return created ?? throw new InvalidOperationException("The API returned an empty barber request response.");
    }
}
