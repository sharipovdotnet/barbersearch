using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана поиска: фильтры, справочники, выдача и подсказки пустого результата.
/// </summary>
public sealed class SearchViewModelTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly Guid SpecializationId = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid HairServiceId = Guid.Parse("a1000000-0000-4000-8000-000000000002");

    [Fact]
    public async Task SearchAsync_FillsBarbersAndClearsError()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса"), Barber("Борис")));
        var viewModel = Create(api);

        viewModel.City = "Тюмень";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Barbers.Count);
        Assert.Equal("Алиса", viewModel.Barbers[0].DisplayName);
        Assert.False(viewModel.IsLoading);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.HasError);
        Assert.Equal("Показаны 2 барбера", viewModel.ResultsCountText);
        Assert.False(viewModel.ShowEmptyState);
        Assert.Equal("Тюмень", Assert.Single(api.SearchCalls).City);
    }

    [Fact]
    public async Task SearchAsync_ReplacesPreviousResults()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        var viewModel = Create(api);

        await viewModel.SearchCommand.ExecuteAsync(null);
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Barbers);
        Assert.Equal(2, api.SearchCalls.Count);
    }

    [Fact]
    public async Task SearchAsync_ShowsApiErrorAndStopsLoading()
    {
        var api = FakeApi.Throwing(new BarberSearchApiException(
            HttpStatusCode.InternalServerError,
            "Сервер не смог выполнить поиск.",
            ["barberSearch.search.failed"],
            new Dictionary<string, string[]>()));
        var viewModel = Create(api);

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.Equal("Сервер не смог выполнить поиск.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
        Assert.Empty(viewModel.Barbers);
        Assert.False(viewModel.ShowEmptyState);
    }

    [Fact]
    public async Task SearchAsync_ShowsNetworkErrorText()
    {
        var api = FakeApi.Throwing(new HttpRequestException("Connection refused"));
        var viewModel = Create(api);

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.Equal("Нет связи с сервером. Проверьте подключение и повторите попытку.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task SearchAsync_SendsEveryFilterInOneRequest()
    {
        var api = FakeApi.Returning(Page());
        api.Specializations = [new SpecializationResponse(SpecializationId, "Классика", null)];
        api.HairServices = [new HairServiceResponse(HairServiceId, "Стрижка", ServiceCategory.Haircut, 40, null)];

        var viewModel = Create(api);

        await viewModel.InitializeAsync();

        viewModel.City = "  Тюмень  ";
        viewModel.AdditionalService = "  борода  ";
        viewModel.SelectedDate = viewModel.Dates[3];
        viewModel.SelectedLanguage = viewModel.Languages[1];
        viewModel.SelectedSpecialization = viewModel.Specializations[1];
        viewModel.SelectedHairService = viewModel.HairServices[1];

        await viewModel.SearchCommand.ExecuteAsync(null);

        var filters = Assert.Single(api.SearchCalls);

        Assert.Equal("Тюмень", filters.City);
        Assert.Equal(Today.AddDays(2), filters.Date);
        Assert.Equal(Language.Ru, filters.Language);
        Assert.Equal(SpecializationId, filters.SpecializationId);
        Assert.Equal(HairServiceId, filters.HairServiceId);
        Assert.Equal("борода", filters.AdditionalService);
    }

    [Fact]
    public async Task SearchAsync_AnySelectionsSendNoFilters()
    {
        var api = FakeApi.Returning(Page());
        var viewModel = Create(api);

        await viewModel.SearchCommand.ExecuteAsync(null);

        var filters = Assert.Single(api.SearchCalls);

        Assert.Equal(string.Empty, filters.City);
        Assert.Null(filters.Date);
        Assert.Null(filters.Language);
        Assert.Null(filters.SpecializationId);
        Assert.Null(filters.HairServiceId);
        Assert.Equal(string.Empty, filters.AdditionalService);
    }

    [Fact]
    public async Task InitializeAsync_LoadsCatalogsWithAnyOptionFirst()
    {
        var api = FakeApi.Returning(Page());
        api.Specializations = [new SpecializationResponse(SpecializationId, "Классика", null)];
        api.HairServices = [new HairServiceResponse(HairServiceId, "Стрижка", ServiceCategory.Haircut, 40, null)];

        var viewModel = Create(api);

        await viewModel.InitializeAsync();

        Assert.Equal("Любая специализация", viewModel.Specializations[0].DisplayText);
        Assert.Null(viewModel.Specializations[0].Id);
        Assert.Equal("Классика", viewModel.Specializations[1].DisplayText);
        Assert.Equal(SpecializationId, viewModel.Specializations[1].Id);
        Assert.Same(viewModel.Specializations[0], viewModel.SelectedSpecialization);

        Assert.Equal("Любая услуга", viewModel.HairServices[0].DisplayText);
        Assert.Equal("Стрижка", viewModel.HairServices[1].DisplayText);
        Assert.Same(viewModel.HairServices[0], viewModel.SelectedHairService);

        Assert.False(viewModel.IsCatalogsLoading);
        Assert.True(viewModel.IsCatalogsEnabled);
        Assert.True(viewModel.ShowSpecializationPicker);
        Assert.True(viewModel.ShowHairServicePicker);
        Assert.False(viewModel.HasCatalogsError);
    }

    [Fact]
    public async Task InitializeAsync_LoadsCatalogsOnlyOnce()
    {
        var api = FakeApi.Returning(Page());
        var viewModel = Create(api);

        await viewModel.InitializeAsync();
        await viewModel.InitializeAsync();

        Assert.Equal(1, api.SpecializationCalls);
    }

    [Fact]
    public async Task InitializeAsync_EmptyCatalog_HidesPicker()
    {
        var api = FakeApi.Returning(Page());
        var viewModel = Create(api);

        await viewModel.InitializeAsync();

        Assert.Single(viewModel.Specializations);
        Assert.Single(viewModel.HairServices);
        Assert.False(viewModel.ShowSpecializationPicker);
        Assert.False(viewModel.ShowHairServicePicker);
    }

    [Fact]
    public async Task InitializeAsync_CatalogFailure_ShowsWebTextAndKeepsSearchWorking()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        api.CatalogFailure = new HttpRequestException("Connection refused");

        var viewModel = Create(api);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasCatalogsError);
        Assert.Equal("Нет связи с сервером. Проверьте подключение и повторите попытку.", viewModel.CatalogsErrorText);
        Assert.False(viewModel.IsCatalogsEnabled);
        Assert.True(viewModel.ShowSpecializationPicker);
        Assert.Equal("Любая специализация", viewModel.Specializations[0].DisplayText);

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Barbers);
    }

    [Fact]
    public async Task RetryCatalogsAsync_AfterFailure_LoadsCatalogsAndClearsError()
    {
        var api = FakeApi.Returning(Page());
        api.CatalogFailure = new BarberSearchApiException(
            HttpStatusCode.InternalServerError,
            "Catalog is down.",
            ["hairServices.get.failed"],
            new Dictionary<string, string[]>());

        var viewModel = Create(api);

        await viewModel.InitializeAsync();

        Assert.Equal("Не удалось загрузить фильтры.", viewModel.CatalogsErrorText);

        api.CatalogFailure = null;
        api.Specializations = [new SpecializationResponse(SpecializationId, "Классика", null)];

        await viewModel.RetryCatalogsCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasCatalogsError);
        Assert.True(viewModel.IsCatalogsEnabled);
        Assert.Equal("Классика", viewModel.Specializations[1].DisplayText);
        Assert.Equal(2, api.SpecializationCalls);
    }

    [Fact]
    public void Constructor_BuildsDatesFromTodayWithAnyFirst()
    {
        var viewModel = Create(FakeApi.Returning(Page()));

        Assert.Equal(32, viewModel.Dates.Count);
        Assert.Equal("Любая дата", viewModel.Dates[0].DisplayText);
        Assert.Null(viewModel.Dates[0].Date);
        Assert.Equal("Сегодня, 15 ноября", viewModel.Dates[1].DisplayText);
        Assert.Equal(Today, viewModel.Dates[1].Date);
        Assert.Equal("Завтра, 16 ноября", viewModel.Dates[2].DisplayText);
        Assert.Equal("вт, 17 ноября", viewModel.Dates[3].DisplayText);
        Assert.Same(viewModel.Dates[0], viewModel.SelectedDate);
    }

    [Fact]
    public void Constructor_BuildsLanguageOptionsFromApiEnum()
    {
        var viewModel = Create(FakeApi.Returning(Page()));

        Assert.Equal(7, viewModel.Languages.Count);
        Assert.Equal("Любой язык", viewModel.Languages[0].DisplayText);
        Assert.Null(viewModel.Languages[0].Value);
        Assert.Equal("Русский", viewModel.Languages[1].DisplayText);
        Assert.Equal(Language.Ru, viewModel.Languages[1].Value);
        Assert.Same(viewModel.Languages[0], viewModel.SelectedLanguage);
    }

    [Fact]
    public void BeforeFirstSearch_ShowsEntryHintWithoutTitle()
    {
        var viewModel = Create(FakeApi.Returning(Page()));

        Assert.True(viewModel.ShowEmptyState);
        Assert.False(viewModel.HasEmptyTitle);
        Assert.Equal(string.Empty, viewModel.EmptyTitle);
        Assert.Equal("Введите город и нажмите «Найти»", viewModel.EmptyDescription);
    }

    [Fact]
    public async Task SearchAsync_EmptyResultWithCity_ShowsCityHint()
    {
        var viewModel = Create(FakeApi.Returning(Page()));

        viewModel.City = "Тюмень";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(viewModel.ShowEmptyState);
        Assert.Equal("В этом городе никого не нашли", viewModel.EmptyTitle);
        Assert.Equal(
            "Проверьте написание — город учитывается точно, например «Москва».",
            viewModel.EmptyDescription);
    }

    [Fact]
    public async Task SearchAsync_EmptyResultWithDate_ShowsDateHint()
    {
        var viewModel = Create(FakeApi.Returning(Page()));

        viewModel.SelectedDate = viewModel.Dates[2];

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal("На выбранную дату никого не нашли", viewModel.EmptyTitle);
        Assert.Equal(
            "Фильтр оставляет барберов с рабочим днём в этот день недели. Попробуйте другую дату.",
            viewModel.EmptyDescription);
    }

    [Fact]
    public async Task SearchAsync_EmptyResultWithChoiceFilter_ShowsFiltersHint()
    {
        var viewModel = Create(FakeApi.Returning(Page()));

        viewModel.SelectedLanguage = viewModel.Languages[1];

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal("С такими фильтрами никого не нашли", viewModel.EmptyTitle);
        Assert.Equal(
            "Попробуйте другой язык, специализацию, услугу или дополнительную услугу.",
            viewModel.EmptyDescription);
    }

    [Fact]
    public async Task SearchAsync_EmptyResultWithoutFilters_ShowsNoBarbersHint()
    {
        var viewModel = Create(FakeApi.Returning(Page()));

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Барберов пока нет", viewModel.EmptyTitle);
        Assert.Equal("Пока никто не подтверждён. Загляните позже.", viewModel.EmptyDescription);
    }

    [Fact]
    public async Task SearchAsync_Completed_RaisesSearchCompleted()
    {
        var api = FakeApi.Returning(Page());
        var viewModel = Create(api);
        var completed = 0;

        viewModel.SearchCompleted += (_, _) => completed++;

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(1, completed);
    }

    /// <summary>
    /// Создаёт ViewModel с фиксированным «сегодня»: список дат не зависит от машины теста.
    /// </summary>
    private static SearchViewModel Create(FakeApi api) => new(api, new FixedTimeProvider(Today));

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
    /// «Сегодня» для расчёта списка дат: полдень 15 ноября 2026 года.
    /// </summary>
    /// <remarks>
    /// Пояс подменён на UTC, чтобы <see cref="TimeProvider.GetLocalNow"/> не зависел от
    /// часового пояса машины, на которой идут тесты.
    /// </remarks>
    private sealed class FixedTimeProvider(DateOnly today) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() =>
            new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }

    /// <summary>
    /// Подменённое API: отдаёт заданную страницу, справочники и запоминает параметры.
    /// </summary>
    private sealed class FakeApi : IBarberSearchApi
    {
        private readonly Func<Task<PagedResponse<BarberSearchItemResponse>>> _response;

        private FakeApi(Func<Task<PagedResponse<BarberSearchItemResponse>>> response) => _response = response;

        public IReadOnlyList<SpecializationResponse> Specializations { get; set; } = [];

        public IReadOnlyList<HairServiceResponse> HairServices { get; set; } = [];

        public Exception? CatalogFailure { get; set; }

        public List<BarberSearchFilters> SearchCalls { get; } = [];

        public int SpecializationCalls { get; private set; }

        public static FakeApi Returning(PagedResponse<BarberSearchItemResponse> page) =>
            new(() => Task.FromResult(page));

        public static FakeApi Throwing(Exception exception) =>
            new(() => Task.FromException<PagedResponse<BarberSearchItemResponse>>(exception));

        public Task<PagedResponse<BarberSearchItemResponse>> SearchBarbersAsync(
            BarberSearchFilters filters,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default)
        {
            SearchCalls.Add(filters);

            return _response();
        }

        public Task<IReadOnlyList<SpecializationResponse>> GetSpecializationsAsync(
            CancellationToken cancellationToken = default)
        {
            SpecializationCalls++;

            return CatalogFailure is null
                ? Task.FromResult(Specializations)
                : Task.FromException<IReadOnlyList<SpecializationResponse>>(CatalogFailure);
        }

        public Task<IReadOnlyList<HairServiceResponse>> GetHairServicesAsync(
            CancellationToken cancellationToken = default) =>
            CatalogFailure is null
                ? Task.FromResult(HairServices)
                : Task.FromException<IReadOnlyList<HairServiceResponse>>(CatalogFailure);

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

        public Task<PagedResponse<ReviewResponse>> GetReviewsAsync(
            Guid barberProfileId,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Поиск не читает отзывы.");
    }
}
