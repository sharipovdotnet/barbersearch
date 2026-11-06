using BarberSearch.Application.BarberRequests;
using BarberSearch.Application.BarberRequests.Admin.ApproveBarberRequest;
using BarberSearch.Application.BarberRequests.Admin.GetAdminBarberRequest;
using BarberSearch.Application.BarberRequests.Admin.GetAdminBarberRequests;
using BarberSearch.Application.BarberRequests.Admin.RejectBarberRequest;
using BarberSearch.Application.BarberRequests.CreateBarberRequest;
using BarberSearch.Application.BarberRequests.GetMyBarberRequest;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.BarberRequests.CreateBarberRequest;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Domain.RefreshTokens;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Заявка в барберы на уровне Application: подача, дубликат, одобрение/отказ и чтения.
/// </summary>
/// <remarks>
/// Репозитории заменены фейками, транзакция — <see cref="FakeUnitOfWork"/>: тесты проверяют
/// решения обработчиков (роль, отзыв токенов, коды ошибок), а не SQL.
/// </remarks>
public class BarberRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_WithValidData_StoresPendingRequestForCurrentUser()
    {
        var customer = CreateCustomer();
        var users = new FakeUserRepository();
        users.Add(customer);
        var requests = new FakeBarberRequestRepository();
        var handler = CreateHandler(requests, users, customer);

        var result = await handler.Handle(
            new CreateBarberRequestCommand(NewRequest()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BarberRequestStatus.Pending, result.Value.Status);
        Assert.Equal("Алексей Смирнов", result.Value.DisplayName);
        Assert.Equal(7, result.Value.YearsOfExperience);
        Assert.Equal("Москва", result.Value.City);
        Assert.Equal("Стригу с 2015 года.", result.Value.About);
        Assert.False(result.Value.HasSalon);
        Assert.Equal(Now, result.Value.CreatedAt);
        Assert.Null(result.Value.ReviewedAt);

        var stored = Assert.Single(requests.Requests);
        Assert.Equal(customer.Id, stored.UserId);
        Assert.Equal(BarberRequestStatus.Pending, stored.Status);
    }

    [Fact]
    public async Task Create_WhenRoleIsBarber_ReturnsForbiddenAndPersistsNothing()
    {
        var barber = User.Create(
            "barber@barbersearch.test",
            "hash",
            "Иван",
            "Барбер",
            UserRole.Barber).Value;
        var users = new FakeUserRepository();
        users.Add(barber);
        var requests = new FakeBarberRequestRepository();
        var handler = CreateHandler(requests, users, barber);

        var result = await handler.Handle(
            new CreateBarberRequestCommand(NewRequest()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.FORBIDDEN, result.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.BarberRequest.CreateForbidden,
            result.Error.Errors[0].Code);
        Assert.Empty(requests.Requests);
        Assert.Equal(0, requests.AddCallCount);
    }

    [Fact]
    public async Task Create_WhenPendingRequestExists_ReturnsAlreadyPending()
    {
        var customer = CreateCustomer();
        var users = new FakeUserRepository();
        users.Add(customer);
        var requests = new FakeBarberRequestRepository();
        requests.Add(CreateRequest(customer.Id));
        var handler = CreateHandler(requests, users, customer);

        var result = await handler.Handle(
            new CreateBarberRequestCommand(NewRequest()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.BarberRequest.CreateAlreadyPending,
            result.Error.Errors[0].Code);
        Assert.Single(requests.Requests);
        Assert.Equal(0, requests.AddCallCount);
    }

    [Fact]
    public async Task Create_WhenUniqueIndexRaceIsLost_ReturnsAlreadyPending()
    {
        var customer = CreateCustomer();
        var users = new FakeUserRepository();
        users.Add(customer);
        var requests = new FakeBarberRequestRepository
        {
            // Предпроверка прошла, но параллельная подача уже заняла уникальный индекс.
            AddBehavior = _ => UnitResult.Failure<Failure>(new Failure(Error.Conflict(
                ApiErrorCodes.BarberRequest.AddUniqueViolation,
                "A pending barber request already exists for this user.")))
        };
        var handler = CreateHandler(requests, users, customer);

        var result = await handler.Handle(
            new CreateBarberRequestCommand(NewRequest()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.BarberRequest.CreateAlreadyPending,
            result.Error.Errors[0].Code);
        Assert.Equal(1, requests.AddCallCount);
        Assert.Empty(requests.Requests);
    }

    [Fact]
    public async Task Create_WithInvalidFields_ReturnsAllValidationErrors()
    {
        var customer = CreateCustomer();
        var users = new FakeUserRepository();
        users.Add(customer);
        var requests = new FakeBarberRequestRepository();
        var handler = CreateHandler(requests, users, customer);

        var result = await handler.Handle(
            new CreateBarberRequestCommand(new CreateBarberRequestRequest(
                DisplayName: " ",
                YearsOfExperience: 0,
                City: " ",
                About: null,
                HasSalon: false)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(3, result.Error.Errors.Count);
        Assert.All(
            result.Error.Errors,
            error => Assert.Equal(ErrorType.VALIDATION, error.Type));
        Assert.Empty(requests.Requests);
    }

    [Fact]
    public async Task Approve_ChangesRoleRevokesTokensAndStoresReview()
    {
        var applicant = CreateCustomer();
        var admin = CreateAdmin();
        var users = new FakeUserRepository();
        users.Add(applicant);
        users.Add(admin);

        var request = CreateRequest(applicant.Id);
        var requests = new FakeBarberRequestRepository();
        requests.Add(request);

        var tokens = new FakeRefreshTokenRepository();
        var liveToken = await AddLiveTokenAsync(tokens, applicant.Id);

        var unitOfWork = new FakeUnitOfWork(users, requests, tokens);

        // Обе записи идут в одной транзакции: смена роли, отзыв токенов и решение по заявке
        // либо фиксируются вместе, либо откатываются вместе.
        users.UpdateBehavior = _ =>
        {
            Assert.True(unitOfWork.IsInTransaction);
            return UnitResult.Success<Failure>();
        };
        requests.UpdateBehavior = _ =>
        {
            Assert.True(unitOfWork.IsInTransaction);
            return UnitResult.Success<Failure>();
        };

        var handler = CreateApproveHandler(requests, users, tokens, unitOfWork, admin);

        var result = await handler.Handle(
            new ApproveBarberRequestCommand(request.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BarberRequestStatus.Approved, result.Value.Status);
        Assert.Equal(admin.Id, result.Value.ReviewedByUserId);
        Assert.Equal(Now.AddHours(1), result.Value.ReviewedAt);
        Assert.Equal(applicant.Email, result.Value.Applicant.Email);

        Assert.Equal(BarberRequestStatus.Approved, request.Status);
        Assert.Equal(admin.Id, request.ReviewedByUserId);
        Assert.Equal(UserRole.Barber, applicant.Role);
        Assert.Null(applicant.BarberProfileId);
        Assert.True(liveToken.IsRevoked);
        Assert.Equal(1, users.UpdateCallCount);
        Assert.Equal(1, requests.UpdateCallCount);
        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.Equal(0, unitOfWork.RollbackCount);
        Assert.False(unitOfWork.IsInTransaction);
    }

    [Fact]
    public async Task Approve_WhenTokenRevocationFails_RollsBackRoleAndRequest()
    {
        var applicant = CreateCustomer();
        var admin = CreateAdmin();
        var users = new FakeUserRepository();
        users.Add(applicant);
        users.Add(admin);

        var request = CreateRequest(applicant.Id);
        var requests = new FakeBarberRequestRepository();
        requests.Add(request);

        var tokens = new FakeRefreshTokenRepository();
        var liveToken = await AddLiveTokenAsync(tokens, applicant.Id);
        tokens.RevokeAllBehavior = (_, _) => UnitResult.Failure<Failure>(new Failure(
            Error.Failure(
                "refreshToken.revokeAll.database",
                "Simulated revocation failure.")));

        var unitOfWork = new FakeUnitOfWork(users, requests, tokens);
        var handler = CreateApproveHandler(requests, users, tokens, unitOfWork, admin);

        var result = await handler.Handle(
            new ApproveBarberRequestCommand(request.Id),
            CancellationToken.None);

        // Роль уже записана, но отзыв токенов упал: транзакция откатывает обе записи,
        // поэтому заявитель остаётся Customer, а заявка — Pending.
        Assert.True(result.IsFailure);
        Assert.Equal("refreshToken.revokeAll.database", result.Error.Errors[0].Code);
        Assert.Equal(1, users.UpdateCallCount);
        Assert.Equal(0, requests.UpdateCallCount);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.Equal(1, unitOfWork.RollbackCount);

        var storedUser = Assert.Single(users.Users, user => user.Id == applicant.Id);
        Assert.Equal(UserRole.Customer, storedUser.Role);

        var storedRequest = Assert.Single(
            requests.Requests,
            candidate => candidate.Id == request.Id);
        Assert.Equal(BarberRequestStatus.Pending, storedRequest.Status);
        Assert.False(liveToken.IsRevoked);
    }

    [Fact]
    public async Task Approve_WhenAlreadyApproved_ReturnsConflictAndKeepsRole()
    {
        var applicant = CreateCustomer();
        var admin = CreateAdmin();
        var users = new FakeUserRepository();
        users.Add(applicant);
        users.Add(admin);

        var request = CreateRequest(applicant.Id);
        request.Approve(admin.Id, Now);
        var requests = new FakeBarberRequestRepository();
        requests.Add(request);

        var tokens = new FakeRefreshTokenRepository();
        var liveToken = await AddLiveTokenAsync(tokens, applicant.Id);

        var unitOfWork = new FakeUnitOfWork(users, requests, tokens);
        var handler = CreateApproveHandler(requests, users, tokens, unitOfWork, admin);

        var result = await handler.Handle(
            new ApproveBarberRequestCommand(request.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.BarberRequest.ReviewInvalidStatus,
            result.Error.Errors[0].Code);
        Assert.Equal(UserRole.Customer, applicant.Role);
        Assert.False(liveToken.IsRevoked);
        Assert.Equal(1, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Approve_WhenApplicantIsNoLongerCustomer_ReturnsConflict()
    {
        var applicant = User.Create(
            "barber@barbersearch.test",
            "hash",
            "Иван",
            "Барбер",
            UserRole.Barber).Value;
        var admin = CreateAdmin();
        var users = new FakeUserRepository();
        users.Add(applicant);
        users.Add(admin);

        var request = CreateRequest(applicant.Id);
        var requests = new FakeBarberRequestRepository();
        requests.Add(request);

        var tokens = new FakeRefreshTokenRepository();
        var unitOfWork = new FakeUnitOfWork(users, requests, tokens);
        var handler = CreateApproveHandler(requests, users, tokens, unitOfWork, admin);

        var result = await handler.Handle(
            new ApproveBarberRequestCommand(request.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.BarberRequest.ReviewInvalidApplicant,
            result.Error.Errors[0].Code);
        Assert.Equal(UserRole.Barber, applicant.Role);
        Assert.Equal(BarberRequestStatus.Pending, request.Status);
    }

    [Fact]
    public async Task Approve_WhenRequestDoesNotExist_ReturnsNotFound()
    {
        var applicant = CreateCustomer();
        var admin = CreateAdmin();
        var users = new FakeUserRepository();
        users.Add(applicant);
        users.Add(admin);
        var requests = new FakeBarberRequestRepository();
        var tokens = new FakeRefreshTokenRepository();
        var unitOfWork = new FakeUnitOfWork(users, requests, tokens);
        var handler = CreateApproveHandler(requests, users, tokens, unitOfWork, admin);

        var result = await handler.Handle(
            new ApproveBarberRequestCommand(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.BarberRequest.NotFound, result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task Reject_KeepsRoleAndStoresTrimmedReason()
    {
        var applicant = CreateCustomer();
        var admin = CreateAdmin();
        var users = new FakeUserRepository();
        users.Add(applicant);
        users.Add(admin);

        var request = CreateRequest(applicant.Id);
        var requests = new FakeBarberRequestRepository();
        requests.Add(request);

        var tokens = new FakeRefreshTokenRepository();
        var liveToken = await AddLiveTokenAsync(tokens, applicant.Id);

        var unitOfWork = new FakeUnitOfWork(users, requests, tokens);
        requests.UpdateBehavior = _ =>
        {
            Assert.True(unitOfWork.IsInTransaction);
            return UnitResult.Success<Failure>();
        };
        var handler = CreateRejectHandler(requests, users, unitOfWork, admin);

        var result = await handler.Handle(
            new RejectBarberRequestCommand(request.Id, "  Мало подтверждённого опыта  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BarberRequestStatus.Rejected, result.Value.Status);
        Assert.Equal("Мало подтверждённого опыта", result.Value.RejectionReason);
        Assert.Equal(admin.Id, result.Value.ReviewedByUserId);

        Assert.Equal(UserRole.Customer, applicant.Role);
        Assert.False(liveToken.IsRevoked);
        Assert.Equal(BarberRequestStatus.Rejected, request.Status);
        Assert.Equal(0, users.UpdateCallCount);
        Assert.Equal(1, requests.UpdateCallCount);
        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.Equal(0, unitOfWork.RollbackCount);
        Assert.False(unitOfWork.IsInTransaction);
    }

    [Fact]
    public async Task Reject_WhenRequestUpdateFails_RollsBackStatus()
    {
        var applicant = CreateCustomer();
        var admin = CreateAdmin();
        var users = new FakeUserRepository();
        users.Add(applicant);
        users.Add(admin);

        var request = CreateRequest(applicant.Id);
        var requests = new FakeBarberRequestRepository();
        requests.Add(request);
        requests.UpdateBehavior = _ => UnitResult.Failure<Failure>(new Failure(
            Error.Failure(
                "barberRequest.update.database",
                "Simulated request update failure.")));

        var tokens = new FakeRefreshTokenRepository();
        var liveToken = await AddLiveTokenAsync(tokens, applicant.Id);

        var unitOfWork = new FakeUnitOfWork(users, requests, tokens);
        var handler = CreateRejectHandler(requests, users, unitOfWork, admin);

        var result = await handler.Handle(
            new RejectBarberRequestCommand(request.Id, "Мало подтверждённого опыта"),
            CancellationToken.None);

        // Домен уже перевёл заявку в Rejected, но запись упала: откат возвращает Pending,
        // роль остаётся Customer, токены не отзываются.
        Assert.True(result.IsFailure);
        Assert.Equal("barberRequest.update.database", result.Error.Errors[0].Code);
        Assert.Equal(0, users.UpdateCallCount);
        Assert.Equal(1, requests.UpdateCallCount);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.Equal(1, unitOfWork.RollbackCount);

        var storedRequest = Assert.Single(
            requests.Requests,
            candidate => candidate.Id == request.Id);
        Assert.Equal(BarberRequestStatus.Pending, storedRequest.Status);
        Assert.Null(storedRequest.RejectionReason);
        Assert.Equal(
            UserRole.Customer,
            Assert.Single(users.Users, user => user.Id == applicant.Id).Role);
        Assert.False(liveToken.IsRevoked);
    }

    [Fact]
    public async Task Reject_WithoutReason_ReturnsReasonRequired()
    {
        var applicant = CreateCustomer();
        var admin = CreateAdmin();
        var users = new FakeUserRepository();
        users.Add(applicant);
        users.Add(admin);

        var request = CreateRequest(applicant.Id);
        var requests = new FakeBarberRequestRepository();
        requests.Add(request);

        var tokens = new FakeRefreshTokenRepository();
        var unitOfWork = new FakeUnitOfWork(users, requests, tokens);
        var handler = CreateRejectHandler(requests, users, unitOfWork, admin);

        var result = await handler.Handle(
            new RejectBarberRequestCommand(request.Id, "   "),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.BarberRequest.RejectionReasonRequired,
            result.Error.Errors[0].Code);
        Assert.Equal(BarberRequestStatus.Pending, request.Status);
        Assert.Equal(1, unitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Reject_WithTooLongReason_ReturnsReasonTooLong()
    {
        var applicant = CreateCustomer();
        var admin = CreateAdmin();
        var users = new FakeUserRepository();
        users.Add(applicant);
        users.Add(admin);

        var request = CreateRequest(applicant.Id);
        var requests = new FakeBarberRequestRepository();
        requests.Add(request);

        var tokens = new FakeRefreshTokenRepository();
        var unitOfWork = new FakeUnitOfWork(users, requests, tokens);
        var handler = CreateRejectHandler(requests, users, unitOfWork, admin);

        var result = await handler.Handle(
            new RejectBarberRequestCommand(request.Id, new string('a', 501)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.BarberRequest.RejectionReasonTooLong,
            result.Error.Errors[0].Code);
        Assert.Equal(BarberRequestStatus.Pending, request.Status);
    }

    [Fact]
    public async Task Reject_WhenAlreadyRejected_ReturnsInvalidStatus()
    {
        var applicant = CreateCustomer();
        var admin = CreateAdmin();
        var users = new FakeUserRepository();
        users.Add(applicant);
        users.Add(admin);

        var request = CreateRequest(applicant.Id);
        request.Reject(admin.Id, "Уже отказали", Now);
        var requests = new FakeBarberRequestRepository();
        requests.Add(request);

        var tokens = new FakeRefreshTokenRepository();
        var unitOfWork = new FakeUnitOfWork(users, requests, tokens);
        var handler = CreateRejectHandler(requests, users, unitOfWork, admin);

        var result = await handler.Handle(
            new RejectBarberRequestCommand(request.Id, "Ещё раз"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, result.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.BarberRequest.ReviewInvalidStatus,
            result.Error.Errors[0].Code);
        Assert.Equal("Уже отказали", request.RejectionReason);
    }

    [Fact]
    public async Task GetMine_ReturnsOnlyTheLatestRequestOfCurrentUser()
    {
        var customer = CreateCustomer();
        var other = CreateCustomer("other@barbersearch.test");
        var users = new FakeUserRepository();
        users.Add(customer);
        users.Add(other);

        var requests = new FakeBarberRequestRepository();
        var older = CreateRequest(customer.Id, Now.AddMinutes(-5));
        var newer = CreateRequest(customer.Id, Now);
        requests.Add(older);
        requests.Add(newer);
        requests.Add(CreateRequest(other.Id, Now.AddMinutes(1)));

        var handler = new GetMyBarberRequestHandler(
            requests,
            new FakeCurrentUser(customer.Id));

        var result = await handler.Handle(
            new GetMyBarberRequestQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Request);
        Assert.Equal(newer.Id, result.Value.Request.Id);
    }

    [Fact]
    public async Task GetMine_WhenNoRequests_ReturnsNull()
    {
        var customer = CreateCustomer();
        var handler = new GetMyBarberRequestHandler(
            new FakeBarberRequestRepository(),
            new FakeCurrentUser(customer.Id));

        var result = await handler.Handle(
            new GetMyBarberRequestQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Request);
    }

    [Fact]
    public async Task GetAdminList_WithUnknownStatus_ReturnsValidation()
    {
        var handler = new GetAdminBarberRequestsHandler(
            new FakeBarberRequestRepository());

        var result = await handler.Handle(
            new GetAdminBarberRequestsQuery("archived", null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.BarberRequest.StatusInvalid,
            result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetAdminList_WithPageSizeOutsideBounds_ReturnsValidation()
    {
        var handler = new GetAdminBarberRequestsHandler(
            new FakeBarberRequestRepository());

        var result = await handler.Handle(
            new GetAdminBarberRequestsQuery(null, null, 0),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(
            "barberRequest.list.pageSize",
            result.Error.Errors[0].Code);
    }

    [Fact]
    public async Task GetAdminList_FiltersPendingOldestFirstAndPagesWithCursor()
    {
        var applicant = CreateCustomer();
        var admin = CreateAdmin();

        var oldest = CreateRequest(applicant.Id, Now.AddMinutes(-2));
        var approved = CreateRequest(applicant.Id, Now.AddMinutes(-1));
        approved.Approve(admin.Id, Now);
        var newest = CreateRequest(applicant.Id, Now);

        var requests = new FakeBarberRequestRepository();
        requests.AddAdminRow(BarberRequestResponseMapper.MapAdmin(oldest, applicant));
        requests.AddAdminRow(BarberRequestResponseMapper.MapAdmin(approved, applicant));
        requests.AddAdminRow(BarberRequestResponseMapper.MapAdmin(newest, applicant));

        var handler = new GetAdminBarberRequestsHandler(requests);

        var pending = await handler.Handle(
            new GetAdminBarberRequestsQuery(null, null, 1),
            CancellationToken.None);

        Assert.True(pending.IsSuccess);
        var pendingFirst = Assert.Single(pending.Value.Items);
        Assert.Equal(oldest.Id, pendingFirst.Id);
        Assert.NotNull(pending.Value.NextCursor);

        var pendingSecond = await handler.Handle(
            new GetAdminBarberRequestsQuery("pending", pending.Value.NextCursor, 1),
            CancellationToken.None);

        Assert.True(pendingSecond.IsSuccess);
        var pendingNext = Assert.Single(pendingSecond.Value.Items);
        Assert.Equal(newest.Id, pendingNext.Id);
        Assert.Null(pendingSecond.Value.NextCursor);

        var all = await handler.Handle(
            new GetAdminBarberRequestsQuery("all", null, null),
            CancellationToken.None);

        Assert.True(all.IsSuccess);
        Assert.Equal(3, all.Value.Items.Count);
        Assert.Equal(newest.Id, all.Value.Items[0].Id);
        Assert.Equal(approved.Id, all.Value.Items[1].Id);
        Assert.Equal(oldest.Id, all.Value.Items[2].Id);
    }

    [Fact]
    public async Task GetAdminDetail_WhenRequestDoesNotExist_ReturnsNotFound()
    {
        var handler = new GetAdminBarberRequestHandler(
            new FakeBarberRequestRepository());

        var result = await handler.Handle(
            new GetAdminBarberRequestQuery(Guid.CreateVersion7()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NOT_FOUND, result.Error.Errors[0].Type);
        Assert.Equal(ApiErrorCodes.BarberRequest.NotFound, result.Error.Errors[0].Code);
    }

    private static CreateBarberRequestHandler CreateHandler(
        FakeBarberRequestRepository requests,
        FakeUserRepository users,
        User currentUser)
        => new(
            requests,
            users,
            new FakeCurrentUser(currentUser.Id),
            new FakeTimeProvider(Now),
            NullLogger<CreateBarberRequestHandler>.Instance);

    private static ApproveBarberRequestHandler CreateApproveHandler(
        FakeBarberRequestRepository requests,
        FakeUserRepository users,
        FakeRefreshTokenRepository tokens,
        FakeUnitOfWork unitOfWork,
        User admin)
        => new(
            requests,
            users,
            tokens,
            unitOfWork,
            new FakeCurrentUser(admin.Id, isAdmin: true),
            new FakeTimeProvider(Now.AddHours(1)),
            NullLogger<ApproveBarberRequestHandler>.Instance);

    private static RejectBarberRequestHandler CreateRejectHandler(
        FakeBarberRequestRepository requests,
        FakeUserRepository users,
        FakeUnitOfWork unitOfWork,
        User admin)
        => new(
            requests,
            users,
            unitOfWork,
            new FakeCurrentUser(admin.Id, isAdmin: true),
            new FakeTimeProvider(Now.AddHours(1)),
            NullLogger<RejectBarberRequestHandler>.Instance);

    private static User CreateCustomer(string email = "customer@barbersearch.test")
        => User.Create(email, "hash", "Иван", "Петров").Value;

    private static User CreateAdmin()
        => User.Create(
            "admin@barbersearch.test",
            "hash",
            "Админ",
            "Тестов",
            UserRole.Admin).Value;

    private static CreateBarberRequestRequest NewRequest()
        => new(
            DisplayName: "Алексей Смирнов",
            YearsOfExperience: 7,
            City: "  Москва  ",
            About: "  Стригу с 2015 года.  ",
            HasSalon: false);

    private static BarberRequest CreateRequest(
        Guid userId,
        DateTimeOffset? createdAt = null)
        => BarberRequest.Create(
            userId,
            "Алексей Смирнов",
            7,
            "Москва",
            "Стригу с 2015 года.",
            hasSalon: false,
            createdAt ?? Now).Value;

    private static async Task<RefreshToken> AddLiveTokenAsync(
        FakeRefreshTokenRepository tokens,
        Guid userId)
    {
        var token = RefreshToken.Create(
            userId,
            new string('a', RefreshToken.HASH_LENGTH),
            Now.AddDays(14),
            Now).Value;

        await tokens.AddAsync(token);

        return token;
    }
}
