using System.Net;
using BarberSearch.Contracts.Bookings.GetBookings;
using BarberSearch.Domain.Bookings;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Api.Reviews;
using BarberSearch.Mobile.Core.ViewModels;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки экрана «Отзыв о визите»: оценка, текст, счётчик и разбор ошибок сервера.
/// </summary>
/// <remarks>
/// Строки сверяются дословно с вебом и разделом «Тексты и форматы» <c>prompts/03-bookings.md</c>.
/// Отзыв оставляют только по завершённому визиту, поэтому строка списка — <c>Completed</c>
/// без уже оставленного отзыва; ошибки разбираются по кодам, потом по статусу.
/// </remarks>
public sealed class ReviewBookingViewModelTests
{
    private static readonly Guid BookingId = Guid.Parse("9e400000-0000-4000-8000-000000000001");
    private static readonly Guid ProfileId = Guid.Parse("9e400000-0000-4000-8000-000000000002");
    private static readonly Guid BarberServiceId = Guid.Parse("9e400000-0000-4000-8000-000000000003");
    private static readonly Guid SalonId = Guid.Parse("9e400000-0000-4000-8000-000000000004");
    private static readonly Guid ReviewId = Guid.Parse("9e400000-0000-4000-8000-00000000000a");

    private static readonly DateTimeOffset Now = new(2026, 11, 15, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task SubmitAsync_NoRating_MakesNoRequest()
    {
        var harness = new Harness();

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Поставьте оценку от 1 до 5.", harness.ViewModel.ErrorMessage);
        Assert.True(harness.ViewModel.HasError);
        Assert.Empty(harness.Api.Requests);
        Assert.False(harness.ViewModel.IsSubmitting);
    }

    [Fact]
    public void SelectRatingCommand_SameStarTwice_ClearsRating()
    {
        var harness = new Harness();

        harness.ViewModel.SelectRatingCommand.Execute(4);

        Assert.Equal(4, harness.ViewModel.Rating);
        Assert.True(harness.ViewModel.HasRating);
        Assert.Equal("4 / 5", harness.ViewModel.RatingText);

        harness.ViewModel.SelectRatingCommand.Execute(4);

        Assert.Equal(0, harness.ViewModel.Rating);
        Assert.False(harness.ViewModel.HasRating);
        Assert.Equal("не выбрана", harness.ViewModel.RatingText);
    }

    [Fact]
    public async Task SubmitAsync_WhitespaceText_SendsNull()
    {
        var harness = new Harness();
        harness.ViewModel.SelectRatingCommand.Execute(4);
        harness.ViewModel.Text = "   ";

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Api.Requests);
        Assert.Equal(BookingId, request.BookingId);
        Assert.Equal(4, request.Rating);
        Assert.Null(request.Text);
        Assert.Null(harness.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_Text_IsTrimmed()
    {
        var harness = new Harness();
        harness.ViewModel.SelectRatingCommand.Execute(5);
        harness.ViewModel.Text = "  Отличная стрижка  ";

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Api.Requests);
        Assert.Equal("Отличная стрижка", request.Text);
    }

    [Fact]
    public void TextCounterText_Text_ShowsLengthOfMaximum()
    {
        var harness = new Harness();

        Assert.Equal("0 / 2000", harness.ViewModel.TextCounterText);

        harness.ViewModel.Text = "Отзыв";

        Assert.Equal("5 / 2000", harness.ViewModel.TextCounterText);
    }

    [Fact]
    public async Task SubmitAsync_Success_RaisesSubmitted()
    {
        var harness = new Harness();
        harness.ViewModel.SelectRatingCommand.Execute(5);

        var raised = 0;
        harness.ViewModel.Submitted += (_, _) => raised++;

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(1, raised);
        Assert.False(harness.ViewModel.IsSubmitting);
        Assert.Null(harness.ViewModel.ErrorMessage);
        Assert.Single(harness.Api.Requests);
    }

    [Fact]
    public async Task SubmitAsync_BookingNotCompletedCodeOnConflict_ShowsCodeMessage()
    {
        var harness = new Harness();
        harness.ViewModel.SelectRatingCommand.Execute(5);
        harness.Api.Create = (_, _, _) => Task.FromException<Guid>(
            ApiError(HttpStatusCode.Conflict, "review.create.bookingNotCompleted"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(
            "Отзыв можно оставить только после завершённого визита.",
            harness.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_UniqueViolationOnConflict_ShowsAlreadyReviewedMessage()
    {
        var harness = new Harness();
        harness.ViewModel.SelectRatingCommand.Execute(5);
        harness.Api.Create = (_, _, _) => Task.FromException<Guid>(
            ApiError(HttpStatusCode.Conflict, "review.add.uniqueViolation"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(
            "По этой записи уже есть отзыв — второй оставить нельзя.",
            harness.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_Forbidden_ShowsCustomerOnlyMessage()
    {
        var harness = new Harness();
        harness.ViewModel.SelectRatingCommand.Execute(5);
        harness.Api.Create = (_, _, _) => Task.FromException<Guid>(
            ApiError(HttpStatusCode.Forbidden));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(
            "Оставить отзыв может только клиент этой записи.",
            harness.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_RatingCode_ShowsRatingMessage()
    {
        var harness = new Harness();
        harness.ViewModel.SelectRatingCommand.Execute(5);
        harness.Api.Create = (_, _, _) => Task.FromException<Guid>(
            ApiError(HttpStatusCode.BadRequest, "review.rating"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Оценка должна быть от 1 до 5.", harness.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_TextTooLongCode_ShowsTextMessage()
    {
        var harness = new Harness();
        harness.ViewModel.SelectRatingCommand.Execute(5);
        harness.Api.Create = (_, _, _) => Task.FromException<Guid>(
            ApiError(HttpStatusCode.BadRequest, "review.text.tooLong"));

        await harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(
            "Слишком длинный текст: максимум 2000 символов.",
            harness.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_DoubleCall_MakesOneRequest()
    {
        var harness = new Harness();
        harness.ViewModel.SelectRatingCommand.Execute(5);

        var pending = new TaskCompletionSource<Guid>();
        harness.Api.Create = (_, _, _) => pending.Task;

        var first = harness.ViewModel.SubmitCommand.ExecuteAsync(null);
        var second = harness.ViewModel.SubmitCommand.ExecuteAsync(null);

        pending.SetResult(ReviewId);
        await Task.WhenAll(first, second);

        Assert.Single(harness.Api.Requests);
        Assert.False(harness.ViewModel.IsSubmitting);
    }

    /// <summary>
    /// Строка списка: завершённый визит, отзыва ещё нет.
    /// </summary>
    private static BookingListItem CreateItem() =>
        new(
            new BookingResponse(
                BookingId,
                Guid.CreateVersion7(),
                ProfileId,
                BarberServiceId,
                SalonId,
                Now.AddHours(-1),
                Now,
                2400m,
                BookingStatus.Completed,
                "idempotency-key-1",
                null,
                null,
                null,
                Now.AddDays(-1),
                false),
            Now);

    private static BarberSearchApiException ApiError(HttpStatusCode statusCode, params string[] codes) =>
        new(statusCode, "Request failed.", codes, new Dictionary<string, string[]>());

    /// <summary>
    /// Подменённый клиент отзывов: запоминает бронь, оценку и текст, отдаёт заданный результат.
    /// </summary>
    private sealed class FakeReviewsApi : IReviewsApi
    {
        public List<ReviewRequest> Requests { get; } = [];

        public Func<Guid, int, string?, Task<Guid>> Create { get; set; } =
            (_, _, _) => Task.FromResult(ReviewId);

        public Task<Guid> CreateReviewAsync(
            Guid bookingId,
            int rating,
            string? text,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(new ReviewRequest(bookingId, rating, text));

            return Create(bookingId, rating, text);
        }
    }

    /// <summary>
    /// Тело отправленного отзыва: текст либо <c>null</c>, если комментария нет.
    /// </summary>
    private sealed record ReviewRequest(Guid BookingId, int Rating, string? Text);

    /// <summary>
    /// Собранный экран: подменённый клиент и ViewModel с уже открытым визитом.
    /// </summary>
    private sealed class Harness
    {
        public Harness()
        {
            ViewModel = new ReviewBookingViewModel(Api);
            ViewModel.Initialize(CreateItem());
        }

        public FakeReviewsApi Api { get; } = new();

        public ReviewBookingViewModel ViewModel { get; }
    }
}
