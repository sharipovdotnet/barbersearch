using BarberSearch.Application.Addresses;
using BarberSearch.Application.Addresses.SuggestAddresses;
using BarberSearch.Application.Addresses.SuggestCities;
using BarberSearch.Contracts.Addresses.SuggestAddresses;
using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Подсказки города и адреса: нормализация запроса, разные пороги городов и адреса,
/// зажим лимита, маппинг в контракт и отказ без вызова порта.
/// </summary>
/// <remarks>
/// Проверяется только поведение обработчиков: <c>AddressSuggestionQueryValidator</c> —
/// <c>internal</c>, а <c>InternalsVisibleTo</c> в Application нет. Наблюдаемое поведение
/// валидатора — это ровно то, что доходит до порта и что видит клиент в <see cref="Failure"/>,
/// поэтому тесты не зависят от его внутреннего устройства.
/// </remarks>
public class AddressSuggestionTests
{
    private static readonly AddressSuggestionsOptions ConfiguredOptions = new()
    {
        Enabled = true,
        ApiKey = "test-key",
        BaseUrl = AddressSuggestionsOptions.DEFAULT_BASE_URL,
        TimeoutSeconds = AddressSuggestionsOptions.DEFAULT_TIMEOUT_SECONDS,
        MinQueryLength = AddressSuggestionsOptions.DEFAULT_MIN_QUERY_LENGTH,
        MinCityQueryLength = AddressSuggestionsOptions.DEFAULT_MIN_CITY_QUERY_LENGTH,
        MaxSuggestions = AddressSuggestionsOptions.DEFAULT_MAX_SUGGESTIONS,
        CacheTtlSeconds = AddressSuggestionsOptions.DEFAULT_CACHE_TTL_SECONDS
    };

    [Fact]
    public async Task Cities_TrimsAndCollapsesWhitespaceInQuery()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateCitiesHandler(provider);

        var result = await handler.Handle(
            new SuggestCitiesQuery("  Екат   бург  ", Limit: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(provider.Requests);
        Assert.Equal("Екат бург", request.Query);
        Assert.Equal(AddressSuggestionBound.City, request.Bound);
        Assert.Equal(AddressSuggestionsOptions.DEFAULT_MAX_SUGGESTIONS, request.Limit);
    }

    [Fact]
    public async Task Addresses_TrimsAndCollapsesWhitespaceInQueryAndCity()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateAddressesHandler(provider);

        var result = await handler.Handle(
            new SuggestAddressesQuery("  ул.   Лен  ", CityProviderId: null, City: "  Екат   бург ", Limit: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(provider.Requests);
        Assert.Equal("ул. Лен", request.Query);
        Assert.Equal("Екат бург", request.City);
    }

    [Fact]
    public async Task Addresses_WhitespaceOnlyCity_ReachesThePortAsNull()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateAddressesHandler(provider);

        var result = await handler.Handle(
            new SuggestAddressesQuery("ул. Ленина", CityProviderId: null, City: "   ", Limit: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(provider.Requests);
        Assert.Null(request.City);
    }

    [Fact]
    public async Task Cities_OneCharacterQuery_ReturnsTooShortWithoutCallingThePort()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateCitiesHandler(provider);

        var result = await handler.Handle(
            new SuggestCitiesQuery("Е", Limit: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertValidation(result.Error, ApiErrorCodes.AddressSuggestions.QueryTooShort, "query");
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task Cities_TwoCharacterQuery_CallsThePort()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateCitiesHandler(provider);

        var result = await handler.Handle(
            new SuggestCitiesQuery("Уф", Limit: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(provider.Requests);
    }

    [Fact]
    public async Task Addresses_TwoCharacterQuery_ReturnsTooShortWithoutCallingThePort()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateAddressesHandler(provider);

        var result = await handler.Handle(
            new SuggestAddressesQuery("ул", CityProviderId: null, City: null, Limit: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertValidation(result.Error, ApiErrorCodes.AddressSuggestions.QueryTooShort, "query");
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task Addresses_QueryLongerThanTheLimit_ReturnsTooLong()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateAddressesHandler(provider);

        var result = await handler.Handle(
            new SuggestAddressesQuery(
                new string('а', AddressSuggestionLimits.MAX_QUERY_LENGTH + 1),
                CityProviderId: null,
                City: null,
                Limit: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertValidation(result.Error, ApiErrorCodes.AddressSuggestions.QueryTooLong, "query");
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task Addresses_CityLongerThanTheLimit_ReturnsCityTooLong()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateAddressesHandler(provider);

        var result = await handler.Handle(
            new SuggestAddressesQuery(
                "ул. Ленина",
                CityProviderId: null,
                City: new string('г', AddressSuggestionLimits.MAX_CITY_LENGTH + 1),
                Limit: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertValidation(result.Error, ApiErrorCodes.AddressSuggestions.CityTooLong, "city");
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task Addresses_CityProviderIdLongerThanTheLimit_ReturnsInvalidCityProviderId()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateAddressesHandler(provider);

        var result = await handler.Handle(
            new SuggestAddressesQuery(
                "ул. Ленина",
                CityProviderId: new string('f', AddressSuggestionLimits.MAX_CITY_PROVIDER_ID_LENGTH + 1),
                City: null,
                Limit: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertValidation(
            result.Error,
            ApiErrorCodes.AddressSuggestions.CityProviderIdInvalid,
            "cityProviderId");
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task Addresses_CityProviderIdWithWhitespace_ReturnsInvalidCityProviderId()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateAddressesHandler(provider);

        var result = await handler.Handle(
            new SuggestAddressesQuery(
                "ул. Ленина",
                CityProviderId: "fias id",
                City: null,
                Limit: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertValidation(
            result.Error,
            ApiErrorCodes.AddressSuggestions.CityProviderIdInvalid,
            "cityProviderId");
        Assert.Empty(provider.Requests);
    }

    [Theory]
    [InlineData(null, 8)]
    [InlineData(0, 1)]
    [InlineData(999, 8)]
    [InlineData(5, 5)]
    public async Task Limit_IsClampedToTheConfiguredRange(int? limit, int expected)
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateAddressesHandler(provider);

        var result = await handler.Handle(
            new SuggestAddressesQuery("ул. Ленина", CityProviderId: null, City: null, Limit: limit),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(provider.Requests);
        Assert.Equal(expected, request.Limit);
    }

    [Fact]
    public async Task Cities_SendsTheCityBoundWithoutCityFilter()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateCitiesHandler(provider);

        var result = await handler.Handle(
            new SuggestCitiesQuery("Екат", Limit: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(provider.Requests);
        Assert.Equal(AddressSuggestionBound.City, request.Bound);
        Assert.Null(request.CityProviderId);
        Assert.Null(request.City);
    }

    [Fact]
    public async Task Addresses_SendsTheAddressBound()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateAddressesHandler(provider);

        var result = await handler.Handle(
            new SuggestAddressesQuery("ул. Ленина", CityProviderId: null, City: null, Limit: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(provider.Requests);
        Assert.Equal(AddressSuggestionBound.Address, request.Bound);
    }

    [Fact]
    public async Task Addresses_ForwardsCityProviderIdAndCity()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateAddressesHandler(provider);

        var result = await handler.Handle(
            new SuggestAddressesQuery(
                "ул. Ленина",
                CityProviderId: "fias",
                City: "Екатеринбург",
                Limit: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(provider.Requests);
        // Приоритет задаёт адаптер: обработчик передаёт оба поля как есть.
        Assert.Equal("fias", request.CityProviderId);
        Assert.Equal("Екатеринбург", request.City);
    }

    [Fact]
    public async Task Addresses_MapsTheProviderSuggestionsInOrder()
    {
        var first = new AddressSuggestion(
            "fias-1",
            "г. Екатеринбург, ул. Ленина, д. 1",
            "ул. Ленина, 1",
            "Екатеринбург",
            "Свердловская обл.",
            "Россия",
            "620000",
            56.8389,
            60.5975);
        var second = new AddressSuggestion(
            ProviderId: null,
            Display: "г. Екатеринбург, ул. Мира",
            Address: "ул. Мира",
            City: "Екатеринбург",
            Region: null,
            Country: "Россия",
            PostalCode: null,
            Latitude: null,
            Longitude: null);

        var provider = new FakeAddressSuggestionsProvider { Suggestions = [first, second] };
        var handler = CreateAddressesHandler(provider);

        var result = await handler.Handle(
            new SuggestAddressesQuery("ул.", CityProviderId: null, City: null, Limit: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);

        Assert.Equal(
            new AddressSuggestionResponse(
                "fias-1",
                "г. Екатеринбург, ул. Ленина, д. 1",
                "ул. Ленина, 1",
                "Екатеринбург",
                "Свердловская обл.",
                "Россия",
                "620000",
                56.8389,
                60.5975,
                TimeZoneId: null),
            result.Value[0]);
        Assert.Equal(
            new AddressSuggestionResponse(
                null,
                "г. Екатеринбург, ул. Мира",
                "ул. Мира",
                "Екатеринбург",
                null,
                "Россия",
                null,
                null,
                null,
                TimeZoneId: null),
            result.Value[1]);

        // Пояс зарезервирован в контракте и в этом срезе всегда null.
        Assert.All(result.Value, response => Assert.Null(response.TimeZoneId));
    }

    [Fact]
    public async Task Cities_MapsTheProviderSuggestionWithoutAddressFields()
    {
        var provider = new FakeAddressSuggestionsProvider
        {
            Suggestions =
            [
                new AddressSuggestion(
                    "2763c110-cb8b-416a-9dac-ad28a55b4402",
                    "г. Екатеринбург",
                    Address: null,
                    City: "Екатеринбург",
                    Region: "Свердловская обл.",
                    Country: "Россия",
                    PostalCode: null,
                    Latitude: 56.8389,
                    Longitude: 60.5975)
            ]
        };
        var handler = CreateCitiesHandler(provider);

        var result = await handler.Handle(
            new SuggestCitiesQuery("Екат", Limit: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new CitySuggestionResponse(
                "2763c110-cb8b-416a-9dac-ad28a55b4402",
                "г. Екатеринбург",
                "Екатеринбург",
                "Свердловская обл.",
                "Россия",
                56.8389,
                60.5975),
            Assert.Single(result.Value));

        // У города этих полей в контракте нет вовсе, а не просто null.
        Assert.Null(typeof(CitySuggestionResponse).GetProperty("Address"));
        Assert.Null(typeof(CitySuggestionResponse).GetProperty("PostalCode"));
        Assert.Null(typeof(CitySuggestionResponse).GetProperty("TimeZoneId"));
    }

    [Fact]
    public async Task Addresses_WhenTheProviderFails_ReturnsUnavailableConflict()
    {
        var provider = new FakeAddressSuggestionsProvider
        {
            Failure = new Failure(Error.Failure("provider.down", "Provider is down."))
        };
        var handler = CreateAddressesHandler(provider);

        var result = await handler.Handle(
            new SuggestAddressesQuery("ул. Ленина", CityProviderId: null, City: null, Limit: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal(ApiErrorCodes.AddressSuggestions.Unavailable, error.Code);
    }

    [Fact]
    public async Task Addresses_WhenTheProviderReturnsAnEmptyList_ReturnsEmptySuccess()
    {
        var provider = new FakeAddressSuggestionsProvider { Suggestions = [] };
        var handler = CreateAddressesHandler(provider);

        var result = await handler.Handle(
            new SuggestAddressesQuery("ул. Ленина", CityProviderId: null, City: null, Limit: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Cities_WhenTheProviderReturnsAnEmptyList_ReturnsEmptySuccess()
    {
        var provider = new FakeAddressSuggestionsProvider { Suggestions = [] };
        var handler = CreateCitiesHandler(provider);

        var result = await handler.Handle(
            new SuggestCitiesQuery("Екат", Limit: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Addresses_WhenOptionsAreNotConfigured_ReturnsConflictWithoutCallingThePort()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateAddressesHandler(provider, DisabledOptions());

        var result = await handler.Handle(
            new SuggestAddressesQuery("ул. Ленина", CityProviderId: null, City: null, Limit: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal(ApiErrorCodes.AddressSuggestions.Unavailable, error.Code);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task Cities_WhenOptionsAreNotConfigured_ReturnsConflictWithoutCallingThePort()
    {
        var provider = new FakeAddressSuggestionsProvider();
        var handler = CreateCitiesHandler(provider, DisabledOptions());

        var result = await handler.Handle(
            new SuggestCitiesQuery("Екат", Limit: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal(ApiErrorCodes.AddressSuggestions.Unavailable, error.Code);
        Assert.Empty(provider.Requests);
    }

    private static SuggestAddressesHandler CreateAddressesHandler(
        FakeAddressSuggestionsProvider provider,
        AddressSuggestionsOptions? options = null)
        => new(provider, options ?? ConfiguredOptions);

    private static SuggestCitiesHandler CreateCitiesHandler(
        FakeAddressSuggestionsProvider provider,
        AddressSuggestionsOptions? options = null)
        => new(provider, options ?? ConfiguredOptions);

    private static AddressSuggestionsOptions DisabledOptions()
        => new() { Enabled = false, ApiKey = null };

    private static void AssertValidation(Failure failure, string code, string invalidField)
    {
        var error = Assert.Single(failure.Errors);
        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal(code, error.Code);
        Assert.Equal(invalidField, error.InvalidField);
    }

    /// <summary>
    /// Фейковый порт подсказок: запоминает запросы и отдаёт заранее заданный исход.
    /// </summary>
    /// <remarks>
    /// Исход задаётся <see cref="Suggestions"/> и <see cref="Failure"/>: один и тот же фейк
    /// проверяет успех, пустой список и недоступность провайдера.
    /// </remarks>
    private sealed class FakeAddressSuggestionsProvider : IAddressSuggestionsProvider
    {
        public List<AddressSuggestionRequest> Requests { get; } = [];

        public IReadOnlyList<AddressSuggestion> Suggestions { get; init; } = [];

        public Failure? Failure { get; init; }

        public Task<Result<IReadOnlyList<AddressSuggestion>, Failure>> SuggestAsync(
            AddressSuggestionRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(Failure is null
                ? Result.Success<IReadOnlyList<AddressSuggestion>, Failure>(Suggestions)
                : Result.Failure<IReadOnlyList<AddressSuggestion>, Failure>(Failure));
        }
    }
}
