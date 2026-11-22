using System.Net;
using BarberSearch.Contracts.Salons.GetSalonDirectory;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки страницы «Добавить салон»: поиск по публичному каталогу с паузой, привязка
/// первого салона как основного и устаревшая привязка, которая не трогает скрытый список.
/// </summary>
/// <remarks>
/// Пауза каталога приходит делегатом, поэтому debounce проверяется без ожидания таймера.
/// Страница не читает список «Мои салоны»: после успеха она объявляет результат и
/// закрывается, а раздел перечитывает список на Pop. Запись привязки сама не повторяется.
/// </remarks>
public sealed class BarberSalonAddViewModelTests
{
    [Fact]
    public async Task CatalogQuery_BelowMinimumShowsHintAndOnlyTwoCharsSearch()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness(
            delay: (_, cancellationToken) => gate.Task.WaitAsync(cancellationToken));

        var viewModel = harness.ViewModel;

        viewModel.CatalogQuery = "H";

        Assert.False(viewModel.CanSearchCatalog);
        Assert.Contains("минимум 2", viewModel.CatalogHint, StringComparison.Ordinal);
        Assert.False(viewModel.IsCatalogLoading);
        Assert.Empty(harness.Search.SalonQueries);

        viewModel.CatalogQuery = "Ha";

        Assert.True(viewModel.CanSearchCatalog);
        Assert.Equal(string.Empty, viewModel.CatalogHint);
        Assert.True(viewModel.IsCatalogLoading);
        Assert.Empty(harness.Search.SalonQueries);

        gate.SetResult();

        await viewModel.CatalogOperation;

        var query = Assert.Single(harness.Search.SalonQueries);

        Assert.Equal(string.Empty, query.City);
        Assert.Equal("Ha", query.Search);
        Assert.False(viewModel.IsCatalogLoading);
        Assert.True(viewModel.ShowCatalogResults);
        Assert.Equal("Hairo", viewModel.Catalog[0].Name);
        Assert.Equal("Барбершоп", viewModel.Catalog[0].KindText);
        Assert.False(viewModel.CanAddSalon);
    }

    [Fact]
    public void Initialize_EmptySection_FirstSalonBecomesPrimary()
    {
        var empty = new Harness();
        var viewModel = empty.ViewModel;

        Assert.True(viewModel.FirstSalonBecomesPrimary);
        Assert.Equal("Первый салон станет основным", viewModel.PrimaryToggleText);

        var filled = new Harness(hasSalons: true);

        Assert.False(filled.ViewModel.FirstSalonBecomesPrimary);
        Assert.Equal("Сделать основным", filled.ViewModel.PrimaryToggleText);
    }

    [Fact]
    public async Task AddSalonAsync_FirstSalon_IsAttachedAsPrimary()
    {
        var harness = new Harness();

        var viewModel = harness.ViewModel;
        string? attached = null;

        viewModel.Attached += (_, message) => attached = message;

        viewModel.CatalogQuery = "Ha";

        await viewModel.CatalogOperation;

        viewModel.SelectedCatalogSalon = viewModel.Catalog[0];

        Assert.True(viewModel.FirstSalonBecomesPrimary);
        Assert.True(viewModel.CanAddSalon);

        await viewModel.AddSalonCommand.ExecuteAsync(null);

        var attach = Assert.Single(harness.Profile.SalonAttaches);

        Assert.Equal(BarberCabinetFixtures.BarberProfileId, attach.ProfileId);
        Assert.Equal(BarberCabinetFixtures.OtherSalonId, attach.SalonId);
        Assert.True(attach.IsPrimary);
        Assert.Equal("Салон добавлен.", attached);
        Assert.Null(viewModel.SelectedCatalogSalon);
        Assert.Empty(viewModel.Catalog);
        Assert.False(viewModel.IsAddingSalon);
        Assert.Equal(0, harness.Profile.SalonCalls);
    }

    [Fact]
    public async Task AddSalonAsync_Duplicate_ShowsReasonWithoutReload()
    {
        var harness = new Harness(hasSalons: true);

        var viewModel = harness.ViewModel;
        var attachedCount = 0;

        viewModel.Attached += (_, _) => attachedCount++;

        viewModel.CatalogQuery = "Ha";

        await viewModel.CatalogOperation;

        viewModel.SelectedCatalogSalon = viewModel.Catalog[0];

        harness.Profile.AddSalon = (_, _, _) => Task.FromException(
            BarberCabinetApiError.Create(
                HttpStatusCode.Conflict,
                "barberProfile.addSalon.duplicate"));

        await viewModel.AddSalonCommand.ExecuteAsync(null);

        Assert.Equal("Этот салон уже добавлен.", viewModel.AddSalonError);
        Assert.True(viewModel.HasAddSalonError);
        Assert.Equal(0, attachedCount);
        Assert.NotNull(viewModel.SelectedCatalogSalon);
        Assert.Single(viewModel.Catalog);
        Assert.False(viewModel.IsAddingSalon);
        Assert.Equal(0, harness.Profile.SalonCalls);
    }

    [Fact]
    public async Task SelectedCatalogSalon_TogglesRadioMarker()
    {
        var harness = new Harness();

        var viewModel = harness.ViewModel;

        viewModel.CatalogQuery = "Ha";

        await viewModel.CatalogOperation;

        var item = viewModel.Catalog[0];

        Assert.False(item.IsSelected);

        viewModel.SelectedCatalogSalon = item;

        Assert.True(item.IsSelected);

        viewModel.SelectedCatalogSalon = null;

        Assert.False(item.IsSelected);
    }

    [Fact]
    public async Task CatalogFailure_RetriesSameQueryUntilLoaded()
    {
        var harness = new Harness(catalogRetry: BarberCabinetRetry.Fast());
        var calls = 0;

        harness.Search.Salons = (_, _, _) =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<IReadOnlyList<SalonDirectoryItemResponse>>(
                    new HttpRequestException("offline"))
                : Task.FromResult<IReadOnlyList<SalonDirectoryItemResponse>>(
                    [BarberCabinetData.DirectorySalon()]);
        };

        var viewModel = harness.ViewModel;

        viewModel.StartAutoRetry();

        viewModel.CatalogQuery = "Ha";

        await viewModel.CatalogOperation;
        await viewModel.CatalogRetryCompletion;

        Assert.Equal(2, calls);
        Assert.Equal(2, harness.Search.SalonQueries.Count);
        Assert.All(
            harness.Search.SalonQueries,
            query => Assert.Equal("Ha", query.Search));
        Assert.Null(viewModel.CatalogError);
        Assert.True(viewModel.ShowCatalogResults);
        Assert.False(viewModel.IsCatalogAutoRetrying);
    }

    /// <summary>
    /// Собранная страница: подменённые клиенты, пауза каталога и цикл повтора.
    /// </summary>
    private sealed class Harness
    {
        public Harness(
            bool hasSalons = false,
            Func<TimeSpan, CancellationToken, Task>? delay = null,
            AutoRetryLoop? catalogRetry = null)
        {
            ViewModel = new BarberSalonAddViewModel(
                Profile,
                Search,
                delay ?? (static (_, _) => Task.CompletedTask),
                catalogRetry);

            ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId, hasSalons);
        }

        public FakeBarberProfileApi Profile { get; } = new();

        public FakeBarberSearchApi Search { get; } = new();

        public BarberSalonAddViewModel ViewModel { get; }
    }
}
