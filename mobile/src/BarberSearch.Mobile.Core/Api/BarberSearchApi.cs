using System.Globalization;
using System.Net.Http.Json;
using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Contracts.Salons.GetSalonDirectory;
using BarberSearch.Contracts.Specializations.GetSpecializations;

namespace BarberSearch.Mobile.Core.Api;

/// <summary>
/// HTTP-клиент публичных чтений: поиск, справочники фильтров, карточка мастера и слоты.
/// </summary>
/// <remarks>
/// Клиент тонкий: адрес, сериализация и разбор ошибок. Никакой бизнес-логики здесь нет —
/// она живёт в ViewModel, чтобы её можно было проверить без сети.
/// </remarks>
public sealed class BarberSearchApi : IBarberSearchApi
{
    /// <summary>
    /// Сколько подсказок города просить у сервера: восемь — его же максимум и значение по умолчанию.
    /// </summary>
    private const int CitySuggestionsLimit = 8;

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
        BarberSearchFilters filters,
        string? cursor = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);

        using var response = await _httpClient
            .GetAsync(BuildSearchPath(filters, cursor, pageSize), cancellationToken)
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
    public async Task<IReadOnlyList<SpecializationResponse>> GetSpecializationsAsync(
        CancellationToken cancellationToken = default) =>
        await GetCatalogAsync<SpecializationResponse>("api/specializations", cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<HairServiceResponse>> GetHairServicesAsync(
        CancellationToken cancellationToken = default) =>
        await GetCatalogAsync<HairServiceResponse>("api/hair-services", cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CitySuggestionResponse>> GetCitySuggestionsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await GetCatalogAsync<CitySuggestionResponse>(
                BuildCitySuggestionsPath(query),
                cancellationToken)
            .ConfigureAwait(false);
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<SalonDirectoryItemResponse>> SearchSalonsAsync(
        string? city,
        string? search,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync(BuildSalonsPath(city, search), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var salons = await response.Content
            .ReadFromJsonAsync<List<SalonDirectoryItemResponse>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        // Пустой каталог — законный ответ, а null означает потерянное тело: это разные
        // состояния, и «салонов нет» нельзя показывать вместо сбоя.
        return salons ?? throw new InvalidOperationException(
            "The API returned an empty salon directory response.");
    }

    /// <summary>
    /// Собирает адрес публичного каталога салонов.
    /// </summary>
    /// <remarks>
    /// Фильтры тримятся: город и поиск сравниваются на сервере как строки, и пробел по краям
    /// сделал бы условие никогда не совпадающим. Пустые значения не попадают в адрес вовсе,
    /// чтобы отсутствие фильтра было видно и в логах, и в кэше.
    /// </remarks>
    private static string BuildSalonsPath(string? city, string? search)
    {
        var parameters = new List<string>(2);
        var trimmedCity = city?.Trim();
        var trimmedSearch = search?.Trim();

        if (!string.IsNullOrEmpty(trimmedCity))
        {
            parameters.Add(string.Concat("city=", Uri.EscapeDataString(trimmedCity)));
        }

        if (!string.IsNullOrEmpty(trimmedSearch))
        {
            parameters.Add(string.Concat("search=", Uri.EscapeDataString(trimmedSearch)));
        }

        return parameters.Count == 0
            ? "api/salons"
            : string.Concat("api/salons?", string.Join('&', parameters));
    }

    /// <summary>
    /// Собирает относительный адрес поиска из заданных фильтров.
    /// </summary>
    /// <remarks>
    /// Пустые фильтры не передаются вовсе: сервер трактует отсутствие параметра и пустую строку
    /// одинаково, но короткий адрес читаемее в логах и кэше. Дата уходит календарной строкой
    /// в инвариантной культуре, язык — именем enum (<c>Ru</c>): числовую форму сервер отвергает.
    /// </remarks>
    private static string BuildSearchPath(BarberSearchFilters filters, string? cursor, int? pageSize)
    {
        var parameters = new List<string>(8);

        if (!string.IsNullOrWhiteSpace(filters.City))
        {
            parameters.Add(string.Concat("city=", Uri.EscapeDataString(filters.City)));
        }

        if (filters.Date is { } date)
        {
            parameters.Add(string.Concat(
                "date=",
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        if (filters.SpecializationId is { } specializationId)
        {
            parameters.Add(string.Concat(
                "specializationId=",
                specializationId.ToString("D", CultureInfo.InvariantCulture)));
        }

        if (filters.HairServiceId is { } hairServiceId)
        {
            parameters.Add(string.Concat(
                "hairServiceId=",
                hairServiceId.ToString("D", CultureInfo.InvariantCulture)));
        }

        if (filters.Language is { } language)
        {
            parameters.Add(string.Concat("language=", language.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(filters.AdditionalService))
        {
            parameters.Add(string.Concat(
                "additionalService=",
                Uri.EscapeDataString(filters.AdditionalService)));
        }

        // Геопозиция уходит только полной парой: сервер отвергает половину, а «широта без
        // долготы» ничего не сортирует. Формат — round-trip в инвариантной культуре, иначе
        // телефон с запятой в качестве разделителя отправил бы «56,8389» и получил 400.
        if (filters is { Latitude: { } latitude, Longitude: { } longitude })
        {
            parameters.Add(string.Concat(
                "latitude=",
                latitude.ToString("R", CultureInfo.InvariantCulture)));
            parameters.Add(string.Concat(
                "longitude=",
                longitude.ToString("R", CultureInfo.InvariantCulture)));
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
    /// Читает публичный справочник: сервер отдаёт голый JSON-массив без пагинации.
    /// </summary>
    /// <remarks>
    /// Пустой массив — не ошибка: справочник может быть пуст, и экран просто скроет селект.
    /// Нулевое тело — нарушение контракта, поэтому превращается в исключение, а не в пустой
    /// список: «фильтров нет» и «ответ потерян» — разные состояния.
    /// </remarks>
    /// <typeparam name="T">Строка справочника из Contracts.</typeparam>
    /// <param name="path">Относительный адрес справочника.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Строки справочника в порядке сервера.</returns>
    private async Task<IReadOnlyList<T>> GetCatalogAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(path, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await BarberSearchApiException
                .FromResponseAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        var items = await response.Content
            .ReadFromJsonAsync<List<T>>(ApiJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return items ?? throw new InvalidOperationException("The API returned an empty catalog response.");
    }

    /// <summary>
    /// Собирает адрес подсказок города.
    /// </summary>
    /// <remarks>
    /// Запрос тримится и уходит экранированным: город может содержать дефис, пробел или точку.
    /// Лимит передаётся явно, чтобы клиент не зависел от серверного значения по умолчанию, если
    /// оно изменится. Пустой запрос отсекает <see cref="GetCitySuggestionsAsync"/>: сервер
    /// ответил бы <c>400</c> на строку короче двух символов, а порог проверяет вызывающий.
    /// </remarks>
    private static string BuildCitySuggestionsPath(string query) =>
        string.Concat(
            "api/addresses/cities?query=",
            Uri.EscapeDataString(query.Trim()),
            "&limit=",
            CitySuggestionsLimit.ToString(CultureInfo.InvariantCulture));

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
