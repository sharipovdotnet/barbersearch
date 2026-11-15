using System.Net.Http.Json;
using BarberSearch.Contracts.Push.RegisterPushDevice;
using BarberSearch.Contracts.Push.UnregisterPushDevice;
using BarberSearch.Domain.Push;

namespace BarberSearch.Mobile.Core.Api.Push;

/// <summary>
/// HTTP-клиент push-устройств.
/// </summary>
/// <remarks>
/// Тело собирается из контрактов бэкенда, чтобы расхождение с API ломало сборку, а не
/// отправку. Оба ответа — <c>204</c> без тела: успех здесь означает отсутствие ошибки,
/// читать из ответа нечего. Перечисления уходят строками (<c>"platform":"Android"</c>)
/// через <see cref="ApiJson.Options"/> — тем же, что у остальных клиентов.
/// </remarks>
public sealed class PushApi : IPushApi
{
    private const string DevicesPath = "api/push/devices";
    private const string UnregisterPath = "api/push/devices/unregister";

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public PushApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task RegisterDeviceAsync(
        string token,
        PushPlatform platform,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var request = new RegisterPushDeviceRequest(token, platform);

        using var response = await _httpClient
            .PostAsJsonAsync(DevicesPath, request, ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnregisterDeviceAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var request = new UnregisterPushDeviceRequest(token);

        using var response = await _httpClient
            .PostAsJsonAsync(UnregisterPath, request, ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Бросает разобранную ошибку, если ответ неуспешный.
    /// </summary>
    /// <remarks>
    /// Успешные ответы здесь только <c>204</c>: тело не читается, поэтому достаточно
    /// проверить статус.
    /// </remarks>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw await BarberSearchApiException
            .FromResponseAsync(response, cancellationToken)
            .ConfigureAwait(false);
    }
}
