using System.Net;
using System.Text.Json;
using BarberSearch.Application.Addresses;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// API подсказок города и адреса: город доступен анонимно, адрес — только после входа,
/// точные имена полей JSON, разные пороги запроса у городов и адреса, передача
/// <c>cityProviderId</c> и отказ без вызова провайдера.
/// </summary>
/// <remarks>
/// Внешний DaData не вызывается: <see cref="TestApp"/> подставляет фейковый
/// <see cref="IAddressSuggestionsProvider"/>, который запоминает дошедший запрос. Поэтому
/// проверяется и контракт HTTP, и то, что обработчик действительно вызывает порт.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class AddressSuggestionsTests
{
    private const string CitiesEndpoint = "api/addresses/cities";
    private const string SuggestEndpoint = "api/addresses/suggest";
    private const string ApiKey = "integration-test-key";

    private static readonly Dictionary<string, string?> EnabledConfiguration = new(StringComparer.Ordinal)
    {
        ["AddressSuggestions__Enabled"] = "true",
        ["AddressSuggestions__ApiKey"] = ApiKey
    };

    private static readonly Dictionary<string, string?> DisabledConfiguration = new(StringComparer.Ordinal)
    {
        ["AddressSuggestions__Enabled"] = "false",
        ["AddressSuggestions__ApiKey"] = string.Empty
    };

    private readonly PostgresFixture _database;

    public AddressSuggestionsTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Anonymous_gets_city_suggestions_but_not_address_suggestions()
    {
        var provider = new FakeAddressSuggestionsProvider();

        await using var app = await TestApp.StartAsync(
            _database,
            EnabledConfiguration,
            addressSuggestionsProvider: provider);

        using var client = app.CreateClient();

        // Город подсказывают фильтру публичной витрины и мобильному поиску: вход не нужен.
        using (var cities = await client.GetAsync($"{CitiesEndpoint}?query=Екат"))
        {
            Assert.Equal(HttpStatusCode.OK, cities.StatusCode);

            using var document = JsonDocument.Parse(await cities.Content.ReadAsStringAsync());
            var item = Assert.Single(document.RootElement.EnumerateArray());

            Assert.Equal("г. Екатеринбург", item.GetProperty("display").GetString());
            Assert.Equal("Екатеринбург", item.GetProperty("city").GetString());
        }

        // Адрес остаётся закрытым: форма салона требует входа, квота не тратится анонимно.
        using (var suggest = await client.GetAsync(
                   $"{SuggestEndpoint}?query={Uri.EscapeDataString("ул. Ленина")}"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, suggest.StatusCode);
        }

        // До порта дошёл только публичный запрос города.
        var request = Assert.Single(provider.Requests);
        Assert.Equal(AddressSuggestionBound.City, request.Bound);
    }

    [Fact(Timeout = 180_000)]
    public async Task Authenticated_barber_gets_camelCase_city_and_address_suggestions()
    {
        var provider = new FakeAddressSuggestionsProvider();

        await using var app = await TestApp.StartAsync(
            _database,
            EnabledConfiguration,
            addressSuggestionsProvider: provider);

        await AuthenticateBarberAsync(app);

        using (var cities = await app.Client.GetAsync($"{CitiesEndpoint}?query=Екат"))
        {
            Assert.Equal(HttpStatusCode.OK, cities.StatusCode);

            using var document = JsonDocument.Parse(await cities.Content.ReadAsStringAsync());
            var item = Assert.Single(document.RootElement.EnumerateArray());

            Assert.Equal("fias-city", item.GetProperty("providerId").GetString());
            Assert.Equal("г. Екатеринбург", item.GetProperty("display").GetString());
            Assert.Equal("Екатеринбург", item.GetProperty("city").GetString());
            Assert.Equal("Свердловская обл.", item.GetProperty("region").GetString());
            Assert.Equal("Россия", item.GetProperty("country").GetString());
            Assert.Equal(56.8389, item.GetProperty("latitude").GetDouble());
            Assert.Equal(60.5975, item.GetProperty("longitude").GetDouble());

            // У города нет ни адреса, ни индекса, ни часового пояса — полей нет в контракте.
            Assert.False(item.TryGetProperty("address", out _));
            Assert.False(item.TryGetProperty("postalCode", out _));
            Assert.False(item.TryGetProperty("timeZoneId", out _));
        }

        using (var addresses = await app.Client.GetAsync(
                   $"{SuggestEndpoint}?query={Uri.EscapeDataString("ул. Ленина")}&cityProviderId=fias-city"))
        {
            Assert.Equal(HttpStatusCode.OK, addresses.StatusCode);

            using var document = JsonDocument.Parse(await addresses.Content.ReadAsStringAsync());
            var item = Assert.Single(document.RootElement.EnumerateArray());

            Assert.Equal("fias-address", item.GetProperty("providerId").GetString());
            Assert.Equal(
                "г. Екатеринбург, ул. Ленина, д. 1",
                item.GetProperty("display").GetString());
            Assert.Equal("ул. Ленина, 1", item.GetProperty("address").GetString());
            Assert.Equal("Екатеринбург", item.GetProperty("city").GetString());
            Assert.Equal("Свердловская обл.", item.GetProperty("region").GetString());
            Assert.Equal("Россия", item.GetProperty("country").GetString());
            Assert.Equal("620000", item.GetProperty("postalCode").GetString());
            Assert.Equal(56.8389, item.GetProperty("latitude").GetDouble());
            Assert.Equal(60.5975, item.GetProperty("longitude").GetDouble());

            // Поле зарезервировано контрактом и в этом срезе всегда null.
            Assert.True(item.TryGetProperty("timeZoneId", out var timeZoneId));
            Assert.Equal(JsonValueKind.Null, timeZoneId.ValueKind);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Cities_accept_two_character_query_while_addresses_reject_it()
    {
        var provider = new FakeAddressSuggestionsProvider();

        await using var app = await TestApp.StartAsync(
            _database,
            EnabledConfiguration,
            addressSuggestionsProvider: provider);

        await AuthenticateBarberAsync(app);

        using (var cities = await app.Client.GetAsync($"{CitiesEndpoint}?query=Уф"))
        {
            Assert.Equal(HttpStatusCode.OK, cities.StatusCode);
        }

        using (var addresses = await app.Client.GetAsync($"{SuggestEndpoint}?query=ул"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, addresses.StatusCode);

            var body = await addresses.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            Assert.True(
                root.GetProperty("errors").TryGetProperty("query", out _),
                $"Validation failure must carry 'errors.query'; body: {body}");

            var errorCodes = root.GetProperty("errorCodes")
                .EnumerateArray()
                .Select(code => code.GetString())
                .ToArray();

            Assert.Contains(ApiErrorCodes.AddressSuggestions.QueryTooShort, errorCodes);
        }

        // До порта дошёл только успешный запрос городов.
        var request = Assert.Single(provider.Requests);
        Assert.Equal(AddressSuggestionBound.City, request.Bound);
    }

    [Fact(Timeout = 180_000)]
    public async Task City_provider_id_reaches_the_provider()
    {
        var provider = new FakeAddressSuggestionsProvider();

        await using var app = await TestApp.StartAsync(
            _database,
            EnabledConfiguration,
            addressSuggestionsProvider: provider);

        await AuthenticateBarberAsync(app);

        using var response = await app.Client.GetAsync(
            $"{SuggestEndpoint}?query={Uri.EscapeDataString("ул. Ленина")}"
            + "&cityProviderId=fias-city&city=" + Uri.EscapeDataString("Екатеринбург") + "&limit=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var request = Assert.Single(provider.Requests);
        Assert.Equal(AddressSuggestionBound.Address, request.Bound);
        Assert.Equal("fias-city", request.CityProviderId);
        Assert.Equal("Екатеринбург", request.City);
        Assert.Equal(5, request.Limit);
    }

    [Fact(Timeout = 180_000)]
    public async Task Provider_failure_is_reported_as_conflict()
    {
        var provider = new FakeAddressSuggestionsProvider
        {
            Failure = new Failure(Error.Failure("provider.down", "Provider is down."))
        };

        await using var app = await TestApp.StartAsync(
            _database,
            EnabledConfiguration,
            addressSuggestionsProvider: provider);

        await AuthenticateBarberAsync(app);

        using var response = await app.Client.GetAsync(
            $"{SuggestEndpoint}?query={Uri.EscapeDataString("ул. Ленина")}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var errorCodes = await ReadErrorCodesAsync(response);
        Assert.Contains(ApiErrorCodes.AddressSuggestions.Unavailable, errorCodes);
    }

    [Fact(Timeout = 180_000)]
    public async Task Disabled_feature_returns_conflict_without_calling_the_provider()
    {
        var provider = new FakeAddressSuggestionsProvider();

        await using var app = await TestApp.StartAsync(
            _database,
            DisabledConfiguration,
            addressSuggestionsProvider: provider);

        await AuthenticateBarberAsync(app);

        using (var cities = await app.Client.GetAsync($"{CitiesEndpoint}?query=Екат"))
        {
            Assert.Equal(HttpStatusCode.Conflict, cities.StatusCode);
            Assert.Contains(
                ApiErrorCodes.AddressSuggestions.Unavailable,
                await ReadErrorCodesAsync(cities));
        }

        using (var addresses = await app.Client.GetAsync(
                   $"{SuggestEndpoint}?query={Uri.EscapeDataString("ул. Ленина")}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, addresses.StatusCode);
            Assert.Contains(
                ApiErrorCodes.AddressSuggestions.Unavailable,
                await ReadErrorCodesAsync(addresses));
        }

        // Проверка «фича выключена» сидит в обработчике: порт не вызывается вообще.
        Assert.Empty(provider.Requests);
    }

    private static async Task AuthenticateBarberAsync(TestApp app)
    {
        var email = $"address-suggestions-barber-{Guid.NewGuid():N}@barbersearch.test";

        await app.CreateUserAsync(email, UserRole.Barber);
        await app.AuthenticateAsync(email);
    }

    private static async Task<IReadOnlyList<string>> ReadErrorCodesAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (!document.RootElement.TryGetProperty("errorCodes", out var codes) ||
            codes.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"Response {response.StatusCode} does not contain errorCodes.");
        }

        return codes.EnumerateArray()
            .Select(code => code.GetString() ?? string.Empty)
            .ToList();
    }

    /// <summary>
    /// Фейковый порт подсказок: запоминает запросы и отдаёт подсказку нужного bound.
    /// </summary>
    private sealed class FakeAddressSuggestionsProvider : IAddressSuggestionsProvider
    {
        private static readonly AddressSuggestion CityItem = new(
            "fias-city",
            "г. Екатеринбург",
            Address: null,
            City: "Екатеринбург",
            Region: "Свердловская обл.",
            Country: "Россия",
            PostalCode: null,
            Latitude: 56.8389,
            Longitude: 60.5975);

        private static readonly AddressSuggestion AddressItem = new(
            "fias-address",
            "г. Екатеринбург, ул. Ленина, д. 1",
            Address: "ул. Ленина, 1",
            City: "Екатеринбург",
            Region: "Свердловская обл.",
            Country: "Россия",
            PostalCode: "620000",
            Latitude: 56.8389,
            Longitude: 60.5975);

        public List<AddressSuggestionRequest> Requests { get; } = [];

        public Failure? Failure { get; init; }

        public Task<Result<IReadOnlyList<AddressSuggestion>, Failure>> SuggestAsync(
            AddressSuggestionRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            if (Failure is not null)
            {
                return Task.FromResult(
                    Result.Failure<IReadOnlyList<AddressSuggestion>, Failure>(Failure));
            }

            IReadOnlyList<AddressSuggestion> suggestions =
                request.Bound == AddressSuggestionBound.City ? [CityItem] : [AddressItem];

            return Task.FromResult(
                Result.Success<IReadOnlyList<AddressSuggestion>, Failure>(suggestions));
        }
    }
}
