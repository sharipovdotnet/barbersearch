using System.Net;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileService;
using BarberSearch.Contracts.HairServices.GetHairServices;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Domain.HairServices;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки редактора услуги: ленивые справочники, добавление из каталога, правка цены и
/// длительности, клиентские границы полей и одна попытка записи.
/// </summary>
/// <remarks>
/// Справочники читаются только при открытии добавления: правка уже стоящей услуги в них не
/// нуждается. Услуги, которые уже в прайсе, из выдачи убираются — повторное добавление
/// сервер отверг бы кодом дубликата. Выдача приходит группами по специализациям, а выбранная
/// строка подсвечивается и раскрывает карточку цены над списком.
/// </remarks>
public sealed class BarberServiceEditorViewModelTests
{
    private const string SaveFailedText = "Не удалось сохранить услугу. Попробуйте ещё раз.";

    [Fact]
    public async Task InitializeForAdd_LoadsCatalogGroupedBySpecialization()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())]);

        await harness.ViewModel.CatalogLoading;

        var viewModel = harness.ViewModel;

        Assert.Equal(1, harness.Search.HairServiceCalls);
        Assert.Equal(1, harness.Search.SpecializationCalls);
        Assert.True(viewModel.IsAddMode);
        Assert.False(viewModel.IsEditMode);
        Assert.False(viewModel.IsLoadingCatalog);
        Assert.False(viewModel.IsFormVisible);

        var group = Assert.Single(viewModel.CatalogGroups);
        var service = Assert.Single(group.Services);

        Assert.Equal("Борода", group.Title);
        Assert.Equal(BarberCabinetData.SecondHairServiceId, service.Id);
        Assert.Equal("Борода", service.Name);
        Assert.Equal("Борода", service.CategoryText);
        Assert.Equal("Борода · 45 мин", service.MetaText);
        Assert.False(service.IsSelected);
        Assert.False(viewModel.IsCatalogEmpty);
        Assert.Null(viewModel.CatalogError);
    }

    [Fact]
    public async Task InitializeForAdd_GroupsSpecializationsInCatalogueOrderNotAlphabetical()
    {
        var harness = new Harness();

        var fadeId = Guid.Parse("9e100000-0000-4000-8000-0000000000f1");
        var beardId = Guid.Parse("9e100000-0000-4000-8000-0000000000f2");
        var stylingId = Guid.Parse("9e100000-0000-4000-8000-0000000000f3");

        harness.Search.Specializations = _ => Task.FromResult<IReadOnlyList<SpecializationResponse>>(
        [
            BarberCabinetData.Specialization(stylingId, "Укладка", category: ServiceCategory.Styling),
            BarberCabinetData.Specialization(beardId, "Барбер", category: ServiceCategory.Beard),
            BarberCabinetData.Specialization(fadeId, "Фейд", category: ServiceCategory.Haircut),
        ]);

        harness.Search.HairServices = _ => Task.FromResult<IReadOnlyList<HairServiceResponse>>(
        [
            BarberCabinetData.HairService(
                BarberCabinetData.SecondHairServiceId, "Укладка", 20, ServiceCategory.Styling, stylingId),
            BarberCabinetData.HairService(
                Guid.Parse("9e100000-0000-4000-8000-0000000000f4"), "Стрижка", 30, ServiceCategory.Haircut, fadeId),
            BarberCabinetData.HairService(
                Guid.Parse("9e100000-0000-4000-8000-0000000000f5"), "Борода", 45, ServiceCategory.Beard, beardId),
        ]);

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())]);

        await harness.ViewModel.CatalogLoading;

        // Стрижка, борода, укладка: каталожный порядок категорий, а не алфавит «Барбер, Укладка, Фейд».
        Assert.Equal(
            ["Фейд", "Барбер", "Укладка"],
            harness.ViewModel.CatalogGroups.Select(group => group.Title));
    }

    [Fact]
    public async Task InitializeForAdd_ServiceWithUnknownSpecialization_EndsInOtherServicesGroup()
    {
        var harness = new Harness();

        harness.Search.HairServices = _ => Task.FromResult<IReadOnlyList<HairServiceResponse>>(
        [
            BarberCabinetData.HairService(
                BarberCabinetData.SecondHairServiceId,
                "Борода",
                45,
                ServiceCategory.Beard,
                Guid.Parse("9e100000-0000-4000-8000-0000000000f9")),
        ]);

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())]);

        await harness.ViewModel.CatalogLoading;

        var group = Assert.Single(harness.ViewModel.CatalogGroups);

        Assert.Equal("Прочие услуги", group.Title);
        Assert.Equal("Борода", Assert.Single(group.Services).Name);
    }

    [Fact]
    public async Task SelectService_MarksOnlyChosenRowAndRevealsForm()
    {
        var harness = new Harness();

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())]);

        await harness.ViewModel.CatalogLoading;

        var viewModel = harness.ViewModel;
        var service = OnlyCatalogService(viewModel);

        Assert.False(viewModel.IsFormVisible);

        viewModel.SelectedCatalogService = service;

        Assert.True(service.IsSelected);
        Assert.True(viewModel.IsFormVisible);
        Assert.Equal("Добавить", viewModel.SaveButtonText);
        Assert.Single(viewModel.CatalogGroups);
    }

    [Fact]
    public async Task SelectService_AnotherChoiceMovesTheHighlight()
    {
        var harness = new Harness();

        harness.Search.HairServices = _ => Task.FromResult<IReadOnlyList<HairServiceResponse>>(
        [
            BarberCabinetData.HairService(
                BarberCabinetData.SecondHairServiceId, "Фейд", 45, ServiceCategory.Haircut),
            BarberCabinetData.HairService(
                Guid.Parse("9e100000-0000-4000-8000-0000000000f7"), "Классика", 30, ServiceCategory.Haircut),
        ]);

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())]);

        await harness.ViewModel.CatalogLoading;

        var viewModel = harness.ViewModel;
        var services = viewModel.CatalogGroups[0].Services;

        viewModel.SelectedCatalogService = services[0];
        viewModel.SelectedCatalogService = services[1];

        Assert.False(services[0].IsSelected);
        Assert.True(services[1].IsSelected);
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

        Assert.Empty(harness.ViewModel.CatalogGroups);
        Assert.True(harness.ViewModel.IsCatalogEmpty);
    }

    [Fact]
    public async Task CatalogFailure_ShowsReasonAndRetryReadsBothCataloguesAgain()
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
        Assert.True(viewModel.HasCatalogError);
        Assert.Empty(viewModel.CatalogGroups);

        await viewModel.RetryCatalogCommand.ExecuteAsync(null);

        Assert.Null(viewModel.CatalogError);
        Assert.False(viewModel.HasCatalogError);
        Assert.Equal(2, calls);

        // Заголовки групп приходят вторым справочником: без него ни одной группы не собрать,
        // поэтому повтор спрашивает оба.
        Assert.Equal(2, harness.Search.SpecializationCalls);
        Assert.Single(OnlyGroup(viewModel).Services);
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
        Assert.True(viewModel.HasFormError);
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
            [BarberCabinetData.SecondSpecializationId]);

        await harness.ViewModel.CatalogLoading;

        var viewModel = harness.ViewModel;

        viewModel.Saved += (_, text) => announcement = text;
        viewModel.SelectedCatalogService = OnlyCatalogService(viewModel);

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
        viewModel.SelectedCatalogService = OnlyCatalogService(viewModel);
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

        viewModel.SelectedCatalogService = OnlyCatalogService(viewModel);
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

        viewModel.SelectedCatalogService = OnlyCatalogService(viewModel);
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

        // Правка не выбирает услугу из справочника: карточка цены стоит сразу и называется
        // «Сохранить», а не «Добавить».
        Assert.True(viewModel.IsFormVisible);
        Assert.Equal("Сохранить", viewModel.SaveButtonText);
        Assert.Empty(viewModel.CatalogGroups);
        Assert.Equal(0, harness.Search.HairServiceCalls);
        Assert.Equal(0, harness.Search.SpecializationCalls);

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

        // Строки ошибок показываются флагом, а не «значение не null»: пустая подпись
        // занимает место в карточке и раздвигает её.
        Assert.True(viewModel.HasPriceError);
        Assert.True(viewModel.HasDurationError);
        Assert.False(viewModel.HasFormError);
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

    [Fact]
    public async Task ServiceQuery_Substring_FiltersRowsOfMatchingGroupAndHidesOthers()
    {
        var harness = await LoadThreeGroupCatalogAsync();

        var viewModel = harness.ViewModel;
        var fadeGroup = viewModel.CatalogGroups[0];
        var beardGroup = viewModel.CatalogGroups[1];
        var stylingGroup = viewModel.CatalogGroups[2];

        viewModel.ServiceQuery = "фейд";

        Assert.True(viewModel.HasServiceQuery);
        Assert.True(viewModel.IsServiceSearchVisible);

        // Строка группы остаётся прежним экземпляром: фильтр не пересоздаёт справочник,
        // а показывает снимок API-порядка целиком или только его совпадения.
        Assert.Same(fadeGroup.Services[0], Assert.Single(fadeGroup.VisibleServices));
        Assert.Equal(2, fadeGroup.Services.Count);

        Assert.False(beardGroup.HasMatches);
        Assert.Empty(beardGroup.VisibleServices);
        Assert.False(stylingGroup.HasMatches);
        Assert.Empty(stylingGroup.VisibleServices);
    }

    [Fact]
    public async Task ServiceQuery_MatchCount_CountsOnlyVisibleRows()
    {
        var harness = await LoadThreeGroupCatalogAsync();

        var viewModel = harness.ViewModel;
        var fadeGroup = viewModel.CatalogGroups[0];
        var beardGroup = viewModel.CatalogGroups[1];

        viewModel.ServiceQuery = "классика";

        // В группе две строки, но под запрос подходит одна: счётчик обещает только видимое.
        Assert.Equal(2, fadeGroup.Services.Count);
        Assert.Equal(1, fadeGroup.MatchCount);
        Assert.Equal("1", fadeGroup.CountText);
        Assert.True(fadeGroup.HasMatches);

        Assert.Equal(0, beardGroup.MatchCount);
        Assert.Equal("0", beardGroup.CountText);
    }

    [Fact]
    public async Task ServiceQuery_NoMatch_SetsHasNoMatchesButKeepsCatalogueNonEmpty()
    {
        var harness = await LoadThreeGroupCatalogAsync();

        var viewModel = harness.ViewModel;

        viewModel.ServiceQuery = "шарлотка";

        Assert.True(viewModel.HasNoMatches);

        // «Ничего не найдено» — не пустой справочник: группы на месте, их просто спрятал фильтр.
        Assert.False(viewModel.IsCatalogEmpty);
        Assert.True(viewModel.IsServiceSearchVisible);
        Assert.Equal(3, viewModel.CatalogGroups.Count);
        Assert.All(viewModel.CatalogGroups, group => Assert.False(group.HasMatches));
    }

    [Fact]
    public async Task ClearServiceQueryCommand_NoMatch_RestoresRowsAndClearsHasNoMatches()
    {
        var harness = await LoadThreeGroupCatalogAsync();

        var viewModel = harness.ViewModel;

        viewModel.ServiceQuery = "шарлотка";

        Assert.True(viewModel.HasNoMatches);

        viewModel.ClearServiceQueryCommand.Execute(null);

        Assert.Equal(string.Empty, viewModel.ServiceQuery);
        Assert.False(viewModel.HasServiceQuery);
        Assert.False(viewModel.HasNoMatches);
        Assert.All(viewModel.CatalogGroups, group =>
        {
            Assert.True(group.HasMatches);
            Assert.Equal(group.Services.Count, group.MatchCount);
            Assert.Equal(group.Services, group.VisibleServices);
        });
    }

    [Fact]
    public async Task ServiceQuery_SpacesOnly_LeavesAllGroupsVisible()
    {
        var harness = await LoadThreeGroupCatalogAsync();

        var viewModel = harness.ViewModel;

        viewModel.ServiceQuery = "   ";

        // Пробелы набраны — «×» видна, но искать нечего: фильтр пропускает весь справочник.
        Assert.True(viewModel.HasServiceQuery);
        Assert.False(viewModel.HasNoMatches);
        Assert.True(viewModel.IsServiceSearchVisible);
        Assert.All(viewModel.CatalogGroups, group =>
        {
            Assert.True(group.HasMatches);
            Assert.Equal(group.Services, group.VisibleServices);
        });
    }

    [Fact]
    public async Task InitializeForAdd_DefaultExpansion_OpensOnlyFirstGroup()
    {
        var harness = await LoadThreeGroupCatalogAsync();

        var groups = harness.ViewModel.CatalogGroups;

        Assert.True(groups[0].IsExpanded);
        Assert.False(groups[1].IsExpanded);
        Assert.False(groups[2].IsExpanded);
    }

    [Fact]
    public async Task SelectCatalogService_OpensItsGroupWithoutTouchingOthers()
    {
        var harness = await LoadThreeGroupCatalogAsync();

        var viewModel = harness.ViewModel;
        var groups = viewModel.CatalogGroups;

        viewModel.SelectedCatalogService = groups[2].Services[0];

        // Выбор раскрывает свою группу: подсвеченная строка не должна остаться под закрытым
        // заголовком. Первую группу он не закрывает, вторую не открывает.
        Assert.True(groups[2].IsExpanded);
        Assert.True(groups[0].IsExpanded);
        Assert.False(groups[1].IsExpanded);
        Assert.Equal("20", viewModel.DurationText);
    }

    [Fact]
    public async Task ToggleGroupCommand_ClosesAndReopensOneGroupAndLeavesAnother()
    {
        var harness = await LoadThreeGroupCatalogAsync();

        var viewModel = harness.ViewModel;
        var groups = viewModel.CatalogGroups;

        viewModel.ToggleGroupCommand.Execute(groups[0]);

        Assert.False(groups[0].IsExpanded);
        Assert.False(groups[1].IsExpanded);

        viewModel.ToggleGroupCommand.Execute(groups[0]);

        Assert.True(groups[0].IsExpanded);

        viewModel.ToggleGroupCommand.Execute(groups[1]);

        Assert.True(groups[1].IsExpanded);
        Assert.True(groups[0].IsExpanded);
    }

    [Fact]
    public async Task ServiceQuery_OpensEveryGroupWithMatches()
    {
        var harness = await LoadThreeGroupCatalogAsync();

        var viewModel = harness.ViewModel;
        var groups = viewModel.CatalogGroups;

        // Мастер свернул открытую по умолчанию группу: поиск обязан открыть её снова,
        // если в ней есть совпадение, — и так же поступить со второй совпавшей группой.
        viewModel.ToggleGroupCommand.Execute(groups[0]);

        Assert.False(groups[0].IsExpanded);

        viewModel.ServiceQuery = "ка";

        Assert.True(groups[0].HasMatches);
        Assert.True(groups[2].HasMatches);
        Assert.True(groups[0].IsExpanded);
        Assert.True(groups[2].IsExpanded);
        Assert.False(groups[1].HasMatches);
        Assert.False(groups[1].IsExpanded);
    }

    [Fact]
    public async Task ClearServiceQuery_RestoresExpansionFromBeforeSearch()
    {
        var harness = await LoadThreeGroupCatalogAsync();

        var viewModel = harness.ViewModel;
        var groups = viewModel.CatalogGroups;

        // Мастер открыл «Бороду» и свернул открытую по умолчанию «Фейд»: после поиска он
        // должен вернуться к своему виду, а не к виду по умолчанию.
        viewModel.ToggleGroupCommand.Execute(groups[1]);
        viewModel.ToggleGroupCommand.Execute(groups[0]);

        viewModel.ServiceQuery = "ка";

        Assert.True(groups[0].IsExpanded);
        Assert.True(groups[2].IsExpanded);

        viewModel.ClearServiceQueryCommand.Execute(null);

        Assert.False(groups[0].IsExpanded);
        Assert.True(groups[1].IsExpanded);
        Assert.False(groups[2].IsExpanded);
    }

    [Fact]
    public async Task ServiceQuery_KeepsSelectedServiceAndDuration()
    {
        var harness = await LoadThreeGroupCatalogAsync();

        var viewModel = harness.ViewModel;
        var chosen = viewModel.CatalogGroups[2].Services[0];

        viewModel.SelectedCatalogService = chosen;

        Assert.Equal("20", viewModel.DurationText);

        // Запрос прячет группу выбранной услуги: выбор и длительность остаются на месте,
        // меняется только список под карточкой цены.
        viewModel.ServiceQuery = "фейд";

        Assert.Same(chosen, viewModel.SelectedCatalogService);
        Assert.Equal("20", viewModel.DurationText);
        Assert.True(chosen.IsSelected);

        viewModel.ServiceQuery = "уклад";

        Assert.Same(chosen, viewModel.SelectedCatalogService);
        Assert.Equal("20", viewModel.DurationText);
    }

    /// <summary>
    /// Единственная группа выдачи: тесты с одной специализацией не перечисляют её по имени.
    /// </summary>
    /// <param name="viewModel">Редактор с прочитанным справочником.</param>
    /// <returns>Группа справочника.</returns>
    private static BarberServiceCatalogGroup OnlyGroup(BarberServiceEditorViewModel viewModel) =>
        Assert.Single(viewModel.CatalogGroups);

    /// <summary>
    /// Единственная доступная услуга справочника.
    /// </summary>
    /// <param name="viewModel">Редактор с прочитанным справочником.</param>
    /// <returns>Строка справочника.</returns>
    private static BarberServiceCatalogItem OnlyCatalogService(BarberServiceEditorViewModel viewModel) =>
        Assert.Single(OnlyGroup(viewModel).Services);

    /// <summary>
    /// Справочник из трёх групп: «Фейд» с двумя услугами, «Борода» и «Укладка».
    /// </summary>
    /// <remarks>
    /// Порядок групп каталожный: стрижка, борода, укладка. Две строки в первой группе нужны,
    /// чтобы фильтр мог спрятать одну из них, не пряча заголовок, а три группы — чтобы видеть,
    /// что раскрытие и поиск не трогают соседние направления.
    /// </remarks>
    /// <returns>Собранный редактор с прочитанным справочником.</returns>
    private static async Task<Harness> LoadThreeGroupCatalogAsync()
    {
        var fadeSpecializationId = Guid.Parse("9e100000-0000-4000-8000-0000000000d1");
        var beardSpecializationId = Guid.Parse("9e100000-0000-4000-8000-0000000000d2");
        var stylingSpecializationId = Guid.Parse("9e100000-0000-4000-8000-0000000000d3");
        var fadeServiceId = Guid.Parse("9e100000-0000-4000-8000-0000000000d4");
        var classicServiceId = Guid.Parse("9e100000-0000-4000-8000-0000000000d5");
        var beardServiceId = Guid.Parse("9e100000-0000-4000-8000-0000000000d6");
        var stylingServiceId = Guid.Parse("9e100000-0000-4000-8000-0000000000d7");

        var harness = new Harness();

        harness.Search.Specializations = _ => Task.FromResult<IReadOnlyList<SpecializationResponse>>(
        [
            BarberCabinetData.Specialization(
                fadeSpecializationId, "Фейд", category: ServiceCategory.Haircut),
            BarberCabinetData.Specialization(
                beardSpecializationId, "Борода", category: ServiceCategory.Beard),
            BarberCabinetData.Specialization(
                stylingSpecializationId, "Укладка", category: ServiceCategory.Styling),
        ]);

        harness.Search.HairServices = _ => Task.FromResult<IReadOnlyList<HairServiceResponse>>(
        [
            BarberCabinetData.HairService(
                fadeServiceId, "Фейд", 30, ServiceCategory.Haircut, fadeSpecializationId),
            BarberCabinetData.HairService(
                classicServiceId, "Классика", 40, ServiceCategory.Haircut, fadeSpecializationId),
            BarberCabinetData.HairService(
                beardServiceId, "Борода", 45, ServiceCategory.Beard, beardSpecializationId),
            BarberCabinetData.HairService(
                stylingServiceId, "Укладка", 20, ServiceCategory.Styling, stylingSpecializationId),
        ]);

        harness.ViewModel.InitializeForAdd(
            BarberCabinetFixtures.BarberProfileId,
            [new BarberServiceItem(BarberCabinetFixtures.Service())]);

        await harness.ViewModel.CatalogLoading;

        return harness;
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
