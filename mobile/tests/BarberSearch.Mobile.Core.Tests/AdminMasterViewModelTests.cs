using System.Net;
using BarberSearch.Contracts.BarberProfiles.Admin;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Domain.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels.Admin;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки карточки мастера: чтение, готовность, портфолио и решение о подтверждении.
/// </summary>
/// <remarks>
/// Карточка — отдельное чтение. Здесь проверяется, что ответ раскладывается по карточкам
/// экрана, что подтверждение скрытого профиля недоступно по той же причине, что и у веба,
/// и что снятие подтверждения требует непустой причины.
/// </remarks>
public sealed class AdminMasterViewModelTests
{
    private static readonly Guid MasterId = Guid.Parse("9e100000-0000-4000-8000-0000000000e1");
    private static readonly Guid UserId = Guid.Parse("9e100000-0000-4000-8000-0000000000e2");

    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InitializeAsync_Detail_LoadsIdentityContextAndAvatar()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf());

        await harness.ViewModel.InitializeAsync(MasterId);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowContent);
        Assert.False(viewModel.IsLoading);
        Assert.Equal("Иван Мастер", viewModel.DisplayName);
        Assert.Equal("ivan@example.com", viewModel.Email);
        Assert.Equal("+7 900 000-00-00", viewModel.PhoneText);
        Assert.Equal("Мастер", viewModel.GradeText);
        Assert.Equal("Стригу с 2015 года.", viewModel.BioText);
        Assert.Equal("9 лет", viewModel.ExperienceText);
        Assert.Contains("4,8", viewModel.RatingText, StringComparison.Ordinal);
        Assert.Contains("12 отзывов", viewModel.RatingText, StringComparison.Ordinal);
        Assert.Equal("ИМ", viewModel.Initials);
        Assert.Equal("На проверке", viewModel.StatusText);
        Assert.True(viewModel.HasAvatar);
        Assert.Equal(
            "http://localhost:5243/api/media/avatars/ivan.webp?size=md",
            viewModel.AvatarImageUrl);
        Assert.False(viewModel.HasAvatarState);
        Assert.False(viewModel.HasAvatarRejection);

        // Служебные моменты показываются в поясе устройства и читаются без пустых строк.
        Assert.Contains("в", viewModel.CreatedAtText, StringComparison.Ordinal);
        Assert.Contains("в", viewModel.UpdatedAtText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_NullId_ShowsNotFoundWithoutRequest()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync(null);

        Assert.True(harness.ViewModel.IsNotFound);
        Assert.False(harness.ViewModel.ShowContent);
        Assert.Equal(0, harness.MastersApi.Calls);
    }

    [Fact]
    public async Task InitializeAsync_NotFound_ShowsNotFoundState()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromException<AdminBarberDetailResponse>(
            ApiError(HttpStatusCode.NotFound, "barberProfile.notFound"));

        await harness.ViewModel.InitializeAsync(MasterId);

        Assert.True(harness.ViewModel.IsNotFound);
        Assert.False(harness.ViewModel.ShowContent);
        Assert.False(harness.ViewModel.ShowError);
    }

    [Fact]
    public async Task InitializeAsync_Forbidden_ShowsForbiddenState()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromException<AdminBarberDetailResponse>(
            ApiError(HttpStatusCode.Forbidden, "auth.forbidden"));

        await harness.ViewModel.InitializeAsync(MasterId);

        Assert.True(harness.ViewModel.ShowForbidden);
        Assert.False(harness.ViewModel.ShowContent);
    }

    [Fact]
    public async Task InitializeAsync_NetworkFailure_ShowsErrorThenAutoRetries()
    {
        var harness = new Harness(autoRetry: FastRetry());
        var calls = 0;

        harness.MastersApi.GetMaster = _ =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<AdminBarberDetailResponse>(new HttpRequestException("refused"))
                : Task.FromResult(DetailOf());
        };

        await harness.ViewModel.InitializeAsync(MasterId);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowError);
        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            viewModel.LoadErrorText);

        viewModel.StartAutoRetry();
        await viewModel.AutoRetryCompletion;

        Assert.Equal(1, viewModel.AutoRetry.Attempts);
        Assert.False(viewModel.ShowError);
        Assert.Equal("Иван Мастер", viewModel.DisplayName);
    }

    [Fact]
    public async Task RetryAsync_AfterError_LoadsProfile()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromException<AdminBarberDetailResponse>(
            ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        await harness.ViewModel.InitializeAsync(MasterId);

        Assert.True(harness.ViewModel.ShowError);

        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf());
        await harness.ViewModel.RetryCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.ShowError);
        Assert.True(harness.ViewModel.ShowContent);
    }

    [Fact]
    public async Task InitializeAsync_Readiness_BuildsSixItemsAndCountsDone()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf(
            profileFilled: true,
            emailVerified: false,
            servicesCount: 0,
            salonsCount: 1,
            scheduleDaysCount: 5,
            photosCount: 2));

        await harness.ViewModel.InitializeAsync(MasterId);

        var items = harness.ViewModel.ReadinessItems;

        Assert.Equal(6, items.Count);
        Assert.Equal(4, items.Count(item => item.IsDone));
        Assert.Equal("4 из 6", harness.ViewModel.ReadinessDoneText);
        Assert.False(harness.ViewModel.IsReadinessComplete);

        // Пункты и значения — слова веба; пустые счётчики объясняются, а не показывают «0».
        Assert.Equal("Профиль заполнен", items[0].Label);
        Assert.Equal("Описание и опыт есть", items[0].Value);
        Assert.Equal("Email подтверждён", items[1].Label);
        Assert.Equal("Не подтверждён", items[1].Value);
        Assert.Equal("Услуги", items[2].Label);
        Assert.Equal("Ни одной услуги", items[2].Value);
        Assert.Equal("Салоны", items[3].Label);
        Assert.Equal("1", items[3].Value);
        Assert.Equal("Расписание", items[4].Label);
        Assert.Equal("5 дней", items[4].Value);
        Assert.Equal("Фото в портфолио", items[5].Label);
        Assert.Equal("2", items[5].Value);
    }

    [Fact]
    public async Task InitializeAsync_AllReadinessDone_ReportsComplete()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf());

        await harness.ViewModel.InitializeAsync(MasterId);

        Assert.True(harness.ViewModel.IsReadinessComplete);
        Assert.Equal("6 из 6", harness.ViewModel.ReadinessDoneText);
    }

    [Fact]
    public async Task InitializeAsync_Photos_ShowModerationStates()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf(photos:
        [
            PhotoOf(description: "Стрижка"),
            PhotoOf(description: null, isBlocked: true),
            PhotoOf(description: "Борода", status: PhotoModerationStatus.Rejected, rejectionReason: "Не по теме"),
            PhotoOf(description: "Окрашивание", isPublished: false),
        ]));

        await harness.ViewModel.InitializeAsync(MasterId);

        var photos = harness.ViewModel.Photos;

        Assert.Equal(4, photos.Count);
        Assert.True(harness.ViewModel.HasPhotos);
        Assert.False(harness.ViewModel.HasNoPhotos);

        Assert.Equal("Стрижка", photos[0].Caption);
        Assert.Equal("Опубликовано", photos[0].StatusText);
        Assert.False(photos[0].HasRejection);

        Assert.Equal("Без подписи", photos[1].Caption);
        Assert.Equal("Скрыто администратором", photos[1].StatusText);

        Assert.Equal("Отклонено", photos[2].StatusText);
        Assert.True(photos[2].HasRejection);
        Assert.Equal("Причина: Не по теме", photos[2].RejectionText);

        Assert.Equal("Скрыто владельцем", photos[3].StatusText);
    }

    [Fact]
    public async Task InitializeAsync_BlockedAvatar_ShowsBlockedStateBeforeRejection()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf(
            avatarBlocked: true,
            avatarStatus: PhotoModerationStatus.Rejected,
            avatarReason: "Не по теме"));

        await harness.ViewModel.InitializeAsync(MasterId);

        Assert.True(harness.ViewModel.HasAvatarState);
        Assert.Equal("Скрыто администратором", harness.ViewModel.AvatarStateText);
        Assert.True(harness.ViewModel.HasAvatarRejection);
        Assert.Equal("Причина: Не по теме", harness.ViewModel.AvatarRejectionText);
    }

    [Fact]
    public async Task VerifyAsync_ActiveProfile_VerifiesAndEnablesUnverify()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf());
        await harness.ViewModel.InitializeAsync(MasterId);

        var error = await harness.ViewModel.VerifyAsync();

        Assert.Null(error);
        Assert.Equal(1, harness.MastersApi.VerifyCalls);
        Assert.True(harness.ViewModel.IsVerified);
        Assert.Equal("Подтверждён", harness.ViewModel.StatusText);
        Assert.Equal("Профиль подтверждён и участвует в поиске.", harness.ViewModel.VerificationStateText);
        Assert.False(harness.ViewModel.CanVerify);
        Assert.True(harness.ViewModel.CanUnverify);
        Assert.Null(harness.ViewModel.ActionErrorText);
    }

    [Fact]
    public async Task VerifyAsync_InactiveProfile_IsDisabledWithReason()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf(isActive: false));
        await harness.ViewModel.InitializeAsync(MasterId);

        Assert.False(harness.ViewModel.CanVerify);
        Assert.True(harness.ViewModel.HasVerifyBlockedReason);
        Assert.Contains(
            "верните профиль в поиск",
            harness.ViewModel.VerifyBlockedReason,
            StringComparison.Ordinal);

        var error = await harness.ViewModel.VerifyAsync();

        Assert.Null(error);
        Assert.Equal(0, harness.MastersApi.VerifyCalls);
    }

    [Fact]
    public async Task VerifyAsync_Failure_ReturnsTextAndKeepsProfile()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf());
        await harness.ViewModel.InitializeAsync(MasterId);

        harness.MastersApi.Verify = _ => Task.FromException(
            ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        var error = await harness.ViewModel.VerifyAsync();

        Assert.Equal("Не удалось подтвердить профиль. Попробуйте ещё раз.", error);
        Assert.Equal(error, harness.ViewModel.ActionErrorText);
        Assert.True(harness.ViewModel.HasActionError);
        Assert.False(harness.ViewModel.IsVerified);
        Assert.True(harness.ViewModel.CanVerify);
    }

    [Fact]
    public async Task VerifyAsync_InactiveDomainCode_ReturnsSpecificText()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf());
        await harness.ViewModel.InitializeAsync(MasterId);

        harness.MastersApi.Verify = _ => Task.FromException(
            ApiError(HttpStatusCode.Conflict, "barberProfile.verify.inactive"));

        var error = await harness.ViewModel.VerifyAsync();

        Assert.Contains("Сначала верните его в поиск", error, StringComparison.Ordinal);
        Assert.Equal(error, harness.ViewModel.ActionErrorText);
    }

    [Fact]
    public async Task VerifyAsync_Unauthorized_ClearsSessionAndShowsSignIn()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf());
        await harness.ViewModel.InitializeAsync(MasterId);

        harness.MastersApi.Verify = _ => Task.FromException(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        var error = await harness.ViewModel.VerifyAsync();

        Assert.Null(error);
        Assert.False(harness.Session.IsAuthenticated);
        Assert.True(harness.ViewModel.ShowSignInRequired);
        Assert.False(harness.ViewModel.ShowContent);
    }

    [Fact]
    public async Task OpenUnverifyPanel_RequiresNonEmptyReason()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf(isVerified: true));
        await harness.ViewModel.InitializeAsync(MasterId);

        harness.ViewModel.OpenUnverifyPanelCommand.Execute(null);

        Assert.True(harness.ViewModel.IsUnverifyPanelOpen);
        Assert.False(harness.ViewModel.CanConfirmUnverify);

        harness.ViewModel.UnverifyReason = "   ";
        Assert.False(harness.ViewModel.CanConfirmUnverify);

        harness.ViewModel.UnverifyReason = "Подтвердили по ошибке";
        Assert.True(harness.ViewModel.CanConfirmUnverify);

        harness.ViewModel.CancelUnverifyCommand.Execute(null);

        Assert.False(harness.ViewModel.IsUnverifyPanelOpen);
        Assert.Equal(string.Empty, harness.ViewModel.UnverifyReason);
    }

    [Fact]
    public async Task ConfirmUnverifyAsync_WithReason_UnverifiesAndClosesPanel()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf(isVerified: true));
        await harness.ViewModel.InitializeAsync(MasterId);

        harness.ViewModel.OpenUnverifyPanelCommand.Execute(null);
        harness.ViewModel.UnverifyReason = "Подтвердили по ошибке";

        var error = await harness.ViewModel.ConfirmUnverifyAsync();

        Assert.Null(error);
        Assert.Equal(1, harness.MastersApi.UnverifyCalls);
        Assert.False(harness.ViewModel.IsVerified);
        Assert.Equal("На проверке", harness.ViewModel.StatusText);
        Assert.False(harness.ViewModel.IsUnverifyPanelOpen);
        Assert.Equal(string.Empty, harness.ViewModel.UnverifyReason);
        Assert.True(harness.ViewModel.CanVerify);
    }

    [Fact]
    public async Task ConfirmUnverifyAsync_Failure_KeepsPanelAndReturnsText()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf(isVerified: true));
        await harness.ViewModel.InitializeAsync(MasterId);

        harness.ViewModel.OpenUnverifyPanelCommand.Execute(null);
        harness.ViewModel.UnverifyReason = "Подтвердили по ошибке";

        harness.MastersApi.Unverify = _ => Task.FromException(
            ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        var error = await harness.ViewModel.ConfirmUnverifyAsync();

        Assert.Equal("Не удалось снять подтверждение. Попробуйте ещё раз.", error);
        Assert.True(harness.ViewModel.IsVerified);
        Assert.True(harness.ViewModel.IsUnverifyPanelOpen);
        Assert.Equal(error, harness.ViewModel.ActionErrorText);
    }

    [Fact]
    public async Task ConfirmUnverifyAsync_EmptyReason_DoesNotCallApi()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf(isVerified: true));
        await harness.ViewModel.InitializeAsync(MasterId);

        harness.ViewModel.OpenUnverifyPanelCommand.Execute(null);

        var error = await harness.ViewModel.ConfirmUnverifyAsync();

        Assert.Null(error);
        Assert.Equal(0, harness.MastersApi.UnverifyCalls);
        Assert.True(harness.ViewModel.IsVerified);
    }

    [Fact]
    public async Task ApplySignedOutState_ClearsDetail()
    {
        var harness = new Harness();
        harness.MastersApi.GetMaster = _ => Task.FromResult(DetailOf());
        await harness.ViewModel.InitializeAsync(MasterId);

        harness.ViewModel.ApplySignedOutState();

        Assert.True(harness.ViewModel.ShowSignInRequired);
        Assert.False(harness.ViewModel.ShowContent);
        Assert.False(harness.ViewModel.HasDetail);
        Assert.Empty(harness.ViewModel.ReadinessItems);
        Assert.Empty(harness.ViewModel.Photos);
    }

    /// <summary>
    /// Цикл с мгновенной паузой: секунды расписания в тесте не тратятся.
    /// </summary>
    private static AutoRetryLoop FastRetry() =>
        new(
            static (_, _) => Task.CompletedTask,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    private static AdminBarberDetailResponse DetailOf(
        bool isActive = true,
        bool isVerified = false,
        string? phoneNumber = "+7 900 000-00-00",
        string? bio = "Стригу с 2015 года.",
        bool profileFilled = true,
        bool emailVerified = true,
        int servicesCount = 3,
        int salonsCount = 1,
        int scheduleDaysCount = 5,
        int photosCount = 2,
        bool avatarBlocked = false,
        PhotoModerationStatus avatarStatus = PhotoModerationStatus.Approved,
        string? avatarReason = null,
        IReadOnlyList<BarberProfilePhotoResponse>? photos = null) =>
        new(
            MasterId,
            UserId,
            "Иван Мастер",
            "ivan@example.com",
            phoneNumber,
            "/api/media/avatars/ivan.webp",
            Grade.MiddleBarber,
            bio,
            9,
            4.8m,
            12,
            isActive,
            isVerified,
            new AdminBarberReadinessResponse(
                profileFilled,
                emailVerified,
                servicesCount,
                salonsCount,
                scheduleDaysCount,
                photosCount),
            new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 15, 12, 30, 0, TimeSpan.Zero),
            photos ?? [],
            avatarBlocked,
            avatarStatus,
            avatarReason);

    private static BarberProfilePhotoResponse PhotoOf(
        string? description = "Стрижка",
        bool isPublished = true,
        bool isBlocked = false,
        PhotoModerationStatus status = PhotoModerationStatus.Approved,
        string? rejectionReason = null) =>
        new(
            Guid.CreateVersion7(),
            "/api/media/portfolio/work.webp",
            "/api/media/portfolio/work.webp?size=md",
            description,
            0,
            isPublished,
            isBlocked,
            status,
            rejectionReason);

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, string errorCode) =>
        new(statusCode, "Request failed.", [errorCode], new Dictionary<string, string[]>());

    /// <summary>
    /// Собранная карточка: сессия с парой, фальшивый клиент и часы теста.
    /// </summary>
    private sealed class Harness
    {
        public Harness(
            bool withTokens = true,
            AutoRetryLoop? autoRetry = null)
        {
            Store = new FakeTokenStore
            {
                Tokens = withTokens ? new StoredTokens("access-1", "refresh-1") : null,
            };
            Session = new TokenSession(Store);

            MastersApi.GetMaster = _ => Task.FromResult(DetailOf());

            ViewModel = new AdminMasterViewModel(
                MastersApi,
                ApiOptions,
                Session,
                new FixedTimeProvider(),
                autoRetry);
        }

        public BarberSearchApiOptions ApiOptions { get; } =
            BarberSearchApiOptions.FromBaseAddress("http://localhost:5243");

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public FakeAdminMastersApi MastersApi { get; } = new();

        public AdminMasterViewModel ViewModel { get; }
    }

    /// <summary>
    /// Часы теста: «сейчас» фиксировано, поэтому год в датах не зависит от дня запуска.
    /// </summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// Подменённый клиент мастеров: карточка и записи подтверждения.
    /// </summary>
    private sealed class FakeAdminMastersApi : IAdminMastersApi
    {
        public int Calls { get; private set; }

        public int VerifyCalls { get; private set; }

        public int UnverifyCalls { get; private set; }

        public Func<Guid, Task<AdminBarberDetailResponse>> GetMaster { get; set; } =
            _ => Task.FromResult(DetailOf());

        public Func<Guid, Task> Verify { get; set; } = _ => Task.CompletedTask;

        public Func<Guid, Task> Unverify { get; set; } = _ => Task.CompletedTask;

        public Task<PagedResponse<AdminBarberResponse>> GetMastersAsync(
            AdminMasterFilter filter,
            string? cursor,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Карточка не читает очередь.");

        public Task<AdminBarberDetailResponse> GetMasterAsync(
            Guid masterId,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            return GetMaster(masterId);
        }

        public Task VerifyMasterAsync(Guid barberProfileId, CancellationToken cancellationToken = default)
        {
            VerifyCalls++;

            return Verify(barberProfileId);
        }

        public Task UnverifyMasterAsync(Guid barberProfileId, CancellationToken cancellationToken = default)
        {
            UnverifyCalls++;

            return Unverify(barberProfileId);
        }
    }

    /// <summary>
    /// Подменённое хранилище пары.
    /// </summary>
    private sealed class FakeTokenStore : ITokenStore
    {
        public StoredTokens? Tokens { get; set; }

        public Task<StoredTokens?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Tokens);

        public Task SaveAsync(StoredTokens tokens, CancellationToken cancellationToken = default)
        {
            Tokens = tokens;

            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Tokens = null;

            return Task.CompletedTask;
        }
    }
}
