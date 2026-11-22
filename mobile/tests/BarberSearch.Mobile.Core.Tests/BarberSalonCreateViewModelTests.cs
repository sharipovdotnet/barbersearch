using System.Net;
using BarberSearch.Contracts.Addresses.SuggestAddresses;
using BarberSearch.Contracts.Addresses.SuggestCities;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Reviews.GetReviews;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Contracts.Salons.GetSalonDirectory;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Addresses;
using BarberSearch.Mobile.Core.Api.Salons;
using BarberSearch.Mobile.Core.ViewModels;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки формы «Создать салон»: начальное состояние, тип места и каталог, проверки полей,
/// тело запроса, подсказки города и адреса и тексты отказов.
/// </summary>
/// <remarks>
/// Подсказки собраны без паузы и без сети: пауза приходит делегатом, а клиенты подменены,
/// поэтому тест идёт синхронно и не зависит от таймеров. Записи не повторяются, поэтому
/// каждый отказ проверяется одним вызовом.
/// </remarks>
public sealed class BarberSalonCreateViewModelTests
{
    private const string ListingHintText =
        "Скрытое место не попадает в каталог и не требует проверки, но остаётся доступным для записи с вашей карточки.";

    private static readonly Guid CreatedSalonId = Guid.Parse("9e100000-0000-4000-8000-0000000000e9");

    private static readonly CitySuggestionResponse MoscowSuggestion = new(
        "fias-msk",
        "г. Москва",
        "Москва",
        "Москва",
        "Россия",
        55.7558,
        37.6173);

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

    [Fact]
    public void Constructor_Defaults_ShowsSalonWithDeviceTimeZone()
    {
        var viewModel = new Harness().ViewModel;

        Assert.Equal(5, viewModel.KindOptions.Count);
        Assert.Same(viewModel.KindOptions[0], viewModel.SelectedKindOption);
        Assert.Equal(PlaceKind.Salon, viewModel.SelectedKindOption.Kind);
        Assert.Equal("Барбершоп", viewModel.SelectedKindOption.Label);
        Assert.Equal("обычный салон со штатом", viewModel.KindHint);
        Assert.True(viewModel.IsListed);
        Assert.False(viewModel.CanToggleListing);
        Assert.Equal("Отправить на проверку", viewModel.SubmitText);
        Assert.True(viewModel.CanSubmit);
        Assert.Equal(ListingHintText, viewModel.ListingHint);
        Assert.Equal(string.Empty, viewModel.Name);
        Assert.Equal(string.Empty, viewModel.Address);
        Assert.Equal(string.Empty, viewModel.City);
        Assert.Equal(string.Empty, viewModel.Country);
        Assert.Equal(string.Empty, viewModel.Latitude);
        Assert.Equal(string.Empty, viewModel.Longitude);
        Assert.Equal(string.Empty, viewModel.PhoneNumber);
        Assert.Equal(string.Empty, viewModel.WebsiteUrl);
        Assert.Null(viewModel.FormError);
        Assert.False(viewModel.IsSaving);
        Assert.Null(viewModel.NameError);
        Assert.Null(viewModel.AddressError);
        Assert.Null(viewModel.CityError);
        Assert.Null(viewModel.CountryError);
        Assert.Null(viewModel.TimeZoneError);
        Assert.Null(viewModel.LatitudeError);
        Assert.Null(viewModel.LongitudeError);
        Assert.Null(viewModel.PhoneNumberError);
        Assert.Null(viewModel.WebsiteError);
        Assert.Null(viewModel.IsListedError);
        Assert.False(viewModel.CanEnterAddress);
        Assert.Equal("Сначала выберите город.", viewModel.AddressHint);

        var device = TimeZoneInfo.Local.Id;

        Assert.Equal(
            string.IsNullOrWhiteSpace(device) ? "Europe/Moscow" : device.Trim(),
            viewModel.TimeZones[0]);
        Assert.Equal(viewModel.TimeZones[0], viewModel.TimeZoneId);
    }

    [Fact]
    public void KindOptions_FollowWebOrderAndWording()
    {
        var options = new Harness().ViewModel.KindOptions;

        Assert.Equal(
            [PlaceKind.Salon, PlaceKind.RentalStudio, PlaceKind.SoloStudio, PlaceKind.Home, PlaceKind.Mobile],
            options.Select(option => option.Kind));
        Assert.Equal(
            ["Барбершоп", "Студия", "Кабинет", "На дому", "Выезд"],
            options.Select(option => option.Label));
        Assert.Equal(
            [
                "обычный салон со штатом",
                "сдаёт кресла другим барберам",
                "принимаете только вы",
                "адрес виден в карточке",
                "приезжаете к клиенту",
            ],
            options.Select(option => option.Hint));
    }

    [Fact]
    public void KindHelpers_RepeatWebRules()
    {
        Assert.True(BarberSalonKindOption.DefaultIsListed(PlaceKind.Salon));
        Assert.True(BarberSalonKindOption.DefaultIsListed(PlaceKind.RentalStudio));
        Assert.False(BarberSalonKindOption.DefaultIsListed(PlaceKind.SoloStudio));
        Assert.False(BarberSalonKindOption.DefaultIsListed(PlaceKind.Home));
        Assert.False(BarberSalonKindOption.DefaultIsListed(PlaceKind.Mobile));

        Assert.True(BarberSalonKindOption.IsListedAllowed(PlaceKind.Salon));
        Assert.True(BarberSalonKindOption.IsListedAllowed(PlaceKind.RentalStudio));
        Assert.True(BarberSalonKindOption.IsListedAllowed(PlaceKind.SoloStudio));
        Assert.False(BarberSalonKindOption.IsListedAllowed(PlaceKind.Home));
        Assert.False(BarberSalonKindOption.IsListedAllowed(PlaceKind.Mobile));
    }

    [Fact]
    public void SelectedKind_SoloStudio_AllowsToggleAndDefaultsHidden()
    {
        var viewModel = new Harness().ViewModel;

        SelectKind(viewModel, PlaceKind.SoloStudio);

        Assert.False(viewModel.IsListed);
        Assert.True(viewModel.CanToggleListing);
        Assert.Equal("принимаете только вы", viewModel.KindHint);
        Assert.Equal("Создать место", viewModel.SubmitText);

        viewModel.IsListed = true;

        Assert.Equal("Отправить на проверку", viewModel.SubmitText);
    }

    [Fact]
    public void SelectedKind_Home_ForcesHiddenAndDisablesToggle()
    {
        var viewModel = new Harness().ViewModel;

        SelectKind(viewModel, PlaceKind.Home);

        Assert.False(viewModel.IsListed);
        Assert.False(viewModel.CanToggleListing);
        Assert.Equal("адрес виден в карточке", viewModel.KindHint);
        Assert.Equal("Создать место", viewModel.SubmitText);
    }

    [Fact]
    public void SelectedKind_SalonAndRentalStudio_ForceListed()
    {
        var viewModel = new Harness().ViewModel;

        SelectKind(viewModel, PlaceKind.SoloStudio);
        viewModel.IsListed = true;

        SelectKind(viewModel, PlaceKind.RentalStudio);

        Assert.True(viewModel.IsListed);
        Assert.False(viewModel.CanToggleListing);

        viewModel.IsListed = false;

        SelectKind(viewModel, PlaceKind.Salon);

        Assert.True(viewModel.IsListed);
        Assert.False(viewModel.CanToggleListing);
    }

    [Fact]
    public void SubmitText_WhileSaving_UsesProgressWord()
    {
        var viewModel = new Harness().ViewModel;

        viewModel.IsSaving = true;

        Assert.Equal("Отправляем…", viewModel.SubmitText);
        Assert.False(viewModel.CanSubmit);

        viewModel.IsListed = false;

        Assert.Equal("Создаём…", viewModel.SubmitText);

        viewModel.IsSaving = false;

        Assert.Equal("Создать место", viewModel.SubmitText);
        Assert.True(viewModel.CanSubmit);
    }

    [Fact]
    public async Task SubmitAsync_EmptyForm_ShowsEveryRequiredErrorWithoutRequest()
    {
        var harness = new Harness();

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var viewModel = harness.ViewModel;

        Assert.Equal("Введите название.", viewModel.NameError);
        Assert.Equal("Введите адрес.", viewModel.AddressError);
        Assert.Equal("Введите город.", viewModel.CityError);
        Assert.Equal("Введите страну.", viewModel.CountryError);
        Assert.Null(viewModel.TimeZoneError);
        Assert.Equal("От -90 до 90.", viewModel.LatitudeError);
        Assert.Equal("От -180 до 180.", viewModel.LongitudeError);
        Assert.Null(viewModel.PhoneNumberError);
        Assert.Null(viewModel.WebsiteError);
        Assert.Null(viewModel.FormError);
        Assert.Empty(harness.Salons.Requests);
        Assert.False(viewModel.IsSaving);
    }

    [Fact]
    public async Task SubmitAsync_EmptyTimeZone_ShowsTimeZoneError()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.ViewModel.TimeZoneId = string.Empty;

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Выберите часовой пояс из списка.", harness.ViewModel.TimeZoneError);
        Assert.Empty(harness.Salons.Requests);
    }

    [Theory]
    [InlineData("91", "37.6173", "От -90 до 90.", null)]
    [InlineData("abc", "37.6173", "От -90 до 90.", null)]
    [InlineData("", "37.6173", "От -90 до 90.", null)]
    [InlineData("55.7558", "-181", null, "От -180 до 180.")]
    [InlineData("55.7558", "", null, "От -180 до 180.")]
    public async Task SubmitAsync_InvalidCoordinates_ShowsRangeError(
        string latitude,
        string longitude,
        string? expectedLatitudeError,
        string? expectedLongitudeError)
    {
        var harness = new Harness();

        harness.FillValid();
        harness.ViewModel.Latitude = latitude;
        harness.ViewModel.Longitude = longitude;

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(expectedLatitudeError, harness.ViewModel.LatitudeError);
        Assert.Equal(expectedLongitudeError, harness.ViewModel.LongitudeError);
        Assert.Empty(harness.Salons.Requests);
    }

    [Fact]
    public async Task SubmitAsync_CommaCoordinates_AreAcceptedAsDecimalSeparator()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.ViewModel.Latitude = "55,7558";
        harness.ViewModel.Longitude = "37,6173";

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Salons.Requests);

        Assert.Equal(55.7558, request.Latitude);
        Assert.Equal(37.6173, request.Longitude);
        Assert.Null(harness.ViewModel.LatitudeError);
        Assert.Null(harness.ViewModel.LongitudeError);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("http://")]
    public async Task SubmitAsync_WebsiteWithoutHttpScheme_ShowsWebsiteError(string website)
    {
        var harness = new Harness();

        harness.FillValid();
        harness.ViewModel.WebsiteUrl = website;

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Адрес сайта начинается с http:// или https://.", harness.ViewModel.WebsiteError);
        Assert.Empty(harness.Salons.Requests);
    }

    [Fact]
    public async Task SubmitAsync_TooLongWebsite_ShowsWebsiteError()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.ViewModel.WebsiteUrl = string.Concat("https://", new string('a', 2050));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Адрес сайта начинается с http:// или https://.", harness.ViewModel.WebsiteError);
        Assert.Empty(harness.Salons.Requests);
    }

    [Fact]
    public async Task SubmitAsync_ValidWebsite_IsSentTrimmed()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.ViewModel.WebsiteUrl = "  https://example.com  ";

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Null(harness.ViewModel.WebsiteError);
        Assert.Equal("https://example.com", Assert.Single(harness.Salons.Requests).WebsiteUrl);
    }

    [Fact]
    public async Task SubmitAsync_TooLongPhone_ShowsPhoneError()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.ViewModel.PhoneNumber = new string('9', 33);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Не длиннее 32 символов.", harness.ViewModel.PhoneNumberError);
        Assert.Empty(harness.Salons.Requests);
    }

    [Fact]
    public async Task SubmitAsync_PhoneAtLimit_IsAccepted()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.ViewModel.PhoneNumber = new string('9', 32);

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Null(harness.ViewModel.PhoneNumberError);
        Assert.Equal(new string('9', 32), Assert.Single(harness.Salons.Requests).PhoneNumber);
    }

    [Fact]
    public async Task FieldChange_ClearsOnlyItsOwnError()
    {
        var harness = new Harness();

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.NotNull(harness.ViewModel.NameError);
        Assert.NotNull(harness.ViewModel.CityError);

        harness.ViewModel.Name = "Hairo";

        Assert.Null(harness.ViewModel.NameError);
        Assert.NotNull(harness.ViewModel.CityError);
    }

    [Fact]
    public async Task SubmitAsync_EmptyOptionalFields_SendsNulls()
    {
        var harness = new Harness();

        harness.FillValid();

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Salons.Requests);

        Assert.Null(request.PostalCode);
        Assert.Null(request.PhoneNumber);
        Assert.Null(request.WebsiteUrl);
    }

    [Fact]
    public async Task SubmitAsync_Payload_TrimsFieldsAndMapsKindAndListing()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.ViewModel.Name = "  Hairo  ";
        harness.ViewModel.City = "  Москва  ";
        harness.ViewModel.Address = "  Ленина, 1  ";
        harness.ViewModel.Country = "  Россия  ";
        harness.ViewModel.TimeZoneId = "  Europe/Moscow  ";
        harness.ViewModel.PhoneNumber = "  +7 495 000-00-00  ";
        harness.ViewModel.WebsiteUrl = "  https://example.com  ";

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Salons.Requests);

        Assert.Equal("Hairo", request.Name);
        Assert.Equal("Москва", request.City);
        Assert.Equal("Ленина, 1", request.Address);
        Assert.Equal("Россия", request.Country);
        Assert.Equal("Europe/Moscow", request.TimeZoneId);
        Assert.Equal("+7 495 000-00-00", request.PhoneNumber);
        Assert.Equal("https://example.com", request.WebsiteUrl);
        Assert.Equal(PlaceKind.Salon, request.Kind);
        Assert.True(request.IsListed);
    }

    [Fact]
    public async Task SubmitAsync_SalonKind_ForcesListingEvenWhenFlagWasCleared()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.ViewModel.IsListed = false;

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(harness.Salons.Requests).IsListed);
        Assert.Null(harness.ViewModel.IsListedError);
    }

    [Fact]
    public async Task SubmitAsync_HomeKind_ForcesHiddenEvenWhenFlagWasSet()
    {
        var harness = new Harness();

        SelectKind(harness.ViewModel, PlaceKind.Home);
        harness.FillValid();
        harness.ViewModel.IsListed = true;

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Salons.Requests);

        Assert.Equal(PlaceKind.Home, request.Kind);
        Assert.False(request.IsListed);
    }

    [Fact]
    public async Task SubmitAsync_HiddenSoloStudio_RaisesCreatedMessage()
    {
        var harness = new Harness();
        string? message = null;

        harness.ViewModel.Saved += (_, text) => message = text;

        SelectKind(harness.ViewModel, PlaceKind.SoloStudio);
        harness.FillValid();

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.False(Assert.Single(harness.Salons.Requests).IsListed);
        Assert.Equal("Место приёма создано.", message);
        Assert.False(harness.ViewModel.IsSaving);
        Assert.True(harness.ViewModel.CanSubmit);
        Assert.Null(harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SubmitAsync_ListedRentalStudio_RaisesPendingMessage()
    {
        var harness = new Harness();
        string? message = null;

        harness.ViewModel.Saved += (_, text) => message = text;

        SelectKind(harness.ViewModel, PlaceKind.RentalStudio);
        harness.FillValid();

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(harness.Salons.Requests).IsListed);
        Assert.Equal("Салон отправлен на проверку.", message);
    }

    [Fact]
    public async Task SubmitAsync_ServerFieldCodes_FillFieldsWithoutFormError()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.Salons.Create = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(
                HttpStatusCode.BadRequest,
                "salon.name",
                "salon.city"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Введите название.", harness.ViewModel.NameError);
        Assert.Equal("Введите город.", harness.ViewModel.CityError);
        Assert.Null(harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SubmitAsync_ServerWebsiteAndTimeZoneCodes_FillBothFields()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.Salons.Create = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(
                HttpStatusCode.BadRequest,
                "salon.websiteUrl",
                "salon.timeZoneId.unknown"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(
            "Адрес сайта начинается с http:// или https://.",
            harness.ViewModel.WebsiteError);
        Assert.Equal("Выберите часовой пояс из списка.", harness.ViewModel.TimeZoneError);
        Assert.Null(harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SubmitAsync_ServerIsListedCode_FillsListingError()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.Salons.Create = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(HttpStatusCode.BadRequest, "salon.isListed.kind"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(
            "Барбершоп и студию нельзя скрыть из каталога.",
            harness.ViewModel.IsListedError);
        Assert.Null(harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SubmitAsync_ForbiddenWithoutProfile_ShowsProfileMessage()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.Salons.Create = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(HttpStatusCode.Forbidden, "barberProfile.notFound"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(
            "Профиль барбера не найден. Создайте профиль и повторите.",
            harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SubmitAsync_ForbiddenForeignSalon_ShowsOwnerMessage()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.Salons.Create = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(HttpStatusCode.Forbidden, "auth.forbidden"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(
            "Это чужой салон — править его может только владелец.",
            harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SubmitAsync_ConflictConcurrency_ShowsReloadMessage()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.Salons.Create = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(
                HttpStatusCode.Conflict,
                "salon.update.concurrencyConflict"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Запись изменили одновременно. Обновите список.", harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SubmitAsync_UnknownKind_ShowsRefreshMessage()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.Salons.Create = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(HttpStatusCode.BadRequest, "salon.kind.unknown"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(
            "Неизвестный тип места. Обновите страницу и попробуйте ещё раз.",
            harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SubmitAsync_TooManyRequests_ShowsWaitMessage()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.Salons.Create = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(HttpStatusCode.TooManyRequests));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(
            "Слишком много действий подряд. Подождите минуту.",
            harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SubmitAsync_Unauthorized_ShowsSessionMessage()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.Salons.Create = (_, _) => Task.FromException<Guid>(
            BarberCabinetApiError.Create(HttpStatusCode.Unauthorized));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Сессия истекла — войдите снова.", harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SubmitAsync_NetworkFailure_ShowsConnectionMessage()
    {
        var harness = new Harness();

        harness.FillValid();
        harness.Salons.Create = (_, _) => Task.FromException<Guid>(
            new HttpRequestException("Connection refused"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SubmitAsync_CancelledByScreen_IsSilent()
    {
        var harness = new Harness();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        harness.FillValid();
        harness.Salons.Create = async (_, cancellationToken) =>
        {
            started.TrySetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return CreatedSalonId;
        };

        var run = harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        await started.Task;

        Assert.True(harness.ViewModel.IsSaving);

        harness.ViewModel.SubmitCommand.Cancel();

        await run;

        Assert.Null(harness.ViewModel.FormError);
        Assert.False(harness.ViewModel.IsSaving);
    }

    [Fact]
    public async Task CityTyping_EnablesAddressAndQueriesCitySuggestions()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.City = "  Москва  ";

        Assert.True(viewModel.CanEnterAddress);
        Assert.Equal(string.Empty, viewModel.AddressHint);

        await viewModel.CityQueryCompletion;

        Assert.Equal("Москва", Assert.Single(harness.CityApi.Queries));
    }

    [Fact]
    public async Task TypingAddress_ClosesCitySuggestions()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        harness.CityApi.Suggestions = _ => Task.FromResult<IReadOnlyList<CitySuggestionResponse>>(
            [MoscowSuggestion]);

        viewModel.City = "Мо";

        await viewModel.CityQueryCompletion;

        Assert.True(viewModel.CitySuggestions.IsVisible);

        viewModel.Address = "Ленина";

        await viewModel.AddressQueryCompletion;

        Assert.False(viewModel.CitySuggestions.IsVisible);
        Assert.Empty(viewModel.CitySuggestions.Items);
    }

    [Fact]
    public async Task UseCitySuggestion_FillsCityCountryAndAddressProvider()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.UseCitySuggestionCommand.Execute(MoscowSuggestion);

        Assert.Equal("Москва", viewModel.City);
        Assert.Equal("Россия", viewModel.Country);
        Assert.True(viewModel.CanEnterAddress);
        Assert.False(viewModel.AddressSuggestions.IsVisible);

        await viewModel.CityQueryCompletion;

        Assert.Empty(harness.CityApi.Queries);

        viewModel.Address = "Ленина";

        await viewModel.AddressQueryCompletion;

        var call = Assert.Single(harness.AddressesApi.Calls);

        Assert.Equal("Ленина", call.Query);
        Assert.Equal("Москва", call.City);
        Assert.Equal("fias-msk", call.CityProviderId);
    }

    [Fact]
    public async Task UseCitySuggestion_EmptyCountry_KeepsTypedCountry()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.Country = "Россия";

        viewModel.UseCitySuggestionCommand.Execute(MoscowSuggestion with { Country = "  " });

        Assert.Equal("Россия", viewModel.Country);
    }

    [Fact]
    public async Task TypingCity_ClearsRememberedAddressProvider()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.UseCitySuggestionCommand.Execute(MoscowSuggestion);
        viewModel.City = "Тверь";
        viewModel.Address = "Советская";

        await viewModel.AddressQueryCompletion;

        var call = Assert.Single(harness.AddressesApi.Calls);

        Assert.Equal("Тверь", call.City);
        Assert.Null(call.CityProviderId);
    }

    [Fact]
    public void UseAddressSuggestion_FillsAddressAndCoordinates()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.City = "Москва";
        viewModel.UseAddressSuggestionCommand.Execute(Lenina);

        Assert.Equal("ул. Ленина, 1", viewModel.Address);
        Assert.Equal("55.7558", viewModel.Latitude);
        Assert.Equal("37.6173", viewModel.Longitude);
        Assert.False(viewModel.AddressSuggestions.IsVisible);
        Assert.Null(viewModel.AddressError);
    }

    [Fact]
    public void UseAddressSuggestion_WithoutAddress_UsesDisplay()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.City = "Москва";
        viewModel.UseAddressSuggestionCommand.Execute(
            Lenina with { Address = null, Display = "Москва, Ленина, 1" });

        Assert.Equal("Москва, Ленина, 1", viewModel.Address);
    }

    [Fact]
    public void UseAddressSuggestion_WithoutCoordinates_KeepsTypedCoordinate()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.City = "Москва";
        viewModel.Latitude = "1";

        viewModel.UseAddressSuggestionCommand.Execute(
            Lenina with { Latitude = null, Longitude = null });

        Assert.Equal("1", viewModel.Latitude);
        Assert.Equal(string.Empty, viewModel.Longitude);
    }

    [Fact]
    public void ManualAddressEdit_ClearsSuggestedCoordinates()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.City = "Москва";
        viewModel.UseAddressSuggestionCommand.Execute(Lenina);

        viewModel.Address = "ул. Ленина, 2";

        Assert.Equal("ул. Ленина, 2", viewModel.Address);
        Assert.Equal(string.Empty, viewModel.Latitude);
        Assert.Equal(string.Empty, viewModel.Longitude);
    }

    [Fact]
    public void ManualAddressEdit_KeepsCoordinatesTypedByHand()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.City = "Москва";
        viewModel.UseAddressSuggestionCommand.Execute(Lenina);
        viewModel.Latitude = "1";

        viewModel.Address = "ул. Ленина, 2";

        Assert.Equal("1", viewModel.Latitude);
        Assert.Equal(string.Empty, viewModel.Longitude);
    }

    [Fact]
    public async Task CityChange_ClearsSuggestedAddressAndCoordinates()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.City = "Москва";
        viewModel.UseAddressSuggestionCommand.Execute(Lenina);

        viewModel.City = "Тверь";

        await viewModel.CityQueryCompletion;

        Assert.Equal(string.Empty, viewModel.Address);
        Assert.Equal(string.Empty, viewModel.Latitude);
        Assert.Equal(string.Empty, viewModel.Longitude);
    }

    [Fact]
    public async Task CityChange_KeepsAddressTypedByHand()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        viewModel.City = "Москва";
        viewModel.Address = "Ленина, 1";

        viewModel.City = "Тверь";

        await viewModel.CityQueryCompletion;

        Assert.Equal("Ленина, 1", viewModel.Address);
    }

    [Fact]
    public async Task CityChange_ClosesAddressSuggestions()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        harness.AddressesApi.Suggestions = (_, _, _) =>
            Task.FromResult<IReadOnlyList<AddressSuggestionResponse>>([Lenina]);

        viewModel.City = "Москва";
        viewModel.Address = "Ленина";

        await viewModel.AddressQueryCompletion;

        Assert.True(viewModel.AddressSuggestions.IsVisible);

        viewModel.City = "Тверь";

        await viewModel.CityQueryCompletion;

        Assert.False(viewModel.AddressSuggestions.IsVisible);
    }

    [Fact]
    public void ClearingAddress_ClosesAddressSuggestions()
    {
        var harness = new Harness();
        var viewModel = harness.ViewModel;

        harness.AddressesApi.Suggestions = (_, _, _) =>
            Task.FromResult<IReadOnlyList<AddressSuggestionResponse>>([Lenina]);

        viewModel.City = "Москва";
        viewModel.Address = "Ленина";
        viewModel.Address = string.Empty;

        Assert.False(viewModel.AddressSuggestions.IsVisible);
    }

    [Fact]
    public void SalonTimeZones_DeviceZoneFirstWithoutDuplicates()
    {
        var zones = SalonTimeZones.Build("Europe/Moscow");

        Assert.Equal("Europe/Moscow", zones[0]);
        Assert.Equal(zones.Count, zones.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("Europe/Kaliningrad", zones);
        Assert.Contains("Asia/Kamchatka", zones);
        Assert.Contains("Asia/Dubai", zones);
        Assert.Contains("UTC", zones);
    }

    [Fact]
    public void SalonTimeZones_BlankDevice_FallsBackToMoscow()
    {
        Assert.Equal("Europe/Moscow", SalonTimeZones.Build("   ")[0]);
        Assert.Equal("Europe/Moscow", SalonTimeZones.Build(string.Empty)[0]);
    }

    [Fact]
    public void SalonTimeZones_DeviceOutsideCurated_IsPrepended()
    {
        var zones = SalonTimeZones.Build("  America/New_York  ");

        Assert.Equal("America/New_York", zones[0]);
        Assert.DoesNotContain("America/New_York", zones.Skip(1));
    }

    [Fact]
    public void SalonTimeZones_KeepsWestToEastOrder()
    {
        // Пояс устройства вне курированного списка встаёт первым и не сдвигает порядок зон.
        var zones = SalonTimeZones.Build("America/New_York").ToList();

        Assert.True(zones.IndexOf("Europe/Kaliningrad") < zones.IndexOf("Europe/Moscow"));
        Assert.True(zones.IndexOf("Europe/Moscow") < zones.IndexOf("Asia/Yekaterinburg"));
        Assert.True(zones.IndexOf("Asia/Kamchatka") < zones.IndexOf("UTC"));
    }

    /// <summary>
    /// Выбирает тип места так же, как это сделает пикер.
    /// </summary>
    private static void SelectKind(BarberSalonCreateViewModel viewModel, PlaceKind kind) =>
        viewModel.SelectedKindOption = viewModel.KindOptions.Single(option => option.Kind == kind);

    /// <summary>
    /// Собранная форма: подменённые клиенты и подсказки без паузы.
    /// </summary>
    private sealed class Harness
    {
        public Harness()
        {
            CitySuggestions = new CitySuggestions(
                CityApi,
                static (_, _) => Task.CompletedTask);
            AddressSuggestions = new AddressSuggestions(
                AddressesApi,
                static (_, _) => Task.CompletedTask);
            ViewModel = new BarberSalonCreateViewModel(
                Salons,
                CitySuggestions,
                AddressSuggestions);
        }

        public FakeSalonsApi Salons { get; } = new();

        public FakeCitySearchApi CityApi { get; } = new();

        public FakeAddressesApi AddressesApi { get; } = new();

        public CitySuggestions CitySuggestions { get; }

        public AddressSuggestions AddressSuggestions { get; }

        public BarberSalonCreateViewModel ViewModel { get; }

        /// <summary>
        /// Заполняет обязательные поля корректными значениями.
        /// </summary>
        public void FillValid()
        {
            ViewModel.Name = "Hairo";
            ViewModel.City = "Москва";
            ViewModel.Address = "Ленина, 1";
            ViewModel.Country = "Россия";
            ViewModel.Latitude = "55.7558";
            ViewModel.Longitude = "37.6173";
        }
    }

    /// <summary>
    /// Подменённый клиент создания салона: запоминает тела и отдаёт заданный ответ.
    /// </summary>
    private sealed class FakeSalonsApi : ISalonsApi
    {
        public Func<CreateSalonRequest, CancellationToken, Task<Guid>> Create { get; set; } =
            static (_, _) => Task.FromResult(CreatedSalonId);

        public List<CreateSalonRequest> Requests { get; } = [];

        public Task<Guid> CreateOwnAsync(
            CreateSalonRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Create(request, cancellationToken);
        }
    }

    /// <summary>
    /// Подменённые подсказки адреса: запоминают параметры и отдают заданный ответ.
    /// </summary>
    private sealed class FakeAddressesApi : IAddressesApi
    {
        public Func<string, string?, string?, Task<IReadOnlyList<AddressSuggestionResponse>>> Suggestions { get; set; } =
            static (_, _, _) => Task.FromResult<IReadOnlyList<AddressSuggestionResponse>>([]);

        public List<AddressCall> Calls { get; } = [];

        public Task<IReadOnlyList<AddressSuggestionResponse>> GetAddressSuggestionsAsync(
            string query,
            string? city,
            string? cityProviderId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new AddressCall(query, city, cityProviderId));

            return Suggestions(query, city, cityProviderId);
        }
    }

    /// <summary>
    /// Запрос подсказок адреса, ушедший в подменённый клиент.
    /// </summary>
    /// <param name="Query">Строка запроса.</param>
    /// <param name="City">Свободный текст города.</param>
    /// <param name="CityProviderId">Провайдерский токен города.</param>
    private sealed record AddressCall(string Query, string? City, string? CityProviderId);

    /// <summary>
    /// Подменённое публичное API: форма использует только подсказки города.
    /// </summary>
    /// <remarks>
    /// Остальные чтения честно падают: если форма внезапно начнёт их звать, тест скажет об
    /// этом, а не вернёт пустоту.
    /// </remarks>
    private sealed class FakeCitySearchApi : IBarberSearchApi
    {
        public Func<string, Task<IReadOnlyList<CitySuggestionResponse>>> Suggestions { get; set; } =
            static _ => Task.FromResult<IReadOnlyList<CitySuggestionResponse>>([]);

        public List<string> Queries { get; } = [];

        public Task<IReadOnlyList<CitySuggestionResponse>> GetCitySuggestionsAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);

            return Suggestions(query);
        }

        public Task<PagedResponse<BarberSearchItemResponse>> SearchBarbersAsync(
            BarberSearchFilters filters,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Форма салона не ищет мастеров.");

        public Task<IReadOnlyList<SpecializationResponse>> GetSpecializationsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Форма салона не читает справочники.");

        public Task<IReadOnlyList<HairServiceResponse>> GetHairServicesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Форма салона не читает справочники.");

        public Task<BarberProfileResponse> GetBarberProfileAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Форма салона не читает карточки.");

        public Task<BarberSlotsResponse> GetSlotsAsync(
            Guid barberProfileId,
            DateOnly slotDate,
            Guid? hairServiceId,
            Guid? salonId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Форма салона не читает слоты.");

        public Task<PagedResponse<ReviewResponse>> GetReviewsAsync(
            Guid barberProfileId,
            string? cursor = null,
            int? pageSize = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Форма салона не читает отзывы.");

        public Task<IReadOnlyList<SalonDirectoryItemResponse>> SearchSalonsAsync(
            string? city,
            string? search,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Форма салона не читает каталог.");
    }
}
