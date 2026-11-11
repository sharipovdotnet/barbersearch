using BarberSearch.Domain;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.PortfolioPhotos;
using Shared;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Премодерация кадра портфолио: новый кадр ждёт проверки, администратор одобряет его или
/// отклоняет с причиной, а действия владельца (публикация, скрытие, порядок) статус
/// модерации не меняют.
/// </summary>
/// <remarks>
/// Публичная видимость кадра — это три независимых признака: <c>ModerationStatus</c>
/// (допуск администратора), <c>IsPublished</c> (намерение владельца) и <c>IsBlocked</c>
/// (административная маска). Тесты фиксируют, что решения администратора и действия
/// владельца не подменяют друг друга.
/// </remarks>
public class PortfolioPhotoModerationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StartsPendingWithoutReviewOrReason()
    {
        var photo = AddPhoto();

        Assert.Equal(PhotoModerationStatus.Pending, photo.ModerationStatus);
        Assert.Null(photo.RejectionReason);
        Assert.Null(photo.ReviewedAt);
        Assert.Null(photo.ReviewedByUserId);

        // Намерение владельца опубликовать кадр сохраняется: его отсекает модерация.
        Assert.True(photo.IsPublished);
        Assert.False(photo.IsBlocked);
    }

    [Fact]
    public void AddUploadedPhoto_StartsPending()
    {
        var profile = CreateProfile();
        var url = PortfolioMediaUrl.Build(profile.Id, Guid.CreateVersion7(), "jpg");

        Assert.True(profile.AddUploadedPhoto(url, null).IsSuccess);

        var photo = Assert.Single(profile.PortfolioPhotos);

        Assert.Equal(PhotoModerationStatus.Pending, photo.ModerationStatus);
        Assert.Null(photo.ReviewedAt);
    }

    [Fact]
    public void Approve_FromPending_ApprovesAndRecordsReview()
    {
        var photo = AddPhoto();
        var adminId = Guid.CreateVersion7();
        var before = photo.UpdatedAt;

        var result = photo.Approve(adminId, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(PhotoModerationStatus.Approved, photo.ModerationStatus);
        Assert.Equal(adminId, photo.ReviewedByUserId);
        Assert.Equal(Now, photo.ReviewedAt);
        Assert.Null(photo.RejectionReason);
        Assert.True(photo.UpdatedAt >= before);
    }

    [Fact]
    public void Approve_ClearsLeftoverRejectionReason()
    {
        var photo = AddPhoto();

        // В рабочем сценарии Pending и непустая причина вместе не встречаются, но очистка
        // причины при одобрении — часть контракта. Ставим её в обход сеттера, как EF Core.
        SetPrivateProperty(photo, nameof(PortfolioPhoto.RejectionReason), "Privately seeded reason");
        Assert.Equal("Privately seeded reason", photo.RejectionReason);

        Assert.True(photo.Approve(Guid.CreateVersion7(), Now).IsSuccess);
        Assert.Null(photo.RejectionReason);
    }

    [Fact]
    public void Approve_WhenAlreadyApproved_ReturnsConflict()
    {
        var photo = AddPhoto();
        photo.Approve(Guid.CreateVersion7(), Now);

        var result = photo.Approve(Guid.CreateVersion7(), Now.AddMinutes(1));

        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Error.Errors);

        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal("portfolioPhoto.moderation.invalidStatus", error.Code);
        Assert.Equal(Now, photo.ReviewedAt);
    }

    [Fact]
    public void Approve_WhenRejected_ReturnsConflict()
    {
        var photo = AddPhoto();
        photo.Reject(Guid.CreateVersion7(), "Нет резкости", Now);

        var result = photo.Approve(Guid.CreateVersion7(), Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("portfolioPhoto.moderation.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(PhotoModerationStatus.Rejected, photo.ModerationStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithoutReason_ReturnsValidation(string? reason)
    {
        var photo = AddPhoto();

        var result = photo.Reject(Guid.CreateVersion7(), reason, Now);

        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Error.Errors);

        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal("portfolioPhoto.moderation.reasonRequired", error.Code);
        Assert.Equal("reason", error.InvalidField);
        Assert.Equal(PhotoModerationStatus.Pending, photo.ModerationStatus);
        Assert.Null(photo.RejectionReason);
    }

    [Fact]
    public void Reject_WithTooLongReason_ReturnsValidation()
    {
        var photo = AddPhoto();
        var reason = new string('a', Constants.MAX_PHOTO_REJECTION_REASON_LENGTH + 1);

        var result = photo.Reject(Guid.CreateVersion7(), reason, Now);

        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Error.Errors);

        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal("portfolioPhoto.moderation.reasonTooLong", error.Code);
        Assert.Equal("reason", error.InvalidField);
        Assert.Equal(PhotoModerationStatus.Pending, photo.ModerationStatus);
        Assert.Null(photo.RejectionReason);
    }

    [Fact]
    public void Reject_WithValidReason_TrimsAndStoresDecision()
    {
        var photo = AddPhoto();
        var adminId = Guid.CreateVersion7();

        var result = photo.Reject(adminId, "  Нет резкости  ", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(PhotoModerationStatus.Rejected, photo.ModerationStatus);
        Assert.Equal("Нет резкости", photo.RejectionReason);
        Assert.Equal(adminId, photo.ReviewedByUserId);
        Assert.Equal(Now, photo.ReviewedAt);
    }

    [Fact]
    public void Reject_WhenAlreadyRejected_ReturnsConflict()
    {
        var photo = AddPhoto();
        photo.Reject(Guid.CreateVersion7(), "Нет резкости", Now);

        var result = photo.Reject(Guid.CreateVersion7(), "Ещё раз", Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("portfolioPhoto.moderation.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal("Нет резкости", photo.RejectionReason);
    }

    [Fact]
    public void Reject_WithReasonAtLimit_Succeeds()
    {
        var photo = AddPhoto();
        var reason = new string('a', Constants.MAX_PHOTO_REJECTION_REASON_LENGTH);

        var result = photo.Reject(Guid.CreateVersion7(), reason, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Constants.MAX_PHOTO_REJECTION_REASON_LENGTH, photo.RejectionReason!.Length);
    }

    [Fact]
    public void PublishAndUnpublish_DoNotChangeModerationStatus()
    {
        var pending = AddPhoto();

        Assert.True(pending.Unpublish().IsSuccess);
        Assert.True(pending.Publish().IsSuccess);

        Assert.Equal(PhotoModerationStatus.Pending, pending.ModerationStatus);
        Assert.Null(pending.ReviewedAt);

        pending.Approve(Guid.CreateVersion7(), Now);

        Assert.True(pending.Unpublish().IsSuccess);
        Assert.True(pending.Publish().IsSuccess);

        Assert.Equal(PhotoModerationStatus.Approved, pending.ModerationStatus);
        Assert.Equal(Now, pending.ReviewedAt);
    }

    [Fact]
    public void BlockAndUnblock_DoNotChangeModerationStatus()
    {
        var pending = AddPhoto();

        Assert.True(pending.Block().IsSuccess);
        Assert.True(pending.Unblock().IsSuccess);

        Assert.Equal(PhotoModerationStatus.Pending, pending.ModerationStatus);

        pending.Reject(Guid.CreateVersion7(), "Нет резкости", Now);

        Assert.True(pending.Block().IsSuccess);
        Assert.True(pending.Unblock().IsSuccess);

        Assert.Equal(PhotoModerationStatus.Rejected, pending.ModerationStatus);
        Assert.Equal("Нет резкости", pending.RejectionReason);
    }

    [Fact]
    public void Reorder_DoesNotChangeModerationStatus()
    {
        var profile = CreateProfile();
        profile.AddPhoto("https://cdn.example.com/1.jpg");
        profile.AddPhoto("https://cdn.example.com/2.jpg");

        var photos = profile.PortfolioPhotos.ToList();
        photos[0].Reject(Guid.CreateVersion7(), "Нет резкости", Now);

        Assert.True(profile.ReorderPhotos(photos.Select(photo => photo.Id).Reverse().ToArray()).IsSuccess);

        Assert.Equal(PhotoModerationStatus.Rejected, photos[0].ModerationStatus);
        Assert.Equal(PhotoModerationStatus.Pending, photos[1].ModerationStatus);
    }

    private static PortfolioPhoto AddPhoto()
    {
        var profile = CreateProfile();
        Assert.True(profile.AddPhoto("https://cdn.example.com/1.jpg").IsSuccess);

        return Assert.Single(profile.PortfolioPhotos);
    }

    private static BarberProfile CreateProfile()
        => BarberProfile.Create(
            Guid.CreateVersion7(),
            "Иван Петров",
            5,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null).Value;

    /// <summary>
    /// Проставляет свойство в обход приватного сеттера — так же, как это делает EF Core.
    /// </summary>
    private static void SetPrivateProperty<T>(T entity, string propertyName, object? value)
        => typeof(T).GetProperty(propertyName)!.SetValue(entity, value);
}
