using System.Net;
using BarberSearch.Contracts.Addresses.SuggestAddresses;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Addresses;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки подсказок адреса: пауза в наборе, минимальная длина, отмена устаревшего ответа,
/// пустой ответ и тихий отказ без ошибки на экране.
/// </summary>
/// <remarks>
/// Пауза приходит делегатом, поэтому debounce проверяется без ожидания таймера. Подсказки —
/// улучшение ввода: отказ сервера или сети закрывает список, а не показывает ошибку.
/// </remarks>
public sealed class AddressSuggestionsTests
{
    private static readonly AddressSuggestionResponse Lenina = new(
        "fias-lenina",
        "г. Москва, ул. Ленина, 1",
        "ул. Ленина, 1",
        "Москва",
        "Москва",
        "Россия",
        "101000",
        55.7558,
        37.6173,
        null);

    private static readonly AddressSuggestionResponse LeninaTwo = new(
        "fias-lenina-2",
        "г. Москва, ул. Ленина, 2",
        "ул. Ленина, 2",
        "Москва",
        "Москва",
        "Россия",
        null,
        55.756,
        37.618,
        null);

    [Fact]
    public async Task QueryAsync_LongEnough_LoadsSuggestionsAndOpensList()
    {
        var api = FakeApi.Returning([Lenina, LeninaTwo]);
        var suggestions = Create(api);

        await suggestions.QueryAsync("  Ленина  ", " Москва ", "fias-msk");

        var call = Assert.Single(api.Calls);
        Assert.Equal("Ленина", call.Query);
        Assert.Equal(" Москва ", call.City);
        Assert.Equal("fias-msk", call.CityProviderId);
        Assert.Equal(2, suggestions.Items.Count);
        Assert.True(suggestions.IsOpen);
        Assert.True(suggestions.IsVisible);
        Assert.False(suggestions.IsEmpty);
    }

    [Fact]
    public async Task QueryAsync_EmptyAnswer_ShowsEmptyStateInsteadOfList()
    {
        var api = FakeApi.Returning([]);
        var suggestions = Create(api);

        await suggestions.QueryAsync("Ленина", "Москва", null);

        Assert.Empty(suggestions.Items);
        Assert.True(suggestions.IsEmpty);
        Assert.True(suggestions.IsVisible);
    }

    [Fact]
    public async Task QueryAsync_ShorterThanThree_ClosesListWithoutRequest()
    {
        var api = FakeApi.Returning([Lenina]);
        var suggestions = Create(api);

        await suggestions.QueryAsync("Ленина", "Москва", null);

        Assert.True(suggestions.IsVisible);

        await suggestions.QueryAsync("Ле", "Москва", null);

        Assert.Single(api.Calls);
        Assert.Empty(suggestions.Items);
        Assert.False(suggestions.IsVisible);
    }

    [Fact]
    public async Task QueryAsync_BurstOfKeystrokes_RequestsOnlyTheLastQuery()
    {
        var api = FakeApi.Returning([Lenina]);
        var gates = new List<TaskCompletionSource>();

        var suggestions = Create(
            api,
            (_, cancellationToken) =>
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                gates.Add(gate);
                cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken));

                return gate.Task;
            });

        var first = suggestions.QueryAsync("Лен", "Москва", null);
        var second = suggestions.QueryAsync("Ленин", "Москва", null);

        // Пауза первого запроса уже отменена вторым нажатием; отпускаем только последнюю.
        gates[1].SetResult();

        await second;
        await first;

        var call = Assert.Single(api.Calls);
        Assert.Equal("Ленин", call.Query);
        Assert.Equal("ул. Ленина, 1", Assert.Single(suggestions.Items).Address);
    }

    [Fact]
    public async Task QueryAsync_StaleResponse_DoesNotReplaceTheNewerList()
    {
        var api = FakeApi.Held();
        var suggestions = Create(api);

        var first = suggestions.QueryAsync("Лен", "Москва", null);
        var second = suggestions.QueryAsync("Ленин", "Москва", null);

        // Медленный ответ на «Лен» приходит уже после отмены: он не должен перезаписать «Ленин».
        api.Complete("Лен", [LeninaTwo]);
        api.Complete("Ленин", [Lenina]);

        await Task.WhenAll(first, second);

        Assert.Equal("ул. Ленина, 1", Assert.Single(suggestions.Items).Address);
    }

    [Fact]
    public async Task Select_ClosesListAndSuppressesRepeatQuery()
    {
        var api = FakeApi.Returning([Lenina]);
        var suggestions = Create(api);

        await suggestions.QueryAsync("Лен", "Москва", null);
        suggestions.Select(Lenina);

        Assert.Empty(suggestions.Items);
        Assert.False(suggestions.IsVisible);

        // Экран подставил улицу в поле — это тот же выбор, а не новый ввод: запроса нет.
        await suggestions.QueryAsync("  ул. Ленина, 1  ", "Москва", null);

        Assert.Single(api.Calls);
        Assert.False(suggestions.IsVisible);
    }

    [Fact]
    public async Task Select_WithoutAddress_SuppressesDisplayValue()
    {
        var api = FakeApi.Returning([Lenina]);
        var suggestions = Create(api);
        var withoutAddress = Lenina with { Address = null, Display = "Москва, Ленина, 1" };

        suggestions.Select(withoutAddress);
        await suggestions.QueryAsync("Москва, Ленина, 1", "Москва", null);

        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task QueryAsync_ApiFailure_HidesListSilently()
    {
        var api = FakeApi.Throwing(new BarberSearchApiException(
            HttpStatusCode.Unauthorized,
            "Authentication is required.",
            ["auth.unauthorized"],
            new Dictionary<string, string[]>()));
        var suggestions = Create(api);

        await suggestions.QueryAsync("Ленина", "Москва", null);

        // Исключение не выходит из метода: подсказки — улучшение ввода, а не его условие.
        Assert.False(suggestions.IsVisible);
        Assert.Empty(suggestions.Items);
    }

    [Fact]
    public async Task QueryAsync_NetworkFailure_HidesListSilently()
    {
        var api = FakeApi.Throwing(new HttpRequestException("Connection refused"));
        var suggestions = Create(api);

        await suggestions.QueryAsync("Ленина", "Москва", null);

        Assert.False(suggestions.IsVisible);
    }

    [Fact]
    public async Task Close_ClearsItemsAndHidesList()
    {
        var api = FakeApi.Returning([Lenina]);
        var suggestions = Create(api);

        await suggestions.QueryAsync("Ленина", "Москва", null);
        suggestions.Close();

        Assert.Empty(suggestions.Items);
        Assert.False(suggestions.IsOpen);
        Assert.False(suggestions.IsVisible);
    }

    /// <summary>
    /// Создаёт подсказки без паузы: тест не ждёт 250 мс, а проверяет поведение, а не таймер.
    /// </summary>
    private static AddressSuggestions Create(
        FakeApi api,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(api, delay ?? (static (_, _) => Task.CompletedTask));

    /// <summary>
    /// Подменённое API подсказок: отдаёт заданный ответ, помнит запросы и умеет держать ответ.
    /// </summary>
    private sealed class FakeApi : IAddressesApi
    {
        private readonly bool _held;

        private readonly Dictionary<string, TaskCompletionSource<IReadOnlyList<AddressSuggestionResponse>>> _pending =
            new(StringComparer.Ordinal);

        private FakeApi(
            IReadOnlyList<AddressSuggestionResponse>? result = null,
            Exception? failure = null,
            bool held = false)
        {
            Result = result ?? [];
            Failure = failure;
            _held = held;
        }

        public IReadOnlyList<AddressSuggestionResponse> Result { get; set; }

        public Exception? Failure { get; set; }

        public List<Call> Calls { get; } = [];

        public static FakeApi Returning(IReadOnlyList<AddressSuggestionResponse> result) => new(result);

        public static FakeApi Throwing(Exception failure) => new(failure: failure);

        public static FakeApi Held() => new(held: true);

        public void Complete(string query, IReadOnlyList<AddressSuggestionResponse> result) =>
            _pending[query].SetResult(result);

        public Task<IReadOnlyList<AddressSuggestionResponse>> GetAddressSuggestionsAsync(
            string query,
            string? city,
            string? cityProviderId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new Call(query, city, cityProviderId));

            if (Failure is not null)
            {
                return Task.FromException<IReadOnlyList<AddressSuggestionResponse>>(Failure);
            }

            if (!_held)
            {
                return Task.FromResult(Result);
            }

            var gate = new TaskCompletionSource<IReadOnlyList<AddressSuggestionResponse>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _pending[query] = gate;

            return gate.Task;
        }
    }

    /// <summary>
    /// Запрос подсказок, ушедший в подменённый клиент.
    /// </summary>
    /// <param name="Query">Очищенная строка.</param>
    /// <param name="City">Свободный текст города.</param>
    /// <param name="CityProviderId">Провайдерский токен города.</param>
    private sealed record Call(string Query, string? City, string? CityProviderId);
}
