using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Contracts.BarberRequests.RejectBarberRequest;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api.Admin;

/// <summary>
/// HTTP-клиент администраторских заявок на роль мастера.
/// </summary>
/// <remarks>
/// <para>
/// Тело отказа собирается из контракта бэкенда: расхождение ломает сборку, а не отправку.
/// У одобрения тела нет, у отказа есть только причина. Оба ответа несут обновлённую заявку,
/// но экран уходит назад и перечитывает очередь, поэтому тела не разбираются: успех — это
/// статус ответа.
/// </para>
/// <para>
/// Курсор непрозрачен и кодируется целиком: он может содержать символы, недопустимые в адресе.
/// </para>
/// </remarks>
public sealed class AdminRequestsApi : IAdminRequestsApi
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public AdminRequestsApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<PagedResponse<AdminBarberRequestResponse>> GetRequestsAsync(
        AdminRequestListFilter filter,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildRequestsPath(filter, cursor, pageSize), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<AdminBarberRequestResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return page ?? throw new InvalidOperationException("The API returned an empty admin requests response.");
    }

    /// <inheritdoc />
    public async Task<AdminBarberRequestResponse> GetRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildRequestPath(requestId), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var request = await response.Content
            .ReadFromJsonAsync<AdminBarberRequestResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return request ?? throw new InvalidOperationException("The API returned an empty admin request response.");
    }

    /// <inheritdoc />
    public async Task ApproveRequestAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        // У одобрения нет тела, поэтому PostAsync, а не PostAsJsonAsync: пустой объект
        // серверу не нужен, а лишнее тело читалось бы как данные решения.
        using var response = await _httpClient
            .PostAsync(BuildApprovalPath(requestId), content: null, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RejectRequestAsync(
        Guid requestId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var request = new RejectBarberRequestRequest(reason);

        using var response = await _httpClient
            .PostAsJsonAsync(BuildRejectionPath(requestId), request, ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Собирает адрес страницы заявок.
    /// </summary>
    /// <remarks>
    /// Пустой курсор не передаётся вовсе: отсутствие параметра — это первая страница,
    /// а пустая строка была бы невалидным курсором.
    /// </remarks>
    private static string BuildRequestsPath(
        AdminRequestListFilter filter,
        string? cursor,
        int pageSize)
    {
        var path = string.Concat(
            "api/admin/barber-requests?status=",
            StatusValue(filter),
            "&pageSize=",
            pageSize.ToString(CultureInfo.InvariantCulture));

        return string.IsNullOrWhiteSpace(cursor)
            ? path
            : string.Concat(path, "&cursor=", Uri.EscapeDataString(cursor));
    }

    /// <summary>
    /// Переводит фильтр экрана в значение параметра <c>status</c>.
    /// </summary>
    private static string StatusValue(AdminRequestListFilter filter) => filter switch
    {
        AdminRequestListFilter.All => "all",
        _ => "pending",
    };

    /// <summary>
    /// Собирает адрес одной заявки.
    /// </summary>
    private static string BuildRequestPath(Guid requestId) => string.Concat(
        "api/admin/barber-requests/",
        requestId.ToString("D", CultureInfo.InvariantCulture));

    /// <summary>
    /// Собирает адрес одобрения.
    /// </summary>
    private static string BuildApprovalPath(Guid requestId) =>
        string.Concat(BuildRequestPath(requestId), "/approval");

    /// <summary>
    /// Собирает адрес отказа.
    /// </summary>
    private static string BuildRejectionPath(Guid requestId) =>
        string.Concat(BuildRequestPath(requestId), "/rejection");
}
