using System.Net;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels.Admin;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки карточки заявки «Стать мастером»: анкета, состояния чтения и два решения.
/// </summary>
/// <remarks>
/// Карточка читается отдельным запросом и показывает всю анкету, включая контакты, которые
/// видит только администратор. Отказ требует причину: пустая и слишком длинная не уходят на
/// сервер вовсе, а ответы <c>409</c> и <c>404</c> объясняются человеку, а не показываются
/// английским <c>detail</c>.
/// </remarks>
public sealed class AdminRequestViewModelTests
{
    private static readonly Guid RequestId = Guid.Parse("9e300000-0000-4000-8000-0000000000b1");

    private static readonly DateTimeOffset Now =
        new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InitializeAsync_PendingRequest_ShowsWholeApplication()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf());

        await harness.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowContent);
        Assert.True(viewModel.ShowDecision);
        Assert.False(viewModel.ShowReviewedNote);

        Assert.Equal("Игорь Мастер", viewModel.DisplayNameText);
        Assert.Equal("Игорь Иванов", viewModel.ApplicantNameText);
        Assert.Equal("igor@example.com", viewModel.ApplicantEmailText);
        Assert.Equal("Москва", viewModel.CityText);
        Assert.Equal("5 лет", viewModel.ExperienceText);
        Assert.Equal("Нет", viewModel.SalonText);
        Assert.False(string.IsNullOrEmpty(viewModel.SubmittedAtText));
        Assert.Equal("Стригу с 2018 года.", viewModel.AboutText);
        Assert.True(viewModel.HasAbout);
        Assert.False(viewModel.HasNoAbout);
        Assert.False(viewModel.HasReviewedAt);
        Assert.Equal("На рассмотрении", viewModel.StatusText);
        Assert.True(viewModel.IsPending);
        Assert.False(viewModel.ShowRejectionReason);
        Assert.Equal("Причина не указана.", viewModel.RejectionReasonText);
    }

    [Fact]
    public async Task InitializeAsync_WithoutAbout_ExplainsEmptySection()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf(about: null));

        await harness.InitializeAsync();

        Assert.False(harness.ViewModel.HasAbout);
        Assert.True(harness.ViewModel.HasNoAbout);
        Assert.Equal(string.Empty, harness.ViewModel.AboutText);
    }

    [Fact]
    public async Task InitializeAsync_RejectedRequest_ShowsReasonAndNoDecision()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf(
            status: BarberRequestStatus.Rejected,
            reviewedAt: new DateTimeOffset(2026, 11, 16, 12, 30, 0, TimeSpan.Zero),
            rejectionReason: "Мало опыта"));

        await harness.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.False(viewModel.ShowDecision);
        Assert.True(viewModel.ShowReviewedNote);
        Assert.True(viewModel.ShowRejectionReason);
        Assert.True(viewModel.IsRejected);
        Assert.Equal("Отклонена", viewModel.StatusText);
        Assert.Equal("Мало опыта", viewModel.RejectionReasonText);
        Assert.True(viewModel.HasReviewedAt);
        Assert.False(string.IsNullOrEmpty(viewModel.ReviewedAtText));
    }

    [Fact]
    public async Task InitializeAsync_NotFound_ShowsNotFoundState()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromException<AdminBarberRequestResponse>(
            ApiError(HttpStatusCode.NotFound, "barberRequest.notFound"));

        await harness.InitializeAsync();

        Assert.True(harness.ViewModel.ShowNotFound);
        Assert.False(harness.ViewModel.ShowContent);
        Assert.False(harness.ViewModel.ShowDecision);
    }

    [Fact]
    public async Task InitializeAsync_Forbidden_ShowsForbiddenState()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromException<AdminBarberRequestResponse>(
            ApiError(HttpStatusCode.Forbidden, "auth.forbidden"));

        await harness.InitializeAsync();

        Assert.True(harness.ViewModel.ShowForbidden);
        Assert.False(harness.ViewModel.ShowContent);
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_ClearsSessionAndShowsSignIn()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromException<AdminBarberRequestResponse>(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.InitializeAsync();

        Assert.False(harness.Session.IsAuthenticated);
        Assert.Null(harness.Store.Tokens);
        Assert.True(harness.ViewModel.ShowSignInRequired);
        Assert.False(harness.ViewModel.ShowDecision);
    }

    [Fact]
    public async Task InitializeAsync_NetworkFailure_ShowsNetworkText()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromException<AdminBarberRequestResponse>(
            new HttpRequestException("refused"));

        await harness.InitializeAsync();

        Assert.True(harness.ViewModel.ShowError);
        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.ReadError);
    }

    [Fact]
    public async Task RetryReadAsync_AfterError_LoadsCard()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromException<AdminBarberRequestResponse>(
            ApiError(HttpStatusCode.InternalServerError, "internal.database"));

        await harness.InitializeAsync();
        Assert.True(harness.ViewModel.ShowError);

        harness.Api.GetRequest = _ => Task.FromResult(RequestOf());
        await harness.ViewModel.RetryReadCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.ShowError);
        Assert.True(harness.ViewModel.ShowContent);
    }

    [Fact]
    public async Task ApproveAsync_Success_CallsApiAndReturnsNull()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf());
        await harness.InitializeAsync();

        var error = await harness.ViewModel.ApproveAsync();

        Assert.Null(error);
        Assert.Equal(RequestId, harness.Api.ApprovedRequestId);
        Assert.Equal(1, harness.Api.ApproveCalls);
        Assert.Null(harness.ViewModel.ActionError);
        Assert.False(harness.ViewModel.IsActing);
    }

    [Fact]
    public async Task ApproveAsync_AlreadyReviewed_ReturnsTextAndStays()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf());
        await harness.InitializeAsync();

        harness.Api.Approve = _ => Task.FromException(
            ApiError(HttpStatusCode.Conflict, "barberRequest.review.invalidStatus"));

        var error = await harness.ViewModel.ApproveAsync();

        Assert.Equal("Заявку уже рассмотрели. Обновите список.", error);
        Assert.Equal(error, harness.ViewModel.ActionError);
        Assert.True(harness.ViewModel.HasActionError);
        Assert.True(harness.ViewModel.ShowDecision);
    }

    [Fact]
    public async Task ApproveAsync_Unauthorized_EndsSession()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf());
        await harness.InitializeAsync();

        harness.Api.Approve = _ => Task.FromException(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        var error = await harness.ViewModel.ApproveAsync();

        Assert.Null(error);
        Assert.True(harness.ViewModel.ShowSignInRequired);
        Assert.False(harness.Session.IsAuthenticated);
        Assert.Null(harness.Store.Tokens);
    }

    [Fact]
    public async Task ApproveAsync_NetworkFailure_ReturnsNetworkText()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf());
        await harness.InitializeAsync();

        harness.Api.Approve = _ => Task.FromException(new HttpRequestException("refused"));

        var error = await harness.ViewModel.ApproveAsync();

        Assert.Equal("Нет связи с сервером. Проверьте подключение и повторите попытку.", error);
    }

    [Fact]
    public async Task StartRejection_EmptyReason_CannotConfirmAndOpensPanel()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf());
        await harness.InitializeAsync();

        harness.ViewModel.StartRejectionCommand.Execute(null);

        Assert.True(harness.ViewModel.ShowReasonPanel);
        Assert.False(harness.ViewModel.ShowDecisionHint);
        Assert.False(harness.ViewModel.CanConfirmReject);
        Assert.False(harness.ViewModel.CanRunPrimaryAction);
        Assert.Equal("Отклонить", harness.ViewModel.PrimaryActionText);
        Assert.Equal("Отмена", harness.ViewModel.SecondaryActionText);

        harness.ViewModel.RejectionReason = "Мало опыта";

        // Причина введена: второй шаг подтверждения открыт.
        Assert.True(harness.ViewModel.CanConfirmReject);
        Assert.True(harness.ViewModel.CanRunPrimaryAction);
    }

    [Fact]
    public async Task CancelRejection_ClosesPanelAndRestoresActions()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf());
        await harness.InitializeAsync();

        harness.ViewModel.StartRejectionCommand.Execute(null);
        harness.ViewModel.RejectionReason = "Мало опыта";
        harness.ViewModel.CancelRejectionCommand.Execute(null);

        Assert.False(harness.ViewModel.ShowReasonPanel);
        Assert.True(harness.ViewModel.ShowDecisionHint);
        Assert.Equal(string.Empty, harness.ViewModel.RejectionReason);
        Assert.Equal("Одобрить", harness.ViewModel.PrimaryActionText);
        Assert.Equal("Отклонить", harness.ViewModel.SecondaryActionText);
    }

    [Fact]
    public async Task RejectAsync_EmptyReason_DoesNotCallApi()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf());
        await harness.InitializeAsync();

        harness.ViewModel.StartRejectionCommand.Execute(null);

        var error = await harness.ViewModel.RejectAsync();

        Assert.Equal("Укажите причину отказа.", error);
        Assert.Equal(0, harness.Api.RejectCalls);
    }

    [Fact]
    public async Task RejectAsync_TooLongReason_DoesNotCallApi()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf());
        await harness.InitializeAsync();

        harness.ViewModel.StartRejectionCommand.Execute(null);
        harness.ViewModel.RejectionReason = new string('а', AdminRequestViewModel.MaxRejectionReasonLength + 1);

        var error = await harness.ViewModel.RejectAsync();

        Assert.Contains("500", error, StringComparison.Ordinal);
        Assert.Equal(0, harness.Api.RejectCalls);
    }

    [Fact]
    public async Task RejectAsync_Success_TrimsReasonAndSendsIt()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf());
        await harness.InitializeAsync();

        harness.ViewModel.StartRejectionCommand.Execute(null);
        harness.ViewModel.RejectionReason = "  Мало опыта  ";

        var error = await harness.ViewModel.RejectAsync();

        Assert.Null(error);
        Assert.Equal(1, harness.Api.RejectCalls);
        Assert.Equal("Мало опыта", harness.Api.RejectedReason);
        Assert.False(harness.ViewModel.IsActing);
    }

    [Fact]
    public async Task RejectAsync_ReasonTooLongFromServer_ReturnsLimitText()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf());
        await harness.InitializeAsync();

        harness.Api.Reject = (_, _) => Task.FromException(
            ApiError(HttpStatusCode.BadRequest, "barberRequest.rejection.reasonTooLong"));

        harness.ViewModel.StartRejectionCommand.Execute(null);
        harness.ViewModel.RejectionReason = "Мало опыта";

        var error = await harness.ViewModel.RejectAsync();

        Assert.Contains("максимум 500 символов", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectAsync_RateLimited_ReturnsRateLimitText()
    {
        var harness = new Harness();
        harness.Api.GetRequest = _ => Task.FromResult(RequestOf());
        await harness.InitializeAsync();

        harness.Api.Reject = (_, _) => Task.FromException(
            ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit"));

        harness.ViewModel.StartRejectionCommand.Execute(null);
        harness.ViewModel.RejectionReason = "Мало опыта";

        var error = await harness.ViewModel.RejectAsync();

        Assert.Equal("Слишком много запросов. Подождите минуту и повторите.", error);
    }

    [Fact]
    public async Task StartAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        var harness = new Harness(autoRetry: FastRetry());
        var calls = 0;

        harness.Api.GetRequest = _ =>
        {
            calls++;

            return calls <= 2
                ? Task.FromException<AdminBarberRequestResponse>(new HttpRequestException("refused"))
                : Task.FromResult(RequestOf());
        };

        await harness.InitializeAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowError);

        viewModel.StartAutoRetry();
        await viewModel.AutoRetryCompletion;

        Assert.Equal(2, viewModel.AutoRetry.Attempts);
        Assert.True(viewModel.ShowContent);
        Assert.False(viewModel.ShowError);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task StartAutoRetry_TerminalFailure_DoesNotStart(HttpStatusCode statusCode)
    {
        var harness = new Harness(autoRetry: FastRetry());
        harness.Api.GetRequest = _ => Task.FromException<AdminBarberRequestResponse>(
            ApiError(statusCode, "error.terminal"));

        await harness.InitializeAsync();

        harness.ViewModel.StartAutoRetry();

        Assert.Equal(0, harness.ViewModel.AutoRetry.Attempts);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    /// <summary>
    /// Цикл с мгновенной паузой: секунды расписания в тесте не тратятся.
    /// </summary>
    private static AutoRetryLoop FastRetry() =>
        new(
            static (_, _) => Task.CompletedTask,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    private static AdminBarberRequestResponse RequestOf(
        string displayName = "Игорь Мастер",
        string city = "Москва",
        int yearsOfExperience = 5,
        bool hasSalon = false,
        string? about = "Стригу с 2018 года.",
        BarberRequestStatus status = BarberRequestStatus.Pending,
        DateTimeOffset? reviewedAt = null,
        string? rejectionReason = null) =>
        new(
            RequestId,
            status,
            displayName,
            yearsOfExperience,
            city,
            about,
            hasSalon,
            new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero),
            reviewedAt,
            rejectionReason,
            null,
            new AdminBarberRequestApplicantResponse(
                Guid.CreateVersion7(),
                "Игорь Иванов",
                "igor@example.com"));

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, string errorCode) =>
        new(statusCode, "Request failed.", [errorCode], new Dictionary<string, string[]>());

    /// <summary>
    /// Часы с фиксированным моментом: даты карточки не зависят от времени запуска.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// Собранная карточка: сессия с парой, подменённый клиент заявок и открытый ViewModel.
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

            ViewModel = new AdminRequestViewModel(
                Api,
                Session,
                new FixedTimeProvider(Now),
                autoRetry);
        }

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public FakeAdminRequestsApi Api { get; } = new();

        public AdminRequestViewModel ViewModel { get; }

        public async Task InitializeAsync()
        {
            ViewModel.Initialize(RequestId);

            await ViewModel.InitializeAsync();
        }
    }

    /// <summary>
    /// Подменённый клиент заявок: карточка по id и два решения.
    /// </summary>
    private sealed class FakeAdminRequestsApi : IAdminRequestsApi
    {
        public int ApproveCalls { get; private set; }

        public int RejectCalls { get; private set; }

        public Guid? ApprovedRequestId { get; private set; }

        public string? RejectedReason { get; private set; }

        public Func<Guid, Task<AdminBarberRequestResponse>> GetRequest { get; set; } =
            _ => Task.FromResult(RequestOf());

        public Func<Guid, Task> Approve { get; set; } = _ => Task.CompletedTask;

        public Func<Guid, string, Task> Reject { get; set; } = (_, _) => Task.CompletedTask;

        public Task<PagedResponse<AdminBarberRequestResponse>> GetRequestsAsync(
            AdminRequestListFilter filter,
            string? cursor,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Карточка не читает очередь.");

        public Task<AdminBarberRequestResponse> GetRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken = default) =>
            GetRequest(requestId);

        public Task ApproveRequestAsync(Guid requestId, CancellationToken cancellationToken = default)
        {
            ApproveCalls++;
            ApprovedRequestId = requestId;

            return Approve(requestId);
        }

        public Task RejectRequestAsync(
            Guid requestId,
            string reason,
            CancellationToken cancellationToken = default)
        {
            RejectCalls++;
            RejectedReason = reason;

            return Reject(requestId, reason);
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
