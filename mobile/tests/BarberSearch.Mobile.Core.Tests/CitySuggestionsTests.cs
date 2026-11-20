using System.Net;
using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки подсказок города: пауза в наборе, минимальная длина, отмена устаревшего ответа,
/// пустой ответ и тихий отказ без ошибки на экране.
/// </summary>
public sealed class CitySuggestionsTests
{
    private static readonly CitySuggestionResponse Moscow = new(
        "fias-moscow",
        "г. Москва",
        "Москва",
        "Москва",
        "Россия",
        55.7558,
        37.6173);

    private static readonly CitySuggestionResponse MoscowRegion = new(
        "fias-moscow-region",
        "г. Московский",
        "Московский",
        "Москва",
        "Россия",
        55.5991,
        37.3549);

    [Fact]
    public async Task QueryAsync_LongEnough_LoadsSuggestionsAndOpensList()
    {
        var api = FakeApi.Returning([Moscow, MoscowRegion]);
        var suggestions = Create(api);

        await suggestions.QueryAsync("  Екат  ");

        Assert.Equal("Екат", Assert.Single(api.Queries));
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

        await suggestions.QueryAsync("Аша");

        Assert.Empty(suggestions.Items);
        Assert.True(suggestions.IsEmpty);
        Assert.True(suggestions.IsVisible);
    }

    [Fact]
    public async Task QueryAsync_OneCharacter_ClosesListWithoutRequest()
    {
        var api = FakeApi.Returning([Moscow]);
        var suggestions = Create(api);

        await suggestions.QueryAsync("Москва");

        Assert.True(suggestions.IsVisible);

        await suggestions.QueryAsync("М");

        Assert.Equal("Москва", Assert.Single(api.Queries));
        Assert.Empty(suggestions.Items);
        Assert.False(suggestions.IsVisible);
    }

    [Fact]
    public async Task QueryAsync_BurstOfKeystrokes_RequestsOnlyTheLastQuery()
    {
        var api = FakeApi.Returning([Moscow]);
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

        var first = suggestions.QueryAsync("Мо");
        var second = suggestions.QueryAsync("Моск");

        // Пауза первого запроса уже отменена вторым нажатием; отпускаем только последнюю.
        gates[1].SetResult();

        await second;
        await first;

        Assert.Equal("Моск", Assert.Single(api.Queries));
        Assert.Equal("Москва", Assert.Single(suggestions.Items).City);
    }

    [Fact]
    public async Task QueryAsync_StaleResponse_DoesNotReplaceTheNewerList()
    {
        var api = FakeApi.Held();
        var suggestions = Create(api);

        var first = suggestions.QueryAsync("Мо");
        var second = suggestions.QueryAsync("Моск");

        // Медленный ответ на «Мо» приходит уже после отмены: он не должен перезаписать «Моск».
        api.Complete("Мо", [MoscowRegion]);
        api.Complete("Моск", [Moscow]);

        await Task.WhenAll(first, second);

        Assert.Equal("Москва", Assert.Single(suggestions.Items).City);
    }

    [Fact]
    public async Task Select_ClosesListAndSkipsTheSameCity()
    {
        var api = FakeApi.Returning([Moscow]);
        var suggestions = Create(api);

        await suggestions.QueryAsync("Мо");
        suggestions.Select(Moscow);

        Assert.Empty(suggestions.Items);
        Assert.False(suggestions.IsVisible);

        // Экран подставил город в поле — это тот же выбор, а не новый ввод: запроса нет.
        await suggestions.QueryAsync("Москва");

        Assert.Equal("Мо", Assert.Single(api.Queries));
        Assert.False(suggestions.IsVisible);
    }

    [Fact]
    public async Task QueryAsync_AfterSelectionWasCleared_QueriesTheSameCityAgain()
    {
        var api = FakeApi.Returning([Moscow]);
        var suggestions = Create(api);

        await suggestions.QueryAsync("Мо");
        suggestions.Select(Moscow);

        // Поле очистили — выбор снят; набранный заново тот же город снова получает подсказки.
        await suggestions.QueryAsync(string.Empty);
        await suggestions.QueryAsync("Москва");

        Assert.Equal(2, api.Queries.Count);
        Assert.Equal("Мо", api.Queries[0]);
        Assert.Equal("Москва", api.Queries[1]);
    }

    [Fact]
    public async Task QueryAsync_ApiFailure_HidesListSilently()
    {
        var api = FakeApi.Throwing(new BarberSearchApiException(
            HttpStatusCode.Conflict,
            "Подсказки недоступны.",
            ["addressSuggestions.unavailable"],
            new Dictionary<string, string[]>()));
        var suggestions = Create(api);

        await suggestions.QueryAsync("Екат");

        // Исключение не выходит из метода: подсказки — улучшение ввода, а не его условие.
        Assert.False(suggestions.IsVisible);
        Assert.Empty(suggestions.Items);
    }

    [Fact]
    public async Task QueryAsync_NetworkFailure_HidesListSilently()
    {
        var api = FakeApi.Throwing(new HttpRequestException("Connection refused"));
        var suggestions = Create(api);

        await suggestions.QueryAsync("Екат");

        Assert.False(suggestions.IsVisible);
    }

    [Fact]
    public async Task QueryAsync_AfterFailure_NextQueryShowsListAgain()
    {
        var api = FakeApi.Throwing(new HttpRequestException("Connection refused"));
        var suggestions = Create(api);

        await suggestions.QueryAsync("Екат");

        api.Failure = null;
        api.Result = [Moscow];

        await suggestions.QueryAsync("Моск");

        Assert.True(suggestions.IsVisible);
        Assert.Equal("Москва", Assert.Single(suggestions.Items).City);
    }

    [Fact]
    public async Task Close_ClearsItemsAndHidesList()
    {
        var api = FakeApi.Returning([Moscow]);
        var suggestions = Create(api);

        await suggestions.QueryAsync("Мо");
        suggestions.Close();

        Assert.Empty(suggestions.Items);
        Assert.False(suggestions.IsOpen);
        Assert.False(suggestions.IsVisible);
    }

    /// <summary>
    /// Создаёт секцию без паузы: тест не ждёт 250 мс, а проверяет поведение, а не таймер.
    /// </summary>
    private static CitySuggestions Create(
        FakeApi api,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(api, delay ?? (static (_, _) => Task.CompletedTask));

    /// <summary>
    /// Подменённое API подсказок: отдаёт заданный ответ, помнит запросы и умеет держать ответ.
    /// </summary>
    private sealed class FakeApi : IBarberSearchApi
    {
        private readonly bool _held;

        private readonly Dictionary<string, TaskCompletionSource<IReadOnlyList<CitySuggestionResponse>>> _pending =
            new(StringComparer.Ordinal);

        private FakeApi(
            IReadOnlyList<CitySuggestionResponse>? result = null,
            Exception? failure = null,
            bool held = false)
        {
            Result = result ?? [];
            Failure = failure;
            _held = held;
        }

        public IReadOnlyList<CitySuggestionResponse> Result { get; set; }

        /// <summary>
        /// Ошибка следующего запроса; <c>null</c> — API снова отвечает.
        /// </summary>
        public Exception? Failure { get; set; }

        public List<string> Queries { get; } = [];

        public static FakeApi Returning(IReadOnlyList<CitySuggestionResponse> result) => new(result);

        public static FakeApi Throwing(Exception failure) => new(failure: failure);

        public static FakeApi Held() => new(held: true);

        public void Complete(string query, IReadOnlyList<CitySuggestionResponse> result) =>
            _pending[query].SetResult(result);

        public Task<IReadOnlyList<CitySuggestionResponse>> GetCitySuggestionsAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);

            if (Failure is not null)
            {
                return Task.FromException<IReadOnlyList<CitySuggestionResponse>>(Failure);
            }

            if (!_held)
            {
                return Task.FromResult(Result);
            }

            var gate = new TaskCompletionSource<IReadOnlyList<CitySuggestionResponse>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _pending[query] = gate;

            return gate.Task;
        }

        public Task<PagedResponse<BarberSearchItemResponse>> SearchBarbersAsync(
            BarberSearchFilters filters,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Подсказки не ищут мастеров.");

        public Task<IReadOnlyList<SpecializationResponse>> GetSpecializationsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Подсказки не читают справочники.");

        public Task<IReadOnlyList<HairServiceResponse>> GetHairServicesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Подсказки не читают справочники.");

        public Task<BarberProfileResponse> GetBarberProfileAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Подсказки не читают карточку.");

        public Task<BarberSlotsResponse> GetSlotsAsync(
            Guid barberProfileId,
            DateOnly slotDate,
            Guid? hairServiceId,
            Guid? salonId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Подсказки не читают свободное время.");

        public Task<PagedResponse<ReviewResponse>> GetReviewsAsync(
            Guid barberProfileId,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Подсказки не читают отзывы.");

        public Task<IReadOnlyList<BarberSearch.Contracts.Salons.GetSalonDirectory.SalonDirectoryItemResponse>>
            SearchSalonsAsync(
                string? city,
                string? search,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Подсказки не читают каталог салонов.");
    }
}
