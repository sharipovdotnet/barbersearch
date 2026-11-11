using BarberSearch.Domain;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using Shared;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Премодерация аватара: реальная загрузка переводит кадр в <c>Pending</c>, удаление
/// возвращает нейтральное состояние, администратор одобряет кадр или отклоняет с причиной,
/// а повторение того же URL ничего не меняет.
/// </summary>
/// <remarks>
/// Публичный аватар — это <c>AvatarModerationStatus = Approved</c> и
/// <c>IsAvatarBlocked = false</c>. Премодерация не заменяет блокировку #87: блокировать
/// по-прежнему можно только существующий кадр, а повторная блокировка идемпотентна.
/// </remarks>
public class UserAvatarModerationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StartsWithApprovedStatusAndNoDates()
    {
        var user = CreateUser();

        Assert.Equal(PhotoModerationStatus.Approved, user.AvatarModerationStatus);
        Assert.Null(user.AvatarSubmittedAt);
        Assert.Null(user.AvatarReviewedAt);
        Assert.Null(user.AvatarReviewedByUserId);
        Assert.Null(user.AvatarRejectionReason);
    }

    [Fact]
    public void SetAvatar_WithNewUrl_StartsPendingAndStampsSubmission()
    {
        var user = CreateUser();
        var before = DateTimeOffset.UtcNow;

        var result = user.SetAvatar("https://cdn.example.com/avatar.png");

        var after = DateTimeOffset.UtcNow;

        Assert.True(result.IsSuccess);
        Assert.Equal(PhotoModerationStatus.Pending, user.AvatarModerationStatus);
        Assert.NotNull(user.AvatarSubmittedAt);
        Assert.InRange(user.AvatarSubmittedAt!.Value, before, after);
        Assert.Null(user.AvatarReviewedAt);
        Assert.Null(user.AvatarReviewedByUserId);
        Assert.Null(user.AvatarRejectionReason);
    }

    [Fact]
    public void SetAvatar_WithNewUrlAfterBlock_ClearsBlockAndStartsPending()
    {
        var user = CreateUser();
        Assert.True(user.SetAvatar("https://cdn.example.com/first.png").IsSuccess);
        Assert.True(user.BlockAvatar().IsSuccess);

        Assert.True(user.SetAvatar("https://cdn.example.com/second.png").IsSuccess);

        Assert.False(user.IsAvatarBlocked);
        Assert.Equal(PhotoModerationStatus.Pending, user.AvatarModerationStatus);
    }

    [Fact]
    public void SetAvatar_WithNull_ResetsToApprovedAndClearsReview()
    {
        var user = CreateUser();
        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);
        Assert.True(user.RejectAvatar(Guid.CreateVersion7(), "Нет резкости", Now).IsSuccess);

        Assert.True(user.SetAvatar(null).IsSuccess);

        Assert.Null(user.AvatarUrl);
        Assert.Equal(PhotoModerationStatus.Approved, user.AvatarModerationStatus);
        Assert.Null(user.AvatarSubmittedAt);
        Assert.Null(user.AvatarReviewedAt);
        Assert.Null(user.AvatarReviewedByUserId);
        Assert.Null(user.AvatarRejectionReason);
        Assert.False(user.IsAvatarBlocked);
    }

    [Fact]
    public void SetAvatar_WithSameUrl_KeepsModerationStateAndUpdatedAt()
    {
        var user = CreateUser();
        var avatarUrl = "https://cdn.example.com/avatar.png";
        Assert.True(user.SetAvatar(avatarUrl).IsSuccess);
        Assert.True(user.ApproveAvatar(Guid.CreateVersion7(), Now).IsSuccess);

        var submittedAt = user.AvatarSubmittedAt;
        var updatedAt = user.UpdatedAt;

        // Повторная запись того же URL — не новая публикация: ни статус, ни метки,
        // ни UpdatedAt не меняются.
        Assert.True(user.SetAvatar(avatarUrl).IsSuccess);

        Assert.Equal(PhotoModerationStatus.Approved, user.AvatarModerationStatus);
        Assert.Equal(submittedAt, user.AvatarSubmittedAt);
        Assert.Equal(Now, user.AvatarReviewedAt);
        Assert.Equal(updatedAt, user.UpdatedAt);
    }

    [Fact]
    public void UpdateProfile_WithNewUrl_StartsPending()
    {
        var user = CreateUser();

        var updated = user.UpdateProfile(
            "Ivan",
            "Petrov",
            null,
            "https://cdn.example.com/avatar.png");

        Assert.True(updated.IsSuccess);
        Assert.Equal(PhotoModerationStatus.Pending, user.AvatarModerationStatus);
        Assert.NotNull(user.AvatarSubmittedAt);
    }

    [Fact]
    public void UpdateProfile_WithSameUrl_KeepsModerationStateAndBlock()
    {
        var user = CreateUser();
        var avatarUrl = "https://cdn.example.com/avatar.png";
        Assert.True(user.SetAvatar(avatarUrl).IsSuccess);
        Assert.True(user.ApproveAvatar(Guid.CreateVersion7(), Now).IsSuccess);
        Assert.True(user.BlockAvatar().IsSuccess);

        var submittedAt = user.AvatarSubmittedAt;

        // Сохранение имени отправляет текущий avatarUrl обратно: статус, метки и
        // блокировка обязаны сохраниться.
        var updated = user.UpdateProfile("Ivan", "Petrov", null, avatarUrl);

        Assert.True(updated.IsSuccess);
        Assert.Equal(PhotoModerationStatus.Approved, user.AvatarModerationStatus);
        Assert.Equal(submittedAt, user.AvatarSubmittedAt);
        Assert.Equal(Now, user.AvatarReviewedAt);
        Assert.NotNull(user.AvatarReviewedByUserId);
        Assert.True(user.IsAvatarBlocked);
    }

    [Fact]
    public void ApproveAvatar_WithoutAvatar_ReturnsNoAvatarConflict()
    {
        var user = CreateUser();

        var result = user.ApproveAvatar(Guid.CreateVersion7(), Now);

        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Error.Errors);

        Assert.Equal(ErrorType.CONFLICT, error.Type);
        Assert.Equal("user.avatarModeration.noAvatar", error.Code);
        Assert.Equal(PhotoModerationStatus.Approved, user.AvatarModerationStatus);
    }

    [Fact]
    public void ApproveAvatar_FromPending_ApprovesAndRecordsReview()
    {
        var user = CreateUser();
        var avatarUrl = "https://cdn.example.com/avatar.png";
        Assert.True(user.SetAvatar(avatarUrl).IsSuccess);
        var adminId = Guid.CreateVersion7();

        var result = user.ApproveAvatar(adminId, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(PhotoModerationStatus.Approved, user.AvatarModerationStatus);
        Assert.Equal(adminId, user.AvatarReviewedByUserId);
        Assert.Equal(Now, user.AvatarReviewedAt);
        Assert.Null(user.AvatarRejectionReason);
        Assert.Equal(avatarUrl, user.AvatarUrl);
    }

    [Fact]
    public void ApproveAvatar_ClearsLeftoverRejectionReason()
    {
        var user = CreateUser();
        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);

        // В рабочем сценарии Pending и непустая причина вместе не встречаются, но очистка
        // причины при одобрении — часть контракта. Ставим её в обход сеттера, как EF Core.
        SetPrivateProperty(user, nameof(User.AvatarRejectionReason), "Privately seeded reason");

        Assert.True(user.ApproveAvatar(Guid.CreateVersion7(), Now).IsSuccess);
        Assert.Null(user.AvatarRejectionReason);
    }

    [Fact]
    public void ApproveAvatar_WhenAlreadyApproved_ReturnsInvalidStatus()
    {
        var user = CreateUser();
        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);
        Assert.True(user.ApproveAvatar(Guid.CreateVersion7(), Now).IsSuccess);

        var result = user.ApproveAvatar(Guid.CreateVersion7(), Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, Assert.Single(result.Error.Errors).Type);
        Assert.Equal("user.avatarModeration.invalidStatus", result.Error.Errors[0].Code);
    }

    [Fact]
    public void ApproveAvatar_WhenRejected_ReturnsInvalidStatus()
    {
        var user = CreateUser();
        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);
        Assert.True(user.RejectAvatar(Guid.CreateVersion7(), "Нет резкости", Now).IsSuccess);

        var result = user.ApproveAvatar(Guid.CreateVersion7(), Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, Assert.Single(result.Error.Errors).Type);
        Assert.Equal("user.avatarModeration.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(PhotoModerationStatus.Rejected, user.AvatarModerationStatus);
    }

    [Fact]
    public void RejectAvatar_WithoutAvatar_ReturnsNoAvatarConflict()
    {
        var user = CreateUser();

        var result = user.RejectAvatar(Guid.CreateVersion7(), "Нет резкости", Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, Assert.Single(result.Error.Errors).Type);
        Assert.Equal("user.avatarModeration.noAvatar", result.Error.Errors[0].Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectAvatar_WithoutReason_ReturnsValidation(string? reason)
    {
        var user = CreateUser();
        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);

        var result = user.RejectAvatar(Guid.CreateVersion7(), reason, Now);

        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Error.Errors);

        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal("user.avatarModeration.reasonRequired", error.Code);
        Assert.Equal("reason", error.InvalidField);
        Assert.Equal(PhotoModerationStatus.Pending, user.AvatarModerationStatus);
        Assert.Null(user.AvatarRejectionReason);
    }

    [Fact]
    public void RejectAvatar_WithTooLongReason_ReturnsValidation()
    {
        var user = CreateUser();
        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);
        var reason = new string('a', Constants.MAX_PHOTO_REJECTION_REASON_LENGTH + 1);

        var result = user.RejectAvatar(Guid.CreateVersion7(), reason, Now);

        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Error.Errors);

        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal("user.avatarModeration.reasonTooLong", error.Code);
        Assert.Equal("reason", error.InvalidField);
        Assert.Equal(PhotoModerationStatus.Pending, user.AvatarModerationStatus);
        Assert.Null(user.AvatarRejectionReason);
    }

    [Fact]
    public void RejectAvatar_WithValidReason_TrimsAndStoresDecision()
    {
        var user = CreateUser();
        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);
        var adminId = Guid.CreateVersion7();

        var result = user.RejectAvatar(adminId, "  Нет резкости  ", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(PhotoModerationStatus.Rejected, user.AvatarModerationStatus);
        Assert.Equal("Нет резкости", user.AvatarRejectionReason);
        Assert.Equal(adminId, user.AvatarReviewedByUserId);
        Assert.Equal(Now, user.AvatarReviewedAt);
    }

    [Fact]
    public void RejectAvatar_WhenAlreadyRejected_ReturnsInvalidStatus()
    {
        var user = CreateUser();
        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);
        Assert.True(user.RejectAvatar(Guid.CreateVersion7(), "Нет резкости", Now).IsSuccess);

        var result = user.RejectAvatar(Guid.CreateVersion7(), "Ещё раз", Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, Assert.Single(result.Error.Errors).Type);
        Assert.Equal("user.avatarModeration.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal("Нет резкости", user.AvatarRejectionReason);
    }

    [Fact]
    public void RejectAvatar_WithNewUpload_ResetsPreviousDecision()
    {
        var user = CreateUser();
        Assert.True(user.SetAvatar("https://cdn.example.com/first.png").IsSuccess);
        Assert.True(user.RejectAvatar(Guid.CreateVersion7(), "Нет резкости", Now).IsSuccess);

        var submittedAt = user.AvatarSubmittedAt;

        Assert.True(user.SetAvatar("https://cdn.example.com/second.png").IsSuccess);

        Assert.Equal(PhotoModerationStatus.Pending, user.AvatarModerationStatus);
        Assert.Null(user.AvatarRejectionReason);
        Assert.Null(user.AvatarReviewedAt);
        Assert.Null(user.AvatarReviewedByUserId);
        Assert.True(user.AvatarSubmittedAt >= submittedAt);
    }

    [Fact]
    public void BlockAndUnblockAvatar_StillWorkForPendingAvatar()
    {
        var user = CreateUser();
        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);
        Assert.Equal(PhotoModerationStatus.Pending, user.AvatarModerationStatus);

        Assert.True(user.BlockAvatar().IsSuccess);
        Assert.True(user.IsAvatarBlocked);

        var updatedAt = user.UpdatedAt;

        // #87 не меняется: повторная блокировка идемпотентна.
        Assert.True(user.BlockAvatar().IsSuccess);
        Assert.Equal(updatedAt, user.UpdatedAt);
        Assert.True(user.IsAvatarBlocked);

        Assert.True(user.UnblockAvatar().IsSuccess);
        Assert.False(user.IsAvatarBlocked);

        // Блокировка — маска, а не вердикт: статус проверки не меняется.
        Assert.Equal(PhotoModerationStatus.Pending, user.AvatarModerationStatus);
    }

    [Fact]
    public void BlockAvatar_WithoutAvatar_StillReturnsConflict()
    {
        var user = CreateUser();

        var result = user.BlockAvatar();

        Assert.True(result.IsFailure);
        Assert.Equal("user.blockAvatar.noAvatar", Assert.Single(result.Error.Errors).Code);
        Assert.False(user.IsAvatarBlocked);
    }

    private static User CreateUser()
        => User.Create("barber@example.com", "hash", "Ivan", "Petrov").Value;

    /// <summary>
    /// Проставляет свойство в обход приватного сеттера — так же, как это делает EF Core.
    /// </summary>
    private static void SetPrivateProperty<T>(T entity, string propertyName, object? value)
        => typeof(T).GetProperty(propertyName)!.SetValue(entity, value);
}
