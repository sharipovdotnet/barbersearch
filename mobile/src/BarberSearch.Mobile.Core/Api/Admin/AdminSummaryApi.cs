using System.Net.Http.Json;
using BarberSearch.Contracts.Admin;

namespace BarberSearch.Mobile.Core.Api.Admin;

/// <summary>
/// HTTP-клиент админской сводки.
/// </summary>
/// <remarks>
/// Ходит через клиент с <c>AuthenticatedHttpHandler</c>: роль берётся из токена, в запросе
/// её нет. Ответ целиком разбирается в контракт бэкенда — экран показывает те же числа,
/// что и веб, без пересчёта на клиенте.
/// </remarks>
public sealed class AdminSummaryApi : IAdminSummaryApi
{
    private const string SummaryPath = "api/admin/summary";

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public AdminSummaryApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<AdminSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(SummaryPath, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var summary = await response.Content
            .ReadFromJsonAsync<AdminSummaryResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return summary ?? throw new InvalidOperationException("The API returned an empty admin summary response.");
    }
}
