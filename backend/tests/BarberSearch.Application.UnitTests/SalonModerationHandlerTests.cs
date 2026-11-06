using BarberSearch.Application.Salons.ActivateSalon;
using BarberSearch.Application.Salons.Admin.ApproveSalon;
using BarberSearch.Application.Salons.Admin.RejectSalon;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Domain.Salons;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Админские решения по салону на уровне Application: одобрение, отказ и активация.
/// </summary>
/// <remarks>
/// Транзакция заменена <see cref="FakeUnitOfWork"/>, домен — настоящий: обработчики проверяют
/// только загрузку агрегата, вызов решения и сохранение. Повторное решение и активация
/// до одобрения обязаны вернуть доменные коды и не писать в репозиторий.
/// </remarks>
public class SalonModerationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Approve_PendingSalon_ActivatesAndStoresReview()
    {
        var salon = CreatePendingSalon();
        var adminId = Guid.CreateVersion7();
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);

        var unitOfWork = new FakeUnitOfWork(salons);
        salons.UpdateBehavior = _ =>
        {
            Assert.True(unitOfWork.IsInTransaction);
            return UnitResult.Success<Failure>();
        };

        var handler = CreateApproveHandler(salons, unitOfWork, adminId);

        var result = await handler.Handle(
            new ApproveSalonCommand(salon.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SalonModerationStatus.Active, salon.ModerationStatus);
        Assert.True(salon.IsActive);
        Assert.Equal(adminId, salon.ReviewedByUserId);
        Assert.Equal(Now, salon.ReviewedAt);
        Assert.Null(salon.RejectionReason);
        Assert.Equal(1, salons.UpdateCalls);
        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.Equal(0, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Approve_WhenAlreadyApproved_ReturnsConflictAndDoesNotSave()
    {
        var salon = CreatePendingSalon();
        var adminId = Guid.CreateVersion7();
        salon.Approve(adminId, Now);
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);
        var unitOfWork = new FakeUnitOfWork(salons);

        var handler = CreateApproveHandler(salons, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new ApproveSalonCommand(salon.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("salon.review.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(0, salons.UpdateCalls);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Reject_WithoutReason_ReturnsValidationAndDoesNotSave()
    {
        var salon = CreatePendingSalon();
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);
        var unitOfWork = new FakeUnitOfWork(salons);
        var handler = CreateRejectHandler(salons, unitOfWork, Guid.CreateVersion7());

        var result = await handler.Handle(
            new RejectSalonCommand(salon.Id, "   "),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("salon.reject.reasonRequired", result.Error.Errors[0].Code);
        Assert.Equal(SalonModerationStatus.Pending, salon.ModerationStatus);
        Assert.False(salon.IsActive);
        Assert.Equal(0, salons.UpdateCalls);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Reject_WithReason_StoresTrimmedReasonAndHidesSalon()
    {
        var salon = CreatePendingSalon();
        var adminId = Guid.CreateVersion7();
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);
        var unitOfWork = new FakeUnitOfWork(salons);
        var handler = CreateRejectHandler(salons, unitOfWork, adminId);

        var result = await handler.Handle(
            new RejectSalonCommand(salon.Id, "  Нет подтверждённого адреса  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SalonModerationStatus.Rejected, salon.ModerationStatus);
        Assert.False(salon.IsActive);
        Assert.Equal("Нет подтверждённого адреса", salon.RejectionReason);
        Assert.Equal(adminId, salon.ReviewedByUserId);
        Assert.Equal(Now, salon.ReviewedAt);
        Assert.Equal(1, salons.UpdateCalls);
        Assert.Equal(1, unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Activate_WhenPending_ReturnsNotApprovedAndDoesNotSave()
    {
        var salon = CreatePendingSalon();
        var salons = new FakeSalonRepository();
        salons.AddSalon(salon);
        var handler = new ActivateSalonHandler(
            salons,
            NullLogger<ActivateSalonHandler>.Instance);

        var result = await handler.Handle(
            new ActivateSalonCommand(salon.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("salon.activation.notApproved", result.Error.Errors[0].Code);
        Assert.Equal(SalonModerationStatus.Pending, salon.ModerationStatus);
        Assert.False(salon.IsActive);
        Assert.Equal(0, salons.UpdateCalls);
    }

    private static ApproveSalonHandler CreateApproveHandler(
        FakeSalonRepository salons,
        FakeUnitOfWork unitOfWork,
        Guid adminId)
        => new(
            salons,
            unitOfWork,
            new FakeCurrentUser(adminId, isAdmin: true),
            new FakeTimeProvider(Now),
            NullLogger<ApproveSalonHandler>.Instance);

    private static RejectSalonHandler CreateRejectHandler(
        FakeSalonRepository salons,
        FakeUnitOfWork unitOfWork,
        Guid adminId)
        => new(
            salons,
            unitOfWork,
            new FakeCurrentUser(adminId, isAdmin: true),
            new FakeTimeProvider(Now),
            NullLogger<RejectSalonHandler>.Instance);

    private static Salon CreatePendingSalon()
        => Salon.SubmitByOwner(
            Guid.CreateVersion7(),
            "Pending Salon",
            "Tverskaya 1",
            "Moscow",
            "RU",
            "Europe/Moscow",
            55.7558,
            37.6173).Value;
}
