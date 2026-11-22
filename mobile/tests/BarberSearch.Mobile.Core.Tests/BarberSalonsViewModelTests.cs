using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfileSalons;
using BarberSearch.Domain.Salons;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки раздела «Салоны»: рабочий список со статусами модерации и основной салон.
/// </summary>
/// <remarks>
/// Записи (смена основного) не повторяются: после сбоя исход неизвестен. Поиск нового
/// салона и привязка живут на отдельной странице и проверяются в
/// <see cref="BarberSalonAddViewModelTests"/>.
/// </remarks>
public sealed class BarberSalonsViewModelTests
{
    private const string EmptyTitleText = "Салон не добавлен";

    private const string EmptyDescriptionText =
        "Без салона расписание и запись не работают.";

    [Fact]
    public async Task ActivateAsync_LoadsSalonsInServerOrderWithPrimaryFlag()
    {
        var harness = new Harness();

        harness.Profile.Salons = _ => Task.FromResult<IReadOnlyList<BarberSalonSummaryResponse>>(
        [
            BarberCabinetData.SalonSummary(isPrimary: true),
            BarberCabinetData.SalonSummary(
                BarberCabinetFixtures.OtherSalonId,
                "Chop-Chop",
                "Тверь",
                kind: PlaceKind.RentalStudio),
        ]);

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(1, harness.Profile.SalonCalls);
        Assert.True(viewModel.HasLoaded);
        Assert.True(viewModel.ShowContent);
        Assert.True(viewModel.HasSalons);
        Assert.Equal(2, viewModel.Salons.Count);
        Assert.Equal(BarberCabinetFixtures.SalonId, viewModel.Salons[0].SalonId);
        Assert.True(viewModel.Salons[0].IsPrimary);
        Assert.Equal("Основной", viewModel.Salons[0].PrimaryBadgeText);
        Assert.Equal(string.Empty, viewModel.Salons[1].PrimaryBadgeText);
        Assert.Equal("Москва, Ленина, 1", viewModel.Salons[0].FullAddress);
        Assert.True(viewModel.Salons[0].HasAddress);
        Assert.True(viewModel.Salons[0].HasPhoneNumber);
        Assert.True(viewModel.Salons[0].HasCoordinates);
        Assert.Equal("Часовой пояс: Europe/Moscow", viewModel.Salons[0].TimeZoneText);
        Assert.Equal("Барбершоп", viewModel.Salons[0].KindText);
        Assert.Equal("Студия", viewModel.Salons[1].KindText);
    }

    [Fact]
    public async Task ActivateAsync_ShowsModerationStatusesAndNotListedPlace()
    {
        var harness = new Harness();

        harness.Profile.Salons = _ => Task.FromResult<IReadOnlyList<BarberSalonSummaryResponse>>(
        [
            BarberCabinetData.SalonSummary(status: SalonModerationStatus.Active),
            BarberCabinetData.SalonSummary(
                BarberCabinetFixtures.OtherSalonId,
                status: SalonModerationStatus.Pending),
            BarberCabinetData.SalonSummary(
                Guid.Parse("9e100000-0000-4000-8000-0000000000d2"),
                status: SalonModerationStatus.Rejected,
                rejectionReason: "Мало места"),
            BarberCabinetData.SalonSummary(
                Guid.Parse("9e100000-0000-4000-8000-0000000000d3"),
                isListed: false),
        ]);

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal("Одобрен", viewModel.Salons[0].StatusText);
        Assert.Equal("approved", viewModel.Salons[0].StatusKind);
        Assert.Equal(string.Empty, viewModel.Salons[0].PendingHint);

        Assert.Equal("На проверке", viewModel.Salons[1].StatusText);
        Assert.Equal("pending", viewModel.Salons[1].StatusKind);
        Assert.Equal(
            "Салон появится в поиске после проверки администратором.",
            viewModel.Salons[1].PendingHint);

        Assert.Equal("Отклонён", viewModel.Salons[2].StatusText);
        Assert.True(viewModel.Salons[2].IsRejected);
        Assert.Equal("Причина отказа: Мало места", viewModel.Salons[2].RejectionReasonText);

        Assert.Equal("Не в каталоге", viewModel.Salons[3].StatusText);
        Assert.Equal("notlisted", viewModel.Salons[3].StatusKind);
        Assert.True(viewModel.Salons[3].IsNotListed);
        Assert.False(viewModel.Salons[3].IsRejected);
        Assert.Equal(string.Empty, viewModel.Salons[3].RejectionReasonText);
    }

    [Fact]
    public async Task ActivateAsync_EmptyList_ShowsEmptyState()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Empty(viewModel.Salons);
        Assert.True(viewModel.HasLoaded);
        Assert.True(viewModel.ShowEmpty);
        Assert.False(viewModel.ShowContent);
        Assert.False(viewModel.ShowError);
        Assert.Equal(EmptyTitleText, viewModel.EmptyTitle);
        Assert.Equal(EmptyDescriptionText, viewModel.EmptyDescription);
    }

    [Fact]
    public async Task ActivateAsync_TransportFailure_RetriesUntilLoaded()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness(salonsRetry: BarberCabinetRetry.Gated(gate.Task));
        var calls = 0;

        harness.Profile.Salons = _ =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<IReadOnlyList<BarberSalonSummaryResponse>>(
                    new HttpRequestException("offline"))
                : Task.FromResult<IReadOnlyList<BarberSalonSummaryResponse>>(
                    [BarberCabinetData.SalonSummary()]);
        };

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            viewModel.ErrorMessage);
        Assert.True(viewModel.ShowError);
        Assert.True(viewModel.IsAutoRetrying);

        gate.SetResult();

        await viewModel.SalonsRetryCompletion;

        Assert.Equal(2, calls);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Single(viewModel.Salons);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task SetPrimaryAsync_SendsSalonReloadsAndAnnounces()
    {
        var harness = new Harness();

        harness.Profile.Salons = _ => Task.FromResult<IReadOnlyList<BarberSalonSummaryResponse>>(
        [
            BarberCabinetData.SalonSummary(isPrimary: true),
            BarberCabinetData.SalonSummary(BarberCabinetFixtures.OtherSalonId, "Chop-Chop"),
        ]);

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.SetPrimaryCommand.ExecuteAsync(viewModel.Salons[1]);

        var request = Assert.Single(harness.Profile.PrimaryRequests);

        Assert.Equal(BarberCabinetFixtures.OtherSalonId, request.SalonId);
        Assert.Equal("Основной салон изменён.", viewModel.Announcement);
        Assert.True(viewModel.HasAnnouncement);
        Assert.Null(viewModel.ActionError);
        Assert.False(viewModel.IsSettingPrimary);
        Assert.Equal(2, harness.Profile.SalonCalls);
    }

    [Fact]
    public async Task SetPrimaryAsync_AlreadyPrimary_SendsNothing()
    {
        var harness = new Harness();

        harness.Profile.Salons = _ => Task.FromResult<IReadOnlyList<BarberSalonSummaryResponse>>(
            [BarberCabinetData.SalonSummary(isPrimary: true)]);

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.SetPrimaryCommand.ExecuteAsync(viewModel.Salons[0]);

        Assert.Empty(harness.Profile.PrimaryRequests);
        Assert.Null(viewModel.Announcement);
    }

    [Fact]
    public async Task SetPrimaryAsync_NotFound_ShowsReasonAndReloads()
    {
        var harness = new Harness();

        harness.Profile.Salons = _ => Task.FromResult<IReadOnlyList<BarberSalonSummaryResponse>>(
        [
            BarberCabinetData.SalonSummary(isPrimary: true),
            BarberCabinetData.SalonSummary(BarberCabinetFixtures.OtherSalonId, "Chop-Chop"),
        ]);

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        harness.Profile.SetPrimarySalon = (_, _) => Task.FromException(
            BarberCabinetApiError.Create(HttpStatusCode.NotFound, "barberProfile.setPrimary.notFound"));

        await viewModel.SetPrimaryCommand.ExecuteAsync(viewModel.Salons[1]);

        Assert.Equal("Салон больше не доступен.", viewModel.ActionError);
        Assert.True(viewModel.HasActionError);
        Assert.Equal(2, harness.Profile.SalonCalls);
    }

    /// <summary>
    /// Собранный раздел: подменённый клиент профиля и цикл повтора списка.
    /// </summary>
    private sealed class Harness
    {
        public Harness(AutoRetryLoop? salonsRetry = null)
        {
            ViewModel = new BarberSalonsViewModel(
                Profile,
                salonsRetry);

            ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);
        }

        public FakeBarberProfileApi Profile { get; } = new();

        public BarberSalonsViewModel ViewModel { get; }
    }
}
