using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Application.Users.Admin.ApproveUserAvatar;
using BarberSearch.Application.Users.Admin.RejectUserAvatar;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Решение администратора по аватару пользователя на уровне Application: роль, 404, 409,
/// обязательная причина, сохранение и состав журнала.
/// </summary>
/// <remarks>
/// Транзакция заменена <see cref="FakeUnitOfWork"/>, домен — настоящий: обработчик проверяет
/// только роль, загрузку агрегата, вызов решения и сохранение. Повторное решение и отказ без
/// причины обязаны вернуть доменные коды и не писать в репозиторий.
/// </remarks>
public class UserAvatarDecisionTests
{
    private const string AvatarUrl = "https://cdn.example.com/avatar.png";

    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Approve_ByAdmin_ApprovesAndSavesInTransaction()
    {
        var user = CreatePendingUser();
        var adminId = Guid.CreateVersion7();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var unitOfWork = new FakeUnitOfWork(repository);
        repository.UpdateBehavior = _ =>
        {
            Assert.True(unitOfWork.IsInTransaction);

            return UnitResult.Success<Failure>();
        };
        var handler = CreateApproveHandler(repository, unitOfWork, adminId);

        var result = await handler.Handle(
            new ApproveUserAvatarCommand(user.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PhotoModerationStatus.Approved, user.AvatarModerationStatus);
        Assert.Equal(adminId, user.AvatarReviewedByUserId);
        Assert.Equal(Now, user.AvatarReviewedAt);
        Assert.Null(user.AvatarRejectionReason);
        Assert.Equal(1, repository.GetByIdCallCount);
        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.Equal(0, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Approve_ByOwner_ReturnsForbiddenWithoutReadingOrSaving()
    {
        var user = CreatePendingUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = new ApproveUserAvatarHandler(
            repository,
            unitOfWork,
            new FakeCurrentUser(user.Id),
            new FakeTimeProvider(Now),
            NullLogger<ApproveUserAvatarHandler>.Instance);

        var result = await handler.Handle(
            new ApproveUserAvatarCommand(user.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
        Assert.Equal(PhotoModerationStatus.Pending, user.AvatarModerationStatus);
        Assert.Equal(0, repository.GetByIdCallCount);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Approve_Anonymous_ReturnsUnauthorized()
    {
        var user = CreatePendingUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = new ApproveUserAvatarHandler(
            repository,
            unitOfWork,
            FakeCurrentUser.Anonymous(),
            new FakeTimeProvider(Now),
            NullLogger<ApproveUserAvatarHandler>.Instance);

        var result = await handler.Handle(
            new ApproveUserAvatarCommand(user.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.GetByIdCallCount);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Approve_WhenUserMissing_ReturnsNotFoundWithoutSaving()
    {
        var repository = new FakeUserRepository();
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateApproveHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new ApproveUserAvatarCommand(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("user.notFound", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Approve_WhenAvatarIsMissing_ReturnsNoAvatarConflict()
    {
        var user = User.Create("no-avatar@example.com", "hash", "Ivan", "Petrov").Value;
        var repository = new FakeUserRepository();
        repository.Add(user);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateApproveHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new ApproveUserAvatarCommand(user.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("user.avatarModeration.noAvatar", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Approve_WhenAlreadyReviewed_ReturnsInvalidStatusConflict()
    {
        var user = CreatePendingUser();
        Assert.True(user.ApproveAvatar(Guid.CreateVersion7(), Now).IsSuccess);
        var repository = new FakeUserRepository();
        repository.Add(user);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateApproveHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new ApproveUserAvatarCommand(user.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("user.avatarModeration.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Approve_LogsOnlyIdentifiersAndDecision()
    {
        var user = CreatePendingUser();
        var adminId = Guid.CreateVersion7();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var unitOfWork = new FakeUnitOfWork(repository);
        var logger = new RecordingLogger<ApproveUserAvatarHandler>();
        var handler = CreateApproveHandler(repository, unitOfWork, adminId, logger);

        var result = await handler.Handle(
            new ApproveUserAvatarCommand(user.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var message = Assert.Single(logger.Messages);
        Assert.Contains(user.Id.ToString(), message, StringComparison.Ordinal);
        Assert.Contains(adminId.ToString(), message, StringComparison.Ordinal);
        Assert.Contains("approved", message, StringComparison.Ordinal);
        Assert.DoesNotContain(AvatarUrl, message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reject_ByAdmin_StoresTrimmedReasonAndSavesInTransaction()
    {
        var user = CreatePendingUser();
        var adminId = Guid.CreateVersion7();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var unitOfWork = new FakeUnitOfWork(repository);
        repository.UpdateBehavior = _ =>
        {
            Assert.True(unitOfWork.IsInTransaction);

            return UnitResult.Success<Failure>();
        };
        var handler = CreateRejectHandler(repository, unitOfWork, adminId);

        var result = await handler.Handle(
            new RejectUserAvatarCommand(user.Id, "  Лицо не видно  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PhotoModerationStatus.Rejected, user.AvatarModerationStatus);
        Assert.Equal("Лицо не видно", user.AvatarRejectionReason);
        Assert.Equal(adminId, user.AvatarReviewedByUserId);
        Assert.Equal(Now, user.AvatarReviewedAt);
        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Equal(1, unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Reject_WithoutReason_ReturnsReasonRequiredAndDoesNotSave()
    {
        var user = CreatePendingUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateRejectHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new RejectUserAvatarCommand(user.Id, "   "),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("user.avatarModeration.reasonRequired", result.Error.Errors[0].Code);
        Assert.Equal("reason", result.Error.Errors[0].InvalidField);
        Assert.Equal(PhotoModerationStatus.Pending, user.AvatarModerationStatus);
        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Reject_WithTooLongReason_ReturnsReasonTooLongAndDoesNotSave()
    {
        var user = CreatePendingUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateRejectHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new RejectUserAvatarCommand(user.Id, new string('x', 501)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("user.avatarModeration.reasonTooLong", result.Error.Errors[0].Code);
        Assert.Equal("reason", result.Error.Errors[0].InvalidField);
        Assert.Equal(PhotoModerationStatus.Pending, user.AvatarModerationStatus);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Reject_ByOwner_ReturnsForbiddenWithoutReadingOrSaving()
    {
        var user = CreatePendingUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = new RejectUserAvatarHandler(
            repository,
            unitOfWork,
            new FakeCurrentUser(user.Id),
            new FakeTimeProvider(Now),
            NullLogger<RejectUserAvatarHandler>.Instance);

        var result = await handler.Handle(
            new RejectUserAvatarCommand(user.Id, "Лицо не видно"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal("auth.forbidden", result.Error.Errors[0].Code);
        Assert.Equal(PhotoModerationStatus.Pending, user.AvatarModerationStatus);
        Assert.Equal(0, repository.GetByIdCallCount);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Reject_Anonymous_ReturnsUnauthorized()
    {
        var user = CreatePendingUser();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = new RejectUserAvatarHandler(
            repository,
            unitOfWork,
            FakeCurrentUser.Anonymous(),
            new FakeTimeProvider(Now),
            NullLogger<RejectUserAvatarHandler>.Instance);

        var result = await handler.Handle(
            new RejectUserAvatarCommand(user.Id, "Лицо не видно"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.UNAUTHORIZED, result.Error.Errors[0].Type);
        Assert.Equal("auth.unauthorized", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Reject_WhenUserMissing_ReturnsNotFoundWithoutSaving()
    {
        var repository = new FakeUserRepository();
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateRejectHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new RejectUserAvatarCommand(Guid.CreateVersion7(), "Лицо не видно"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal("user.notFound", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Reject_WhenAlreadyReviewed_ReturnsInvalidStatusConflict()
    {
        var user = CreatePendingUser();
        Assert.True(user.ApproveAvatar(Guid.CreateVersion7(), Now).IsSuccess);
        var repository = new FakeUserRepository();
        repository.Add(user);
        var unitOfWork = new FakeUnitOfWork(repository);
        var handler = CreateRejectHandler(repository, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new RejectUserAvatarCommand(user.Id, "Лицо не видно"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("user.avatarModeration.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task Reject_LogsNoReasonOrUrl()
    {
        const string reason = "Причина, которую нельзя писать в журнал";
        var user = CreatePendingUser();
        var adminId = Guid.CreateVersion7();
        var repository = new FakeUserRepository();
        repository.Add(user);
        var unitOfWork = new FakeUnitOfWork(repository);
        var logger = new RecordingLogger<RejectUserAvatarHandler>();
        var handler = CreateRejectHandler(repository, unitOfWork, adminId, logger);

        var result = await handler.Handle(
            new RejectUserAvatarCommand(user.Id, reason),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var message = Assert.Single(logger.Messages);
        Assert.Contains(user.Id.ToString(), message, StringComparison.Ordinal);
        Assert.Contains("rejected", message, StringComparison.Ordinal);
        Assert.DoesNotContain(reason, message, StringComparison.Ordinal);
        Assert.DoesNotContain(AvatarUrl, message, StringComparison.Ordinal);
    }

    private static ApproveUserAvatarHandler CreateApproveHandler(
        FakeUserRepository repository,
        FakeUnitOfWork unitOfWork,
        Guid adminId,
        ILogger<ApproveUserAvatarHandler>? logger = null)
        => new(
            repository,
            unitOfWork,
            new FakeCurrentUser(adminId, isAdmin: true),
            new FakeTimeProvider(Now),
            logger ?? NullLogger<ApproveUserAvatarHandler>.Instance);

    private static RejectUserAvatarHandler CreateRejectHandler(
        FakeUserRepository repository,
        FakeUnitOfWork unitOfWork,
        Guid adminId,
        ILogger<RejectUserAvatarHandler>? logger = null)
        => new(
            repository,
            unitOfWork,
            new FakeCurrentUser(adminId, isAdmin: true),
            new FakeTimeProvider(Now),
            logger ?? NullLogger<RejectUserAvatarHandler>.Instance);

    private static User CreatePendingUser()
    {
        var user = User.Create(
            $"{Guid.NewGuid():N}@example.com",
            "hash",
            "Ivan",
            "Petrov").Value;

        Assert.True(user.SetAvatar(AvatarUrl).IsSuccess);

        return user;
    }
}
