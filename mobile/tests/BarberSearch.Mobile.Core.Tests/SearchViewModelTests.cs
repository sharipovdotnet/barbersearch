using System.Net;
using BarberSearch.Contracts.Addresses.SuggestCities;
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
using BarberSearch.Mobile.Core.Location;
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
        Assert.Equal("Показаны 2 мастера", viewModel.ResultsCountText);
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
        Assert.Equal("Не удалось загрузить мастеров.", viewModel.ErrorMessage);
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
        viewModel.SelectedDate = Today.AddDays(2).ToDateTime(TimeOnly.MinValue);
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
    public void City_TwoCharacters_ShowsSuggestionsFromServer()
    {
        var api = FakeApi.Returning(Page());
        api.CitySuggestions =
        [
            Suggestion("Москва", "г. Москва"),
            Suggestion("Московский", "г. Московский"),
        ];

        var viewModel = Create(api);

        viewModel.City = "Мо";

        Assert.Equal("Мо", Assert.Single(api.CitySuggestionQueries));
        Assert.Equal(2, viewModel.CitySuggestions.Items.Count);
        Assert.True(viewModel.CitySuggestions.IsVisible);
        Assert.False(viewModel.CitySuggestions.IsEmpty);
    }

    [Fact]
    public void City_OneCharacter_DoesNotQuery()
    {
        var api = FakeApi.Returning(Page());
        var viewModel = Create(api);

        viewModel.City = "М";

        Assert.Empty(api.CitySuggestionQueries);
        Assert.False(viewModel.CitySuggestions.IsVisible);
    }

    [Fact]
    public void UseCitySuggestion_PutsBareCityAndClosesList()
    {
        var api = FakeApi.Returning(Page());
        api.CitySuggestions = [Suggestion("Москва", "г. Москва")];

        var viewModel = Create(api);

        viewModel.City = "Мо";

        var suggestion = Assert.Single(viewModel.CitySuggestions.Items);

        viewModel.UseCitySuggestionCommand.Execute(suggestion);

        // В поле — голое имя города: сервер сравнивает город точно, «г. Москва» не нашло бы салон.
        Assert.Equal("Москва", viewModel.City);
        Assert.Empty(viewModel.CitySuggestions.Items);
        Assert.False(viewModel.CitySuggestions.IsVisible);

        // Для выбранного города новый запрос не уходит: список не открывается сразу после выбора.
        Assert.Equal("Мо", Assert.Single(api.CitySuggestionQueries));
    }

    [Fact]
    public async Task SearchAsync_ClosesSuggestions()
    {
        var api = FakeApi.Returning(Page());
        api.CitySuggestions = [Suggestion("Москва", "г. Москва")];

        var viewModel = Create(api);

        viewModel.City = "Мо";

        Assert.True(viewModel.CitySuggestions.IsVisible);

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.False(viewModel.CitySuggestions.IsVisible);
        Assert.Empty(viewModel.CitySuggestions.Items);
        Assert.Equal("Мо", Assert.Single(api.SearchCalls).City);
    }

    [Fact]
    public async Task City_SuggestionsFailure_HidesListWithoutBreakingSearch()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        api.CitySuggestionsFailure = new BarberSearchApiException(
            HttpStatusCode.Conflict,
            "Подсказки выключены.",
            ["addressSuggestions.unavailable"],
            new Dictionary<string, string[]>());

        var viewModel = Create(api);

        viewModel.City = "Екат";

        // Отказ подсказок — тихий: поле остаётся вводом, поиск не получает ошибку.
        Assert.False(viewModel.CitySuggestions.IsVisible);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal("Екат", viewModel.City);

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Barbers);
        Assert.False(viewModel.HasError);
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
    public void Constructor_StartsWithAnyDateAndBookingHorizon()
    {
        var viewModel = Create(FakeApi.Returning(Page()));

        Assert.Null(viewModel.SelectedDate);
        Assert.True(viewModel.IsAnyDate);
        Assert.False(viewModel.HasSelectedDate);
        Assert.Equal(Today.ToDateTime(TimeOnly.MinValue), viewModel.MinimumDate);
        Assert.Equal(Today.AddDays(30).ToDateTime(TimeOnly.MinValue), viewModel.MaximumDate);
    }

    [Fact]
    public void ClearDate_ReturnsToAnyDate()
    {
        var viewModel = Create(FakeApi.Returning(Page()));

        viewModel.SelectedDate = Today.AddDays(3).ToDateTime(TimeOnly.MinValue);
        viewModel.ClearDateCommand.Execute(null);

        Assert.Null(viewModel.SelectedDate);
        Assert.True(viewModel.IsAnyDate);
        Assert.False(viewModel.HasSelectedDate);
    }

    [Fact]
    public async Task SearchAsync_AfterClearDate_SendsNoDate()
    {
        var api = FakeApi.Returning(Page());
        var viewModel = Create(api);

        viewModel.SelectedDate = Today.AddDays(3).ToDateTime(TimeOnly.MinValue);
        viewModel.ClearDateCommand.Execute(null);

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(api.SearchCalls).Date);
    }

    [Fact]
    public async Task InitializeAsync_DateOutsideHorizon_ResetsToAnyDate()
    {
        var viewModel = Create(FakeApi.Returning(Page()));

        viewModel.SelectedDate = Today.AddDays(31).ToDateTime(TimeOnly.MinValue);

        await viewModel.InitializeAsync();

        Assert.Null(viewModel.SelectedDate);
        Assert.True(viewModel.IsAnyDate);
    }

    [Fact]
    public async Task InitializeAsync_DateInsideHorizon_KeepsSelectedDate()
    {
        var viewModel = Create(FakeApi.Returning(Page()));
        var date = Today.AddDays(10).ToDateTime(TimeOnly.MinValue);

        viewModel.SelectedDate = date;

        await viewModel.InitializeAsync();

        Assert.Equal(date, viewModel.SelectedDate);
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

        viewModel.SelectedDate = Today.AddDays(1).ToDateTime(TimeOnly.MinValue);

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal("На выбранную дату никого не нашли", viewModel.EmptyTitle);
        Assert.Equal(
            "Фильтр оставляет мастеров с рабочим днём в этот день недели. Попробуйте другую дату.",
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

        Assert.Equal("Мастеров пока нет", viewModel.EmptyTitle);
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

    [Fact]
    public async Task SearchAsync_LocationAvailable_SendsCoordinatesAndShowsNearbySort()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса", 1.2)));
        var location = new FakeLocationService(LocationResult.Ok(new GeoPosition(56.8389, 60.6057)));
        var viewModel = Create(api, location);

        await viewModel.SearchCommand.ExecuteAsync(null);

        var filters = Assert.Single(api.SearchCalls);

        Assert.Equal(56.8389, filters.Latitude);
        Assert.Equal(60.6057, filters.Longitude);
        Assert.True(viewModel.ShowNearbySort);
        Assert.False(viewModel.HasLocationHint);
        Assert.Equal(1, location.Calls);
    }

    [Fact]
    public async Task SearchAsync_LocationDenied_SearchesWithoutCoordinatesAndShowsHint()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        var viewModel = Create(api, new FakeLocationService(LocationResult.Denied()));

        await viewModel.SearchCommand.ExecuteAsync(null);

        var filters = Assert.Single(api.SearchCalls);

        Assert.Null(filters.Latitude);
        Assert.Null(filters.Longitude);
        Assert.True(viewModel.HasLocationHint);
        Assert.Equal(
            "Геопозиция недоступна — показываем всех мастеров. Разрешите доступ в настройках телефона.",
            viewModel.LocationHint);
        Assert.False(viewModel.ShowNearbySort);
        // Отказ в геопозиции не блокирует поиск: выдача та же, просто без расстояний.
        Assert.Single(viewModel.Barbers);
    }

    [Fact]
    public async Task SearchAsync_LocationUnavailable_SearchesWithoutCoordinatesAndShowsHint()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        var viewModel = Create(api, new FakeLocationService(LocationResult.Unavailable()));

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(api.SearchCalls).Latitude);
        Assert.True(viewModel.HasLocationHint);
        Assert.Equal(
            "Не удалось определить геопозицию — показываем всех мастеров.",
            viewModel.LocationHint);
    }

    [Fact]
    public async Task SearchAsync_LocationPresentButServerWithoutDistances_DoesNotClaimNearbySort()
    {
        // Старый сервер проигнорирует координаты и вернёт рейтинговый порядок без distanceKm:
        // подпись «сначала ближайшие» тогда была бы ложью.
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        var viewModel = Create(api, new FakeLocationService(LocationResult.Ok(new GeoPosition(56.84, 60.61))));

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.False(viewModel.ShowNearbySort);
        Assert.False(viewModel.HasLocationHint);
    }

    [Fact]
    public async Task SearchAsync_SecondSearch_DoesNotReadLocationAgain()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        var location = new FakeLocationService(LocationResult.Denied());
        var viewModel = Create(api, location);

        await viewModel.SearchCommand.ExecuteAsync(null);
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(1, location.Calls);
        Assert.Equal(2, api.SearchCalls.Count);
    }

    [Fact]
    public async Task RetryLocationCommand_AfterDenial_ReadsLocationAgainAndSendsCoordinates()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса", 0.4)));
        var location = new FakeLocationService(LocationResult.Denied());
        var viewModel = Create(api, location);

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasLocationHint);

        location.Result = LocationResult.Ok(new GeoPosition(55.75, 37.62));

        await viewModel.RetryLocationCommand.ExecuteAsync(null);

        Assert.Equal(2, location.Calls);
        Assert.Equal(55.75, api.SearchCalls[^1].Latitude);
        Assert.Equal(37.62, api.SearchCalls[^1].Longitude);
        Assert.False(viewModel.HasLocationHint);
        Assert.True(viewModel.ShowNearbySort);
    }

    [Fact]
    public async Task InitializeAsync_LocationAvailable_FillsCityFromGeocoder()
    {
        var api = FakeApi.Returning(Page());
        var location = new FakeLocationService(LocationResult.Ok(new GeoPosition(56.8389, 60.6057)));
        var cityLocator = new FakeCityLocator { City = "Екатеринбург" };
        var viewModel = Create(api, location, cityLocator);

        await viewModel.InitializeAsync();
        await viewModel.CityFillCompletion;

        Assert.Equal("Екатеринбург", viewModel.City);
        // Геокодер получил ту самую точку, которую прочитал сервис геопозиции.
        Assert.Equal(new GeoPosition(56.8389, 60.6057), Assert.Single(cityLocator.Positions));
        Assert.False(viewModel.IsCityLocating);
        Assert.Equal(1, location.Calls);
    }

    [Fact]
    public async Task InitializeAsync_GeocoderReturnsPrefixedCity_StripsPrefix()
    {
        var api = FakeApi.Returning(Page());
        var location = new FakeLocationService(LocationResult.Ok(new GeoPosition(55.79, 49.12)));
        var cityLocator = new FakeCityLocator { City = "г. Казань" };
        var viewModel = Create(api, location, cityLocator);

        await viewModel.InitializeAsync();
        await viewModel.CityFillCompletion;

        // В поле идёт голое имя: сервер сравнивает город точно, «г. Казань» не нашло бы салон.
        Assert.Equal("Казань", viewModel.City);
    }

    [Fact]
    public async Task InitializeAsync_LocationDenied_FillsDefaultCity()
    {
        var api = FakeApi.Returning(Page());
        var cityLocator = new FakeCityLocator { City = "Казань" };
        var viewModel = Create(api, new FakeLocationService(LocationResult.Denied()), cityLocator);

        await viewModel.InitializeAsync();
        await viewModel.CityFillCompletion;

        Assert.Equal("Москва", viewModel.City);
        // Без координат геокодеру нечего определять: его не зовут вовсе.
        Assert.Empty(cityLocator.Positions);
        Assert.False(viewModel.IsCityLocating);
    }

    [Fact]
    public async Task InitializeAsync_LocationUnavailable_FillsDefaultCity()
    {
        var api = FakeApi.Returning(Page());
        var cityLocator = new FakeCityLocator { City = "Казань" };
        var viewModel = Create(api, new FakeLocationService(LocationResult.Unavailable()), cityLocator);

        await viewModel.InitializeAsync();
        await viewModel.CityFillCompletion;

        Assert.Equal("Москва", viewModel.City);
        Assert.Empty(cityLocator.Positions);
    }

    [Fact]
    public async Task InitializeAsync_GeocoderReturnsNull_FillsDefaultCity()
    {
        var api = FakeApi.Returning(Page());
        var location = new FakeLocationService(LocationResult.Ok(new GeoPosition(55.75, 37.62)));
        var viewModel = Create(api, location, new FakeCityLocator());

        await viewModel.InitializeAsync();
        await viewModel.CityFillCompletion;

        Assert.Equal("Москва", viewModel.City);
        Assert.Equal("Москва", SearchViewModel.DefaultCity);
    }

    [Fact]
    public async Task InitializeAsync_GeocoderThrows_FillsDefaultCity()
    {
        var api = FakeApi.Returning(Page());
        var location = new FakeLocationService(LocationResult.Ok(new GeoPosition(55.75, 37.62)));
        var cityLocator = new FakeCityLocator
        {
            Failure = new HttpRequestException("geocoder is down"),
        };
        var viewModel = Create(api, location, cityLocator);

        await viewModel.InitializeAsync();
        await viewModel.CityFillCompletion;

        // Сбой геокодера — это подсказка поля, а не ошибка экрана.
        Assert.Equal("Москва", viewModel.City);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsCityLocating);
    }

    [Fact]
    public async Task InitializeAsync_CityEnteredByUser_KeepsUserCity()
    {
        var api = FakeApi.Returning(Page());
        var location = new FakeLocationService(LocationResult.Ok(new GeoPosition(55.79, 49.12)));
        var cityLocator = new FakeCityLocator { City = "Казань" };
        var viewModel = Create(api, location, cityLocator);

        viewModel.City = "Тюмень";

        await viewModel.InitializeAsync();
        await viewModel.CityFillCompletion;

        // Автоподстановка не перетирает город, который человек набрал сам.
        Assert.Equal("Тюмень", viewModel.City);
    }

    [Fact]
    public async Task InitializeAsync_AutomaticCity_DoesNotRequestSuggestions()
    {
        var api = FakeApi.Returning(Page());
        var location = new FakeLocationService(LocationResult.Ok(new GeoPosition(55.79, 49.12)));
        var cityLocator = new FakeCityLocator { City = "Казань" };
        var viewModel = Create(api, location, cityLocator);

        await viewModel.InitializeAsync();
        await viewModel.CityFillCompletion;

        // Программная подстановка — не ввод: список подсказок при запуске вкладки не открывается.
        Assert.Equal("Казань", viewModel.City);
        Assert.Empty(api.CitySuggestionQueries);
        Assert.False(viewModel.CitySuggestions.IsVisible);
    }

    [Fact]
    public async Task SearchAsync_AfterCityAutoFill_SendsCityAndCoordinatesWithoutSecondRead()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса", 0.4)));
        var location = new FakeLocationService(LocationResult.Ok(new GeoPosition(55.79, 49.12)));
        var cityLocator = new FakeCityLocator { City = "Казань" };
        var viewModel = Create(api, location, cityLocator);

        await viewModel.InitializeAsync();
        await viewModel.CityFillCompletion;

        await viewModel.SearchCommand.ExecuteAsync(null);

        var filters = Assert.Single(api.SearchCalls);

        // Автоподстановка переиспользует уже прочитанную точку: второго разрешения и GPS нет.
        Assert.Equal("Казань", filters.City);
        Assert.Equal(55.79, filters.Latitude);
        Assert.Equal(49.12, filters.Longitude);
        Assert.Equal(1, location.Calls);
        Assert.Equal(1, cityLocator.Calls);
    }

    [Fact]
    public async Task InitializeAsync_SecondCall_DoesNotLocateCityAgain()
    {
        var api = FakeApi.Returning(Page());
        var location = new FakeLocationService(LocationResult.Ok(new GeoPosition(55.79, 49.12)));
        var cityLocator = new FakeCityLocator { City = "Казань" };
        var viewModel = Create(api, location, cityLocator);

        await viewModel.InitializeAsync();
        await viewModel.CityFillCompletion;

        await viewModel.InitializeAsync();
        await viewModel.CityFillCompletion;

        // Экран читает геопозицию один раз за жизнь, а не при каждом появлении вкладки.
        Assert.Equal(1, location.Calls);
        Assert.Equal(1, cityLocator.Calls);
        Assert.Equal("Казань", viewModel.City);
    }

    [Fact]
    public async Task RetryLocationCommand_AfterDeniedFirstAttempt_ReplacesDefaultCity()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса", 0.4)));
        var location = new FakeLocationService(LocationResult.Denied());
        var cityLocator = new FakeCityLocator { City = "Казань" };
        var viewModel = Create(api, location, cityLocator);

        await viewModel.InitializeAsync();
        await viewModel.CityFillCompletion;

        Assert.Equal("Москва", viewModel.City);
        Assert.Equal(1, location.Calls);

        // Человек разрешил доступ в настройках и нажал «Повторить»: «Москва» уступает городу.
        location.Result = LocationResult.Ok(new GeoPosition(55.79, 49.12));

        await viewModel.RetryLocationCommand.ExecuteAsync(null);

        Assert.Equal(2, location.Calls);
        Assert.Equal("Казань", viewModel.City);
        Assert.Equal("Казань", api.SearchCalls[^1].City);
        Assert.Equal(55.79, api.SearchCalls[^1].Latitude);
        Assert.Equal(49.12, api.SearchCalls[^1].Longitude);
    }

    [Fact]
    public async Task InitializeAsync_GeocoderPending_ReportsCityLocatingUntilCompletion()
    {
        var api = FakeApi.Returning(Page());
        var location = new FakeLocationService(LocationResult.Ok(new GeoPosition(55.79, 49.12)));
        var gate = new TaskCompletionSource();
        var cityLocator = new FakeCityLocator { City = "Казань", Gate = gate };
        var viewModel = Create(api, location, cityLocator);

        var initialization = viewModel.InitializeAsync();

        // Геокодер ещё думает: поле пустое, а экран объясняет задержку.
        Assert.True(viewModel.IsCityLocating);
        Assert.Equal(string.Empty, viewModel.City);

        gate.SetResult();

        await initialization;
        await viewModel.CityFillCompletion;

        Assert.False(viewModel.IsCityLocating);
        Assert.Equal("Казань", viewModel.City);
    }

    [Fact]
    public async Task StartAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        var viewModel = Create(api, autoRetry: FastRetry());

        api.Respond = call => call < 3
            ? Task.FromException<PagedResponse<BarberSearchItemResponse>>(new HttpRequestException("refused"))
            : Task.FromResult(Page(Barber("Алиса")));

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);

        viewModel.StartAutoRetry();
        await viewModel.AutoRetryCompletion;

        Assert.Equal(3, viewModel.AutoRetry.Attempts);
        Assert.Single(viewModel.Barbers);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task StartAutoRetry_TerminalFailure_DoesNotStart(HttpStatusCode statusCode)
    {
        var api = FakeApi.Returning(Page());
        api.Respond = _ => Task.FromException<PagedResponse<BarberSearchItemResponse>>(
            new BarberSearchApiException(
                statusCode,
                "Search failed.",
                ["barberSearch.search.failed"],
                new Dictionary<string, string[]>()));

        var viewModel = Create(api, autoRetry: FastRetry());

        await viewModel.SearchCommand.ExecuteAsync(null);

        viewModel.StartAutoRetry();

        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task StopAutoRetry_WhileWaiting_StopsFurtherAttempts()
    {
        var api = FakeApi.Returning(Page());
        api.Respond = _ => Task.FromException<PagedResponse<BarberSearchItemResponse>>(
            new HttpRequestException("refused"));

        var viewModel = Create(api, autoRetry: StopAfterFirstAttemptRetry());

        await viewModel.SearchCommand.ExecuteAsync(null);

        viewModel.StartAutoRetry();

        Assert.Equal(1, viewModel.AutoRetry.Attempts);
        Assert.True(viewModel.IsAutoRetrying);

        viewModel.StopAutoRetry();
        await viewModel.AutoRetryCompletion;

        Assert.Equal(1, viewModel.AutoRetry.Attempts);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task SearchCommand_WhileAutoRetrying_LoadsImmediatelyAndSuccessDoesNotRestart()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        api.Respond = call => call == 0
            ? Task.FromException<PagedResponse<BarberSearchItemResponse>>(new HttpRequestException("refused"))
            : Task.FromResult(Page(Barber("Алиса")));

        var viewModel = Create(api, autoRetry: HangingRetry());

        await viewModel.SearchCommand.ExecuteAsync(null);

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsAutoRetrying);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);

        // Кнопка «Повторить» — это тот же ручной поиск: цикл останавливается, чтение идёт сразу.
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, api.SearchCalls.Count);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.False(viewModel.IsAutoRetrying);
        Assert.Single(viewModel.Barbers);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task SearchCommand_WhileAutoRetrying_LoadFailsRetryable_RestartsAndKeepsRetryingUntilSuccess()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        var restartGate = new TaskCompletionSource();
        var silentAttempt = new TaskCompletionSource<PagedResponse<BarberSearchItemResponse>>();

        api.Respond = call => call switch
        {
            0 or 1 => Task.FromException<PagedResponse<BarberSearchItemResponse>>(
                new HttpRequestException("refused")),
            2 => silentAttempt.Task,
            _ => Task.FromResult(Page(Barber("Алиса"))),
        };

        var viewModel = Create(api, autoRetry: StopThenGateRetry(restartGate.Task));

        await viewModel.SearchCommand.ExecuteAsync(null);

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsAutoRetrying);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);

        // Кнопка при работающем цикле читает сразу: к возврату команды ручной запрос уже ушёл,
        // а перезапущенный цикл ждёт паузы — второго запроса без неё нет.
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, api.SearchCalls.Count);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.True(viewModel.IsAutoRetrying);

        // Повторяемый сбой кнопки возвращает цикл к жизни: первая же тихая попытка падает
        // снова, и цикл не сдаётся, а повторяет до успеха.
        restartGate.SetResult();
        silentAttempt.SetException(new HttpRequestException("refused"));

        await viewModel.AutoRetryCompletion;

        Assert.Equal(2, viewModel.AutoRetry.Attempts);
        Assert.Equal(4, api.SearchCalls.Count);
        Assert.Single(viewModel.Barbers);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_AfterFormEdit_RetriesTheFailedFilters()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        var viewModel = Create(api, autoRetry: FastRetry());

        api.Respond = call => call == 0
            ? Task.FromException<PagedResponse<BarberSearchItemResponse>>(new HttpRequestException("refused"))
            : Task.FromResult(Page(Barber("Алиса")));

        viewModel.City = "Тюмень";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.Equal("Тюмень", Assert.Single(api.SearchCalls).City);

        // Правка формы без «Найти»: цикл обязан повторить упавший запрос, а не собрать новый.
        viewModel.City = "Москва";

        viewModel.StartAutoRetry();
        await viewModel.AutoRetryCompletion;

        Assert.Equal(2, api.SearchCalls.Count);
        Assert.Equal("Тюмень", api.SearchCalls[1].City);
        Assert.Equal("Москва", viewModel.City);
        Assert.Null(viewModel.ErrorMessage);

        // Только «Найти» превращает правку формы в запрос — теперь с новым городом.
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Москва", api.SearchCalls[^1].City);
    }

    [Fact]
    public async Task AutoRetry_FailedSilentAttempt_KeepsErrorMessage()
    {
        var api = FakeApi.Returning(Page());
        var pending = new TaskCompletionSource<PagedResponse<BarberSearchItemResponse>>();
        var viewModel = Create(api, autoRetry: StopAfterFirstAttemptRetry());

        api.Respond = _ => Task.FromException<PagedResponse<BarberSearchItemResponse>>(
            new HttpRequestException("refused"));

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Нет связи с сервером. Проверьте подключение и повторите попытку.", viewModel.ErrorMessage);

        // Попытка висит: тихий режим не имеет права стереть ошибку на старте, иначе экран
        // мигал бы пустым состоянием на каждой паузе между повторами.
        api.Respond = _ => pending.Task;

        viewModel.StartAutoRetry();

        Assert.Equal(1, viewModel.AutoRetry.Attempts);
        Assert.Equal("Нет связи с сервером. Проверьте подключение и повторите попытку.", viewModel.ErrorMessage);
        Assert.True(viewModel.HasError);
        Assert.False(viewModel.ShowEmptyState);

        pending.SetException(new HttpRequestException("refused"));

        Assert.Equal("Нет связи с сервером. Проверьте подключение и повторите попытку.", viewModel.ErrorMessage);
        Assert.True(viewModel.HasError);

        viewModel.StopAutoRetry();
        await viewModel.AutoRetryCompletion;

        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_Timeout_ShowsNetworkText()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        var viewModel = Create(api, autoRetry: FastRetry());

        api.Respond = call => call == 0
            ? Task.FromException<PagedResponse<BarberSearchItemResponse>>(new TaskCanceledException("timeout"))
            : Task.FromResult(Page(Barber("Алиса")));

        await viewModel.SearchCommand.ExecuteAsync(null);

        // Таймаут HttpClient приходит отменой, но токен жив: для человека это «нет связи»,
        // а не тихая отмена. Раньше этот случай проглатывался.
        Assert.Equal("Нет связи с сервером. Проверьте подключение и повторите попытку.", viewModel.ErrorMessage);

        viewModel.StartAutoRetry();
        await viewModel.AutoRetryCompletion;

        Assert.Equal(1, viewModel.AutoRetry.Attempts);
        Assert.Single(viewModel.Barbers);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task IsAutoRetrying_TogglesWithLoopState()
    {
        var api = FakeApi.Returning(Page());
        api.Respond = _ => Task.FromException<PagedResponse<BarberSearchItemResponse>>(
            new HttpRequestException("refused"));

        var viewModel = Create(api, autoRetry: HangingRetry());

        await viewModel.SearchCommand.ExecuteAsync(null);

        viewModel.StartAutoRetry();
        Assert.True(viewModel.IsAutoRetrying);

        viewModel.StopAutoRetry();
        Assert.False(viewModel.IsAutoRetrying);

        await viewModel.AutoRetryCompletion;
    }

    [Fact]
    public async Task SearchCommand_WhenPageHidden_DoesNotStartAutoRetry()
    {
        var api = FakeApi.Returning(Page());
        api.Respond = _ => Task.FromException<PagedResponse<BarberSearchItemResponse>>(
            new HttpRequestException("refused"));

        var viewModel = Create(api, autoRetry: FastRetry());

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);

        // Кнопка «Повторить» — та же команда поиска; страница не сообщала о видимости, значит
        // фон не включается и на скрытом экране сеть не опрашивается.
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task StartAutoRetry_CatalogsTransportFailure_RetriesUntilSuccess()
    {
        var api = FakeApi.Returning(Page());
        api.SpecializationsRespond = call => call < 2 ? SpecializationsRefused() : SpecializationsOk();
        api.HairServicesRespond = call => call < 2 ? HairServicesRefused() : HairServicesOk();

        var viewModel = Create(api, catalogsAutoRetry: FastRetry());

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasCatalogsError);

        viewModel.StartAutoRetry();
        await viewModel.CatalogsAutoRetryCompletion;

        Assert.Equal(2, viewModel.CatalogsAutoRetry.Attempts);
        Assert.False(viewModel.HasCatalogsError);
        Assert.True(viewModel.IsCatalogsEnabled);
        Assert.Equal("Классика", viewModel.Specializations[1].DisplayText);
        Assert.Equal("Стрижка", viewModel.HairServices[1].DisplayText);
        Assert.False(viewModel.IsCatalogsAutoRetrying);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task StartAutoRetry_CatalogsTerminalFailure_DoesNotStart(HttpStatusCode statusCode)
    {
        var api = FakeApi.Returning(Page());
        api.CatalogFailure = ApiError(statusCode);

        var viewModel = Create(api, catalogsAutoRetry: FastRetry());

        await viewModel.InitializeAsync();

        viewModel.StartAutoRetry();

        Assert.Equal(0, viewModel.CatalogsAutoRetry.Attempts);
        Assert.False(viewModel.IsCatalogsAutoRetrying);
        Assert.Equal("Не удалось загрузить фильтры.", viewModel.CatalogsErrorText);
    }

    [Fact]
    public async Task ColdStartOffline_CatalogsTransportFailure_Visible_RecoversThroughLoop()
    {
        var gate = new TaskCompletionSource();
        var api = FakeApi.Returning(Page());
        api.SpecializationsRespond = call => call < 2 ? SpecializationsRefused() : SpecializationsOk();
        api.HairServicesRespond = call => call < 2 ? HairServicesRefused() : HairServicesOk();

        var viewModel = Create(api, catalogsAutoRetry: GatedRetry(gate.Task));

        // Страница видна до первой загрузки — как в OnAppearing у SearchPage.
        viewModel.StartAutoRetry();

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasCatalogsError);
        Assert.True(viewModel.IsCatalogsAutoRetrying);
        Assert.Equal(0, viewModel.CatalogsAutoRetry.Attempts);

        // Сеть вернулась: оба справочника отвечают.
        api.SpecializationsRespond = _ => SpecializationsOk();
        api.HairServicesRespond = _ => HairServicesOk();

        gate.SetResult();

        await viewModel.CatalogsAutoRetryCompletion;

        Assert.Equal(1, viewModel.CatalogsAutoRetry.Attempts);
        Assert.False(viewModel.HasCatalogsError);
        Assert.True(viewModel.IsCatalogsEnabled);
        Assert.False(viewModel.IsCatalogsAutoRetrying);
        Assert.Equal("Классика", viewModel.Specializations[1].DisplayText);
        Assert.Equal("Стрижка", viewModel.HairServices[1].DisplayText);
    }

    [Fact]
    public async Task StopAutoRetry_CatalogsWhileWaiting_StopsFurtherAttempts()
    {
        var api = FakeApi.Returning(Page());
        api.CatalogFailure = new HttpRequestException("refused");

        var viewModel = Create(api, catalogsAutoRetry: StopAfterFirstAttemptRetry());

        await viewModel.InitializeAsync();

        viewModel.StartAutoRetry();

        Assert.Equal(1, viewModel.CatalogsAutoRetry.Attempts);
        Assert.True(viewModel.IsCatalogsAutoRetrying);

        viewModel.StopAutoRetry();
        await viewModel.CatalogsAutoRetryCompletion;

        Assert.Equal(1, viewModel.CatalogsAutoRetry.Attempts);
        Assert.False(viewModel.IsCatalogsAutoRetrying);
    }

    [Fact]
    public async Task RetryCatalogsCommand_WhileAutoRetrying_LoadsImmediatelyAndSuccessDoesNotRestart()
    {
        var api = FakeApi.Returning(Page());
        api.CatalogFailure = new HttpRequestException("refused");

        var viewModel = Create(api, catalogsAutoRetry: HangingRetry());

        await viewModel.InitializeAsync();

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsCatalogsAutoRetrying);
        Assert.Equal(0, viewModel.CatalogsAutoRetry.Attempts);

        api.CatalogFailure = null;
        api.Specializations = [new SpecializationResponse(SpecializationId, "Классика", null)];
        api.HairServices = [new HairServiceResponse(HairServiceId, "Стрижка", ServiceCategory.Haircut, 40, null)];

        // Кнопка «Повторить» — та же загрузка справочников: цикл останавливается, чтение идёт сразу.
        await viewModel.RetryCatalogsCommand.ExecuteAsync(null);

        Assert.Equal(2, api.SpecializationCalls);
        Assert.Equal(0, viewModel.CatalogsAutoRetry.Attempts);
        Assert.False(viewModel.IsCatalogsAutoRetrying);
        Assert.False(viewModel.HasCatalogsError);
        Assert.Equal("Классика", viewModel.Specializations[1].DisplayText);
    }

    [Fact]
    public async Task RetryCatalogsCommand_WhileAutoRetrying_LoadFailsRetryable_RestartsAndKeepsRetryingUntilSuccess()
    {
        var api = FakeApi.Returning(Page());
        var restartGate = new TaskCompletionSource();
        var silentAttempt = new TaskCompletionSource<IReadOnlyList<SpecializationResponse>>();

        api.SpecializationsRespond = call => call switch
        {
            0 or 1 => SpecializationsRefused(),
            2 => silentAttempt.Task,
            _ => SpecializationsOk(),
        };
        api.HairServicesRespond = call => call < 2 ? HairServicesRefused() : HairServicesOk();

        var viewModel = Create(api, catalogsAutoRetry: StopThenGateRetry(restartGate.Task));

        await viewModel.InitializeAsync();

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsCatalogsAutoRetrying);
        Assert.Equal(0, viewModel.CatalogsAutoRetry.Attempts);

        // Кнопка при работающем цикле читает сразу: к возврату команды запрос уже ушёл,
        // а перезапущенный цикл ждёт паузы — второго запроса без неё нет.
        await viewModel.RetryCatalogsCommand.ExecuteAsync(null);

        Assert.Equal(2, api.SpecializationCalls);
        Assert.Equal(0, viewModel.CatalogsAutoRetry.Attempts);
        Assert.True(viewModel.IsCatalogsAutoRetrying);

        // Повторяемый сбой кнопки возвращает цикл к жизни: первая же тихая попытка падает
        // снова, и цикл повторяет её до успеха.
        restartGate.SetResult();
        silentAttempt.SetException(new HttpRequestException("refused"));

        await viewModel.CatalogsAutoRetryCompletion;

        Assert.Equal(2, viewModel.CatalogsAutoRetry.Attempts);
        Assert.Equal(4, api.SpecializationCalls);
        Assert.False(viewModel.HasCatalogsError);
        Assert.Equal("Классика", viewModel.Specializations[1].DisplayText);
        Assert.False(viewModel.IsCatalogsAutoRetrying);
    }

    [Fact]
    public async Task AutoRetry_CatalogsFailedSilentAttempt_KeepsErrorAndLoadedCatalog()
    {
        var api = FakeApi.Returning(Page());
        var silentAttempt = new TaskCompletionSource<IReadOnlyList<HairServiceResponse>>();

        api.SpecializationsRespond = _ => SpecializationsOk();
        api.HairServicesRespond = call => call == 0 ? HairServicesRefused() : silentAttempt.Task;

        var viewModel = Create(api, catalogsAutoRetry: StopAfterFirstAttemptRetry());

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasCatalogsError);
        Assert.Equal("Нет связи с сервером. Проверьте подключение и повторите попытку.", viewModel.CatalogsErrorText);
        Assert.Equal("Классика", viewModel.Specializations[1].DisplayText);

        viewModel.StartAutoRetry();

        // Попытка висит: тихий режим не имеет права стереть ни причину, ни загруженный справочник,
        // иначе экран мигал бы формой на каждой паузе между повторами.
        Assert.Equal(1, viewModel.CatalogsAutoRetry.Attempts);
        Assert.Equal("Нет связи с сервером. Проверьте подключение и повторите попытку.", viewModel.CatalogsErrorText);
        Assert.True(viewModel.HasCatalogsError);
        Assert.Equal("Классика", viewModel.Specializations[1].DisplayText);
        Assert.False(viewModel.IsCatalogsLoading);
        Assert.Equal(1, api.SpecializationCalls);

        silentAttempt.SetException(new HttpRequestException("refused"));

        Assert.True(viewModel.HasCatalogsError);
        Assert.Equal("Классика", viewModel.Specializations[1].DisplayText);

        viewModel.StopAutoRetry();
        await viewModel.CatalogsAutoRetryCompletion;

        Assert.False(viewModel.IsCatalogsAutoRetrying);
    }

    [Fact]
    public async Task StopAutoRetry_DuringCatalogsSilentAttempt_KeepsError()
    {
        var api = FakeApi.Returning(Page());
        var silentAttempt = new TaskCompletionSource<IReadOnlyList<HairServiceResponse>>();

        api.SpecializationsRespond = _ => SpecializationsOk();
        api.HairServicesRespond = call => call == 0 ? HairServicesRefused() : silentAttempt.Task;

        var viewModel = Create(api, catalogsAutoRetry: StopAfterFirstAttemptRetry());

        await viewModel.InitializeAsync();

        viewModel.StartAutoRetry();

        viewModel.StopAutoRetry();

        // Отмена попытки не оставляет форму пустой: причина, кнопка и загруженный справочник живы.
        Assert.True(viewModel.HasCatalogsError);
        Assert.Equal("Нет связи с сервером. Проверьте подключение и повторите попытку.", viewModel.CatalogsErrorText);
        Assert.Equal("Классика", viewModel.Specializations[1].DisplayText);
        Assert.False(viewModel.IsCatalogsAutoRetrying);

        silentAttempt.SetCanceled();
        await viewModel.CatalogsAutoRetryCompletion;
    }

    [Fact]
    public async Task AutoRetry_CatalogsTimeout_ShowsNetworkText()
    {
        var api = FakeApi.Returning(Page());
        api.CatalogFailure = new TaskCanceledException("timeout");

        var viewModel = Create(api, catalogsAutoRetry: FastRetry());

        await viewModel.InitializeAsync();

        // Таймаут HttpClient приходит отменой, но токен жив: для человека это «нет связи».
        Assert.Equal("Нет связи с сервером. Проверьте подключение и повторите попытку.", viewModel.CatalogsErrorText);

        api.CatalogFailure = null;

        viewModel.StartAutoRetry();
        await viewModel.CatalogsAutoRetryCompletion;

        Assert.Equal(1, viewModel.CatalogsAutoRetry.Attempts);
        Assert.False(viewModel.HasCatalogsError);
        Assert.False(viewModel.IsCatalogsAutoRetrying);
    }

    [Fact]
    public async Task IsCatalogsAutoRetrying_TogglesWithLoopState()
    {
        var api = FakeApi.Returning(Page());
        api.CatalogFailure = new HttpRequestException("refused");

        var viewModel = Create(api, catalogsAutoRetry: HangingRetry());

        await viewModel.InitializeAsync();

        viewModel.StartAutoRetry();
        Assert.True(viewModel.IsCatalogsAutoRetrying);

        viewModel.StopAutoRetry();
        Assert.False(viewModel.IsCatalogsAutoRetrying);

        await viewModel.CatalogsAutoRetryCompletion;
    }

    [Fact]
    public async Task AutoRetry_AfterServicesFailure_RetriesOnlyFailedCatalog()
    {
        var api = FakeApi.Returning(Page());

        api.SpecializationsRespond = _ => SpecializationsOk();
        api.HairServicesFailure = new HttpRequestException("refused");

        var viewModel = Create(api, catalogsAutoRetry: FastRetry());

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasCatalogsError);
        Assert.Equal("Классика", viewModel.Specializations[1].DisplayText);
        Assert.Equal(1, api.SpecializationCalls);
        Assert.Equal(1, api.HairServiceCalls);

        api.HairServicesFailure = null;
        api.HairServices = [new HairServiceResponse(HairServiceId, "Стрижка", ServiceCategory.Haircut, 40, null)];

        viewModel.StartAutoRetry();
        await viewModel.CatalogsAutoRetryCompletion;

        // Снимок — какие из двух запросов упали: удавшаяся специализация заново не читается.
        Assert.Equal(1, api.SpecializationCalls);
        Assert.Equal(2, api.HairServiceCalls);
        Assert.Equal(1, viewModel.CatalogsAutoRetry.Attempts);
        Assert.False(viewModel.HasCatalogsError);
        Assert.Equal("Стрижка", viewModel.HairServices[1].DisplayText);
    }

    [Fact]
    public async Task AutoRetry_CatalogsFailure_DoesNotStartResultsRetry()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));

        api.SpecializationsRespond = call => call == 0 ? SpecializationsRefused() : SpecializationsOk();
        api.HairServicesRespond = call => call == 0 ? HairServicesRefused() : HairServicesOk();

        var viewModel = Create(api, autoRetry: FastRetry(), catalogsAutoRetry: FastRetry());

        await viewModel.InitializeAsync();

        viewModel.StartAutoRetry();

        Assert.False(viewModel.AutoRetry.IsRunning);
        Assert.Equal(0, viewModel.AutoRetry.Attempts);

        await viewModel.CatalogsAutoRetryCompletion;

        // Сбой справочников не включает повтор выдачи: это два независимых чтения.
        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.Empty(api.SearchCalls);
        Assert.Empty(viewModel.Barbers);
        Assert.False(viewModel.HasCatalogsError);
    }

    [Fact]
    public async Task AutoRetry_ResultsFailure_DoesNotRetryCatalogs()
    {
        var api = FakeApi.Returning(Page(Barber("Алиса")));
        api.Respond = call => call == 0
            ? Task.FromException<PagedResponse<BarberSearchItemResponse>>(new HttpRequestException("refused"))
            : Task.FromResult(Page(Barber("Алиса")));
        api.Specializations = [new SpecializationResponse(SpecializationId, "Классика", null)];
        api.HairServices = [new HairServiceResponse(HairServiceId, "Стрижка", ServiceCategory.Haircut, 40, null)];

        var viewModel = Create(api, autoRetry: FastRetry(), catalogsAutoRetry: FastRetry());

        await viewModel.InitializeAsync();
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);

        viewModel.StartAutoRetry();
        await viewModel.AutoRetryCompletion;

        // Сбой выдачи не трогает справочники: второй цикл не делает ни одной попытки.
        Assert.Equal(1, api.SpecializationCalls);
        Assert.Equal(1, api.HairServiceCalls);
        Assert.Equal(0, viewModel.CatalogsAutoRetry.Attempts);
        Assert.False(viewModel.HasCatalogsError);
        Assert.Single(viewModel.Barbers);
    }

    [Fact]
    public async Task StartAutoRetry_RateLimitedCatalogs_DoesNotStart()
    {
        var api = FakeApi.Returning(Page());
        api.CatalogFailure = ApiError(HttpStatusCode.TooManyRequests);

        var viewModel = Create(api, catalogsAutoRetry: FastRetry());

        await viewModel.InitializeAsync();

        Assert.Equal("Слишком много запросов. Подождите минуту и повторите.", viewModel.CatalogsErrorText);

        viewModel.StartAutoRetry();

        // 429 в справочниках не повторяется: лимитер просит подождать, и текст остаётся на экране.
        Assert.Equal(0, viewModel.CatalogsAutoRetry.Attempts);
        Assert.False(viewModel.IsCatalogsAutoRetrying);
        Assert.Equal("Слишком много запросов. Подождите минуту и повторите.", viewModel.CatalogsErrorText);
    }

    [Fact]
    public async Task AutoRetry_TerminalAfterRetryable_DoesNotRestart()
    {
        var api = FakeApi.Returning(Page());
        api.Respond = call => call == 0
            ? Task.FromException<PagedResponse<BarberSearchItemResponse>>(new HttpRequestException("refused"))
            : Task.FromException<PagedResponse<BarberSearchItemResponse>>(ApiError(HttpStatusCode.NotFound));

        var viewModel = Create(api, autoRetry: HangingRetry());

        await viewModel.SearchCommand.ExecuteAsync(null);

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsAutoRetrying);

        // Кнопка при работающем цикле читает сразу — и получает окончательный отказ. Старый
        // «транспорт» не должен оживить цикл на отказе, который не изменится.
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(0, viewModel.AutoRetry.Attempts);
        Assert.False(viewModel.IsAutoRetrying);
        Assert.Equal("Не удалось загрузить мастеров.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AutoRetry_CatalogsTerminalAfterRetryable_DoesNotRestart()
    {
        var api = FakeApi.Returning(Page());
        api.CatalogFailure = new HttpRequestException("refused");

        var viewModel = Create(api, catalogsAutoRetry: HangingRetry());

        await viewModel.InitializeAsync();

        viewModel.StartAutoRetry();

        Assert.True(viewModel.IsCatalogsAutoRetrying);

        api.CatalogFailure = ApiError(HttpStatusCode.NotFound);

        await viewModel.RetryCatalogsCommand.ExecuteAsync(null);

        Assert.Equal(0, viewModel.CatalogsAutoRetry.Attempts);
        Assert.False(viewModel.IsCatalogsAutoRetrying);
        Assert.Equal("Не удалось загрузить фильтры.", viewModel.CatalogsErrorText);
    }

    /// <summary>
    /// Создаёт ViewModel с фиксированным «сегодня»: границы календаря не зависят от машины теста.
    /// </summary>
    /// <remarks>
    /// Без явной геопозиции подменённый сервис отвечает «недоступна»: старые проверки поиска
    /// не должны незаметно зависеть от координат. Подменённый геокодер по умолчанию города
    /// не знает, поэтому автоподстановка даёт «Москву» — город по умолчанию, и старые проверки
    /// не зависят и от него. Второй цикл передаётся отдельным параметром: у справочников своя
    /// жизнь, и общий цикл связал бы их сбои с выдачей.
    /// </remarks>
    private static SearchViewModel Create(
        FakeApi api,
        ILocationService? locationService = null,
        ICityLocator? cityLocator = null,
        AutoRetryLoop? autoRetry = null,
        AutoRetryLoop? catalogsAutoRetry = null) =>
        new(
            api,
            locationService ?? new FakeLocationService(LocationResult.Unavailable()),
            new FixedTimeProvider(Today),
            // Подсказки без паузы: тест набирает город и сразу видит результат, не ожидая 250 мс.
            new CitySuggestions(api, static (_, _) => Task.CompletedTask),
            cityLocator ?? new FakeCityLocator(),
            autoRetry,
            catalogsAutoRetry);

    /// <summary>
    /// Цикл с мгновенной паузой: секунды расписания в тесте не тратятся.
    /// </summary>
    private static AutoRetryLoop FastRetry() =>
        new(
            static (_, _) => Task.CompletedTask,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    /// <summary>
    /// Цикл, у которого первая пауза проходит сразу, а вторая висит до остановки: так видно
    /// состояние между попытками, не ожидая реального времени.
    /// </summary>
    private static AutoRetryLoop StopAfterFirstAttemptRetry()
    {
        var waits = 0;

        return new AutoRetryLoop(
            (_, cancellationToken) => Interlocked.Increment(ref waits) == 1
                ? Task.CompletedTask
                : WaitForCancellationAsync(cancellationToken),
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);
    }

    /// <summary>
    /// Цикл, у которого первая пауза висит до остановки, а следующая ждёт внешнего сигнала:
    /// так видно и работу цикла до нажатия кнопки, и его перезапуск после неё, без таймеров.
    /// </summary>
    private static AutoRetryLoop StopThenGateRetry(Task restartGate)
    {
        var waits = 0;

        return new AutoRetryLoop(
            (_, cancellationToken) =>
            {
                var wait = Interlocked.Increment(ref waits);

                return wait == 1
                    ? WaitForCancellationAsync(cancellationToken)
                    : wait == 2
                        ? restartGate
                        : Task.CompletedTask;
            },
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);
    }

    /// <summary>
    /// Цикл, который висит на первой же паузе: проверяется остановка до всякой попытки.
    /// </summary>
    private static AutoRetryLoop HangingRetry() =>
        new(
            (_, cancellationToken) => WaitForCancellationAsync(cancellationToken),
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    /// <summary>
    /// Цикл, который ждёт внешнего сигнала перед каждой попыткой: так видно состояние цикла
    /// до первой попытки, не ожидая реального времени.
    /// </summary>
    private static AutoRetryLoop GatedRetry(Task gate) =>
        new(
            (_, _) => gate,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    /// <summary>
    /// Ожидание, которое заканчивается только отменой: Stop проверяется без таймеров.
    /// </summary>
    private static Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));

        return pending.Task;
    }

    /// <summary>
    /// Успешный справочник специализаций — для проверок частичного успеха и повтора.
    /// </summary>
    private static Task<IReadOnlyList<SpecializationResponse>> SpecializationsOk() =>
        Task.FromResult<IReadOnlyList<SpecializationResponse>>(
            [new SpecializationResponse(SpecializationId, "Классика", null)]);

    /// <summary>
    /// Успешный справочник услуг — для проверок частичного успеха и повтора.
    /// </summary>
    private static Task<IReadOnlyList<HairServiceResponse>> HairServicesOk() =>
        Task.FromResult<IReadOnlyList<HairServiceResponse>>(
            [new HairServiceResponse(HairServiceId, "Стрижка", ServiceCategory.Haircut, 40, null)]);

    /// <summary>
    /// Сетевая ошибка справочника специализаций.
    /// </summary>
    private static Task<IReadOnlyList<SpecializationResponse>> SpecializationsRefused() =>
        Task.FromException<IReadOnlyList<SpecializationResponse>>(new HttpRequestException("refused"));

    /// <summary>
    /// Сетевая ошибка справочника услуг.
    /// </summary>
    private static Task<IReadOnlyList<HairServiceResponse>> HairServicesRefused() =>
        Task.FromException<IReadOnlyList<HairServiceResponse>>(new HttpRequestException("refused"));

    /// <summary>
    /// Ответ API с ошибкой без кодов: важен только статус.
    /// </summary>
    private static BarberSearchApiException ApiError(HttpStatusCode statusCode) =>
        new(statusCode, "Request failed.", [], new Dictionary<string, string[]>());

    private static PagedResponse<BarberSearchItemResponse> Page(params BarberSearchItemResponse[] items) =>
        new(items, NextCursor: null);

    private static BarberSearchItemResponse Barber(string displayName, double? distanceKm = null) => new(
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
        null,
        distanceKm);

    private static CitySuggestionResponse Suggestion(string city, string display) => new(
        string.Concat("fias-", city),
        display,
        city,
        "Московская обл.",
        "Россия",
        55.7558,
        37.6173);

    /// <summary>
    /// Подменённая геопозиция: запоминает число чтений и отдаёт заданный результат.
    /// </summary>
    private sealed class FakeLocationService(LocationResult result) : ILocationService
    {
        public LocationResult Result { get; set; } = result;

        public int Calls { get; private set; }

        public Task<LocationResult> GetPositionAsync(CancellationToken cancellationToken = default)
        {
            Calls++;

            return Task.FromResult(Result);
        }
    }

    /// <summary>
    /// Подменённый геокодер: запоминает полученные точки и число вызовов, отдаёт заданный город,
    /// падает заданным исключением или висит на внешнем сигнале.
    /// </summary>
    private sealed class FakeCityLocator : ICityLocator
    {
        /// <summary>
        /// Город, который «знает» геокодер; <c>null</c> — город определить не удалось.
        /// </summary>
        public string? City { get; set; }

        /// <summary>
        /// Сбой геокодера вместо ответа: экран обязан подставить город по умолчанию.
        /// </summary>
        public Exception? Failure { get; set; }

        /// <summary>
        /// Сигнал, до которого геокодер «думает»: так проверяется состояние экрана во время
        /// подстановки, не ожидая реального времени.
        /// </summary>
        public TaskCompletionSource? Gate { get; set; }

        /// <summary>
        /// Точки, которые получил геокодер, в порядке вызовов.
        /// </summary>
        public List<GeoPosition> Positions { get; } = [];

        /// <summary>
        /// Сколько раз экран спрашивал название города.
        /// </summary>
        public int Calls { get; private set; }

        public async Task<string?> GetCityAsync(
            GeoPosition position,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Positions.Add(position);

            if (Gate is { } gate)
            {
                await gate.Task;
            }

            if (Failure is { } failure)
            {
                throw failure;
            }

            return City;
        }
    }

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

        /// <summary>
        /// Сбой обоих справочников сразу: старый путь, который должны продолжать работать.
        /// </summary>
        public Exception? CatalogFailure { get; set; }

        /// <summary>
        /// Сбой только специализаций: частичный успех услуг проверяется отдельно.
        /// </summary>
        public Exception? SpecializationsFailure { get; set; }

        /// <summary>
        /// Сбой только услуг: частичный успех специализаций проверяется отдельно.
        /// </summary>
        public Exception? HairServicesFailure { get; set; }

        /// <summary>
        /// Ответ специализаций по номеру вызова (с нуля): так проверяется повтор до успеха.
        /// </summary>
        public Func<int, Task<IReadOnlyList<SpecializationResponse>>>? SpecializationsRespond { get; set; }

        /// <summary>
        /// Ответ услуг по номеру вызова (с нуля): так проверяется повтор до успеха.
        /// </summary>
        public Func<int, Task<IReadOnlyList<HairServiceResponse>>>? HairServicesRespond { get; set; }

        public IReadOnlyList<CitySuggestionResponse> CitySuggestions { get; set; } = [];

        public Exception? CitySuggestionsFailure { get; set; }

        public List<string> CitySuggestionQueries { get; } = [];

        public List<BarberSearchFilters> SearchCalls { get; } = [];

        public int SpecializationCalls { get; private set; }

        public int HairServiceCalls { get; private set; }

        /// <summary>
        /// Ответ выдачи по номеру вызова (с нуля): так проверяются сбои, повтор и успех после них.
        /// </summary>
        public Func<int, Task<PagedResponse<BarberSearchItemResponse>>>? Respond { get; set; }

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

            return Respond is null ? _response() : Respond(SearchCalls.Count - 1);
        }

        public Task<IReadOnlyList<SpecializationResponse>> GetSpecializationsAsync(
            CancellationToken cancellationToken = default)
        {
            var call = SpecializationCalls++;

            if (SpecializationsRespond is { } respond)
            {
                return respond(call);
            }

            var failure = SpecializationsFailure ?? CatalogFailure;

            return failure is null
                ? Task.FromResult(Specializations)
                : Task.FromException<IReadOnlyList<SpecializationResponse>>(failure);
        }

        public Task<IReadOnlyList<HairServiceResponse>> GetHairServicesAsync(
            CancellationToken cancellationToken = default)
        {
            var call = HairServiceCalls++;

            if (HairServicesRespond is { } respond)
            {
                return respond(call);
            }

            var failure = HairServicesFailure ?? CatalogFailure;

            return failure is null
                ? Task.FromResult(HairServices)
                : Task.FromException<IReadOnlyList<HairServiceResponse>>(failure);
        }

        public Task<IReadOnlyList<CitySuggestionResponse>> GetCitySuggestionsAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            CitySuggestionQueries.Add(query);

            return CitySuggestionsFailure is null
                ? Task.FromResult(CitySuggestions)
                : Task.FromException<IReadOnlyList<CitySuggestionResponse>>(CitySuggestionsFailure);
        }

        public Task<BarberProfileResponse> GetBarberProfileAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Поиск не читает карточку мастера.");

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
