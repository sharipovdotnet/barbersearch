using BarberSearch.Domain.BarberRequests;
using Shared;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Заявка в барберы: поля, статусы и правила решения.
/// </summary>
/// <remarks>
/// Время подачи и решения передаётся аргументом, поэтому тесты не зависят от системных часов
/// и проверяют порядок очереди детерминированно (R-27).
/// </remarks>
public class BarberRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid AdminId = Guid.CreateVersion7();

    private static BarberRequest CreateValidRequest(
        DateTimeOffset? now = null,
        string? about = "  Стригу с 2015 года.  ")
        => BarberRequest.Create(
            Guid.CreateVersion7(),
            "Алексей Смирнов",
            7,
            "  Москва  ",
            about,
            hasSalon: false,
            now ?? Now).Value;

    [Fact]
    public void Create_WithValidData_StartsPendingAndStoresNormalizedText()
    {
        var userId = Guid.CreateVersion7();

        var result = BarberRequest.Create(
            userId,
            "Алексей Смирнов",
            7,
            "  Москва  ",
            "  Стригу с 2015 года.  ",
            hasSalon: true,
            Now);

        Assert.True(result.IsSuccess);
        var request = result.Value;

        Assert.Equal(BarberRequestStatus.Pending, request.Status);
        Assert.Equal(userId, request.UserId);
        Assert.Equal("Алексей Смирнов", request.DisplayName.Value);
        Assert.Equal(7, request.YearsOfExperience.Value);
        Assert.Equal("Москва", request.City);
        Assert.Equal("Стригу с 2015 года.", request.About);
        Assert.True(request.HasSalon);
        Assert.Equal(Now, request.CreatedAt);
        Assert.Equal(Now, request.UpdatedAt);
        Assert.Null(request.ReviewedAt);
        Assert.Null(request.ReviewedByUserId);
        Assert.Null(request.RejectionReason);
    }

    [Fact]
    public void Create_WithBlankAbout_StoresNull()
    {
        var request = CreateValidRequest(about: "   ");

        Assert.Null(request.About);
    }

    [Fact]
    public void Create_WithEmptyDisplayName_Fails()
    {
        var result = BarberRequest.Create(
            Guid.CreateVersion7(),
            "   ",
            7,
            "Москва",
            null,
            hasSalon: false,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.displayName", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithTooLongDisplayName_Fails()
    {
        var result = BarberRequest.Create(
            Guid.CreateVersion7(),
            new string('a', 121),
            7,
            "Москва",
            null,
            hasSalon: false,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("barberProfile.displayName.tooLong", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithPaddedDisplayName_TrimsValue()
    {
        var result = BarberRequest.Create(
            Guid.CreateVersion7(),
            "  Алексей Смирнов  ",
            7,
            "Москва",
            null,
            false,
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("Алексей Смирнов", result.Value.DisplayName.Value);
    }

    [Fact]
    public void Create_WithPaddedMaxLengthDisplayName_Accepts()
    {
        // 118 символов плюс окружающие пробелы: после Trim длина укладывается в предел 120,
        // а сырая строка — нет. Граница обязана проверять уже срезанное значение.
        var name = new string('a', 118);

        var result = BarberRequest.Create(
            Guid.CreateVersion7(),
            $"  {name}  ",
            7,
            "Москва",
            null,
            false,
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(name, result.Value.DisplayName.Value);
    }

    [Fact]
    public void Create_WithYearsOfExperienceOutOfRange_Fails()
    {
        var tooFew = BarberRequest.Create(
            Guid.CreateVersion7(), "Алексей", 0, "Москва", null, false, Now);
        var tooMany = BarberRequest.Create(
            Guid.CreateVersion7(), "Алексей", 71, "Москва", null, false, Now);

        Assert.True(tooFew.IsFailure);
        Assert.True(tooMany.IsFailure);
        Assert.Equal("barberProfile.yearsOfExperience", tooFew.Error.Errors[0].Code);
        Assert.Equal("barberProfile.yearsOfExperience", tooMany.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithEmptyCity_Fails()
    {
        var result = BarberRequest.Create(
            Guid.CreateVersion7(),
            "Алексей",
            7,
            "   ",
            null,
            false,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("barberRequest.city", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithTooLongCity_Fails()
    {
        var result = BarberRequest.Create(
            Guid.CreateVersion7(),
            "Алексей",
            7,
            new string('a', Constants.MAX_BARBER_REQUEST_CITY_LENGTH + 1),
            null,
            false,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("barberRequest.city.tooLong", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithTooLongAbout_Fails()
    {
        var result = BarberRequest.Create(
            Guid.CreateVersion7(),
            "Алексей",
            7,
            "Москва",
            new string('a', Constants.MAX_TEXT_LENGTH + 1),
            false,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("barberRequest.about.tooLong", result.Error.Errors[0].Code);
    }

    [Fact]
    public void Create_WithSeveralInvalidFields_ReturnsAllErrors()
    {
        var result = BarberRequest.Create(
            Guid.CreateVersion7(),
            " ",
            0,
            " ",
            new string('a', Constants.MAX_TEXT_LENGTH + 1),
            false,
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal(4, result.Error.Errors.Count);
    }

    [Fact]
    public void Approve_FromPending_SetsStatusAndReviewMetadata()
    {
        var request = CreateValidRequest();

        var result = request.Approve(AdminId, Now.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(BarberRequestStatus.Approved, request.Status);
        Assert.Equal(AdminId, request.ReviewedByUserId);
        Assert.Equal(Now.AddHours(1), request.ReviewedAt);
        Assert.Null(request.RejectionReason);
    }

    [Fact]
    public void Approve_WhenAlreadyApproved_ReturnsConflict()
    {
        var request = CreateValidRequest();
        request.Approve(AdminId, Now.AddHours(1));

        var result = request.Approve(Guid.CreateVersion7(), Now.AddHours(2));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("barberRequest.review.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(AdminId, request.ReviewedByUserId);
    }

    [Fact]
    public void Reject_FromPending_StoresTrimmedReasonAndStatus()
    {
        var request = CreateValidRequest();

        var result = request.Reject(AdminId, "  Мало опыта  ", Now.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(BarberRequestStatus.Rejected, request.Status);
        Assert.Equal(AdminId, request.ReviewedByUserId);
        Assert.Equal(Now.AddHours(1), request.ReviewedAt);
        Assert.Equal("Мало опыта", request.RejectionReason);
    }

    [Fact]
    public void Reject_WithoutReason_ReturnsValidation()
    {
        var request = CreateValidRequest();

        var empty = request.Reject(AdminId, "   ", Now.AddHours(1));
        var missing = request.Reject(AdminId, null, Now.AddHours(1));

        Assert.True(empty.IsFailure);
        Assert.True(missing.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, empty.Error.Errors[0].Type);
        Assert.Equal("barberRequest.rejection.reasonRequired", empty.Error.Errors[0].Code);
        Assert.Equal("barberRequest.rejection.reasonRequired", missing.Error.Errors[0].Code);
        Assert.Equal(BarberRequestStatus.Pending, request.Status);
    }

    [Fact]
    public void Reject_WithTooLongReason_ReturnsValidation()
    {
        var request = CreateValidRequest();

        var result = request.Reject(
            AdminId,
            new string('a', Constants.MAX_BARBER_REQUEST_REJECTION_REASON_LENGTH + 1),
            Now.AddHours(1));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("barberRequest.rejection.reasonTooLong", result.Error.Errors[0].Code);
        Assert.Equal(BarberRequestStatus.Pending, request.Status);
    }

    [Fact]
    public void Reject_WhenAlreadyApproved_ReturnsConflict()
    {
        var request = CreateValidRequest();
        request.Approve(AdminId, Now.AddHours(1));

        var result = request.Reject(
            Guid.CreateVersion7(),
            "Передумали",
            Now.AddHours(2));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal("barberRequest.review.invalidStatus", result.Error.Errors[0].Code);
        Assert.Equal(BarberRequestStatus.Approved, request.Status);
        Assert.Null(request.RejectionReason);
    }

    [Fact]
    public void Approve_DoesNotClearReasonOfRejectedRequestBecauseItCannotRun()
    {
        var request = CreateValidRequest();
        request.Reject(AdminId, "Мало опыта", Now.AddHours(1));

        var result = request.Approve(Guid.CreateVersion7(), Now.AddHours(2));

        Assert.True(result.IsFailure);
        Assert.Equal(BarberRequestStatus.Rejected, request.Status);
        Assert.Equal("Мало опыта", request.RejectionReason);
    }
}
