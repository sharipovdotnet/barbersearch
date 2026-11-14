using BarberSearch.Contracts.Reviews.GetMyReviews;
using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки текстов отзыва клиента: статус, звёзды, оценка, дата визита и тело отзыва.
/// </summary>
/// <remarks>
/// <para>
/// Строки сверяются дословно с вебом (<c>frontend/src/components/account/my-review-row.tsx</c>,
/// <c>review-status-badge.tsx</c>, <c>lib/format.ts</c>): одна копия текста на два клиента.
/// </para>
/// <para>
/// Пояс салона передаётся параметром и здесь фиксирован (+03:00): тогда 22:30 UTC 14 ноября —
/// это уже 15 ноября, 01:30, и тест ловит именно перевод даты, а не совпадение с поясом машины.
/// </para>
/// </remarks>
public sealed class ReviewTextTests
{
    /// <summary>
    /// Фиксированный пояс салона +03:00: результат не зависит от пояса машины.
    /// </summary>
    private static readonly TimeZoneInfo PlusThreeHours = TimeZoneInfo.CreateCustomTimeZone(
        "Test",
        TimeSpan.FromHours(3),
        "Test",
        "Test");

    /// <summary>
    /// 14 ноября 2026, 22:30 UTC — это 15 ноября, 01:30 в поясе салона.
    /// </summary>
    private static readonly DateTimeOffset Instant = new(2026, 11, 14, 22, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Status_Published_ShowsPublishedCaption() =>
        Assert.Equal(ReviewText.Published, ReviewText.Status(true));

    [Fact]
    public void Status_NotPublished_ShowsModerationCaption() =>
        // false означает и «ждёт модерации», и «скрыт администратором»: различить их нельзя,
        // поэтому «На модерации» — честный ответ на оба случая, а не обещание «скрыт».
        Assert.Equal(ReviewText.OnModeration, ReviewText.Status(false));

    [Theory]
    [InlineData(1, "★☆☆☆☆")]
    [InlineData(2, "★★☆☆☆")]
    [InlineData(3, "★★★☆☆")]
    [InlineData(4, "★★★★☆")]
    [InlineData(5, "★★★★★")]
    public void Stars_RatingFromOneToFive_ShowsFilledAndEmptyStars(int rating, string expected) =>
        Assert.Equal(expected, ReviewText.Stars(rating));

    [Theory]
    [InlineData(0, "☆☆☆☆☆")]
    [InlineData(7, "★★★★★")]
    public void Stars_RatingOutsideRange_ClampsToFiveCharacters(int rating, string expected) =>
        // Оценка вне диапазона обрезается: пустых звёзд всегда пять, иначе строка «поедет».
        Assert.Equal(expected, ReviewText.Stars(rating));

    [Fact]
    public void Rating_FullScore_ShowsScoreAndMaximum() =>
        Assert.Equal("5 / 5", ReviewText.Rating(5));

    [Fact]
    public void Rating_Four_ShowsScoreAndMaximum() =>
        Assert.Equal("4 / 5", ReviewText.Rating(4));

    [Fact]
    public void ReviewDate_SalonZone_ShowsDateInSalonZone() =>
        // Дата отзыва тоже считается в поясе салона: у 22:30 UTC уже наступило следующее число.
        Assert.Equal("15 ноября 2026", ReviewText.ReviewDate(Instant, PlusThreeHours));

    [Fact]
    public void ReviewDate_UnknownZone_ShowsUtcDate() =>
        // Неизвестный пояс не роняет экран: дата показывается в UTC, как и остальные экраны.
        Assert.Equal("14 ноября 2026", ReviewText.ReviewDate(Instant, null));

    [Fact]
    public void VisitMoment_SalonZone_ShowsDateAndTime() =>
        Assert.Equal("15 ноября 2026 в 01:30", ReviewText.VisitMoment(Instant, PlusThreeHours));

    [Fact]
    public void VisitLine_CompleteVisit_BuildsSingleLine() =>
        // «2 400 ₽» собрано с неразрывными пробелами (U+00A0) между разрядами и перед знаком
        // рубля: цена не должна переноситься, а разделитель полей — « · ».
        Assert.Equal(
            "15 ноября 2026 в 01:30 · Стрижка · Салон «Тест» · 2\u00A0400\u00A0₽",
            ReviewText.VisitLine(Visit(), PlusThreeHours));

    [Theory]
    [InlineData(null, "Без комментария.")]
    [InlineData("  ", "Без комментария.")]
    [InlineData("Текст", "Текст")]
    [InlineData("  Текст  ", "Текст")]
    public void Body_CommentOrBlank_ReturnsTextOrPlaceholder(string? text, string expected) =>
        // Пробельный комментарий — тот же пустой: строка «   » в списке выглядела бы дефектом.
        Assert.Equal(expected, ReviewText.Body(text));

    /// <summary>
    /// Визит из ответа API: услуга, салон и цена заполнены.
    /// </summary>
    private static MyReviewVisitResponse Visit() => new(
        Guid.Parse("9e100000-0000-4000-8000-0000000000c1"),
        Instant,
        "Europe/Moscow",
        2400m,
        "Стрижка",
        "Салон «Тест»");
}
