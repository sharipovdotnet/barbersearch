using System.Net;
using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.BarberRequests.GetMyBarberRequest;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки онбординга мастера: предзаполнение из одобренной заявки, создание профиля,
/// обязательное продление пары токенов, шаг салона и обе ветки сбоя продления.
/// </summary>
/// <remarks>
/// Пара токенов после <c>201</c> обновляется всегда: в старом access-токене ещё нет
/// <c>barber_profile_id</c>, и без продления кабинет остался бы «без профиля». Сбой продления
/// не повторяет создание профиля — <c>POST</c> уже прошёл.
/// </remarks>
public sealed class BarberOnboardingViewModelTests
{
    private const string RefreshFailedTitle = "Профиль создан, но сессия не обновилась";

    private const string ExpiredTitle = "Профиль создан, войдите снова";

    private const string LockedHintText = "Перенесено из заявки — изменить нельзя.";

    [Fact]
    public async Task InitializeAsync_WithoutRequest_ShowsEmptyFormWithRussianSelected()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(1, harness.Requests.MineCalls);
        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.ShowForm);
        Assert.False(viewModel.ShowReadError);
        Assert.False(viewModel.IsSignInRequired);
        Assert.True(viewModel.IsFormEnabled);
        Assert.Equal("Создать профиль", viewModel.SubmitButtonText);
        Assert.Equal(string.Empty, viewModel.DisplayName);
        Assert.Equal(string.Empty, viewModel.ExperienceYears);
        Assert.True(viewModel.Languages.Single(option => option.Language == Language.Ru).IsSelected);
        Assert.False(viewModel.IsPrefilled);
        Assert.False(viewModel.IsDisplayNameLocked);
        Assert.Equal(string.Empty, viewModel.LockedHint);
    }

    [Fact]
    public async Task InitializeAsync_ApprovedRequest_PrefillsAndLocksFields()
    {
        var harness = new Harness();

        harness.Requests.Mine = _ => Task.FromResult(new MyBarberRequestResponse(
            BarberCabinetData.Request()));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowForm);
        Assert.Equal("Тимур Мастеров", viewModel.DisplayName);
        Assert.Equal("8", viewModel.ExperienceYears);
        Assert.Equal("Стригу с 2014 года.", viewModel.Bio);
        Assert.True(viewModel.IsDisplayNameLocked);
        Assert.True(viewModel.IsExperienceLocked);
        Assert.True(viewModel.IsBioLocked);
        Assert.True(viewModel.IsPrefilled);
        Assert.Equal(LockedHintText, viewModel.LockedHint);
    }

    [Fact]
    public async Task InitializeAsync_NotApprovedRequest_DoesNotPrefill()
    {
        var harness = new Harness();

        harness.Requests.Mine = _ => Task.FromResult(new MyBarberRequestResponse(
            BarberCabinetData.Request(BarberRequestStatus.Pending)));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(string.Empty, viewModel.DisplayName);
        Assert.Equal(string.Empty, viewModel.ExperienceYears);
        Assert.False(viewModel.IsPrefilled);
        Assert.False(viewModel.IsDisplayNameLocked);
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_RequiresSignIn()
    {
        var harness = new Harness();

        harness.Requests.Mine = _ => Task.FromException<MyBarberRequestResponse>(
            BarberCabinetApiError.Create(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsSignInRequired);
        Assert.False(viewModel.ShowForm);
        Assert.False(viewModel.ShowReadError);
        Assert.Null(viewModel.ReadError);
        Assert.Equal(0, viewModel.ReadAutoRetry.Attempts);
    }

    [Fact]
    public async Task InitializeAsync_TransportFailure_RetriesUntilRequestIsRead()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness(readRetry: BarberCabinetRetry.Gated(gate.Task));
        var calls = 0;

        harness.Requests.Mine = _ =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<MyBarberRequestResponse>(new HttpRequestException("offline"))
                : Task.FromResult(new MyBarberRequestResponse(BarberCabinetData.Request()));
        };

        harness.ViewModel.StartAutoRetry();

        await harness.ViewModel.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            viewModel.ReadError);
        Assert.True(viewModel.ShowReadError);

        gate.SetResult();

        await viewModel.ReadRetryCompletion;

        Assert.Equal(2, calls);
        Assert.Null(viewModel.ReadError);
        Assert.True(viewModel.ShowForm);
        Assert.Equal("Тимур Мастеров", viewModel.DisplayName);
    }

    [Fact]
    public async Task SubmitAsync_Valid_CreatesProfileRefreshesTokensAndShowsSalonStep()
    {
        var harness = new Harness();

        await harness.PrepareFormAsync();

        var viewModel = harness.ViewModel;

        viewModel.DisplayName = "  Тимур Мастеров  ";
        viewModel.ExperienceYears = " 8 ";
        viewModel.Bio = "  Стригу с 2014 года.  ";

        await viewModel.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Profile.CreateRequests);

        Assert.Equal("Тимур Мастеров", request.DisplayName);
        Assert.Equal(8, request.YearsOfExperience);
        Assert.Equal("Стригу с 2014 года.", request.Bio);
        Assert.Equal([Language.Ru], request.Languages);

        // Сразу после создания пара обязательна: иначе в токене нет barber_profile_id.
        Assert.Equal(["old-refresh"], harness.Auth.RefreshRequests);
        Assert.Equal("new-access", harness.Session.AccessToken);
        Assert.Equal("new-refresh", harness.Store.Tokens?.RefreshToken);

        Assert.True(viewModel.HasCreatedProfile);
        Assert.True(viewModel.IsSalonStep);
        Assert.False(viewModel.IsRefreshingSession);
        Assert.False(viewModel.ShowForm);
        Assert.Equal("Добавьте место приёма", viewModel.SalonStepTitle);
        Assert.True(viewModel.IsFormEnabled == false);
    }

    [Fact]
    public async Task SubmitAsync_RefreshFailure_ShowsRetryStateWithoutRecreatingProfile()
    {
        var harness = new Harness();

        await harness.PrepareFormAsync();

        harness.Auth.Refresh = _ => Task.FromException<TokenResponse>(
            new HttpRequestException("offline"));

        var viewModel = harness.ViewModel;

        viewModel.DisplayName = "Тимур";
        viewModel.ExperienceYears = "8";

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Single(harness.Profile.CreateRequests);
        Assert.True(viewModel.IsSessionRefreshFailed);
        Assert.True(viewModel.ShowSessionState);
        Assert.Equal(RefreshFailedTitle, viewModel.SessionStateTitle);
        Assert.StartsWith("Повторять форму не нужно", viewModel.SessionStateDescription, StringComparison.Ordinal);
        Assert.True(viewModel.CanRetrySession);
        Assert.False(viewModel.ShowForm);
        Assert.False(viewModel.IsSessionExpired);
        Assert.True(viewModel.HasCreatedProfile);
    }

    [Fact]
    public async Task RetrySession_AfterTemporaryFailure_OpensSalonStep()
    {
        var harness = new Harness();

        await harness.PrepareFormAsync();

        harness.Auth.Refresh = _ => Task.FromException<TokenResponse>(
            new HttpRequestException("offline"));

        var viewModel = harness.ViewModel;

        viewModel.DisplayName = "Тимур";
        viewModel.ExperienceYears = "8";

        await viewModel.SubmitCommand.ExecuteAsync(null);

        harness.Auth.Refresh = _ => Task.FromResult(BarberCabinetData.Tokens());

        await viewModel.RetrySessionCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsSessionRefreshFailed);
        Assert.False(viewModel.IsSessionExpired);
        Assert.True(viewModel.IsSalonStep);
        Assert.False(viewModel.CanRetrySession);
        Assert.Equal(2, harness.Auth.RefreshRequests.Count);
        Assert.Single(harness.Profile.CreateRequests);
    }

    [Fact]
    public async Task SubmitAsync_RefreshUnauthorized_ShowsSignInState()
    {
        var harness = new Harness();

        await harness.PrepareFormAsync();

        harness.Auth.Refresh = _ => Task.FromException<TokenResponse>(
            BarberCabinetApiError.Create(HttpStatusCode.Unauthorized, "auth.refresh.reused"));

        var viewModel = harness.ViewModel;

        viewModel.DisplayName = "Тимур";
        viewModel.ExperienceYears = "8";

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSessionExpired);
        Assert.Equal(ExpiredTitle, viewModel.SessionStateTitle);
        Assert.False(viewModel.CanRetrySession);
        Assert.False(viewModel.ShowForm);
        Assert.False(viewModel.IsSessionRefreshFailed);
        Assert.Single(harness.Profile.CreateRequests);
    }

    [Fact]
    public async Task SubmitAsync_AlreadyExists_CountsAsSuccessAndRequestsCabinet()
    {
        var harness = new Harness();
        var requested = 0;

        await harness.PrepareFormAsync();

        harness.Profile.Create = _ => Task.FromException<Guid>(
            BarberCabinetApiError.Create(
                HttpStatusCode.Conflict,
                "barberProfile.create.alreadyExists"));

        var viewModel = harness.ViewModel;

        viewModel.CabinetRequested += (_, _) => requested++;
        viewModel.DisplayName = "Тимур";
        viewModel.ExperienceYears = "8";

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(1, requested);
        Assert.True(viewModel.HasCreatedProfile);
        Assert.False(viewModel.IsSalonStep);
        Assert.Null(viewModel.FormError);
        Assert.Null(viewModel.DisplayNameError);

        // Профиль уже существует: создавать его заново нельзя, но пару всё равно обновляем.
        Assert.Single(harness.Auth.RefreshRequests);
    }

    [Fact]
    public async Task SalonStep_SearchAndAdd_AttachesFirstSalonAsPrimary()
    {
        var harness = new Harness();

        await harness.ReachSalonStepAsync();

        var viewModel = harness.ViewModel;

        viewModel.CatalogQuery = "Ha";

        await viewModel.CatalogOperation;

        Assert.True(viewModel.ShowCatalogResults);

        viewModel.SelectedCatalogSalon = viewModel.Catalog[0];

        Assert.True(viewModel.CanAddSalon);

        await viewModel.AddSalonCommand.ExecuteAsync(null);

        var attach = Assert.Single(harness.Profile.SalonAttaches);

        Assert.Equal(BarberCabinetData.CreatedProfileId, attach.ProfileId);
        Assert.Equal(BarberCabinetFixtures.OtherSalonId, attach.SalonId);
        Assert.True(attach.IsPrimary);
        Assert.True(viewModel.SalonAdded);
        Assert.Equal("Салон добавлен.", viewModel.SalonAddedText);
        Assert.Null(viewModel.SelectedCatalogSalon);
        Assert.Empty(viewModel.Catalog);
        Assert.False(viewModel.CanAddSalon);
    }

    [Fact]
    public async Task GoToCabinet_AfterSalonStep_RaisesCabinetRequested()
    {
        var harness = new Harness();
        var requested = 0;

        await harness.ReachSalonStepAsync();

        harness.ViewModel.CabinetRequested += (_, _) => requested++;

        harness.ViewModel.GoToCabinetCommand.Execute(null);

        Assert.Equal(1, requested);
    }

    /// <summary>
    /// Собранный онбординг: подменённые клиенты, сессия с парой и пауза каталога.
    /// </summary>
    private sealed class Harness
    {
        public Harness(
            Func<TimeSpan, CancellationToken, Task>? delay = null,
            AutoRetryLoop? readRetry = null,
            AutoRetryLoop? catalogRetry = null)
        {
            Store.Tokens = new StoredTokens("old-access", "old-refresh");
            Session = new TokenSession(Store);

            ViewModel = new BarberOnboardingViewModel(
                Profile,
                Requests,
                Auth,
                Session,
                Search,
                delay ?? (static (_, _) => Task.CompletedTask),
                readRetry,
                catalogRetry);
        }

        public FakeBarberProfileApi Profile { get; } = new();

        public FakeBarberRequestsApi Requests { get; } = new();

        public FakeAuthApi Auth { get; } = new();

        public FakeBarberSearchApi Search { get; } = new();

        public FakeTokenStore Store { get; } = new();

        public TokenSession Session { get; }

        public BarberOnboardingViewModel ViewModel { get; }

        /// <summary>
        /// Читает заявку и заполняет форму минимально верными значениями.
        /// </summary>
        /// <returns>Задача подготовки.</returns>
        public Task PrepareFormAsync() => ViewModel.InitializeAsync();

        /// <summary>
        /// Создаёт профиль и доходит до шага «Добавьте место приёма».
        /// </summary>
        /// <returns>Задача подготовки.</returns>
        public async Task ReachSalonStepAsync()
        {
            await ViewModel.InitializeAsync();

            ViewModel.DisplayName = "Тимур Мастеров";
            ViewModel.ExperienceYears = "8";

            await ViewModel.SubmitCommand.ExecuteAsync(null);
        }
    }
}
