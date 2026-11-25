using System.Net;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileService;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Domain.HairServices;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки редактора услуги: ленивый справочник, добавление из каталога, правка цены и
/// длительности, клиентские границы полей и одна попытка записи.
/// </summary>
/// <remarks>
/// Справочник читается только при открытии добавления: правка уже стоящей услуги в нём не
/// нуждается. Услуги, которые уже в прайсе, из выдачи убираются — повторное добавление
/// сервер отверг бы кодом дубликата.
/// </remarks>
public sealed class BarberServiceEditorViewModelTests
{
    private const string SaveFailedText = "Не удалось сохранить услугу. Попробуйте ещё раз.";

    [Fact]
    public async Task InitializeForAdd_LoadsCatalogWithoutServicesAlreadyInPrice()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())]);

        await harness.ViewModel.CatalogLoading;

        var viewModel = harness.ViewModel;

        Assert.Equal(1, harness.Search.HairServiceCalls);
        Assert.True(viewModel.IsAddMode);
        Assert.False(viewModel.IsEditMode);
        Assert.False(viewModel.IsLoadingCatalog);
        Assert.Single(viewModel.Catalog);
        Assert.Equal(BarberCabinetData.SecondHairServiceId, viewModel.Catalog[0].Id);
        Assert.Equal("Борода", viewModel.Catalog[0].Name);
        Assert.Equal("Борода", viewModel.Catalog[0].CategoryText);
        Assert.False(viewModel.IsCatalogEmpty);
        Assert.Null(viewModel.CatalogError);
    }

    [Fact]
    public async Task InitializeForAdd_WholeCatalogInPrice_ShowsEmptyState()
    {
        var harness = new Harness();

        harness.Search.HairServices = _ => Task.FromResult<IReadOnlyList<HairServiceResponse>>(
            [BarberCabinetData.HairService()]);

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())]);

        await harness.ViewModel.CatalogLoading;

        Assert.Empty(harness.ViewModel.Catalog);
        Assert.True(harness.ViewModel.IsCatalogEmpty);
    }

    [Fact]
    public async Task CatalogFailure_ShowsReasonAndRetryReadsAgain()
    {
        var harness = new Harness();
        var calls = 0;

        harness.Search.HairServices = _ =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<IReadOnlyList<HairServiceResponse>>(
                    new HttpRequestException("offline"))
                : Task.FromResult<IReadOnlyList<HairServiceResponse>>(
                    [BarberCabinetData.HairService(BarberCabinetData.SecondHairServiceId)]);
        };

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())]);

        await harness.ViewModel.CatalogLoading;

        var viewModel = harness.ViewModel;

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            viewModel.CatalogError);

        await viewModel.RetryCatalogCommand.ExecuteAsync(null);

        Assert.Null(viewModel.CatalogError);
        Assert.Equal(2, calls);
        Assert.Single(viewModel.Catalog);
    }

    [Fact]
    public async Task SaveAsync_AddModeWithoutChosenService_ShowsHintWithoutRequest()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())]);

        await harness.ViewModel.CatalogLoading;

        var viewModel = harness.ViewModel;

        viewModel.PriceText = "2400";
        viewModel.DurationText = "30";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Выберите услугу из справочника.", viewModel.FormError);
        Assert.Empty(harness.Profile.ServiceAdds);
    }

    [Fact]
    public async Task SaveAsync_AddMode_SendsPostWithPriceDurationAndRaisesAdded()
    {
        var harness = new Harness();
        string? announcement = null;

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())],
            [BarberCabinetFixtures.ServiceSpecializationId]);

        await harness.ViewModel.CatalogLoading;

        var viewModel = harness.ViewModel;

        viewModel.Saved += (_, text) => announcement = text;
        viewModel.SelectedCatalogService = viewModel.Catalog[0];

        Assert.Equal("45", viewModel.DurationText);

        viewModel.PriceText = "2400,00";
        viewModel.DurationText = "45";

        await viewModel.SaveCommand.ExecuteAsync(null);

        var add = Assert.Single(harness.Profile.ServiceAdds);

        Assert.Equal(BarberCabinetFixtures.BarberProfileId, add.ProfileId);
        Assert.Equal(BarberCabinetData.SecondHairServiceId, add.Request.HairServiceId);
        Assert.Equal(2400m, add.Request.Price);
        Assert.Equal(45, add.Request.DurationMinutes);
        Assert.Equal("Услуга добавлена.", announcement);
        Assert.Null(viewModel.FormError);
        Assert.False(viewModel.IsSaving);
        Assert.Empty(harness.Profile.SpecializationAdds);
        Assert.Empty(harness.Profile.ServiceUpdates);
    }

    [Fact]
    public async Task SaveAsync_AddModeWithNewSpecialization_AddsSpecializationWithService()
    {
        var harness = new Harness();
        string? announcement = null;

        harness.Search.HairServices = _ => Task.FromResult<IReadOnlyList<HairServiceResponse>>(
        [
            BarberCabinetData.HairService(
                BarberCabinetData.SecondHairServiceId,
                "Борода",
                45,
                ServiceCategory.Beard,
                BarberCabinetData.SecondSpecializationId),
        ]);

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())],
            [BarberCabinetFixtures.ServiceSpecializationId]);

        await harness.ViewModel.CatalogLoading;

        var viewModel = harness.ViewModel;

        viewModel.Saved += (_, text) => announcement = text;
        viewModel.SelectedCatalogService = viewModel.Catalog[0];
        viewModel.PriceText = "1800";
        viewModel.DurationText = "45";

        await viewModel.SaveCommand.ExecuteAsync(null);

        var specialization = Assert.Single(harness.Profile.SpecializationAdds);

        Assert.Equal(BarberCabinetFixtures.BarberProfileId, specialization.ProfileId);
        Assert.Equal(BarberCabinetData.SecondSpecializationId, specialization.SpecializationId);
        Assert.Single(harness.Profile.ServiceAdds);
        Assert.Equal("Услуга добавлена.", announcement);
        Assert.Null(viewModel.FormError);
    }

    [Fact]
    public async Task SaveAsync_AddModeSpecializationFailure_DoesNotAddService()
    {
        var harness = new Harness();

        harness.Search.HairServices = _ => Task.FromResult<IReadOnlyList<HairServiceResponse>>(
        [
            BarberCabinetData.HairService(
                BarberCabinetData.SecondHairServiceId,
                "Борода",
                45,
                ServiceCategory.Beard,
                BarberCabinetData.SecondSpecializationId),
        ]);

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())]);

        await harness.ViewModel.CatalogLoading;

        var viewModel = harness.ViewModel;

        viewModel.SelectedCatalogService = viewModel.Catalog[0];
        viewModel.PriceText = "1800";
        viewModel.DurationText = "45";

        harness.Profile.AddSpecialization = (_, _) => Task.FromException(
            new HttpRequestException("offline"));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Single(harness.Profile.SpecializationAdds);
        Assert.Empty(harness.Profile.ServiceAdds);
        Assert.Equal(SaveFailedText, viewModel.FormError);
        Assert.False(viewModel.IsSaving);
    }

    [Fact]
    public async Task SaveAsync_AddModeDuplicateSpecialization_StillAddsService()
    {
        var harness = new Harness();

        harness.Search.HairServices = _ => Task.FromResult<IReadOnlyList<HairServiceResponse>>(
        [
            BarberCabinetData.HairService(
                BarberCabinetData.SecondHairServiceId,
                "Борода",
                45,
                ServiceCategory.Beard,
                BarberCabinetData.SecondSpecializationId),
        ]);

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())],
            [BarberCabinetFixtures.ServiceSpecializationId]);

        await harness.ViewModel.CatalogLoading;

        var viewModel = harness.ViewModel;

        viewModel.SelectedCatalogService = viewModel.Catalog[0];
        viewModel.PriceText = "1800";
        viewModel.DurationText = "45";

        harness.Profile.AddSpecialization = (_, _) => Task.FromException(
            BarberCabinetApiError.Create(
                HttpStatusCode.Conflict,
                "barberProfile.addSpecialization.duplicate"));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Single(harness.Profile.SpecializationAdds);
        Assert.Single(harness.Profile.ServiceAdds);
        Assert.Null(viewModel.FormError);
    }

    [Fact]
    public async Task SaveAsync_EditMode_FillsFieldsAndSendsPut()
    {
        var harness = new Harness();
        string? announcement = null;

        var item = new BarberServiceItem(
            BarberCabinetFixtures.Service(1800m, 45) with { IsActive = false });

        harness.ViewModel.InitializeForEdit(BarberCabinetFixtures.BarberProfileId, item);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsEditMode);
        Assert.False(viewModel.IsAddMode);
        Assert.Equal("Стрижка", viewModel.ServiceName);
        Assert.Equal("1800", viewModel.PriceText);
        Assert.Equal("45", viewModel.DurationText);

        viewModel.Saved += (_, text) => announcement = text;
        viewModel.PriceText = "2100";
        viewModel.DurationText = "50";

        await viewModel.SaveCommand.ExecuteAsync(null);

        var update = Assert.Single(harness.Profile.ServiceUpdates);

        Assert.Equal(BarberCabinetFixtures.HairServiceId, update.HairServiceId);
        Assert.Equal(2100m, update.Request.Price);
        Assert.Equal(50, update.Request.DurationMinutes);
        Assert.Equal("Цена обновлена.", announcement);
        Assert.Empty(harness.Profile.ServiceAdds);
    }

    [Fact]
    public async Task SaveAsync_InvalidPriceAndDuration_ShowsFieldErrors()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeForEdit(
            BarberCabinetFixtures.BarberProfileId,
            new BarberServiceItem(BarberCabinetFixtures.Service()));

        var viewModel = harness.ViewModel;

        viewModel.PriceText = string.Empty;
        viewModel.DurationText = "60";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Укажите цену.", viewModel.PriceError);

        viewModel.PriceText = "12,345";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Цена — не больше двух знаков после запятой.", viewModel.PriceError);

        viewModel.PriceText = "-1";
        viewModel.DurationText = "0";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Цена — число от 0 до 99 999 999,99.", viewModel.PriceError);
        Assert.Equal("Длительность — от 1 до 1440 минут.", viewModel.DurationError);
        Assert.Empty(harness.Profile.ServiceUpdates);
    }

    [Fact]
    public async Task SaveAsync_DomainCodes_ShowExactReasons()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeForEdit(
            BarberCabinetFixtures.BarberProfileId,
            new BarberServiceItem(BarberCabinetFixtures.Service()));

        var viewModel = harness.ViewModel;

        harness.Profile.UpdateService = (_, _, _) => Task.FromException(
            BarberCabinetApiError.Create(
                HttpStatusCode.Conflict,
                "barberProfile.addService.duplicate"));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Эта услуга уже есть в прайсе.", viewModel.FormError);

        harness.Profile.UpdateService = (_, _, _) => Task.FromException(
            BarberCabinetApiError.Create(
                HttpStatusCode.Conflict,
                "barberProfile.updateService.notFound"));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Эта услуга уже скрыта. Обновите страницу.", viewModel.FormError);
        Assert.Equal(2, harness.Profile.ServiceUpdates.Count);
    }

    [Fact]
    public async Task SaveAsync_TransportFailure_AttemptsSingleWrite()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeForEdit(
            BarberCabinetFixtures.BarberProfileId,
            new BarberServiceItem(BarberCabinetFixtures.Service()));

        harness.Profile.UpdateService = (_, _, _) => Task.FromException(
            new HttpRequestException("offline"));

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Single(harness.Profile.ServiceUpdates);
        Assert.Equal(SaveFailedText, harness.ViewModel.FormError);
        Assert.False(harness.ViewModel.IsSaving);
    }

    /// <summary>
    /// Собранный редактор: подменённые клиенты профиля и справочника.
    /// </summary>
    private sealed class Harness
    {
        public FakeBarberProfileApi Profile { get; } = new();

        public FakeBarberSearchApi Search { get; } = new();

        public BarberServiceEditorViewModel ViewModel => _viewModel ??= new BarberServiceEditorViewModel(
            Profile,
            Search);

        private BarberServiceEditorViewModel? _viewModel;
    }
}
