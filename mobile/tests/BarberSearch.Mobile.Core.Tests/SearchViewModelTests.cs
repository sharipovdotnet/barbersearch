using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана поиска: заполнение списка, замена выдачи и текст ошибки.
/// </summary>
public sealed class SearchViewModelTests
{
    [Fact]
    public async Task SearchAsync_FillsBarbersAndClearsError()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса"), Barber("Борис")));
        var viewModel = new SearchViewModel(api) { City = "Тюмень" };

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Barbers.Count);
        Assert.Equal("Алиса", viewModel.Barbers[0].DisplayName);
        Assert.False(viewModel.IsLoading);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.HasError);
        Assert.Equal("Тюмень", api.LastCity);
    }

    [Fact]
    public async Task SearchAsync_ReplacesPreviousResults()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        var viewModel = new SearchViewModel(api);

        await viewModel.SearchCommand.ExecuteAsync(null);
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Barbers);
    }

    [Fact]
    public async Task SearchAsync_ShowsApiErrorAndStopsLoading()
    {
        var api = FakeApi.Throwing(new BarberSearchApiException(
            HttpStatusCode.InternalServerError,
            "Сервер не смог выполнить поиск.",
            ["barberSearch.search.failed"],
            new Dictionary<string, string[]>()));
        var viewModel = new SearchViewModel(api);

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.Equal("Сервер не смог выполнить поиск.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
        Assert.Empty(viewModel.Barbers);
    }

    [Fact]
    public async Task SearchAsync_ShowsNetworkErrorText()
    {
        var api = FakeApi.Throwing(new HttpRequestException("Connection refused"));
        var viewModel = new SearchViewModel(api);

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsLoading);
    }

    private static PagedResponse<BarberSearchItemResponse> Page(params BarberSearchItemResponse[] items) =>
        new(items, NextCursor: null);

    private static BarberSearchItemResponse Barber(string displayName) => new(
        Guid.CreateVersion7(),
        displayName,
        Grade.SeniorBarber,
        7,
        [Language.Ru],
        4.5m,
        12,
        IsVerified: true,
        Guid.CreateVersion7(),
        "Барбершоп на Ленина",
        "Тюмень",
        PlaceKind.Salon,
        ["борода"],
        null);

    /// <summary>
    /// Подменённое API: возвращает заданную страницу или заданное исключение.
    /// </summary>
    private sealed class FakeApi : IBarberSearchApi
    {
        private readonly Func<Task<PagedResponse<BarberSearchItemResponse>>> _response;

        private FakeApi(Func<Task<PagedResponse<BarberSearchItemResponse>>> response) => _response = response;

        public string? LastCity { get; private set; }

        public static FakeApi Returning(PagedResponse<BarberSearchItemResponse> page) =>
            new(() => Task.FromResult(page));

        public static FakeApi Throwing(Exception exception) =>
            new(() => Task.FromException<PagedResponse<BarberSearchItemResponse>>(exception));

        public Task<PagedResponse<BarberSearchItemResponse>> SearchBarbersAsync(
            string? city,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default)
        {
            LastCity = city;

            return _response();
        }

        public Task<BarberProfileResponse> GetBarberProfileAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Поиск не читает карточку барбера.");

        public Task<BarberSlotsResponse> GetSlotsAsync(
            Guid barberProfileId,
            DateOnly slotDate,
            Guid? hairServiceId,
            Guid? salonId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Поиск не читает свободное время.");

        public Task<PagedResponse<BarberSearch.Contracts.Reviews.GetReviews.ReviewResponse>> GetReviewsAsync(
            Guid barberProfileId,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Поиск не читает отзывы.");
    }
}
