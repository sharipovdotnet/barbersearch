using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Domain.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Media;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки раздела «Профиль» кабинета: карточка, баннер проверки, прайс, специализации,
/// ленивый справочник и автоповтор чтения.
/// </summary>
/// <remarks>
/// Фотографии в этом срезе только читаются, поэтому проверяется их состояние, а не команды.
/// Записи (скрытие услуги, специализация) не повторяются: после сбоя исход неизвестен.
/// </remarks>
public sealed class BarberCabinetProfileViewModelTests
{
    private const string NetworkText =
        "Нет связи с сервером. Проверьте подключение и повторите попытку.";

    private const string CatalogFailedText =
        "Не удалось загрузить справочник специализаций.";

    [Fact]
    public async Task ActivateAsync_LoadsAndFormatsCardFields()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(1, harness.Profile.OwnCalls);
        Assert.True(viewModel.IsReady);
        Assert.False(viewModel.IsFirstLoading);
        Assert.False(viewModel.IsRefreshing);
        Assert.Equal("Тимур Мастеров", viewModel.DisplayName);
        Assert.Equal("ТМ", viewModel.Initials);
        Assert.Equal("Старший мастер", viewModel.GradeText);
        Assert.Equal("Стригу с 2014 года.", viewModel.Bio);
        Assert.Equal("8 лет", viewModel.ExperienceText);
        Assert.Equal("4,8", viewModel.RatingText);
        Assert.Equal("12 отзывов", viewModel.ReviewsText);
        Assert.Equal("Русский, English", viewModel.LanguagesText);
        Assert.Equal("Наличные, СБП", viewModel.PaymentMethodsText);
        Assert.Equal("Опаздывающих жду 10 минут.", viewModel.LatePolicyText);
        Assert.StartsWith("Обновлено в ", viewModel.UpdatedAtText, StringComparison.Ordinal);
        Assert.True(viewModel.IsVerified);
        Assert.True(viewModel.IsActive);
    }

    [Fact]
    public async Task ActivateAsync_Unverified_ShowsHintAndBlocksPreview()
    {
        var harness = new Harness();
        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(isVerified: false));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.False(viewModel.IsVerified);
        Assert.Equal(
            "Профиль на проверке. Заполняйте карточку — она появится в поиске после подтверждения администратором.",
            viewModel.VerificationHint);
        Assert.False(viewModel.CanPreviewPublic);
    }

    [Fact]
    public async Task ActivateAsync_VerifiedAndActive_AllowsPreviewWithoutHint()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        Assert.True(harness.ViewModel.CanPreviewPublic);
        Assert.Equal(string.Empty, harness.ViewModel.VerificationHint);
    }

    [Fact]
    public async Task ActivateAsync_LoadsHiddenServiceAndPhotoStates()
    {
        var harness = new Harness();

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(
            services:
            [
                BarberCabinetFixtures.Service(),
                BarberCabinetFixtures.Service(1800m, 45) with { IsActive = false },
            ],
            specializations: [BarberCabinetData.ProfileSpecialization()],
            photos:
            [
                BarberCabinetData.Photo("Работа"),
                BarberCabinetData.Photo(
                    isBlocked: true,
                    status: PhotoModerationStatus.Pending),
            ]));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(2, viewModel.Services.Count);
        Assert.True(viewModel.Services[0].IsActive);
        Assert.True(viewModel.Services[1].IsHidden);
        Assert.Equal("Скрыта", viewModel.Services[1].StatusText);
        Assert.True(viewModel.HasServices);

        Assert.Single(viewModel.Specializations);
        Assert.Equal("Фейд", viewModel.Specializations[0].Name);
        Assert.True(viewModel.HasSpecializations);

        Assert.Equal(2, viewModel.Photos.Count);
        Assert.Equal("Опубликовано", viewModel.Photos[0].StatusText);
        Assert.Equal("Скрыто администратором", viewModel.Photos[1].StatusText);
        Assert.True(viewModel.HasPhotos);
    }

    [Fact]
    public async Task HideServiceAsync_SendsDeleteAndReloadsCard()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;
        var item = viewModel.Services[0];

        await viewModel.HideServiceCommand.ExecuteAsync(item);

        var removal = Assert.Single(harness.Profile.ServiceRemovals);

        Assert.Equal(BarberCabinetFixtures.BarberProfileId, removal.ProfileId);
        Assert.Equal(BarberCabinetFixtures.HairServiceId, removal.HairServiceId);
        Assert.Null(viewModel.ActionError);
        Assert.False(viewModel.IsMutating);
        Assert.Equal(2, harness.Profile.OwnCalls);
    }

    [Fact]
    public async Task RestoreServiceAsync_SendsCurrentPriceAndDuration()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(
            services: [BarberCabinetFixtures.Service(1800m, 45) with { IsActive = false }]));

        await viewModel.ActivateAsync();

        var hidden = viewModel.Services[0];

        await viewModel.RestoreServiceCommand.ExecuteAsync(hidden);

        var restore = Assert.Single(harness.Profile.ServiceAdds);

        Assert.Equal(BarberCabinetFixtures.HairServiceId, restore.Request.HairServiceId);
        Assert.Equal(1800m, restore.Request.Price);
        Assert.Equal(45, restore.Request.DurationMinutes);
    }

    [Fact]
    public async Task OpenSpecializationPicker_LoadsCatalogLazilyOnce()
    {
        var harness = new Harness();

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(
            specializations: [BarberCabinetData.ProfileSpecialization()]));

        harness.Search.Specializations = _ => Task.FromResult<IReadOnlyList<SpecializationResponse>>(
        [
            BarberCabinetData.Specialization(),
            BarberCabinetData.Specialization(BarberCabinetData.SecondSpecializationId, "Борода"),
        ]);

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(0, harness.Search.SpecializationCalls);

        await viewModel.OpenSpecializationPickerCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.Search.SpecializationCalls);
        Assert.True(viewModel.IsSpecializationPickerOpen);
        Assert.True(viewModel.HasAvailableSpecializations);
        Assert.Single(viewModel.AvailableSpecializations);
        Assert.Equal(
            BarberCabinetData.SecondSpecializationId,
            viewModel.AvailableSpecializations[0].Id);

        viewModel.CloseSpecializationPickerCommand.Execute(null);
        await viewModel.OpenSpecializationPickerCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.Search.SpecializationCalls);
    }

    [Fact]
    public async Task AddSpecializationAsync_AddsClosesPickerAndRefreshesCatalog()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.OpenSpecializationPickerCommand.ExecuteAsync(null);

        await viewModel.AddSpecializationCommand.ExecuteAsync(
            BarberCabinetData.SecondSpecializationId);

        var add = Assert.Single(harness.Profile.SpecializationAdds);

        Assert.Equal(BarberCabinetData.SecondSpecializationId, add.SpecializationId);
        Assert.False(viewModel.IsSpecializationPickerOpen);
        Assert.False(viewModel.IsMutating);
    }

    [Fact]
    public async Task AddSpecializationAsync_Duplicate_ShowsReason()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        harness.Profile.AddSpecialization = (_, _) => Task.FromException(
            BarberCabinetApiError.Create(
                HttpStatusCode.Conflict,
                "barberProfile.addSpecialization.duplicate"));

        await harness.ViewModel.AddSpecializationCommand.ExecuteAsync(
            BarberCabinetData.SecondSpecializationId);

        Assert.Equal(
            "Эта специализация уже добавлена.",
            harness.ViewModel.SpecializationError);
    }

    [Fact]
    public async Task ActivateAsync_TransportFailure_RetriesUntilLoaded()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness(BarberCabinetRetry.Gated(gate.Task));
        var calls = 0;

        harness.Profile.Own = _ =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<BarberProfileResponse>(new HttpRequestException("offline"))
                : Task.FromResult(BarberCabinetData.Profile());
        };

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(NetworkText, viewModel.ErrorMessage);
        Assert.True(viewModel.IsAutoRetrying);
        Assert.False(viewModel.IsReady);

        gate.SetResult();

        await viewModel.ReadRetryCompletion;

        Assert.Equal(2, calls);
        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.IsReady);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task ActivateAsync_Unauthorized_RequiresSignInWithoutRetry()
    {
        var harness = new Harness();

        harness.Profile.Own = _ => Task.FromException<BarberProfileResponse>(
            BarberCabinetApiError.Create(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsSignInRequired);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsReady);
        Assert.False(viewModel.IsAutoRetrying);
        Assert.Equal(0, harness.ViewModel.ReadAutoRetry.Attempts);
    }

    [Fact]
    public async Task SpecializationCatalog_TransportFailure_RetriesUntilLoaded()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness(catalogRetry: BarberCabinetRetry.Gated(gate.Task));
        var calls = 0;

        harness.Search.Specializations = _ =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<IReadOnlyList<SpecializationResponse>>(
                    new HttpRequestException("offline"))
                : Task.FromResult<IReadOnlyList<SpecializationResponse>>(
                    [BarberCabinetData.Specialization()]);
        };

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.OpenSpecializationPickerCommand.ExecuteAsync(null);

        Assert.Equal(NetworkText, viewModel.SpecializationError);
        Assert.True(viewModel.IsSpecializationAutoRetrying);

        gate.SetResult();
        await viewModel.SpecializationsRetryCompletion;

        Assert.Null(viewModel.SpecializationError);
        Assert.False(viewModel.IsSpecializationAutoRetrying);
        Assert.Equal(2, calls);
        Assert.Single(viewModel.AvailableSpecializations);
    }

    [Fact]
    public async Task SpecializationCatalog_ClosePicker_StopsAutoRetry()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness(catalogRetry: BarberCabinetRetry.Gated(gate.Task));

        harness.Search.Specializations = _ => Task.FromException<IReadOnlyList<SpecializationResponse>>(
            new HttpRequestException("offline"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.OpenSpecializationPickerCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSpecializationAutoRetrying);

        viewModel.CloseSpecializationPickerCommand.Execute(null);
        await viewModel.SpecializationsRetryCompletion;

        Assert.False(viewModel.IsSpecializationAutoRetrying);
        Assert.Equal(1, harness.Search.SpecializationCalls);

        gate.SetResult();
    }

    [Fact]
    public async Task SpecializationCatalog_ReturnToSection_ReadsCatalogAgain()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness(catalogRetry: BarberCabinetRetry.Gated(gate.Task));
        var calls = 0;

        harness.Search.Specializations = _ =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<IReadOnlyList<SpecializationResponse>>(
                    new HttpRequestException("offline"))
                : Task.FromResult<IReadOnlyList<SpecializationResponse>>(
                    [BarberCabinetData.Specialization()]);
        };

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.OpenSpecializationPickerCommand.ExecuteAsync(null);

        Assert.Equal(NetworkText, viewModel.SpecializationError);

        viewModel.Deactivate();
        await viewModel.SpecializationsRetryCompletion;

        gate.SetResult();
        await viewModel.ActivateAsync();

        Assert.Null(viewModel.SpecializationError);
        Assert.Equal(2, calls);
        Assert.Single(viewModel.AvailableSpecializations);
    }

    [Fact]
    public async Task SpecializationCatalog_TerminalFailure_DoesNotArmAutoRetry()
    {
        var harness = new Harness(catalogRetry: BarberCabinetRetry.Fast());

        harness.Search.Specializations = _ => Task.FromException<IReadOnlyList<SpecializationResponse>>(
            BarberCabinetApiError.Create(HttpStatusCode.BadRequest, "validation"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.OpenSpecializationPickerCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.Search.SpecializationCalls);
        Assert.False(viewModel.IsSpecializationAutoRetrying);
        Assert.NotNull(viewModel.SpecializationError);
    }

    [Fact]
    public void Initialize_ExposesProfileIdForThePhotoPage()
    {
        var harness = new Harness();

        Assert.Equal(BarberCabinetFixtures.BarberProfileId, harness.ViewModel.BarberProfileId);
    }

    [Fact]
    public async Task ActivateAsync_TenPhotos_ReachesLimitAndNotifies()
    {
        var harness = new Harness();
        var changed = new List<string?>();

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(
            photos: Photos(PortfolioPhotoLimits.MaxPhotos)));

        harness.ViewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await harness.ViewModel.ActivateAsync();

        Assert.True(harness.ViewModel.IsPhotoLimitReached);
        Assert.False(harness.ViewModel.CanAddPhoto);
        Assert.Contains(nameof(BarberCabinetProfileViewModel.CanAddPhoto), changed);
        Assert.Contains(nameof(BarberCabinetProfileViewModel.IsPhotoLimitReached), changed);
    }

    [Fact]
    public async Task ActivateAsync_NinePhotos_AllowsAdding()
    {
        var harness = new Harness();

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(
            photos: Photos(PortfolioPhotoLimits.MaxPhotos - 1)));

        await harness.ViewModel.ActivateAsync();

        Assert.False(harness.ViewModel.IsPhotoLimitReached);
        Assert.True(harness.ViewModel.CanAddPhoto);
    }

    [Fact]
    public async Task ActivateAsync_FilledPortfolio_ReevaluatesLimit()
    {
        var harness = new Harness();
        var changed = new List<string?>();

        harness.ViewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(
            photos: Photos(PortfolioPhotoLimits.MaxPhotos)));

        await harness.ViewModel.ActivateAsync();

        Assert.Contains(nameof(BarberCabinetProfileViewModel.IsPhotoLimitReached), changed);
    }

    private static IReadOnlyList<BarberProfilePhotoResponse> Photos(int count) =>
    [
        .. Enumerable.Range(0, count).Select(index => BarberCabinetData.Photo(
            $"Работа {index}",
            id: Guid.Parse($"9e100000-0000-4000-8000-{index + 1:D12}"))),
    ];

    /// <summary>
    /// Собранный раздел: подменённые клиенты карточки и справочника, часы и два цикла повтора.
    /// </summary>
    private sealed class Harness
    {
        public Harness(AutoRetryLoop? retry = null, AutoRetryLoop? catalogRetry = null)
        {
            ViewModel = new BarberCabinetProfileViewModel(
                Profile,
                Search,
                new FixedTimeProvider(BarberCabinetFixtures.Now),
                retry,
                catalogRetry);

            ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);
        }

        public FakeBarberProfileApi Profile { get; } = new();

        public FakeBarberSearchApi Search { get; } = new();

        public BarberCabinetProfileViewModel ViewModel { get; }
    }
}
