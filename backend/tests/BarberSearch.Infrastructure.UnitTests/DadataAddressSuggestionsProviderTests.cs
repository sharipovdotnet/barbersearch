using System.Net;
using System.Text;
using System.Text.Json;
using BarberSearch.Application.Addresses;
using BarberSearch.Contracts.Common;
using BarberSearch.Infrastructure.Geocoding;
using BarberSearch.Infrastructure.UnitTests.Fakes;
using CSharpFunctionalExtensions;
using Shared;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет контракт адаптера DaData: метод, адрес, заголовок с ключом, тело запроса,
/// разбор подсказок и отказ без деталей провайдера и запроса.
/// </summary>
/// <remarks>
/// Настоящий DaData не вызывается: <see cref="IHttpClientFactory"/> подменён, поэтому тест
/// не зависит от сети и не тратит платную квоту. Тело ответа задаётся в тесте, а ключ —
/// фиктивный: проверяется только то, что он уходит заголовком и не попадает в текст ошибки.
/// </remarks>
public class DadataAddressSuggestionsProviderTests
{
    private const string EmptyBody = """{"suggestions":[]}""";

    [Fact]
    public async Task SuggestAsync_PostsToTheConfiguredBaseUrlWithTheTokenHeader()
    {
        const string baseUrl = "https://stub.example.test/suggestions/api/4_1/rs/suggest/address";
        var options = ConfiguredOptions(baseUrl);

        using var harness = new Harness(options);
        var result = await harness.Provider.SuggestAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(harness.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(baseUrl, request.Uri.ToString());
        Assert.Equal("Token secret-key", request.Authorization);
        Assert.Contains("application/json", request.Accept, StringComparison.Ordinal);

        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("ул. Лен", root.GetProperty("query").GetString());
        Assert.Equal(8, root.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task SuggestAsync_ForCityBound_SendsCityBoundsAndNoLocations()
    {
        using var harness = new Harness(ConfiguredOptions());
        var result = await harness.Provider.SuggestAsync(
            Request(bound: AddressSuggestionBound.City),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(harness.Handler.Requests);

        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("city", root.GetProperty("from_bound").GetProperty("value").GetString());
        Assert.Equal("city", root.GetProperty("to_bound").GetProperty("value").GetString());
        Assert.False(root.TryGetProperty("locations", out _));
    }

    [Fact]
    public async Task SuggestAsync_ForAddressWithCityProviderId_SendsFiasLocation()
    {
        using var harness = new Harness(ConfiguredOptions());
        var result = await harness.Provider.SuggestAsync(
            Request(cityProviderId: "fias-1", city: "Екатеринбург"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(harness.Handler.Requests);

        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.False(root.TryGetProperty("from_bound", out _));
        Assert.False(root.TryGetProperty("to_bound", out _));

        var locations = Assert.Single(root.GetProperty("locations").EnumerateArray());
        Assert.Equal("fias-1", locations.GetProperty("city_fias_id").GetString());
        // city_fias_id точнее свободной строки: при наличии идентификатора строка не уходит.
        Assert.False(locations.TryGetProperty("city", out _));
    }

    [Fact]
    public async Task SuggestAsync_ForAddressWithCityOnly_SendsCityLocation()
    {
        using var harness = new Harness(ConfiguredOptions());
        var result = await harness.Provider.SuggestAsync(
            Request(city: "Екатеринбург"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(harness.Handler.Requests);

        using var body = JsonDocument.Parse(request.Body);
        var locations = Assert.Single(
            body.RootElement.GetProperty("locations").EnumerateArray());
        Assert.Equal("Екатеринбург", locations.GetProperty("city").GetString());
        Assert.False(locations.TryGetProperty("city_fias_id", out _));
    }

    [Fact]
    public async Task SuggestAsync_ForAddressWithoutCity_SendsNoLocations()
    {
        using var harness = new Harness(ConfiguredOptions());
        var result = await harness.Provider.SuggestAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(harness.Handler.Requests);

        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.False(root.TryGetProperty("locations", out _));
        Assert.False(root.TryGetProperty("from_bound", out _));
        Assert.False(root.TryGetProperty("to_bound", out _));
    }

    [Fact]
    public async Task SuggestAsync_MapsTheProviderSuggestionToThePortModel()
    {
        using var harness = new Harness(
            ConfiguredOptions(),
            (_, _) => Task.FromResult(Json(HttpStatusCode.OK, FullSuggestionBody)));

        var result = await harness.Provider.SuggestAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var suggestion = Assert.Single(result.Value);
        Assert.Equal("fias-1", suggestion.ProviderId);
        Assert.Equal("г. Екатеринбург, ул. Ленина, д. 1", suggestion.Display);
        Assert.Equal("ул. Ленина, 1", suggestion.Address);
        Assert.Equal("Екатеринбург", suggestion.City);
        Assert.Equal("Свердловская обл.", suggestion.Region);
        Assert.Equal("Россия", suggestion.Country);
        Assert.Equal("620000", suggestion.PostalCode);
        // DaData отдаёт координаты строками: адаптер парсит их инвариантной культурой.
        Assert.Equal(56.8389, suggestion.Latitude);
        Assert.Equal(60.5975, suggestion.Longitude);
    }

    [Fact]
    public async Task SuggestAsync_DropsSuggestionsWithoutValueCityOrCountry()
    {
        const string body = """
            {
              "suggestions": [
                {"value": "регион", "data": {"city": null, "country": "Россия"}},
                {"value": "без страны", "data": {"city": "Екатеринбург", "country": null}},
                {"value": "  ", "data": {"city": "Екатеринбург", "country": "Россия"}},
                {"value": "без data", "data": null},
                {"value": "полная", "data": {"city": "Екатеринбург", "country": "Россия"}}
              ]
            }
            """;

        using var harness = new Harness(
            ConfiguredOptions(),
            (_, _) => Task.FromResult(Json(HttpStatusCode.OK, body)));

        var result = await harness.Provider.SuggestAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var suggestion = Assert.Single(result.Value);
        Assert.Equal("полная", suggestion.Display);
    }

    [Fact]
    public async Task SuggestAsync_FallsBackToKladrRegionAndDisplay()
    {
        const string body = """
            {
              "suggestions": [
                {
                  "value": "г. Екатеринбург",
                  "data": {
                    "city": "Екатеринбург",
                    "country": "Россия",
                    "region": "Свердловская область",
                    "fias_id": "   ",
                    "kladr_id": "kladr-only",
                    "geo_lat": "not-a-number",
                    "geo_lon": "60.5975"
                  }
                }
              ]
            }
            """;

        using var harness = new Harness(
            ConfiguredOptions(),
            (_, _) => Task.FromResult(Json(HttpStatusCode.OK, body)));

        var result = await harness.Provider.SuggestAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var suggestion = Assert.Single(result.Value);
        Assert.Equal("kladr-only", suggestion.ProviderId);
        // region_with_type нет — берётся region; улицы нет — адресом становится value.
        Assert.Equal("Свердловская область", suggestion.Region);
        Assert.Equal("г. Екатеринбург", suggestion.Address);
        Assert.Null(suggestion.Latitude);
        Assert.Equal(60.5975, suggestion.Longitude);
        Assert.Null(suggestion.PostalCode);
    }

    [Fact]
    public async Task SuggestAsync_WithEmptySuggestions_ReturnsSuccessAfterASingleCall()
    {
        using var harness = new Harness(ConfiguredOptions());

        var result = await harness.Provider.SuggestAsync(
            Request(cityProviderId: "fias-1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        // Повтора без locations быть не должно: пустой ответ с выбранным городом осмыслен.
        Assert.Single(harness.Handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SuggestAsync_WhenTheProviderRejectsTheCall_ReturnsUnavailableWithoutLeakingInput(
        HttpStatusCode status)
    {
        var options = ConfiguredOptions();

        using var harness = new Harness(
            options,
            (_, _) => Task.FromResult(Json(status, """{"error":"nope"}""")));

        var result = await harness.Provider.SuggestAsync(
            Request(query: "ул. Секретная 1"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ApiErrorCodes.AddressSuggestions.Unavailable, error.Code);
        Assert.DoesNotContain("Секретная", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(options.ApiKey!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuggestAsync_WithMalformedJson_ReturnsUnavailable()
    {
        using var harness = new Harness(
            ConfiguredOptions(),
            (_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{ not json")));

        var result = await harness.Provider.SuggestAsync(Request(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.AddressSuggestions.Unavailable,
            Assert.Single(result.Error.Errors).Code);
    }

    [Fact]
    public async Task SuggestAsync_WhenTheProviderTimesOut_ReturnsUnavailable()
    {
        using var harness = new Harness(
            ConfiguredOptions(),
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

                return Json(HttpStatusCode.OK, EmptyBody);
            },
            timeout: TimeSpan.FromSeconds(1));

        var result = await harness.Provider.SuggestAsync(Request(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.AddressSuggestions.Unavailable,
            Assert.Single(result.Error.Errors).Code);
    }

    [Fact]
    public async Task SuggestAsync_WithoutConfiguration_ReturnsFailureWithoutCallingHttp()
    {
        var options = new AddressSuggestionsOptions { Enabled = false };
        Assert.False(options.IsConfigured);

        using var harness = new Harness(options);

        var result = await harness.Provider.SuggestAsync(Request(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.AddressSuggestions.Unavailable,
            Assert.Single(result.Error.Errors).Code);
        Assert.Empty(harness.Handler.Requests);
    }

    private static AddressSuggestionsOptions ConfiguredOptions(
        string baseUrl = AddressSuggestionsOptions.DEFAULT_BASE_URL)
        => new()
        {
            Enabled = true,
            BaseUrl = baseUrl,
            ApiKey = "secret-key",
            TimeoutSeconds = AddressSuggestionsOptions.DEFAULT_TIMEOUT_SECONDS,
            MinQueryLength = AddressSuggestionsOptions.DEFAULT_MIN_QUERY_LENGTH,
            MinCityQueryLength = AddressSuggestionsOptions.DEFAULT_MIN_CITY_QUERY_LENGTH,
            MaxSuggestions = AddressSuggestionsOptions.DEFAULT_MAX_SUGGESTIONS,
            CacheTtlSeconds = AddressSuggestionsOptions.DEFAULT_CACHE_TTL_SECONDS
        };

    private static AddressSuggestionRequest Request(
        string query = "ул. Лен",
        AddressSuggestionBound bound = AddressSuggestionBound.Address,
        string? cityProviderId = null,
        string? city = null,
        int limit = AddressSuggestionsOptions.DEFAULT_MAX_SUGGESTIONS)
        => new(query, bound, cityProviderId, city, limit);

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private const string FullSuggestionBody = """
        {
          "suggestions": [
            {
              "value": "г. Екатеринбург, ул. Ленина, д. 1",
              "data": {
                "city": "Екатеринбург",
                "region": "Свердловская область",
                "region_with_type": "Свердловская обл.",
                "country": "Россия",
                "postal_code": "620000",
                "geo_lat": "56.8389",
                "geo_lon": "60.5975",
                "street_with_type": "ул. Ленина",
                "house": "1",
                "fias_id": "fias-1",
                "kladr_id": "kladr-1"
              }
            }
          ]
        }
        """;

    private sealed class Harness : IDisposable
    {
        private readonly HttpClient _client;

        public Harness(
            AddressSuggestionsOptions options,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null,
            TimeSpan? timeout = null)
        {
            Handler = new RecordingHandler(
                respond ?? ((_, _) => Task.FromResult(Json(HttpStatusCode.OK, EmptyBody))));
            _client = new HttpClient(Handler)
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(options.TimeoutSeconds)
            };
            Provider = new DadataAddressSuggestionsProvider(
                new StubHttpClientFactory(_client),
                options,
                new RecordingLogger<DadataAddressSuggestionsProvider>());
        }

        public RecordingHandler Handler { get; }

        public DadataAddressSuggestionsProvider Provider { get; }

        public void Dispose() => _client.Dispose();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
            => _respond = respond;

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.TryGetValues("Authorization", out var authorization)
                    ? string.Join(", ", authorization)
                    : null,
                request.Headers.Accept.ToString(),
                body));

            return await _respond(request, cancellationToken);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Authorization,
        string Accept,
        string Body);
}

/// <summary>
/// Проверяет кэширующий декоратор порта: попадание по нормализованному ключу, TTL,
/// отсутствие кэширования отказа и жёсткий предел размера.
/// </summary>
/// <remarks>
/// Время — <see cref="FakeTimeProvider"/>, поэтому TTL двигается без ожидания. Внутренний
/// провайдер — счётчик вызовов: «дошло ли до провайдера» проверяется по нему, а не по
/// HTTP-трафику.
/// </remarks>
public class CachedAddressSuggestionsProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SuggestAsync_SecondIdenticalRequest_DoesNotReachTheInnerProvider()
    {
        var inner = new CountingProvider();
        var cache = CreateCache(inner, new FakeTimeProvider(Now));

        await cache.SuggestAsync(Request(), CancellationToken.None);
        await cache.SuggestAsync(Request(), CancellationToken.None);

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task SuggestAsync_DifferentBoundCityProviderIdOrLimit_UsesAnotherKey()
    {
        var inner = new CountingProvider();
        var cache = CreateCache(inner, new FakeTimeProvider(Now));

        await cache.SuggestAsync(Request(), CancellationToken.None);
        await cache.SuggestAsync(Request(bound: AddressSuggestionBound.City), CancellationToken.None);
        await cache.SuggestAsync(Request(cityProviderId: "fias-1"), CancellationToken.None);
        await cache.SuggestAsync(Request(limit: 5), CancellationToken.None);

        Assert.Equal(4, inner.Calls);
    }

    [Fact]
    public async Task SuggestAsync_AfterTheTtl_ReachesTheInnerProviderAgain()
    {
        var time = new FakeTimeProvider(Now);
        var inner = new CountingProvider();
        var cache = CreateCache(inner, time);

        await cache.SuggestAsync(Request(), CancellationToken.None);

        time.UtcNow = Now.AddSeconds(
            AddressSuggestionsOptions.DEFAULT_CACHE_TTL_SECONDS - 1);
        await cache.SuggestAsync(Request(), CancellationToken.None);
        Assert.Equal(1, inner.Calls);

        time.UtcNow = Now.AddSeconds(
            AddressSuggestionsOptions.DEFAULT_CACHE_TTL_SECONDS + 1);
        await cache.SuggestAsync(Request(), CancellationToken.None);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task SuggestAsync_WhenTheInnerProviderFails_DoesNotCacheTheFailure()
    {
        var inner = new CountingProvider
        {
            Failure = new Failure(Error.Failure("provider.down", "Provider is down."))
        };
        var cache = CreateCache(inner, new FakeTimeProvider(Now));

        var first = await cache.SuggestAsync(Request(), CancellationToken.None);
        var second = await cache.SuggestAsync(Request(), CancellationToken.None);

        Assert.True(first.IsFailure);
        Assert.True(second.IsFailure);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task SuggestAsync_WithZeroTtl_ForwardsEveryRequest()
    {
        var inner = new CountingProvider();
        var cache = CreateCache(inner, new FakeTimeProvider(Now), cacheTtlSeconds: 0);

        await cache.SuggestAsync(Request(), CancellationToken.None);
        await cache.SuggestAsync(Request(), CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task SuggestAsync_WithMoreThanTheLimitOfKeys_EvictsTheOldestEntry()
    {
        var time = new FakeTimeProvider(Now);
        var inner = new CountingProvider();
        var cache = CreateCache(inner, time, cacheTtlSeconds: 86400);

        // Больше предела ровно на одну запись. Метка времени растёт, поэтому самая старая
        // запись определена однозначно, а TTL (сутки) за время вставки не истекает.
        for (var index = 0; index <= CachedAddressSuggestionsProvider.MAX_CACHE_ENTRIES; index++)
        {
            time.UtcNow = Now.AddMilliseconds(index);
            await cache.SuggestAsync(Request(query: $"street {index}"), CancellationToken.None);
        }

        Assert.Equal(CachedAddressSuggestionsProvider.MAX_CACHE_ENTRIES + 1, inner.Calls);

        // Первая запись вытеснена: повторный запрос снова идёт к провайдеру.
        await cache.SuggestAsync(Request(query: "street 0"), CancellationToken.None);
        Assert.Equal(CachedAddressSuggestionsProvider.MAX_CACHE_ENTRIES + 2, inner.Calls);

        // Свежая запись осталась в кэше: провайдер не вызывается.
        await cache.SuggestAsync(
            Request(query: $"street {CachedAddressSuggestionsProvider.MAX_CACHE_ENTRIES}"),
            CancellationToken.None);
        Assert.Equal(CachedAddressSuggestionsProvider.MAX_CACHE_ENTRIES + 2, inner.Calls);
    }

    private static CachedAddressSuggestionsProvider CreateCache(
        CountingProvider inner,
        FakeTimeProvider time,
        int cacheTtlSeconds = AddressSuggestionsOptions.DEFAULT_CACHE_TTL_SECONDS)
        => new(
            inner,
            new AddressSuggestionsOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                BaseUrl = AddressSuggestionsOptions.DEFAULT_BASE_URL,
                CacheTtlSeconds = cacheTtlSeconds
            },
            time);

    private static AddressSuggestionRequest Request(
        string query = "ул. Лен",
        AddressSuggestionBound bound = AddressSuggestionBound.Address,
        string? cityProviderId = null,
        string? city = null,
        int limit = AddressSuggestionsOptions.DEFAULT_MAX_SUGGESTIONS)
        => new(query, bound, cityProviderId, city, limit);

    /// <summary>
    /// Внутренний провайдер-счётчик: запоминает вызовы и отдаёт успех или заданный отказ.
    /// </summary>
    private sealed class CountingProvider : IAddressSuggestionsProvider
    {
        public int Calls { get; private set; }

        public Failure? Failure { get; init; }

        public Task<Result<IReadOnlyList<AddressSuggestion>, Failure>> SuggestAsync(
            AddressSuggestionRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(Failure is null
                ? Result.Success<IReadOnlyList<AddressSuggestion>, Failure>(
                    Array.Empty<AddressSuggestion>())
                : Result.Failure<IReadOnlyList<AddressSuggestion>, Failure>(Failure));
        }
    }
}
