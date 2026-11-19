using System.Net;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.Admin;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Admin;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels.Admin;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки карточки отзыва админки: чтение контекста, состояния и решение модератора.
/// </summary>
/// <remarks>
/// Карточка — «прочитал и решил»: сначала весь контекст веба, потом запись. Здесь проверяется,
/// что ошибка записи остаётся на экране, а <c>404</c> считается уже разобранным отзывом,
/// потому что очередь после возврата перечитается и строки всё равно не будет.
/// </remarks>
public sealed class AdminReviewViewModelTests
{
    private static readonly Guid ReviewId = Guid.Parse("9e100000-0000-4000-8000-0000000000e1");

    private const string NetworkErrorText =
        "Нет связи с сервером. Проверьте подключение и повторите попытку.";

    [Fact]
    public async Task InitializeAsync_ExistingReview_FillsWholeCard()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync(ReviewId);

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowContent);
        Assert.Equal(4, viewModel.Rating);
        Assert.Equal("★★★★☆", viewModel.Stars);
        Assert.Equal("4 / 5", viewModel.RatingText);
        Assert.Equal("На модерации", viewModel.StatusText);
        Assert.False(viewModel.IsPublished);
        Assert.True(viewModel.ShowPublish);
        Assert.True(viewModel.HasReviewText);
        Assert.Equal("Отличная стрижка", viewModel.ReviewBody);
        Assert.Equal("Иван Мастер", viewModel.BarberName);
        Assert.Equal("Дмитрий Крылов", viewModel.CustomerName);
        Assert.Equal("dmitry@example.com", viewModel.CustomerEmail);
        Assert.Contains("Стрижка", viewModel.VisitLine, StringComparison.Ordinal);
        Assert.Contains("Салон «Тест»", viewModel.VisitLine, StringComparison.Ordinal);
        Assert.Contains("₽", viewModel.VisitLine, StringComparison.Ordinal);
        Assert.Contains("2026", viewModel.CreatedAtText, StringComparison.Ordinal);
        Assert.False(viewModel.HasUpdatedAt);
        Assert.Equal("Сейчас у мастера 4,8, 12 отзывов.", viewModel.BarberSummary);
        Assert.Equal("После публикации рейтинг пересчитается.", viewModel.ModerationHint);
    }

    [Fact]
    public async Task InitializeAsync_ReviewWithoutComment_ShowsRatingOnlyWording()
    {
        var harness = new Harness();
        harness.Api.GetReview = _ => Task.FromResult(ReviewOf(text: null));

        await harness.ViewModel.InitializeAsync(ReviewId);

        Assert.False(harness.ViewModel.HasReviewText);
        Assert.True(harness.ViewModel.IsReviewTextEmpty);
        Assert.Empty(harness.ViewModel.ReviewBody);
    }

    [Fact]
    public async Task InitializeAsync_EditedReview_ShowsUpdatedAt()
    {
        var harness = new Harness();
        var created = new DateTimeOffset(2026, 11, 15, 10, 30, 0, TimeSpan.Zero);

        harness.Api.GetReview = _ => Task.FromResult(ReviewOf(
            createdAt: created,
            updatedAt: created.AddHours(2)));

        await harness.ViewModel.InitializeAsync(ReviewId);

        Assert.True(harness.ViewModel.HasUpdatedAt);
        Assert.Contains("2026", harness.ViewModel.UpdatedAtText!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_PublishedReview_HidesPublishAction()
    {
        var harness = new Harness();
        harness.Api.GetReview = _ => Task.FromResult(ReviewOf(isPublished: true));

        await harness.ViewModel.InitializeAsync(ReviewId);

        Assert.Equal("Опубликован", harness.ViewModel.StatusText);
        Assert.False(harness.ViewModel.ShowPublish);
        Assert.Equal("После скрытия рейтинг пересчитается.", harness.ViewModel.ModerationHint);
    }

    [Fact]
    public async Task InitializeAsync_NotFound_ShowsAlreadyHandledState()
    {
        var harness = new Harness();
        harness.Api.GetReview = _ => Task.FromException<AdminReviewResponse>(
            ApiError(HttpStatusCode.NotFound, "review.notFound"));

        await harness.ViewModel.InitializeAsync(ReviewId);

        Assert.True(harness.ViewModel.ShowNotFound);
        Assert.False(harness.ViewModel.ShowContent);
        Assert.False(harness.ViewModel.ShowError);
        Assert.Null(harness.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_Forbidden_ShowsForbiddenState()
    {
        var harness = new Harness();
        harness.Api.GetReview = _ => Task.FromException<AdminReviewResponse>(
            ApiError(HttpStatusCode.Forbidden, "auth.forbidden"));

        await harness.ViewModel.InitializeAsync(ReviewId);

        Assert.True(harness.ViewModel.ShowForbidden);
        Assert.False(harness.ViewModel.ShowContent);
        Assert.False(harness.ViewModel.IsSignInRequired);
    }

    [Fact]
    public async Task InitializeAsync_NetworkFailure_ShowsTextAndRetryLoads()
    {
        var harness = new Harness();
        harness.Api.GetReview = _ => Task.FromException<AdminReviewResponse>(
            new HttpRequestException("refused"));

        await harness.ViewModel.InitializeAsync(ReviewId);

        Assert.True(harness.ViewModel.ShowError);
        Assert.Equal(NetworkErrorText, harness.ViewModel.ErrorMessage);

        harness.Api.GetReview = _ => Task.FromResult(ReviewOf());

        await harness.ViewModel.RetryCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.ShowError);
        Assert.True(harness.ViewModel.ShowContent);
    }

    [Fact]
    public async Task InitializeAsync_Unauthorized_ClearsSessionAndShowsSignIn()
    {
        var harness = new Harness();
        harness.Api.GetReview = _ => Task.FromException<AdminReviewResponse>(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.InitializeAsync(ReviewId);

        Assert.False(harness.Session.IsAuthenticated);
        Assert.True(harness.ViewModel.ShowSignInRequired);
        Assert.False(harness.ViewModel.ShowContent);
    }

    [Fact]
    public async Task InitializeAsync_SecondCall_ReadsCardOnlyOnce()
    {
        var harness = new Harness();

        await harness.ViewModel.InitializeAsync(ReviewId);
        await harness.ViewModel.InitializeAsync(ReviewId);

        Assert.Equal(1, harness.Api.GetCalls);
    }

    [Fact]
    public async Task PublishAsync_Review_SendsPublicationAndReturnsNull()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ReviewId);

        var error = await harness.ViewModel.PublishAsync();

        Assert.Null(error);
        Assert.Equal(1, harness.Api.PublishCalls);
        Assert.Equal(ReviewId, harness.Api.LastPublishedId);
        Assert.False(harness.ViewModel.IsPublishing);
        Assert.False(harness.ViewModel.HasActionError);
    }

    [Fact]
    public async Task PublishAsync_Forbidden_ReturnsForbiddenTextAndKeepsCard()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ReviewId);

        harness.Api.Publish = _ => Task.FromException(
            ApiError(HttpStatusCode.Forbidden, "auth.forbidden"));

        var error = await harness.ViewModel.PublishAsync();

        Assert.Equal("Раздел доступен только администратору", error);
        Assert.Equal(error, harness.ViewModel.ActionError);
        Assert.True(harness.ViewModel.HasActionError);
        Assert.True(harness.ViewModel.ShowContent);
        Assert.False(harness.ViewModel.IsPublishing);
    }

    [Fact]
    public async Task PublishAsync_NotFound_TreatsReviewAsHandled()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ReviewId);

        harness.Api.Publish = _ => Task.FromException(
            ApiError(HttpStatusCode.NotFound, "review.notFound"));

        var error = await harness.ViewModel.PublishAsync();

        // Отзыв уже разобрали: страница закроется, очередь перечитается, строки не будет.
        Assert.Null(error);
        Assert.False(harness.ViewModel.HasActionError);
    }

    [Fact]
    public async Task PublishAsync_NetworkFailure_ReturnsNetworkText()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ReviewId);

        harness.Api.Publish = _ => Task.FromException(new HttpRequestException("refused"));

        var error = await harness.ViewModel.PublishAsync();

        Assert.Equal(NetworkErrorText, error);
        Assert.Equal(NetworkErrorText, harness.ViewModel.ActionError);
    }

    [Fact]
    public async Task HideAsync_ConfirmedInPanel_SendsHideAndClosesPanel()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ReviewId);

        harness.ViewModel.OpenHideConfirmationCommand.Execute(null);

        Assert.True(harness.ViewModel.IsHideConfirmationOpen);
        Assert.False(harness.ViewModel.IsHideConfirmationClosed);

        var error = await harness.ViewModel.HideAsync();

        Assert.Null(error);
        Assert.Equal(1, harness.Api.HideCalls);
        Assert.Equal(ReviewId, harness.Api.LastHiddenId);
        Assert.False(harness.ViewModel.IsHideConfirmationOpen);
    }

    [Fact]
    public async Task HideAsync_RateLimited_ReturnsRateLimitText()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ReviewId);

        harness.Api.Hide = _ => Task.FromException(
            ApiError(HttpStatusCode.TooManyRequests, "auth.rateLimit"));

        var error = await harness.ViewModel.HideAsync();

        Assert.Equal("Слишком много запросов. Подождите минуту и повторите.", error);
    }

    [Fact]
    public async Task OpenHideConfirmation_ThenCancel_ClosesPanelWithoutHide()
    {
        var harness = new Harness();
        await harness.ViewModel.InitializeAsync(ReviewId);

        harness.ViewModel.OpenHideConfirmationCommand.Execute(null);

        // Панель — второй шаг решения: она только показывает последствие и ждёт ответа.
        Assert.True(harness.ViewModel.IsHideConfirmationOpen);

        harness.ViewModel.CancelHideConfirmationCommand.Execute(null);

        Assert.False(harness.ViewModel.IsHideConfirmationOpen);
        Assert.True(harness.ViewModel.IsHideConfirmationClosed);
        Assert.Equal(0, harness.Api.HideCalls);
    }

    [Fact]
    public async Task StartAutoRetry_TransportFailure_RetriesUntilSuccess()
    {
        var harness = new Harness();
        var calls = 0;

        harness.Api.GetReview = _ =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<AdminReviewResponse>(new HttpRequestException("refused"))
                : Task.FromResult(ReviewOf());
        };

        await harness.ViewModel.InitializeAsync(ReviewId);

        Assert.True(harness.ViewModel.ShowError);

        harness.ViewModel.StartAutoRetry();

        // Цикл останавливается сам, когда попытка удалась: ждать расписание не нужно.
        await harness.ViewModel.AutoRetryCompletion;

        Assert.Equal(2, calls);
        Assert.False(harness.ViewModel.ShowError);
        Assert.True(harness.ViewModel.ShowContent);
        Assert.False(harness.ViewModel.IsAutoRetrying);
    }

    private static AdminReviewResponse ReviewOf(
        int rating = 4,
        string? text = "Отличная стрижка",
        bool isPublished = false,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        decimal? barberRating = 4.8m,
        int reviewsCount = 12,
        bool isVerified = true,
        bool isActive = true)
    {
        var created = createdAt ?? new DateTimeOffset(2026, 11, 15, 10, 30, 0, TimeSpan.Zero);

        return new AdminReviewResponse(
            ReviewId,
            rating,
            text,
            isPublished,
            created,
            updatedAt ?? created,
            new AdminReviewBarberResponse(
                Guid.CreateVersion7(),
                "Иван Мастер",
                barberRating,
                reviewsCount,
                isActive,
                isVerified),
            new AdminReviewCustomerResponse(
                Guid.CreateVersion7(),
                "Дмитрий Крылов",
                "dmitry@example.com"),
            new AdminReviewVisitResponse(
                Guid.CreateVersion7(),
                new DateTimeOffset(2026, 11, 14, 22, 30, 0, TimeSpan.Zero),
                2400m,
                "Стрижка",
                "Салон «Тест»",
                "Europe/Moscow"));
    }

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, string errorCode) =>
        new(statusCode, "Request failed.", [errorCode], new Dictionary<string, string[]>());

    /// <summary>
    /// Цикл с мгновенной паузой: секунды расписания в тесте не тратятся.
    /// </summary>
    private static AutoRetryLoop FastRetry() =>
        new(
            static (_, _) => Task.CompletedTask,
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);

    /// <summary>
    /// Собранная карточка: сессия с парой, фальшивый клиент и открытый ViewModel.
    /// </summary>
    private sealed class Harness
    {
        public Harness()
        {
            Store = new FakeTokenStore
            {
                Tokens = new StoredTokens("access-1", "refresh-1"),
            };
            Session = new TokenSession(Store);

            Api.GetReview = _ => Task.FromResult(ReviewOf());

            ViewModel = new AdminReviewViewModel(Api, Session, FastRetry());
        }

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public FakeAdminReviewsApi Api { get; } = new();

        public AdminReviewViewModel ViewModel { get; }
    }

    /// <summary>
    /// Подменённый клиент админских отзывов: карточка, публикация и скрытие.
    /// </summary>
    private sealed class FakeAdminReviewsApi : IAdminReviewsApi
    {
        public int GetCalls { get; private set; }

        public int PublishCalls { get; private set; }

        public int HideCalls { get; private set; }

        public Guid? LastPublishedId { get; private set; }

        public Guid? LastHiddenId { get; private set; }

        public Func<Guid, Task<AdminReviewResponse>> GetReview { get; set; } =
            _ => Task.FromResult(ReviewOf());

        public Func<Guid, Task> Publish { get; set; } = _ => Task.CompletedTask;

        public Func<Guid, Task> Hide { get; set; } = _ => Task.CompletedTask;

        public Task<PagedResponse<AdminReviewResponse>> GetReviewsAsync(
            AdminReviewStatusFilter status,
            string? cursor,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Карточка не читает очередь.");

        public Task<AdminReviewResponse> GetReviewAsync(
            Guid reviewId,
            CancellationToken cancellationToken = default)
        {
            GetCalls++;

            return GetReview(reviewId);
        }

        public Task PublishReviewAsync(Guid reviewId, CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            LastPublishedId = reviewId;

            return Publish(reviewId);
        }

        public Task HideReviewAsync(Guid reviewId, CancellationToken cancellationToken = default)
        {
            HideCalls++;
            LastHiddenId = reviewId;

            return Hide(reviewId);
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
