using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetReviews;

namespace BarberSearch.Mobile.Core.Api;

/// <summary>
/// HTTP-клиент публичных чтений: поиск, карточка барбера и свободные слоты.
/// </summary>
/// <remarks>
/// Клиент тонкий: адрес, сериализация и разбор ошибок. Никакой бизнес-логики здесь нет —
/// она живёт в ViewModel, чтобы её можно было проверить без сети.
/// </remarks>
public sealed class BarberSearchApi : IBarberSearchApi
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Создаёт клиент поверх настроенного <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Клиент с заданным <see cref="HttpClient.BaseAddress"/>.</param>
    public BarberSearchApi(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<PagedResponse<BarberSearchItemResponse>> SearchBarbersAsync(
        string? city,
        string? cursor = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildSearchPath(city, cursor, pageSize), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<BarberSearchItemResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return page ?? throw new InvalidOperationException("The API returned an empty search response.");
    }

    /// <inheritdoc />
    public async Task<BarberProfileResponse> GetBarberProfileAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(string.Concat("api/barbers/", id.ToString("D", CultureInfo.InvariantCulture)), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var profile = await response.Content
            .ReadFromJsonAsync<BarberProfileResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return profile ?? throw new InvalidOperationException("The API returned an empty barber response.");
    }

    /// <inheritdoc />
    public async Task<BarberSlotsResponse> GetSlotsAsync(
        Guid barberProfileId,
        DateOnly slotDate,
        Guid? hairServiceId,
        Guid? salonId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildSlotsPath(barberProfileId, slotDate, hairServiceId, salonId), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var slots = await response.Content
            .ReadFromJsonAsync<BarberSlotsResponse>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return slots ?? throw new InvalidOperationException("The API returned an empty slots response.");
    }

    /// <inheritdoc />
    public async Task<PagedResponse<ReviewResponse>> GetReviewsAsync(
        Guid barberProfileId,
        string? cursor = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildReviewsPath(barberProfileId, cursor, pageSize), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<ReviewResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return page ?? throw new InvalidOperationException("The API returned an empty reviews response.");
    }

    /// <summary>
    /// Собирает относительный адрес поиска из заданных фильтров.
    /// </summary>
    /// <remarks>
    /// Пустые фильтры не передаются вовсе: сервер трактует отсутствие параметра и пустую строку
    /// одинаково, но короткий адрес читаемее в логах и кэше.
    /// </remarks>
    private static string BuildSearchPath(string? city, string? cursor, int? pageSize)
    {
        var parameters = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(city))
        {
            parameters.Add(string.Concat("city=", Uri.EscapeDataString(city)));
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            parameters.Add(string.Concat("cursor=", Uri.EscapeDataString(cursor)));
        }

        if (pageSize is not null)
        {
            parameters.Add(string.Concat(
                "pageSize=",
                pageSize.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return parameters.Count == 0
            ? "api/barbers"
            : string.Concat("api/barbers?", string.Join('&', parameters));
    }

    /// <summary>
    /// Собирает адрес сетки слотов.
    /// </summary>
    /// <remarks>
    /// Дата уходит календарной строкой в инвариантной культуре: сервер разбирает её как
    /// <c>DateOnly</c> в поясе салона. Переводить её в UTC нельзя — «15 ноября» остаётся
    /// 15 ноября в любом поясе, а инстант в полночь UTC в Москве был бы уже 16-м.
    /// </remarks>
    private static string BuildSlotsPath(
        Guid barberProfileId,
        DateOnly slotDate,
        Guid? hairServiceId,
        Guid? salonId)
    {
        var parameters = new List<string>(3)
        {
            string.Concat("date=", slotDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        };

        if (hairServiceId is not null)
        {
            parameters.Add(string.Concat("hairServiceId=", hairServiceId.Value.ToString("D")));
        }

        if (salonId is not null)
        {
            parameters.Add(string.Concat("salonId=", salonId.Value.ToString("D")));
        }

        return string.Concat(
            "api/barbers/",
            barberProfileId.ToString("D"),
            "/slots?",
            string.Join('&', parameters));
    }

    /// <summary>
    /// Собирает адрес страницы отзывов.
    /// </summary>
    /// <remarks>
    /// <c>barberProfileId</c> обязателен. Пустой курсор в адрес не попадает: сервер трактует
    /// отсутствие параметра как первую страницу, а строка <c>cursor=</c> вызвала бы
    /// <c>review.cursor.invalid</c>. Курсор непрозрачен и уходит как есть — только
    /// экранируется, потому что это часть query.
    /// </remarks>
    private static string BuildReviewsPath(Guid barberProfileId, string? cursor, int? pageSize)
    {
        var parameters = new List<string>(3)
        {
            string.Concat("barberProfileId=", barberProfileId.ToString("D")),
        };

        if (pageSize is not null)
        {
            parameters.Add(string.Concat(
                "pageSize=",
                pageSize.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            parameters.Add(string.Concat("cursor=", Uri.EscapeDataString(cursor)));
        }

        return string.Concat("api/reviews?", string.Join('&', parameters));
    }
}
