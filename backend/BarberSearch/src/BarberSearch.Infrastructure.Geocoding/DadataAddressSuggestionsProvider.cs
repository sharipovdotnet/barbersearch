using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Application.Addresses;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Infrastructure.Geocoding;

/// <summary>
/// Подсказки города и адреса от DaData через <see cref="IHttpClientFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Пакета для DaData нет: запрос — обычный <c>POST {BaseUrl}</c> с телом
/// <c>{"query":…,"count":N}</c>, заголовком <c>Authorization: Token {ApiKey}</c> и
/// <c>Accept: application/json</c>. Ключ приходит из <see cref="AddressSuggestionsOptions"/>
/// и не попадает ни в лог, ни в текст ошибки, ни в ответ клиенту. Адрес запроса — ровно
/// <see cref="AddressSuggestionsOptions.BaseUrl"/>: путь к методу подсказок уже в нём, и
/// дописывать сегменты адаптер не должен.
/// </para>
/// <para>
/// Два режима одного метода. <see cref="AddressSuggestionBound.City"/> ограничивает ответ
/// городом через <c>from_bound</c>/<c>to_bound</c>; <see cref="AddressSuggestionBound.Address"/>
/// ищет улицу внутри выбранного города через <c>locations</c> — сначала по
/// <c>city_fias_id</c>, затем по свободному <c>city</c>. Городской режим <c>locations</c>
/// не передаёт вообще: там фильтр — сам bound.
/// </para>
/// <para>
/// Повторов нет. Пустой массив <c>suggestions</c> (и отсутствующий массив) — валидный успех
/// с пустым списком: с выбранным городом он означает «в этом городе такой улицы нет», а
/// повтор без <c>locations</c> вернул бы улицы чужих городов. Сеть, таймаут, не-<c>2xx</c>,
/// битый JSON и незаданные настройки превращаются в
/// <see cref="ApiErrorCodes.AddressSuggestions.Unavailable"/>: подсказки — прогрессивное
/// улучшение, форма обязана остаться рабочей, а клиент показывает обычный ввод.
/// </para>
/// <para>Правила маппинга ответа:</para>
/// <list type="bullet">
/// <item><description>
/// <c>display</c> — <c>value</c>; подсказка с пустым <c>value</c> отбрасывается.
/// </description></item>
/// <item><description>
/// Подсказка без <c>data.city</c> или <c>data.country</c> отбрасывается: город и страна
/// обязательны в домене салона.
/// </description></item>
/// <item><description>
/// <c>address</c> — <c>street_with_type</c> и <c>house</c> через <c>", "</c>, без города;
/// если улицы нет — <c>value</c> целиком.
/// </description></item>
/// <item><description>
/// <c>region</c> — <c>region_with_type</c>, иначе <c>region</c>; <c>postalCode</c> —
/// как пришёл, <c>null</c> допустим.
/// </description></item>
/// <item><description>
/// <c>providerId</c> — <c>fias_id</c>, иначе <c>kladr_id</c>, иначе <c>null</c>.
/// </description></item>
/// <item><description>
/// <c>latitude</c>/<c>longitude</c> — <c>geo_lat</c>/<c>geo_lon</c>, разобранные в
/// <see cref="CultureInfo.InvariantCulture"/>; неразобранное значение — <c>null</c>.
/// </description></item>
/// </list>
/// <para>
/// В журнал попадают только провайдер, причина отказа, bound, код состояния, длительность
/// и длина запроса. Сам запрос, адрес, координаты, ключ и тело ответа не логируются.
/// </para>
/// </remarks>
public sealed class DadataAddressSuggestionsProvider : IAddressSuggestionsProvider
{
    /// <summary>
    /// Имя именованного <see cref="HttpClient"/> в <see cref="IHttpClientFactory"/>.
    /// </summary>
    public const string HTTP_CLIENT_NAME = "geocoding";

    /// <summary>
    /// Имя провайдера для журнала: вендор, а не класс, — по нему видно, кто ответил.
    /// </summary>
    private const string PROVIDER_NAME = "dadata";

    /// <summary>
    /// Значение <c>from_bound</c>/<c>to_bound</c> для подсказок города.
    /// </summary>
    private const string CITY_BOUND = "city";

    /// <summary>
    /// Готовый фильтр «только города»: одно и то же значение в обоих bound.
    /// </summary>
    private static readonly BoundFilter CITY_BOUND_FILTER = new() { Value = CITY_BOUND };

    /// <summary>
    /// Настройки сериализации запроса: <c>null</c>-свойства не пишутся, поэтому
    /// <c>locations</c> и bound появляются в теле только тогда, когда нужны.
    /// </summary>
    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AddressSuggestionsOptions _options;
    private readonly ILogger<DadataAddressSuggestionsProvider> _logger;

    public DadataAddressSuggestionsProvider(
        IHttpClientFactory httpClientFactory,
        AddressSuggestionsOptions options,
        ILogger<DadataAddressSuggestionsProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AddressSuggestion>, Failure>> SuggestAsync(
        AddressSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.IsConfigured)
        {
            // Ключа или BaseUrl нет — HTTP-запрос не выполняется вообще: выключенная фича
            // не должна зависеть от сети. Обработчик отдаёт клиенту тот же код 409.
            return Unavailable("Address suggestions are not configured on the server.");
        }

        var payload = BuildPayload(request);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);

            using var message = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl!)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            // Ключ уходит только заголовком: в URL он попал бы в журнал прокси и HttpClient.
            _ = message.Headers.TryAddWithoutValidation(
                "Authorization",
                "Token " + _options.ApiKey!.Trim());
            _ = message.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await client
                .SendAsync(message, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                LogFailure("failed", status, request, stopwatch);

                return Unavailable($"Address suggestions provider failed (HTTP {status}).");
            }

            var json = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            var suggestions = Map(
                JsonSerializer.Deserialize<SuggestionsResponse>(json, JSON_OPTIONS));

            _logger.LogDebug(
                "Address suggestions provider {Provider} answered with HTTP {Status} "
                + "for bound {Bound} in {Elapsed} ms (query length {QueryLength}).",
                PROVIDER_NAME,
                (int)response.StatusCode,
                request.Bound,
                stopwatch.ElapsedMilliseconds,
                request.Query.Length);

            return Result.Success<IReadOnlyList<AddressSuggestion>, Failure>(suggestions);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Таймаут HttpClient: вызывающий запрос не отменял, значит провайдер не успел.
            // Подсказка, пришедшая через пять секунд, хуже её отсутствия — повторов нет.
            LogFailure("timed out", status: null, request, stopwatch);

            return Unavailable("Address suggestions provider timed out.");
        }
        catch (HttpRequestException)
        {
            LogFailure("is unreachable", status: null, request, stopwatch);

            return Unavailable("Address suggestions provider is unreachable.");
        }
        catch (JsonException)
        {
            LogFailure("returned malformed JSON", status: null, request, stopwatch);

            return Unavailable("Address suggestions provider returned malformed JSON.");
        }
        catch (NotSupportedException)
        {
            LogFailure("returned unsupported JSON", status: null, request, stopwatch);

            return Unavailable("Address suggestions provider returned unsupported JSON.");
        }
    }

    /// <summary>
    /// Собирает тело запроса подсказок.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Город: <c>from_bound</c>/<c>to_bound</c> = <c>city</c> и никаких <c>locations</c>.
    /// Адрес: фильтр по городу, если он известен, — сначала <c>city_fias_id</c> (выбранная
    /// подсказка), затем свободный <c>city</c> (город введён руками); без обоих запрос уходит
    /// без <c>locations</c>. Порядок важен: <c>city_fias_id</c> точнее строки и не даёт
    /// одноимённым городам разных регионов смешаться.
    /// </para>
    /// <para>
    /// <c>count</c> — уже зажатый обработчиком лимит. <c>null</c>-свойства сериализатор
    /// опускает, поэтому форма тела остаётся минимальной.
    /// </para>
    /// </remarks>
    private static string BuildPayload(AddressSuggestionRequest request)
    {
        var isCity = request.Bound == AddressSuggestionBound.City;

        var payload = new SuggestionsRequest
        {
            Query = request.Query,
            Count = request.Limit,
            FromBound = isCity ? CITY_BOUND_FILTER : null,
            ToBound = isCity ? CITY_BOUND_FILTER : null,
            Locations = isCity ? null : BuildLocations(request)
        };

        return JsonSerializer.Serialize(payload, JSON_OPTIONS);
    }

    /// <summary>
    /// Фильтр <c>locations</c> для адресных подсказок: <c>city_fias_id</c>, иначе <c>city</c>,
    /// иначе <c>null</c> (запрос без фильтра).
    /// </summary>
    private static LocationFilter[]? BuildLocations(AddressSuggestionRequest request)
    {
        var cityProviderId = request.CityProviderId?.Trim();
        if (!string.IsNullOrEmpty(cityProviderId))
        {
            return [new LocationFilter { CityFiasId = cityProviderId }];
        }

        var city = request.City?.Trim();
        if (!string.IsNullOrEmpty(city))
        {
            return [new LocationFilter { City = city }];
        }

        return null;
    }

    /// <summary>
    /// Переводит разобранный ответ DaData в провайдер-нейтральные подсказки.
    /// </summary>
    /// <remarks>
    /// Порядок сохраняется: провайдер уже отсортировал подсказки по релевантности.
    /// Отбрасываются только элементы, из которых нельзя собрать доменно валидный адрес:
    /// без <c>value</c>, без города или без страны.
    /// </remarks>
    private static List<AddressSuggestion> Map(SuggestionsResponse? response)
    {
        var items = response?.Suggestions;
        if (items is null || items.Length == 0)
        {
            return [];
        }

        var suggestions = new List<AddressSuggestion>(items.Length);

        foreach (var item in items)
        {
            var data = item.Data;
            var display = item.Value?.Trim();

            if (data is null || string.IsNullOrEmpty(display))
            {
                continue;
            }

            var city = data.City?.Trim();
            var country = data.Country?.Trim();

            // Город и страна обязательны в домене салона: подсказка уровня «регион»
            // форме ничего не даёт и только подставила бы неполный адрес.
            if (string.IsNullOrEmpty(city) || string.IsNullOrEmpty(country))
            {
                continue;
            }

            suggestions.Add(new AddressSuggestion(
                ProviderId: PickProviderId(data),
                Display: display,
                Address: BuildAddress(data, display),
                City: city,
                Region: PickRegion(data),
                Country: country,
                PostalCode: data.PostalCode,
                Latitude: ParseCoordinate(data.GeoLat),
                Longitude: ParseCoordinate(data.GeoLon)));
        }

        return suggestions;
    }

    /// <summary>
    /// Собирает адрес улицы без города: <c>street_with_type</c> + <c>house</c>.
    /// </summary>
    /// <remarks>
    /// Город в <c>Salon.Address</c> не попадает — он идёт отдельным полем. Если улицы нет
    /// (подсказка уровня города или региона), отдаётся <c>value</c> целиком: пустая строка
    /// адреса в форме хуже, чем полная подсказка.
    /// </remarks>
    private static string BuildAddress(SuggestionData data, string display)
    {
        var street = data.StreetWithType?.Trim();
        if (string.IsNullOrEmpty(street))
        {
            return display;
        }

        var house = data.House?.Trim();

        return string.IsNullOrEmpty(house) ? street : string.Concat(street, ", ", house);
    }

    /// <summary>
    /// Идентификатор подсказки у провайдера: <c>fias_id</c>, иначе <c>kladr_id</c>.
    /// </summary>
    /// <remarks>
    /// Клиент возвращает это значение в <c>cityProviderId</c>; формат для сервера
    /// непрозрачен, поэтому пустые строки неотличимы от отсутствия идентификатора.
    /// </remarks>
    private static string? PickProviderId(SuggestionData data)
    {
        var fiasId = data.FiasId?.Trim();
        if (!string.IsNullOrEmpty(fiasId))
        {
            return fiasId;
        }

        var kladrId = data.KladrId?.Trim();

        return string.IsNullOrEmpty(kladrId) ? null : kladrId;
    }

    /// <summary>
    /// Регион для показа: <c>region_with_type</c>, иначе <c>region</c>.
    /// </summary>
    private static string? PickRegion(SuggestionData data)
    {
        var regionWithType = data.RegionWithType?.Trim();
        if (!string.IsNullOrEmpty(regionWithType))
        {
            return regionWithType;
        }

        var region = data.Region?.Trim();

        return string.IsNullOrEmpty(region) ? null : region;
    }

    /// <summary>
    /// Разбирает координату: DaData отдаёт её строкой, поэтому точка — всегда разделитель,
    /// а культура процесса не участвует.
    /// </summary>
    private static double? ParseCoordinate(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Пишет предупреждение об отказе провайдера: только провайдер, причина, bound,
    /// длительность, длина запроса и код состояния.
    /// </summary>
    /// <remarks>
    /// Сам <c>query</c>, адрес, координаты, ключ и тело ответа в журнал не попадают:
    /// подсказка адреса — это персональные данные пользователя, а ключ — секрет.
    /// </remarks>
    private void LogFailure(
        string reason,
        int? status,
        AddressSuggestionRequest request,
        Stopwatch stopwatch)
        => _logger.LogWarning(
            "Address suggestions provider {Provider} {Reason} for bound {Bound} "
            + "in {Elapsed} ms (query length {QueryLength}, HTTP status {Status}).",
            PROVIDER_NAME,
            reason,
            request.Bound,
            stopwatch.ElapsedMilliseconds,
            request.Query.Length,
            status);

    /// <summary>
    /// Ошибка «подсказки недоступны»: единственный публичный код этого адаптера.
    /// </summary>
    /// <remarks>
    /// В сообщении нет ни адреса, ни ключа, ни деталей провайдера: обработчик заменит его
    /// на <c>Error.Conflict</c>, а текст показывается только человеку.
    /// </remarks>
    private static Result<IReadOnlyList<AddressSuggestion>, Failure> Unavailable(string message)
        => Result.Failure<IReadOnlyList<AddressSuggestion>, Failure>(
            new Failure(Error.Failure(ApiErrorCodes.AddressSuggestions.Unavailable, message)));

    /// <summary>
    /// Тело запроса подсказок; <c>null</c>-свойства не сериализуются.
    /// </summary>
    private sealed record SuggestionsRequest
    {
        /// <summary>
        /// Поисковая строка.
        /// </summary>
        [JsonPropertyName("query")]
        public string Query { get; init; } = string.Empty;

        /// <summary>
        /// Сколько подсказок вернуть.
        /// </summary>
        [JsonPropertyName("count")]
        public int Count { get; init; }

        /// <summary>
        /// Нижняя граница подсказки; только для городов.
        /// </summary>
        [JsonPropertyName("from_bound")]
        public BoundFilter? FromBound { get; init; }

        /// <summary>
        /// Верхняя граница подсказки; только для городов.
        /// </summary>
        [JsonPropertyName("to_bound")]
        public BoundFilter? ToBound { get; init; }

        /// <summary>
        /// Фильтр по городу; только для адресов и только когда город известен.
        /// </summary>
        [JsonPropertyName("locations")]
        public LocationFilter[]? Locations { get; init; }
    }

    /// <summary>
    /// Граница подсказки: <c>{"value":"city"}</c>.
    /// </summary>
    private sealed record BoundFilter
    {
        /// <summary>
        /// Значение границы, например <c>city</c>.
        /// </summary>
        [JsonPropertyName("value")]
        public string Value { get; init; } = string.Empty;
    }

    /// <summary>
    /// Фильтр по городу: <c>fias</c> точнее свободной строки.
    /// </summary>
    private sealed record LocationFilter
    {
        /// <summary>
        /// <c>fias_id</c> выбранного города.
        /// </summary>
        [JsonPropertyName("city_fias_id")]
        public string? CityFiasId { get; init; }

        /// <summary>
        /// Город, введённый руками.
        /// </summary>
        [JsonPropertyName("city")]
        public string? City { get; init; }
    }

    // DTO ниже создаёт System.Text.Json через рефлексию: в коде их никто не конструирует,
    // поэтому CA1812 («неинстанцируемый внутренний класс») здесь — ложное срабатывание.
#pragma warning disable CA1812

    /// <summary>
    /// Ответ провайдера; разбирается только то, что нужно подсказке.
    /// </summary>
    private sealed record SuggestionsResponse
    {
        /// <summary>
        /// Подсказки; отсутствие массива — пустой результат, а не ошибка.
        /// </summary>
        [JsonPropertyName("suggestions")]
        public SuggestionItem[]? Suggestions { get; init; }
    }

    /// <summary>
    /// Одна подсказка: показываемая строка и разобранные поля адреса.
    /// </summary>
    private sealed record SuggestionItem
    {
        /// <summary>
        /// Готовая строка подсказки — единственное, что видит пользователь.
        /// </summary>
        [JsonPropertyName("value")]
        public string? Value { get; init; }

        /// <summary>
        /// Разобранные поля адреса.
        /// </summary>
        [JsonPropertyName("data")]
        public SuggestionData? Data { get; init; }
    }

    /// <summary>
    /// Поля адреса из ответа DaData: только те, что нужны форме салона.
    /// </summary>
    private sealed record SuggestionData
    {
        /// <summary>
        /// Город.
        /// </summary>
        [JsonPropertyName("city")]
        public string? City { get; init; }

        /// <summary>
        /// Регион без типового сокращения.
        /// </summary>
        [JsonPropertyName("region")]
        public string? Region { get; init; }

        /// <summary>
        /// Регион с сокращением («Свердловская обл.»).
        /// </summary>
        [JsonPropertyName("region_with_type")]
        public string? RegionWithType { get; init; }

        /// <summary>
        /// Страна.
        /// </summary>
        [JsonPropertyName("country")]
        public string? Country { get; init; }

        /// <summary>
        /// Почтовый индекс.
        /// </summary>
        [JsonPropertyName("postal_code")]
        public string? PostalCode { get; init; }

        /// <summary>
        /// Широта строкой, как её отдаёт провайдер.
        /// </summary>
        [JsonPropertyName("geo_lat")]
        public string? GeoLat { get; init; }

        /// <summary>
        /// Долгота строкой, как её отдаёт провайдер.
        /// </summary>
        [JsonPropertyName("geo_lon")]
        public string? GeoLon { get; init; }

        /// <summary>
        /// Улица с типом («ул.»).
        /// </summary>
        [JsonPropertyName("street_with_type")]
        public string? StreetWithType { get; init; }

        /// <summary>
        /// Номер дома.
        /// </summary>
        [JsonPropertyName("house")]
        public string? House { get; init; }

        /// <summary>
        /// Идентификатор объекта в ФИАС.
        /// </summary>
        [JsonPropertyName("fias_id")]
        public string? FiasId { get; init; }

        /// <summary>
        /// Идентификатор объекта в КЛАДР — резервный, если <c>fias_id</c> пуст.
        /// </summary>
        [JsonPropertyName("kladr_id")]
        public string? KladrId { get; init; }
    }

#pragma warning restore CA1812
}
