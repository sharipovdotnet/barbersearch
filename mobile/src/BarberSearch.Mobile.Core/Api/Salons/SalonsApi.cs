using System.Net.Http.Json;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.CreateSalon;

namespace BarberSearch.Mobile.Core.Api.Salons;

/// <summary>
/// HTTP-клиент создания собственного салона барбера.
/// </summary>
/// <remarks>
/// Клиент тонкий: адрес, сериализация и разбор ответа. Правила полей, тексты ошибок и
/// нормализация <c>isListed</c> живут в ViewModel, чтобы их можно было проверить без сети.
/// Тело уходит через <see cref="ApiJson.Options"/>: сервер принимает перечисления строками,
/// а числа в <c>kind</c> запрещены настройками JSON.
/// </remarks>
public sealed class SalonsApi : ISalonsApi
{
    /// <summary>
    /// Адрес создания салона владельцем: профиль барбера берётся из токена.
    /// </summary>
    private const string CreateOwnPath = "api/salons/own";

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">
    /// Клиент с заданным <see cref="HttpClient.BaseAddress"/> и обработчиком авторизации.
    /// </param>
    public SalonsApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<Guid> CreateOwnAsync(
        CreateSalonRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, CreateOwnPath)
        {
            Content = JsonContent.Create(request, options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
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

        return created is null || created.Id == Guid.Empty
            ? throw new InvalidOperationException("The API returned an empty salon response.")
            : created.Id;
    }
}
