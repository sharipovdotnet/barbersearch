namespace BarberSearch.Mobile.Core.Api.Admin;

/// <summary>
/// HTTP-клиент админских превью: тот же авторизованный клиент, что и у разделов.
/// </summary>
/// <remarks>
/// Отдельный клиент, а не метод раздела, потому что превью нужны двум разделам сразу —
/// очередям фото и карточке мастера, — а правило доступа у них одно. Bearer надевает
/// <c>AuthenticatedHttpHandler</c>: без него сервер отвечает <c>404</c> на неодобренный кадр,
/// и именно это выглядело серым прямоугольником.
/// </remarks>
public sealed class AdminMediaApi : IAdminMediaApi
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/> и Bearer.</param>
    public AdminMediaApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        using var response = await _httpClient
            .GetAsync(new Uri(url, UriKind.Absolute), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        return await response.Content
            .ReadAsByteArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
