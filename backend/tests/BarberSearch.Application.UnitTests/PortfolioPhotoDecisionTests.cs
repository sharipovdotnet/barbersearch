using BarberSearch.Application.PortfolioPhotos.ApprovePortfolioPhoto;
using BarberSearch.Application.PortfolioPhotos.RejectPortfolioPhoto;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.PortfolioPhotos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Решение администратора по фотографии портфолио на уровне Application: роль, 404,
/// 409, обязательная причина, сохранение и состав журнала.
/// </summary>
/// <remarks>
/// Транзакция заменена <see cref="FakeUnitOfWork"/>, домен — настоящий: фотография ищется
/// в коллекции загруженного профиля, поэтому чужая фотография не меняется. Повторное решение
/// и отказ без причины обязаны вернуть доменные коды и не писать в репозиторий.
/// </remarks>
public class PortfolioPhotoDecisionTests
{
    private const string PhotoUrl = "https://cdn.example.com/photo.jpg";

    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Approve_ByAdmin_ApprovesAndSavesInTransaction()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var photo = AddPhoto(profile);
        var adminId = Guid.CreateVersion7();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        repository.UpdateBehavior = _ =>
        {
            Assert.True(unitOfWork.IsInTransaction);

            return UnitResult.Success<Failure>();
        };
        var handler = CreateApproveHandler(repository, unitOfWork, adminId);

        var result = await handler.Handle(
            new ApprovePortfolioPhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PhotoModerationStatus.Approved, photo.ModerationStatus);
        Assert.Equal(adminId, photo.ReviewedByUserId);
        Assert.Equal(Now, photo.ReviewedAt);
        Assert.Null(photo.RejectionReason);
        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Contains("photos", repository.LoadCalls);
        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.Equal(0, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Approve_ByOwner_ReturnsForbiddenWithoutReadingOrSaving()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var photo = AddPhoto(profile);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = new ApprovePortfolioPhotoHandler(
            repository,
            unitOfWork,
            new FakeCurrentUser(ownerId),
            new FakeTimeProvider(Now),
            NullLogger<ApprovePortfolioPhotoHandler>.Instance);

        var result = await handler.Handle(
            new ApprovePortfolioPhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
        Assert.Equal(PhotoModerationStatus.Pending, photo.ModerationStatus);
        Assert.Empty(repository.LoadCalls);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Approve_Anonymous_ReturnsUnauthorized()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var photo = AddPhoto(profile);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = new ApprovePortfolioPhotoHandler(
            repository,
            unitOfWork,
            FakeCurrentUser.Anonymous(),
            new FakeTimeProvider(Now),
            NullLogger<ApprovePortfolioPhotoHandler>.Instance);

        var result = await handler.Handle(
            new ApprovePortfolioPhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Approve_WhenProfileMissing_ReturnsNotFoundWithoutSaving()
    {
        var repository = new FakeBarberProfileRepository();
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateApproveHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new ApprovePortfolioPhotoCommand(Guid.CreateVersion7(), Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("barberProfile.notFound", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Approve_WhenPhotoIsMissing_ReturnsModerationNotFound()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateApproveHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new ApprovePortfolioPhotoCommand(profile.Id, Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("portfolioPhoto.moderation.notFound", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Approve_WhenAlreadyReviewed_ReturnsInvalidStatusConflict()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var photo = AddPhoto(profile);
        Assert.True(photo.Approve(Guid.CreateVersion7(), Now).IsSuccess);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateApproveHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new ApprovePortfolioPhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("portfolioPhoto.moderation.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Approve_LogsOnlyIdentifiersAndDecision()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var photo = AddPhoto(profile);
        var adminId = Guid.CreateVersion7();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        var logger = new RecordingLogger<ApprovePortfolioPhotoHandler>();
        var handler = CreateApproveHandler(repository, unitOfWork, adminId, logger);

        var result = await handler.Handle(
            new ApprovePortfolioPhotoCommand(profile.Id, photo.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var message = Assert.Single(logger.Messages);
        Assert.Contains(photo.Id.ToString(), message, StringComparison.Ordinal);
        Assert.Contains(profile.Id.ToString(), message, StringComparison.Ordinal);
        Assert.Contains("approved", message, StringComparison.Ordinal);
        Assert.DoesNotContain(PhotoUrl, message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reject_ByAdmin_StoresTrimmedReasonAndSavesInTransaction()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var photo = AddPhoto(profile);
        var adminId = Guid.CreateVersion7();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        repository.UpdateBehavior = _ =>
        {
            Assert.True(unitOfWork.IsInTransaction);

            return UnitResult.Success<Failure>();
        };
        var handler = CreateRejectHandler(repository, unitOfWork, adminId);

        var result = await handler.Handle(
            new RejectPortfolioPhotoCommand(profile.Id, photo.Id, "  Нет резкости  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PhotoModerationStatus.Rejected, photo.ModerationStatus);
        Assert.Equal("Нет резкости", photo.RejectionReason);
        Assert.Equal(adminId, photo.ReviewedByUserId);
        Assert.Equal(Now, photo.ReviewedAt);
        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Equal(1, unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Reject_WithoutReason_ReturnsReasonRequiredAndDoesNotSave()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var photo = AddPhoto(profile);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateRejectHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new RejectPortfolioPhotoCommand(profile.Id, photo.Id, "   "),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("portfolioPhoto.moderation.reasonRequired", result.Error.Errors[0].Code);
        Assert.Equal("reason", result.Error.Errors[0].InvalidField);
        Assert.Equal(PhotoModerationStatus.Pending, photo.ModerationStatus);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Reject_WithTooLongReason_ReturnsReasonTooLongAndDoesNotSave()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var photo = AddPhoto(profile);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateRejectHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new RejectPortfolioPhotoCommand(profile.Id, photo.Id, new string('x', 501)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("portfolioPhoto.moderation.reasonTooLong", result.Error.Errors[0].Code);
        Assert.Equal("reason", result.Error.Errors[0].InvalidField);
        Assert.Equal(PhotoModerationStatus.Pending, photo.ModerationStatus);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Reject_ByOwner_ReturnsForbiddenWithoutReadingOrSaving()
    {
        var ownerId = Guid.CreateVersion7();
        var profile = CreateProfile(ownerId);
        var photo = AddPhoto(profile);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = new RejectPortfolioPhotoHandler(
            repository,
            unitOfWork,
            new FakeCurrentUser(ownerId),
            new FakeTimeProvider(Now),
            NullLogger<RejectPortfolioPhotoHandler>.Instance);

        var result = await handler.Handle(
            new RejectPortfolioPhotoCommand(profile.Id, photo.Id, "Нет резкости"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
        Assert.Empty(repository.LoadCalls);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Reject_Anonymous_ReturnsUnauthorized()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var photo = AddPhoto(profile);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = new RejectPortfolioPhotoHandler(
            repository,
            unitOfWork,
            FakeCurrentUser.Anonymous(),
            new FakeTimeProvider(Now),
            NullLogger<RejectPortfolioPhotoHandler>.Instance);

        var result = await handler.Handle(
            new RejectPortfolioPhotoCommand(profile.Id, photo.Id, "Нет резкости"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Reject_WhenPhotoIsMissing_ReturnsModerationNotFound()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateRejectHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new RejectPortfolioPhotoCommand(profile.Id, Guid.CreateVersion7(), "Нет резкости"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("portfolioPhoto.moderation.notFound", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Reject_WhenAlreadyReviewed_ReturnsInvalidStatusConflict()
    {
        var profile = CreateProfile(Guid.CreateVersion7());
        var photo = AddPhoto(profile);
        Assert.True(photo.Approve(Guid.CreateVersion7(), Now).IsSuccess);
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateRejectHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new RejectPortfolioPhotoCommand(profile.Id, photo.Id, "Нет резкости"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("portfolioPhoto.moderation.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Reject_LogsNoReasonOrUrl()
    {
        const string reason = "Причина, которую нельзя писать в журнал";
        var profile = CreateProfile(Guid.CreateVersion7());
        var photo = AddPhoto(profile);
        var adminId = Guid.CreateVersion7();
        var repository = new FakeBarberProfileRepository();
        repository.Add(profile);
        var unitOfWork = new FakeUnitOfWork(repository);
        var logger = new RecordingLogger<RejectPortfolioPhotoHandler>();
        var handler = CreateRejectHandler(repository, unitOfWork, adminId, logger);

        var result = await handler.Handle(
            new RejectPortfolioPhotoCommand(profile.Id, photo.Id, reason),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var message = Assert.Single(logger.Messages);
        Assert.Contains(photo.Id.ToString(), message, StringComparison.Ordinal);
        Assert.Contains("rejected", message, StringComparison.Ordinal);
        Assert.DoesNotContain(reason, message, StringComparison.Ordinal);
        Assert.DoesNotContain(PhotoUrl, message, StringComparison.Ordinal);
    }

    private static ApprovePortfolioPhotoHandler CreateApproveHandler(
        FakeBarberProfileRepository repository,
        FakeUnitOfWork unitOfWork,
        Guid adminId,
        ILogger<ApprovePortfolioPhotoHandler>? logger = null)
        => new(
            repository,
            unitOfWork,
            new FakeCurrentUser(adminId, isAdmin: true),
            new FakeTimeProvider(Now),
            logger ?? NullLogger<ApprovePortfolioPhotoHandler>.Instance);

    private static RejectPortfolioPhotoHandler CreateRejectHandler(
        FakeBarberProfileRepository repository,
        FakeUnitOfWork unitOfWork,
        Guid adminId,
        ILogger<RejectPortfolioPhotoHandler>? logger = null)
        => new(
            repository,
            unitOfWork,
            new FakeCurrentUser(adminId, isAdmin: true),
            new FakeTimeProvider(Now),
            logger ?? NullLogger<RejectPortfolioPhotoHandler>.Instance);

    private static BarberProfile CreateProfile(Guid userId)
        => BarberProfile.Create(
            userId,
            "Master Barber",
            5,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null).Value;

    private static PortfolioPhoto AddPhoto(BarberProfile profile)
    {
        Assert.True(profile.AddPhoto(PhotoUrl).IsSuccess);

        return profile.PortfolioPhotos.Single();
    }
}
