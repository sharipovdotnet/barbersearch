using BarberSearch.Domain.Salons;
using Shared;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Владение салоном и модерация: админская и барберская фабрики, решения администратора,
/// неизменяемость владельца и запрет активации до одобрения.
/// </summary>
/// <remarks>
/// Публичная видимость салона — это два независимых признака: <see cref="Salon.IsActive"/>
/// (виден/участвует в поиске) и <see cref="Salon.ModerationStatus"/>. Тесты фиксируют стык
/// этих признаков: барберский салон ждёт проверки и неактивен, одобрение включает оба,
/// отклонение оставляет неактивным, а активация «в обход» модерации запрещена.
/// </remarks>
public class SalonModerationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StartsActiveWithoutOwner()
    {
        var salon = CreateAdminSalon();

        Assert.Equal(SalonModerationStatus.Active, salon.ModerationStatus);
        Assert.True(salon.IsActive);
        Assert.Null(salon.OwnerUserId);
        Assert.Null(salon.ReviewedAt);
        Assert.Null(salon.ReviewedByUserId);
        Assert.Null(salon.RejectionReason);
    }

    [Fact]
    public void SubmitByOwner_StartsPendingInactiveWithOwner()
    {
        var ownerUserId = Guid.CreateVersion7();

        var salon = CreatePendingSalon(ownerUserId);

        Assert.Equal(SalonModerationStatus.Pending, salon.ModerationStatus);
        Assert.False(salon.IsActive);
        Assert.Equal(ownerUserId, salon.OwnerUserId);
        Assert.Null(salon.ReviewedAt);
        Assert.Null(salon.ReviewedByUserId);
        Assert.Null(salon.RejectionReason);
    }

    [Fact]
    public void Approve_FromPending_ActivatesAndRecordsReview()
    {
        var salon = CreatePendingSalon();
        var adminId = Guid.CreateVersion7();

        var result = salon.Approve(adminId, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(SalonModerationStatus.Active, salon.ModerationStatus);
        Assert.True(salon.IsActive);
        Assert.Equal(adminId, salon.ReviewedByUserId);
        Assert.Equal(Now, salon.ReviewedAt);
        Assert.Null(salon.RejectionReason);
    }

    [Fact]
    public void Approve_ClearsLeftoverRejectionReason()
    {
        var salon = CreatePendingSalon();

        // В рабочем сценарии Pending и непустая причина вместе не встречаются, но очистка
        // причины при одобрении — часть контракта. Ставим её в обход сеттера, как EF Core.
        SetPrivateProperty(salon, nameof(Salon.RejectionReason), "Privately seeded reason");
        Assert.Equal("Privately seeded reason", salon.RejectionReason);

        Assert.True(salon.Approve(Guid.CreateVersion7(), Now).IsSuccess);
        Assert.Null(salon.RejectionReason);
    }

    [Fact]
    public void Approve_WhenAlreadyApproved_ReturnsConflict()
    {
        var salon = CreatePendingSalon();
        salon.Approve(Guid.CreateVersion7(), Now);

        var result = salon.Approve(Guid.CreateVersion7(), Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("salon.review.invalidStatus", result.Error.Errors[0].Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithoutReason_ReturnsValidation(string? reason)
    {
        var salon = CreatePendingSalon();

        var result = salon.Reject(Guid.CreateVersion7(), reason, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("salon.reject.reasonRequired", result.Error.Errors[0].Code);
        Assert.Equal(SalonModerationStatus.Pending, salon.ModerationStatus);
        Assert.False(salon.IsActive);
        Assert.Null(salon.RejectionReason);
    }

    [Fact]
    public void Reject_WithTooLongReason_ReturnsValidation()
    {
        var salon = CreatePendingSalon();
        var reason = new string('a', Constants.MAX_SALON_REJECTION_REASON_LENGTH + 1);

        var result = salon.Reject(Guid.CreateVersion7(), reason, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("salon.reject.reasonTooLong", result.Error.Errors[0].Code);
        Assert.Equal(SalonModerationStatus.Pending, salon.ModerationStatus);
        Assert.Null(salon.RejectionReason);
    }

    [Fact]
    public void Reject_WithValidReason_TrimsAndStoresDecision()
    {
        var salon = CreatePendingSalon();
        var adminId = Guid.CreateVersion7();

        var result = salon.Reject(adminId, "  Нет подтверждённого адреса  ", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(SalonModerationStatus.Rejected, salon.ModerationStatus);
        Assert.False(salon.IsActive);
        Assert.Equal("Нет подтверждённого адреса", salon.RejectionReason);
        Assert.Equal(adminId, salon.ReviewedByUserId);
        Assert.Equal(Now, salon.ReviewedAt);
    }

    [Fact]
    public void Reject_WhenAlreadyRejected_ReturnsConflict()
    {
        var salon = CreatePendingSalon();
        salon.Reject(Guid.CreateVersion7(), "Нет адреса", Now);

        var result = salon.Reject(Guid.CreateVersion7(), "Ещё раз", Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("salon.review.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal("Нет адреса", salon.RejectionReason);
    }

    [Fact]
    public void Approve_AfterReject_ReturnsConflict()
    {
        var salon = CreatePendingSalon();
        salon.Reject(Guid.CreateVersion7(), "Нет адреса", Now);

        var result = salon.Approve(Guid.CreateVersion7(), Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("salon.review.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(SalonModerationStatus.Rejected, salon.ModerationStatus);
        Assert.False(salon.IsActive);
    }

    [Fact]
    public void OwnerUserId_HasNoPublicSetter()
    {
        var property = typeof(Salon).GetProperty(nameof(Salon.OwnerUserId));
        Assert.NotNull(property);
        Assert.True(property!.CanRead);

        // Владелец фиксируется фабрикой SubmitByOwner и не меняется: публичного сеттера нет,
        // есть только приватный — его использует EF Core при материализации.
        Assert.Null(property.GetSetMethod(nonPublic: false));
        Assert.NotNull(property.GetSetMethod(nonPublic: true));
    }

    [Fact]
    public void Activate_WhenPending_ReturnsNotApproved()
    {
        var salon = CreatePendingSalon();

        var result = salon.Activate();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("salon.activation.notApproved", result.Error.Errors[0].Code);
        Assert.False(salon.IsActive);
    }

    [Fact]
    public void Activate_WhenRejected_ReturnsNotApproved()
    {
        var salon = CreatePendingSalon();
        salon.Reject(Guid.CreateVersion7(), "Нет адреса", Now);

        var result = salon.Activate();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("salon.activation.notApproved", result.Error.Errors[0].Code);
        Assert.False(salon.IsActive);
    }

    [Fact]
    public void Activate_WhenApprovedButHidden_Succeeds()
    {
        var salon = CreateAdminSalon();
        Assert.True(salon.Deactivate().IsSuccess);
        Assert.False(salon.IsActive);

        var result = salon.Activate();

        Assert.True(result.IsSuccess);
        Assert.True(salon.IsActive);
    }

    [Fact]
    public void Create_WithInvalidCoordinates_ReturnsValidation()
    {
        var result = Salon.Create(
            "Barbershop",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            latitude: 91,
            longitude: 37.6173);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("salon.latitude", result.Error.Errors[0].Code);
    }

    private static Salon CreateAdminSalon()
        => Salon.Create(
            "Admin Salon",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173).Value;

    private static Salon CreatePendingSalon(Guid? ownerUserId = null)
        => Salon.SubmitByOwner(
            ownerUserId ?? Guid.CreateVersion7(),
            "Owner Salon",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173).Value;

    /// <summary>
    /// Проставляет свойство в обход приватного сеттера — так же, как это делает EF Core.
    /// </summary>
    private static void SetPrivateProperty<T>(T entity, string propertyName, object? value)
        => typeof(T).GetProperty(propertyName)!.SetValue(entity, value);
}
