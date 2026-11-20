using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileSalon;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileService;
using BarberSearch.Contracts.BarberProfiles.CreateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfileSalons;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfileService;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api.Barber;

/// <summary>
/// HTTP-клиент собственного профиля мастера.
/// </summary>
/// <remarks>
/// Дочерние сущности адресуются через профиль (<c>api/barber-profiles/{id}/...</c>): они
/// правятся только через агрегат-корень, и маршрут это отражает. Все записи, кроме создания,
/// отвечают <c>204</c>, поэтому их тело не читается. <c>X-CSRF-TOKEN</c> не отправляется:
/// клиент Bearer, а не cookie.
/// </remarks>
public sealed class BarberProfileApi : IBarberProfileApi
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">
    /// Клиент с заданным <see cref="HttpClient.BaseAddress"/> и обработчиком авторизации.
    /// </param>
    public BarberProfileApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<BarberProfileResponse> GetOwnAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);

        using var response = await _httpClient
            .GetAsync(BuildProfilePath(barberProfileId), cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var profile = await response.Content
            .ReadFromJsonAsync<BarberProfileResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return profile ?? throw new InvalidOperationException(
            "The API returned an empty barber profile response.");
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(
        BarberProfileCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/barber-profiles")
        {
            Content = JsonContent.Create(request, options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var created = await response.Content
            .ReadFromJsonAsync<CreatedResourceResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return created is null || created.Id == Guid.Empty
            ? throw new InvalidOperationException("The API returned an empty barber profile response.")
            : created.Id;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        Guid barberProfileId,
        BarberProfileUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Put,
            BuildProfilePath(barberProfileId))
        {
            Content = JsonContent.Create(request, options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BarberSalonSummaryResponse>> GetSalonsAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);

        using var response = await _httpClient
            .GetAsync(BuildProfilePath(barberProfileId, "salons"), cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var salons = await response.Content
            .ReadFromJsonAsync<List<BarberSalonSummaryResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        // Пустой массив — законный ответ (салонов ещё нет), а null — потеря контракта.
        return salons ?? throw new InvalidOperationException(
            "The API returned an empty barber salons response.");
    }

    /// <inheritdoc />
    public async Task AddSalonAsync(
        Guid barberProfileId,
        Guid salonId,
        bool isPrimary,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);
        EnsureSalonId(salonId);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            BuildProfilePath(barberProfileId, "salons"))
        {
            Content = JsonContent.Create(
                new AddBarberProfileSalonRequest(salonId, isPrimary),
                options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveSalonAsync(
        Guid barberProfileId,
        Guid salonId,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);
        EnsureSalonId(salonId);

        using var response = await _httpClient
            .DeleteAsync(
                BuildProfilePath(
                    barberProfileId,
                    string.Concat("salons/", salonId.ToString("D", CultureInfo.InvariantCulture))),
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetPrimarySalonAsync(
        Guid barberProfileId,
        Guid salonId,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);
        EnsureSalonId(salonId);

        var path = BuildProfilePath(
            barberProfileId,
            string.Concat(
                "salons/",
                salonId.ToString("D", CultureInfo.InvariantCulture),
                "/primary"));

        using var response = await _httpClient
            .PutAsync(path, content: null, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddServiceAsync(
        Guid barberProfileId,
        AddBarberProfileServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            BuildProfilePath(barberProfileId, "services"))
        {
            Content = JsonContent.Create(request, options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateServiceAsync(
        Guid barberProfileId,
        Guid hairServiceId,
        UpdateBarberProfileServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);
        EnsureHairServiceId(hairServiceId);
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Put,
            BuildServicePath(barberProfileId, hairServiceId))
        {
            Content = JsonContent.Create(request, options: ApiJson.Options),
        };

        using var response = await _httpClient
            .SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveServiceAsync(
        Guid barberProfileId,
        Guid hairServiceId,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);
        EnsureHairServiceId(hairServiceId);

        using var response = await _httpClient
            .DeleteAsync(BuildServicePath(barberProfileId, hairServiceId), cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddSpecializationAsync(
        Guid barberProfileId,
        Guid specializationId,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);
        EnsureSpecializationId(specializationId);

        using var response = await _httpClient
            .PostAsync(
                BuildSpecializationPath(barberProfileId, specializationId),
                content: null,
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveSpecializationAsync(
        Guid barberProfileId,
        Guid specializationId,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileId(barberProfileId);
        EnsureSpecializationId(specializationId);

        using var response = await _httpClient
            .DeleteAsync(
                BuildSpecializationPath(barberProfileId, specializationId),
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Собирает адрес профиля или его дочернего ресурса.
    /// </summary>
    private static string BuildProfilePath(Guid barberProfileId, string? tail = null) =>
        tail is null
            ? string.Concat(
                "api/barber-profiles/",
                barberProfileId.ToString("D", CultureInfo.InvariantCulture))
            : string.Concat(
                "api/barber-profiles/",
                barberProfileId.ToString("D", CultureInfo.InvariantCulture),
                "/",
                tail);

    /// <summary>
    /// Собирает адрес услуги в прайсе.
    /// </summary>
    private static string BuildServicePath(Guid barberProfileId, Guid hairServiceId) =>
        BuildProfilePath(
            barberProfileId,
            string.Concat("services/", hairServiceId.ToString("D", CultureInfo.InvariantCulture)));

    /// <summary>
    /// Собирает адрес специализации профиля.
    /// </summary>
    private static string BuildSpecializationPath(Guid barberProfileId, Guid specializationId) =>
        BuildProfilePath(
            barberProfileId,
            string.Concat(
                "specializations/",
                specializationId.ToString("D", CultureInfo.InvariantCulture)));

    /// <summary>
    /// Отсекает пустой идентификатор профиля до запроса.
    /// </summary>
    private static void EnsureProfileId(Guid barberProfileId)
    {
        if (barberProfileId == Guid.Empty)
        {
            throw new ArgumentException(
                "The barber profile identifier must not be empty.",
                nameof(barberProfileId));
        }
    }

    /// <summary>
    /// Отсекает пустой идентификатор салона до запроса.
    /// </summary>
    private static void EnsureSalonId(Guid salonId)
    {
        if (salonId == Guid.Empty)
        {
            throw new ArgumentException("The salon identifier must not be empty.", nameof(salonId));
        }
    }

    /// <summary>
    /// Отсекает пустой идентификатор услуги справочника до запроса.
    /// </summary>
    private static void EnsureHairServiceId(Guid hairServiceId)
    {
        if (hairServiceId == Guid.Empty)
        {
            throw new ArgumentException(
                "The hair service identifier must not be empty.",
                nameof(hairServiceId));
        }
    }

    /// <summary>
    /// Отсекает пустой идентификатор специализации до запроса.
    /// </summary>
    private static void EnsureSpecializationId(Guid specializationId)
    {
        if (specializationId == Guid.Empty)
        {
            throw new ArgumentException(
                "The specialization identifier must not be empty.",
                nameof(specializationId));
        }
    }

    /// <summary>
    /// Бросает <see cref="BarberSearchApiException"/>, если ответ неуспешный.
    /// </summary>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
