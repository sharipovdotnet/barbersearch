using System.Net;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Reviews.GetMyReviews;
using BarberSearch.Contracts.Reviews.UpdateReview;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Reviews;
using BarberSearch.Mobile.Core.Auth;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Изменить отзыв»: звёзды, текст, подсказка по статусу и разбор ошибок.
/// </summary>
/// <remarks>
/// Строка списка приходит объектом маршрута, поэтому экран ничего не читает. Статус после
/// правки берётся только из ответа <c>PUT</c>: правило «правка текста возвращает на
/// модерацию» живёт на сервере, и клиент не должен его повторять.
/// </remarks>
public sealed class EditReviewViewModelTests
{
    private const string PublishedHint =
        "Оценку можно изменить сразу: повторная проверка нужна только для текста.";
    private const string ResubmissionHint =
        "Изменение текста вернёт отзыв на проверку: до одобрения он не покажется на карточке барбера.";
    private const string ModerationHint =
        "Отзыв на проверке: изменения появятся на карточке после одобрения.";

    [Fact]
    public void Initialize_PublishedReview_ShowsRatingAndUnchangedHint()
    {
        var harness = new Harness();
        harness.ViewModel.Initialize(ItemOf(rating: 4, text: "Хорошо", isPublished: true));

        Assert.Equal(4, harness.ViewModel.Rating);
        Assert.Equal("Хорошо", harness.ViewModel.Text);
        Assert.Equal("4 / 5", harness.ViewModel.RatingText);
        Assert.Equal(PublishedHint, harness.ViewModel.StatusHint);
        Assert.True(harness.ViewModel.Star4Selected);
        Assert.False(harness.ViewModel.Star5Selected);
        Assert.Equal("6 / 2000", harness.ViewModel.TextCounterText);
    }

    [Fact]
    public void Initialize_ReviewOnModeration_ShowsModerationHint()
    {
        var harness = new Harness();
        harness.ViewModel.Initialize(ItemOf(isPublished: false));

        Assert.Equal(ModerationHint, harness.ViewModel.StatusHint);
    }

    [Fact]
    public void TextEdit_ChangesPublishedHintToResubmission()
    {
        var harness = new Harness();
        harness.ViewModel.Initialize(ItemOf(text: "Хорошо", isPublished: true));

        harness.ViewModel.Text = "Хорошо, но можно лучше";

        Assert.Equal(ResubmissionHint, harness.ViewModel.StatusHint);
    }

    [Fact]
    public async Task SaveAsync_WithoutRating_ShowsError()
    {
        var harness = new Harness();
        harness.ViewModel.Initialize(ItemOf());
        harness.ViewModel.Rating = 0;

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Оценка должна быть от 1 до 5.", harness.ViewModel.ErrorMessage);
        Assert.Equal(0, harness.ReviewsApi.UpdateCalls);
    }

    [Fact]
    public async Task SaveAsync_WhitespaceText_SendsNull()
    {
        var harness = new Harness();
        harness.ViewModel.Initialize(ItemOf(text: "Старый"));
        harness.ViewModel.Text = "   ";

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.ReviewsApi.UpdateRequests);
        Assert.Null(request.Text);
        Assert.Null(harness.ViewModel.NormalizedText);
        Assert.Equal(1, harness.SavedCount);
    }

    [Fact]
    public async Task SaveAsync_TooLongText_ShowsError()
    {
        var harness = new Harness();
        harness.ViewModel.Initialize(ItemOf());
        harness.ViewModel.Text = new string('а', 2001);

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Слишком длинный текст: максимум 2000 символов.", harness.ViewModel.ErrorMessage);
        Assert.Equal(0, harness.ReviewsApi.UpdateCalls);
    }

    [Fact]
    public async Task SaveAsync_Success_TrimsTextAndRaisesSavedWithResponse()
    {
        var harness = new Harness();
        harness.ViewModel.Initialize(ItemOf(rating: 5, text: "Старый"));
        harness.ViewModel.Rating = 3;
        harness.ViewModel.Text = "  Новый текст  ";

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.ReviewsApi.UpdateRequests);
        Assert.Equal(3, request.Rating);
        Assert.Equal("Новый текст", request.Text);

        Assert.Equal(1, harness.SavedCount);
        Assert.NotNull(harness.SavedResponse);
        Assert.False(harness.SavedResponse!.IsPublished);
        Assert.Null(harness.ViewModel.ErrorMessage);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "review.update.concurrencyConflict", "Отзыв изменился в другой вкладке — обновите список.")]
    [InlineData(HttpStatusCode.NotFound, "review.notFound", "Отзыв больше не существует.")]
    [InlineData(HttpStatusCode.Forbidden, "auth.forbidden", "Править отзыв может только его автор.")]
    [InlineData(HttpStatusCode.BadRequest, "review.update.rating", "Оценка должна быть от 1 до 5.")]
    [InlineData(HttpStatusCode.BadRequest, "review.update.text.tooLong", "Слишком длинный текст: максимум 2000 символов.")]
    [InlineData(HttpStatusCode.TooManyRequests, "auth.rateLimit", "Слишком много запросов. Подождите минуту и повторите.")]
    [InlineData(HttpStatusCode.InternalServerError, "internal.database", "Не удалось сохранить отзыв.")]
    public async Task SaveAsync_ServerFailure_ShowsWebText(
        HttpStatusCode statusCode,
        string code,
        string expected)
    {
        var harness = new Harness();
        harness.ViewModel.Initialize(ItemOf());
        harness.ReviewsApi.Update = (_, _, _) => Task.FromException<UpdateReviewResponse>(
            ApiError(statusCode, code));

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(expected, harness.ViewModel.ErrorMessage);
        Assert.Equal(0, harness.SavedCount);
    }

    [Fact]
    public async Task SaveAsync_ConflictWithoutCode_StillShowsConflictText()
    {
        var harness = new Harness();
        harness.ViewModel.Initialize(ItemOf());
        harness.ReviewsApi.Update = (_, _, _) => Task.FromException<UpdateReviewResponse>(
            ApiError(HttpStatusCode.Conflict, "review.update.other"));

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Отзыв изменился в другой вкладке — обновите список.", harness.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task SaveAsync_NetworkFailure_ShowsNetworkText()
    {
        var harness = new Harness();
        harness.ViewModel.Initialize(ItemOf());
        harness.ReviewsApi.Update = (_, _, _) => Task.FromException<UpdateReviewResponse>(
            new HttpRequestException("refused"));

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(
            "Нет связи с сервером. Проверьте подключение и повторите попытку.",
            harness.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task SaveAsync_DoubleTap_SendsOnce()
    {
        var harness = new Harness();
        harness.ViewModel.Initialize(ItemOf());
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.ReviewsApi.Update = async (_, _, _) =>
        {
            await pending.Task;

            return new UpdateReviewResponse(true, DateTimeOffset.UtcNow);
        };

        var first = harness.ViewModel.SaveCommand.ExecuteAsync(null);
        var second = harness.ViewModel.SaveCommand.ExecuteAsync(null);

        pending.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, harness.ReviewsApi.UpdateCalls);
    }

    [Fact]
    public async Task SaveAsync_Unauthorized_EndsSession()
    {
        var harness = new Harness();
        harness.ViewModel.Initialize(ItemOf());
        harness.ReviewsApi.Update = (_, _, _) => Task.FromException<UpdateReviewResponse>(
            ApiError(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(harness.Session.IsAuthenticated);
    }

    [Fact]
    public void SelectRating_UpdatesRatingText()
    {
        var harness = new Harness();
        harness.ViewModel.Initialize(ItemOf(rating: 1));

        harness.ViewModel.SelectRatingCommand.Execute(3);

        Assert.Equal(3, harness.ViewModel.Rating);
        Assert.Equal("3 / 5", harness.ViewModel.RatingText);
        Assert.False(harness.ViewModel.Star4Selected);
    }

    private static MyReviewListItem ItemOf(
        int rating = 5,
        string? text = "Отлично",
        bool isPublished = true) =>
        new(new MyReviewResponse(
            Guid.CreateVersion7(),
            rating,
            text,
            isPublished,
            new DateTimeOffset(2026, 11, 15, 10, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 15, 10, 30, 0, TimeSpan.Zero),
            new MyReviewBarberResponse(Guid.CreateVersion7(), "Игорь Барбер"),
            new MyReviewVisitResponse(
                Guid.CreateVersion7(),
                new DateTimeOffset(2026, 11, 15, 10, 30, 0, TimeSpan.Zero),
                "UTC",
                2400m,
                "Стрижка",
                "Барбершоп «Тест»")));

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, string errorCode) =>
        new(statusCode, "Request failed.", [errorCode], new Dictionary<string, string[]>());

    /// <summary>
    /// Собранный экран: сессия, фальшивый клиент отзывов и открытый ViewModel.
    /// </summary>
    private sealed class Harness
    {
        public Harness()
        {
            Store = new FakeTokenStore { Tokens = new StoredTokens("access-1", "refresh-1") };
            Session = new TokenSession(Store);
            ViewModel = new EditReviewViewModel(ReviewsApi, Session);

            ViewModel.Saved += (_, response) =>
            {
                SavedCount++;
                SavedResponse = response;
            };
        }

        public FakeTokenStore Store { get; }

        public TokenSession Session { get; }

        public FakeReviewsApi ReviewsApi { get; } = new();

        public EditReviewViewModel ViewModel { get; }

        public int SavedCount { get; private set; }

        public UpdateReviewResponse? SavedResponse { get; private set; }
    }

    /// <summary>
    /// Подменённый клиент отзывов: правка с запоминанием тела запроса.
    /// </summary>
    private sealed class FakeReviewsApi : IReviewsApi
    {
        public int UpdateCalls { get; private set; }

        public List<UpdateRequest> UpdateRequests { get; } = [];

        public Func<Guid, int, string?, Task<UpdateReviewResponse>> Update { get; set; } =
            (_, _, _) => Task.FromResult(new UpdateReviewResponse(false, DateTimeOffset.UtcNow));

        public Task<Guid> CreateReviewAsync(
            Guid bookingId,
            int rating,
            string? text,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран правки не создаёт отзывы.");

        public Task<PagedResponse<MyReviewResponse>> GetMyReviewsAsync(
            string? cursor,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран правки не читает список отзывов.");

        public Task<UpdateReviewResponse> UpdateReviewAsync(
            Guid reviewId,
            int rating,
            string? text,
            CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            UpdateRequests.Add(new UpdateRequest(reviewId, rating, text));

            return Update(reviewId, rating, text);
        }

        public Task DeleteReviewAsync(Guid reviewId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Экран правки не удаляет отзыв.");
    }

    /// <summary>
    /// Тело правки отзыва.
    /// </summary>
    private sealed record UpdateRequest(Guid ReviewId, int Rating, string? Text);

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
