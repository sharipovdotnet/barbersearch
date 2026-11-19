using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api.Admin;

/// <summary>
/// HTTP-клиент админской очереди мастеров.
/// </summary>
/// <remarks>
/// <para>
/// Ходит через клиент с <c>AuthenticatedHttpHandler</c>: роль берётся из токена, в запросе
/// её нет. Ответы разбираются в контракты бэкенда — экран показывает те же поля, что и веб,
/// без пересчёта на клиенте.
/// </para>
/// <para>
/// Фильтр уходит в <c>status</c> теми же словами, что понимает
/// <c>AdminBarbersController</c>: <c>pending</c>, <c>all</c> и <c>hidden</c>. Пустой курсор
/// не отправляется никогда: для сервера это был бы невалидный курсор, а не первая страница.
/// </para>
/// <para>
/// Записи не читают тело ответа: успех — это <c>204</c>, содержимого у него нет.
/// </para>
/// </remarks>
public sealed class AdminMastersApi : IAdminMastersApi
{
    /// <summary>
    /// Размер страницы по умолчанию — тот же, что у веба (<c>ADMIN_BARBERS_PAGE_SIZE</c>).
    /// </summary>
    public const int DefaultPageSize = 20;

    private const string MastersPath = "api/admin/barbers";
    private const string ProfilesPath = "api/barber-profiles";

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public AdminMastersApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<PagedResponse<AdminBarberResponse>> GetMastersAsync(
        AdminMasterFilter filter,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildMastersPath(filter, cursor, pageSize), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<AdminBarberResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return page ?? throw new InvalidOperationException("The API returned an empty admin masters page.");
    }

    /// <inheritdoc />
    public async Task<AdminBarberDetailResponse> GetMasterAsync(
        Guid masterId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildMasterPath(masterId), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var master = await response.Content
            .ReadFromJsonAsync<AdminBarberDetailResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return master ?? throw new InvalidOperationException("The API returned an empty admin master response.");
    }

    /// <inheritdoc />
    public async Task VerifyMasterAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .PostAsync(BuildVerificationPath(barberProfileId), content: null, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task UnverifyMasterAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .DeleteAsync(BuildVerificationPath(barberProfileId), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Собирает адрес страницы: фильтр, размер и необязательный курсор.
    /// </summary>
    /// <remarks>
    /// Порядок параметров постоянный, поэтому адрес проверяется тестом целиком.
    /// Значения экранируются: курсор непрозрачен и может содержать <c>+</c>, <c>/</c> и <c>=</c>.
    /// </remarks>
    /// <param name="filter">Фильтр списка.</param>
    /// <param name="cursor">Курсор следующей страницы; <c>null</c> — первая страница.</param>
    /// <param name="pageSize">Размер страницы.</param>
    /// <returns>Относительный адрес запроса.</returns>
    private static string BuildMastersPath(AdminMasterFilter filter, string? cursor, int pageSize)
    {
        var path = string.Concat(
            MastersPath,
            "?status=",
            RequestValue(filter),
            "&pageSize=",
            pageSize.ToString(CultureInfo.InvariantCulture));

        if (string.IsNullOrEmpty(cursor))
        {
            return path;
        }

        return string.Concat(path, "&cursor=", Uri.EscapeDataString(cursor));
    }

    /// <summary>
    /// Собирает адрес карточки по идентификатору профиля.
    /// </summary>
    /// <param name="masterId">Идентификатор профиля.</param>
    /// <returns>Относительный адрес запроса.</returns>
    private static string BuildMasterPath(Guid masterId) => string.Concat(
        MastersPath,
        "/",
        masterId.ToString("D", CultureInfo.InvariantCulture));

    /// <summary>
    /// Собирает адрес записи подтверждения: публичный контроллер профилей, не админский.
    /// </summary>
    /// <param name="barberProfileId">Идентификатор профиля.</param>
    /// <returns>Относительный адрес запроса.</returns>
    private static string BuildVerificationPath(Guid barberProfileId) => string.Concat(
        ProfilesPath,
        "/",
        barberProfileId.ToString("D", CultureInfo.InvariantCulture),
        "/verification");

    /// <summary>
    /// Переводит фильтр экрана в слово сервера.
    /// </summary>
    /// <param name="filter">Фильтр списка.</param>
    /// <returns><c>pending</c>, <c>all</c> или <c>hidden</c>.</returns>
    private static string RequestValue(AdminMasterFilter filter) => filter switch
    {
        AdminMasterFilter.All => "all",
        AdminMasterFilter.Hidden => "hidden",
        _ => "pending",
    };
}
