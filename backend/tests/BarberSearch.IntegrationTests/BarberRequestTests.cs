using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Contracts.BarberRequests.CreateBarberRequest;
using BarberSearch.Contracts.BarberRequests.GetMyBarberRequest;
using BarberSearch.Contracts.BarberRequests.RejectBarberRequest;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Заявка в барберы на реальной PostgreSQL: права, транзакция решения и отзыв токенов.
/// </summary>
/// <remarks>
/// Сценарии идут через HTTP, потому что проверяют контракт (статусы, коды, JSON), а состояние
/// потом сверяется в БД. Очередь и порядок засеиваются доменными фабриками с явным временем:
/// так FIFO проверяется детерминированно, без «двух запросов подряд».
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class BarberRequestTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Настройки чтения JSON: сервер пишет перечисления строками
    /// (<c>JsonStringEnumConverter</c>), поэтому клиент обязан читать их так же.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _database;

    public BarberRequestTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Customer_submits_request_sees_it_in_mine_and_duplicate_conflicts()
    {
        await using var app = await TestApp.StartAsync(_database);
        var customer = await app.CreateUserAsync(
            $"request-customer-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        using var created = await app.Client.PostAsJsonAsync(
            "api/barber-requests",
            NewRequest());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body = await created.Content.ReadFromJsonAsync<BarberRequestResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal(BarberRequestStatus.Pending, body.Status);
        Assert.Equal("Алексей Смирнов", body.DisplayName);
        Assert.Equal(7, body.YearsOfExperience);
        Assert.Equal("Москва", body.City);
        Assert.Equal("Стригу с 2015 года.", body.About);
        Assert.False(body.HasSalon);
        Assert.Null(body.ReviewedAt);
        Assert.Null(body.RejectionReason);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.BarberRequests.AsNoTracking().SingleAsync();
            Assert.Equal(body.Id, row.Id);
            Assert.Equal(customer.Id, row.UserId);
            Assert.Equal(BarberRequestStatus.Pending, row.Status);
            Assert.Equal("Москва", row.City);
            Assert.Null(row.ReviewedAt);
        }

        using var mine = await app.Client.GetAsync("api/barber-requests/mine");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);

        var mineBody = await mine.Content.ReadFromJsonAsync<MyBarberRequestResponse>(
            JsonOptions);
        Assert.NotNull(mineBody);
        var mineRequest = Assert.IsType<BarberRequestResponse>(mineBody.Request);
        Assert.Equal(body.Id, mineRequest.Id);

        using var duplicate = await app.Client.PostAsJsonAsync(
            "api/barber-requests",
            NewRequest());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains(
            "barberRequest.create.alreadyPending",
            await duplicate.Content.ReadAsStringAsync());

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.BarberRequests.AsNoTracking().CountAsync());
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Partial_unique_index_maps_duplicate_pending_to_conflict_not_500()
    {
        await using var app = await TestApp.StartAsync(_database);
        var customer = await app.CreateUserAsync(
            $"request-index-{Guid.NewGuid():N}@barbersearch.test");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var first = Require(BarberRequest.Create(
            customer.Id,
            "Первый",
            5,
            "Москва",
            null,
            hasSalon: false,
            BaseTime));

        db.BarberRequests.Add(first);
        await db.SaveChangesAsync();

        // Вторая Pending-заявка того же пользователя обходит предпроверку: репозиторий
        // вызывается напрямую, как при гонке двух подач. Частичный уникальный индекс обязан
        // вернуть barberRequest.add.uniqueViolation (CONFLICT → 409), а не бросить исключение.
        var second = Require(BarberRequest.Create(
            customer.Id,
            "Второй",
            5,
            "Москва",
            null,
            hasSalon: false,
            BaseTime.AddMinutes(1)));

        var repository = new BarberRequestRepository(db);

        var added = await repository.AddAsync(second);

        Assert.True(added.IsFailure);
        Assert.Equal(ErrorType.CONFLICT, added.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.BarberRequest.AddUniqueViolation,
            added.Error.Errors[0].Code);

        // Failure вместо 500; обработчик переводит этот код в create.alreadyPending
        // (проверено Application-тестом на фейке).
        Assert.Equal(1, await db.BarberRequests.AsNoTracking().CountAsync());
    }

    [Fact(Timeout = 180_000)]
    public async Task Anonymous_and_non_customer_access_is_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);

        using var anonymousCreate = await app.Client.PostAsJsonAsync(
            "api/barber-requests",
            NewRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousCreate.StatusCode);

        using var anonymousMine = await app.Client.GetAsync("api/barber-requests/mine");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousMine.StatusCode);

        using var anonymousAdmin = await app.Client.GetAsync("api/admin/barber-requests");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousAdmin.StatusCode);

        var barber = await app.CreateUserAsync(
            $"request-barber-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);
        await app.AuthenticateAsync(barber.Email);

        using var forbiddenCreate = await app.Client.PostAsJsonAsync(
            "api/barber-requests",
            NewRequest());
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreate.StatusCode);
        Assert.Contains(
            "barberRequest.create.forbidden",
            await forbiddenCreate.Content.ReadAsStringAsync());

        // Свой статус доступен любой роли: заявок у барбера нет, ответ — null, не 404.
        using var mine = await app.Client.GetAsync("api/barber-requests/mine");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var mineBody = await mine.Content.ReadFromJsonAsync<MyBarberRequestResponse>(
            JsonOptions);
        Assert.NotNull(mineBody);
        Assert.Null(mineBody.Request);

        var customer = await app.CreateUserAsync(
            $"request-admin-forbidden-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        using var forbiddenList = await app.Client.GetAsync("api/admin/barber-requests");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenList.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.BarberRequests.AsNoTracking().CountAsync());
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_lists_pending_fifo_and_all_newest_first()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(
            $"request-list-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        var olderApplicant = await app.CreateUserAsync(
            $"request-older-{Guid.NewGuid():N}@barbersearch.test");
        var newerApplicant = await app.CreateUserAsync(
            $"request-newer-{Guid.NewGuid():N}@barbersearch.test");
        var approvedApplicant = await app.CreateUserAsync(
            $"request-approved-{Guid.NewGuid():N}@barbersearch.test");

        var older = await SeedAsync(app, olderApplicant.Id, BaseTime);
        var approved = await SeedAsync(
            app,
            approvedApplicant.Id,
            BaseTime.AddMinutes(1),
            BarberRequestStatus.Approved,
            reviewerId: admin.Id);
        var newer = await SeedAsync(app, newerApplicant.Id, BaseTime.AddMinutes(2));

        await app.AuthenticateAsync(admin.Email);

        using var pending = await app.Client.GetAsync("api/admin/barber-requests");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);

        var pendingPage = await pending.Content.ReadFromJsonAsync<
            PagedResponse<AdminBarberRequestResponse>>(JsonOptions);
        Assert.NotNull(pendingPage);
        Assert.Equal(2, pendingPage.Items.Count);
        Assert.Equal(older, pendingPage.Items[0].Id);
        Assert.Equal(newer, pendingPage.Items[1].Id);
        Assert.Equal("Integration Test", pendingPage.Items[0].Applicant.FullName);
        Assert.Equal(olderApplicant.Email, pendingPage.Items[0].Applicant.Email);
        Assert.DoesNotContain(approved, pendingPage.Items.Select(item => item.Id));

        using var all = await app.Client.GetAsync("api/admin/barber-requests?status=all");
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);

        var allPage = await all.Content.ReadFromJsonAsync<
            PagedResponse<AdminBarberRequestResponse>>(JsonOptions);
        Assert.NotNull(allPage);
        Assert.Equal(3, allPage.Items.Count);
        Assert.Equal(newer, allPage.Items[0].Id);
        Assert.Equal(approved, allPage.Items[1].Id);
        Assert.Equal(older, allPage.Items[2].Id);

        using var firstPage = await app.Client.GetAsync(
            "api/admin/barber-requests?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);

        var firstPageBody = await firstPage.Content.ReadFromJsonAsync<
            PagedResponse<AdminBarberRequestResponse>>(JsonOptions);
        Assert.NotNull(firstPageBody);
        var firstItem = Assert.Single(firstPageBody.Items);
        Assert.Equal(older, firstItem.Id);
        Assert.NotNull(firstPageBody.NextCursor);

        using var secondPage = await app.Client.GetAsync(
            $"api/admin/barber-requests?pageSize=1&cursor={Uri.EscapeDataString(firstPageBody.NextCursor!)}");
        Assert.Equal(HttpStatusCode.OK, secondPage.StatusCode);

        var secondPageBody = await secondPage.Content.ReadFromJsonAsync<
            PagedResponse<AdminBarberRequestResponse>>(JsonOptions);
        Assert.NotNull(secondPageBody);
        var secondItem = Assert.Single(secondPageBody.Items);
        Assert.Equal(newer, secondItem.Id);
        Assert.Null(secondPageBody.NextCursor);

        using var invalidStatus = await app.Client.GetAsync(
            "api/admin/barber-requests?status=archived");
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);
        Assert.Contains(
            "barberRequest.list.status",
            await invalidStatus.Content.ReadAsStringAsync());
    }

    [Fact(Timeout = 180_000)]
    public async Task Approval_changes_role_revokes_tokens_and_keeps_profile_unlinked()
    {
        await using var app = await TestApp.StartAsync(_database);
        var customer = await app.CreateUserAsync(
            $"request-approve-{Guid.NewGuid():N}@barbersearch.test");

        // Живой refresh-токен заявителя: одобрение обязано его отозвать.
        var applicantTokens = await app.LoginAsync(customer.Email);

        await app.AuthenticateAsync(customer.Email);
        using var created = await app.Client.PostAsJsonAsync(
            "api/barber-requests",
            NewRequest());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var request = await created.Content.ReadFromJsonAsync<BarberRequestResponse>(
            JsonOptions);
        Assert.NotNull(request);

        var admin = await app.CreateUserAsync(
            $"request-approve-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var approved = await app.Client.PostAsync(
            $"api/admin/barber-requests/{request.Id}/approval",
            null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var approvedBody = await approved.Content.ReadFromJsonAsync<
            AdminBarberRequestResponse>(JsonOptions);
        Assert.NotNull(approvedBody);
        Assert.Equal(BarberRequestStatus.Approved, approvedBody.Status);
        Assert.Equal(admin.Id, approvedBody.ReviewedByUserId);
        Assert.NotNull(approvedBody.ReviewedAt);
        Assert.Null(approvedBody.RejectionReason);
        Assert.Equal(customer.Id, approvedBody.Applicant.Id);
        Assert.Equal(customer.Email, approvedBody.Applicant.Email);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var storedUser = await db.Users
                .AsNoTracking()
                .SingleAsync(user => user.Id == customer.Id);
            Assert.Equal(UserRole.Barber, storedUser.Role);
            Assert.Null(storedUser.BarberProfileId);

            var storedRequest = await db.BarberRequests
                .AsNoTracking()
                .SingleAsync(row => row.Id == request.Id);
            Assert.Equal(BarberRequestStatus.Approved, storedRequest.Status);
            Assert.Equal(admin.Id, storedRequest.ReviewedByUserId);

            var tokens = await db.RefreshTokens
                .AsNoTracking()
                .Where(token => token.UserId == customer.Id)
                .ToListAsync();
            Assert.NotEmpty(tokens);
            Assert.All(tokens, token => Assert.NotNull(token.RevokedAt));
        }

        // Смена роли ротирует security stamp: уже выданный access-токен заявителя мёртв.
        using (var stale = new HttpRequestMessage(
                   HttpMethod.Get,
                   "api/barber-requests/mine"))
        {
            stale.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    applicantTokens.AccessToken);

            using var staleResponse = await app.Client.SendAsync(stale);
            Assert.Equal(HttpStatusCode.Unauthorized, staleResponse.StatusCode);
        }

        using var secondApproval = await app.Client.PostAsync(
            $"api/admin/barber-requests/{request.Id}/approval",
            null);
        Assert.Equal(HttpStatusCode.Conflict, secondApproval.StatusCode);
        Assert.Contains(
            "barberRequest.review.invalidStatus",
            await secondApproval.Content.ReadAsStringAsync());
    }

    [Fact(Timeout = 180_000)]
    public async Task Rejection_keeps_role_and_stores_reason()
    {
        await using var app = await TestApp.StartAsync(_database);
        var customer = await app.CreateUserAsync(
            $"request-reject-{Guid.NewGuid():N}@barbersearch.test");
        await app.LoginAsync(customer.Email);

        await app.AuthenticateAsync(customer.Email);
        using var created = await app.Client.PostAsJsonAsync(
            "api/barber-requests",
            NewRequest());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var request = await created.Content.ReadFromJsonAsync<BarberRequestResponse>(
            JsonOptions);
        Assert.NotNull(request);

        var admin = await app.CreateUserAsync(
            $"request-reject-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var rejected = await app.Client.PostAsJsonAsync(
            $"api/admin/barber-requests/{request.Id}/rejection",
            new RejectBarberRequestRequest("  Мало подтверждённого опыта  "));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        var rejectedBody = await rejected.Content.ReadFromJsonAsync<
            AdminBarberRequestResponse>(JsonOptions);
        Assert.NotNull(rejectedBody);
        Assert.Equal(BarberRequestStatus.Rejected, rejectedBody.Status);
        Assert.Equal("Мало подтверждённого опыта", rejectedBody.RejectionReason);
        Assert.Equal(admin.Id, rejectedBody.ReviewedByUserId);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var storedUser = await db.Users
                .AsNoTracking()
                .SingleAsync(user => user.Id == customer.Id);
            Assert.Equal(UserRole.Customer, storedUser.Role);

            var storedRequest = await db.BarberRequests
                .AsNoTracking()
                .SingleAsync(row => row.Id == request.Id);
            Assert.Equal(BarberRequestStatus.Rejected, storedRequest.Status);

            // Отказ не трогает сессии: все токены заявителя остались живыми.
            var tokens = await db.RefreshTokens
                .AsNoTracking()
                .Where(token => token.UserId == customer.Id)
                .ToListAsync();
            Assert.NotEmpty(tokens);
            Assert.All(tokens, token => Assert.Null(token.RevokedAt));
        }

        // Повторное решение — 409, пустая причина на новой заявке — 400.
        using var repeat = await app.Client.PostAsJsonAsync(
            $"api/admin/barber-requests/{request.Id}/rejection",
            new RejectBarberRequestRequest("Ещё раз"));
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);

        var other = await app.CreateUserAsync(
            $"request-reject-other-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(other.Email);
        using var otherCreated = await app.Client.PostAsJsonAsync(
            "api/barber-requests",
            NewRequest());
        Assert.Equal(HttpStatusCode.Created, otherCreated.StatusCode);

        var otherRequest = await otherCreated.Content.ReadFromJsonAsync<BarberRequestResponse>(
            JsonOptions);
        Assert.NotNull(otherRequest);

        await app.AuthenticateAsync(admin.Email);

        using var emptyReason = await app.Client.PostAsJsonAsync(
            $"api/admin/barber-requests/{otherRequest.Id}/rejection",
            new RejectBarberRequestRequest("   "));
        Assert.Equal(HttpStatusCode.BadRequest, emptyReason.StatusCode);
        Assert.Contains(
            "barberRequest.rejection.reasonRequired",
            await emptyReason.Content.ReadAsStringAsync());

        using var longReason = await app.Client.PostAsJsonAsync(
            $"api/admin/barber-requests/{otherRequest.Id}/rejection",
            new RejectBarberRequestRequest(new string('a', 501)));
        Assert.Equal(HttpStatusCode.BadRequest, longReason.StatusCode);
        Assert.Contains(
            "barberRequest.rejection.reasonTooLong",
            await longReason.Content.ReadAsStringAsync());

        using var unknown = await app.Client.PostAsJsonAsync(
            $"api/admin/barber-requests/{Guid.NewGuid()}/rejection",
            new RejectBarberRequestRequest("Нет такой заявки"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains(
            "barberRequest.notFound",
            await unknown.Content.ReadAsStringAsync());
    }

    [Fact(Timeout = 180_000)]
    public async Task Rejected_customer_can_submit_a_new_request()
    {
        await using var app = await TestApp.StartAsync(_database);
        var customer = await app.CreateUserAsync(
            $"request-resubmit-{Guid.NewGuid():N}@barbersearch.test");
        await app.AuthenticateAsync(customer.Email);

        using var created = await app.Client.PostAsJsonAsync(
            "api/barber-requests",
            NewRequest());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var first = await created.Content.ReadFromJsonAsync<BarberRequestResponse>(
            JsonOptions);
        Assert.NotNull(first);

        var admin = await app.CreateUserAsync(
            $"request-resubmit-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        await app.AuthenticateAsync(admin.Email);

        using var rejected = await app.Client.PostAsJsonAsync(
            $"api/admin/barber-requests/{first.Id}/rejection",
            new RejectBarberRequestRequest("Мало опыта"));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        // После отказа частичный уникальный индекс больше не держит пользователя:
        // новая Pending-заявка проходит предпроверку и вставляется.
        await app.AuthenticateAsync(customer.Email);

        using var again = await app.Client.PostAsJsonAsync(
            "api/barber-requests",
            NewRequest());
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);

        var second = await again.Content.ReadFromJsonAsync<BarberRequestResponse>(
            JsonOptions);
        Assert.NotNull(second);
        Assert.Equal(BarberRequestStatus.Pending, second.Status);
        Assert.NotEqual(first.Id, second.Id);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await db.BarberRequests
            .AsNoTracking()
            .Where(row => row.UserId == customer.Id)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(
            BarberRequestStatus.Rejected,
            Assert.Single(rows, row => row.Id == first.Id).Status);
        Assert.Equal(
            BarberRequestStatus.Pending,
            Assert.Single(rows, row => row.Id == second.Id).Status);
    }

    [Fact(Timeout = 180_000)]
    public async Task Admin_detail_returns_request_or_not_found()
    {
        await using var app = await TestApp.StartAsync(_database);
        var admin = await app.CreateUserAsync(
            $"request-detail-admin-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Admin);
        var applicant = await app.CreateUserAsync(
            $"request-detail-applicant-{Guid.NewGuid():N}@barbersearch.test");

        var requestId = await SeedAsync(app, applicant.Id, BaseTime);
        await app.AuthenticateAsync(admin.Email);

        using var detail = await app.Client.GetAsync(
            $"api/admin/barber-requests/{requestId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var detailBody = await detail.Content.ReadFromJsonAsync<
            AdminBarberRequestResponse>(JsonOptions);
        Assert.NotNull(detailBody);
        Assert.Equal(requestId, detailBody.Id);
        Assert.Equal(BarberRequestStatus.Pending, detailBody.Status);
        Assert.Equal(applicant.Email, detailBody.Applicant.Email);

        using var missing = await app.Client.GetAsync(
            $"api/admin/barber-requests/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains(
            "barberRequest.notFound",
            await missing.Content.ReadAsStringAsync());
    }

    [Fact(Timeout = 180_000)]
    public async Task Cookie_submission_without_csrf_token_is_rejected()
    {
        await using var app = await TestApp.StartAsync(_database);
        var customer = await app.CreateUserAsync(
            $"request-csrf-{Guid.NewGuid():N}@barbersearch.test");

        await using var session = await app.SignInWithCookieAsync(customer.Email);
        using var response = await session.Client.PostAsJsonAsync(
            "api/barber-requests",
            NewRequest());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "auth.csrf.invalid",
            await response.Content.ReadAsStringAsync());

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.BarberRequests.AsNoTracking().CountAsync());
    }

    private static CreateBarberRequestRequest NewRequest()
        => new(
            DisplayName: "Алексей Смирнов",
            YearsOfExperience: 7,
            City: "Москва",
            About: "Стригу с 2015 года.",
            HasSalon: false);

    private static async Task<Guid> SeedAsync(
        TestApp app,
        Guid userId,
        DateTimeOffset createdAt,
        BarberRequestStatus status = BarberRequestStatus.Pending,
        Guid? reviewerId = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var request = Require(BarberRequest.Create(
            userId,
            "Алексей Смирнов",
            7,
            "Москва",
            "Стригу с 2015 года.",
            hasSalon: false,
            createdAt));

        if (status == BarberRequestStatus.Approved)
        {
            // reviewed_by_user_id — реальный FK на users: решение должно ссылаться
            // на существующего администратора, иначе вставка нарушит ограничение.
            var reviewer = reviewerId
                ?? throw new ArgumentException(
                    "An approved seeded request needs a reviewer id.",
                    nameof(reviewerId));

            Require(request.Approve(reviewer, createdAt.AddMinutes(1)));
        }

        db.BarberRequests.Add(request);
        await db.SaveChangesAsync();

        return request.Id;
    }

    private static T Require<T>(Result<T, Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }

        return result.Value;
    }

    private static void Require(UnitResult<Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }
    }
}
