using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BarberSearch.Contracts.Audit.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.Users;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Покрытие журнала аудита действий с фотографиями (#91): загрузка, подпись, порядок,
/// публикация, блокировка, решение и удаление портфолио, а также загрузка/замена/удаление
/// аватара и решения по нему.
/// </summary>
/// <remarks>
/// <para>
/// Один <c>[Fact]</c> — одно обещание: позитив («строка есть с нужными полями») и негатив
/// («строки нет») — разные тесты. Действия идут реальными HTTP-запросами, строки читаются
/// из <c>audit_log</c> через <see cref="TestData.ReadAuditRowsAsync"/>, а фильтр проверяется
/// ответом <c>GET api/admin/audit</c> под администратором.
/// </para>
/// <para>
/// Портфолио: владелец ходит Bearer-клиентом, решения и блокировка — cookie-сессией
/// администратора (см. <see cref="PhotoApi"/>). Аватар всегда «свой»: чужих загрузок
/// у маршрута нет.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class PhotoAuditCoverageTests
{
    private const string ENDPOINT = "api/admin/audit";

    private readonly PostgresFixture _database;

    public PhotoAuditCoverageTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Portfolio_upload_writes_one_created_row_with_url_and_pending_status()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Upload Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await PhotoApi.UploadPortfolioPhotoAsync(
            app.Client,
            barber.Profile.Id,
            "Audit upload");

        var row = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "portfolio_photo"
                && candidate.EntityId == photoId);

        Assert.Equal("portfolio_photo.created", row.Action);
        Assert.Equal("portfolio_photo", row.EntityType);
        Assert.Equal(barber.User.Id, row.ActorUserId);

        using var payload = JsonDocument.Parse(row.Payload);
        var values = payload.RootElement.GetProperty("values");

        Assert.Equal(
            barber.Profile.Id.ToString(),
            values.GetProperty("barberProfileId").GetString());
        Assert.StartsWith("/api/media/portfolio/", values.GetProperty("url").GetString());
        Assert.Equal("Audit upload", values.GetProperty("description").GetString());
        Assert.Equal(0, values.GetProperty("sortOrder").GetInt32());
        Assert.True(values.GetProperty("isPublished").GetBoolean());
        Assert.False(values.GetProperty("isBlocked").GetBoolean());
        Assert.Equal("Pending", values.GetProperty("moderationStatus").GetString());

        Assert.False(values.TryGetProperty("thumbnailUrl", out _));
        Assert.False(values.TryGetProperty("rejectionReason", out _));
    }

    [Fact(Timeout = 180_000)]
    public async Task Portfolio_description_change_writes_only_description()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Description Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await PhotoApi.UploadPortfolioPhotoAsync(
            app.Client,
            barber.Profile.Id,
            "Before caption");

        await PhotoApi.UpdateDescriptionAsync(
            app.Client,
            barber.Profile.Id,
            photoId,
            "After caption");

        var row = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "portfolio_photo"
                && candidate.EntityId == photoId
                && candidate.Action == "portfolio_photo.updated");

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var description = changes.GetProperty("description");
        Assert.Equal("Before caption", description.GetProperty("from").GetString());
        Assert.Equal("After caption", description.GetProperty("to").GetString());
    }

    [Fact(Timeout = 180_000)]
    public async Task Portfolio_reorder_writes_sort_order_for_each_moved_photo()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Reorder Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var firstId = await PhotoApi.UploadPortfolioPhotoAsync(
            app.Client,
            barber.Profile.Id,
            "First");
        var secondId = await PhotoApi.UploadPortfolioPhotoAsync(
            app.Client,
            barber.Profile.Id,
            "Second");

        await PhotoApi.ReorderAsync(app.Client, barber.Profile.Id, [secondId, firstId]);

        var photoRows = (await data.ReadAuditRowsAsync())
            .Where(candidate => candidate.EntityType == "portfolio_photo"
                && (candidate.EntityId == firstId || candidate.EntityId == secondId)
                && candidate.Action == "portfolio_photo.updated")
            .ToList();

        Assert.Equal(2, photoRows.Count);

        foreach (var row in photoRows)
        {
            using var payload = JsonDocument.Parse(row.Payload);
            var changes = payload.RootElement.GetProperty("changes");

            Assert.Single(changes.EnumerateObject());
            Assert.True(changes.TryGetProperty("sortOrder", out _));
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Portfolio_publication_toggle_writes_is_published_only()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Publication Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await PhotoApi.UploadPortfolioPhotoAsync(app.Client, barber.Profile.Id);

        await PhotoApi.UnpublishAsync(app.Client, barber.Profile.Id, photoId);
        await PhotoApi.PublishAsync(app.Client, barber.Profile.Id, photoId);

        var updated = (await data.ReadAuditRowsAsync())
            .Where(candidate => candidate.EntityType == "portfolio_photo"
                && candidate.EntityId == photoId
                && candidate.Action == "portfolio_photo.updated")
            .ToList();

        Assert.Equal(2, updated.Count);

        var transitions = updated
            .Select(row =>
            {
                using var payload = JsonDocument.Parse(row.Payload);
                var changes = payload.RootElement.GetProperty("changes");

                Assert.Single(changes.EnumerateObject());

                var isPublished = changes.GetProperty("isPublished");

                return (
                    From: isPublished.GetProperty("from").GetBoolean(),
                    To: isPublished.GetProperty("to").GetBoolean());
            })
            .ToHashSet();

        Assert.Contains((From: true, To: false), transitions);
        Assert.Contains((From: false, To: true), transitions);
    }

    [Fact(Timeout = 180_000)]
    public async Task Portfolio_block_and_unblock_write_one_is_blocked_row_each()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Block Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await PhotoApi.UploadPortfolioPhotoAsync(app.Client, barber.Profile.Id);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);

        using (var block = await AvatarModerationApi.SendAdminAsync(
                   admin,
                   HttpMethod.Post,
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}/block"))
        {
            Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);
        }

        // Идемпотентная повторная блокировка не меняет состояние и не пишет строку.
        using (var repeat = await AvatarModerationApi.SendAdminAsync(
                   admin,
                   HttpMethod.Post,
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}/block"))
        {
            Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);
        }

        using (var unblock = await AvatarModerationApi.SendAdminAsync(
                   admin,
                   HttpMethod.Delete,
                   $"api/barber-profiles/{barber.Profile.Id}/photos/{photoId}/block"))
        {
            Assert.Equal(HttpStatusCode.NoContent, unblock.StatusCode);
        }

        var updated = (await data.ReadAuditRowsAsync())
            .Where(candidate => candidate.EntityType == "portfolio_photo"
                && candidate.EntityId == photoId
                && candidate.Action == "portfolio_photo.updated")
            .ToList();

        Assert.Equal(2, updated.Count);

        foreach (var row in updated)
        {
            Assert.Equal(admin.Admin.Id, row.ActorUserId);

            using var payload = JsonDocument.Parse(row.Payload);
            var changes = payload.RootElement.GetProperty("changes");

            Assert.Single(changes.EnumerateObject());
            Assert.True(changes.TryGetProperty("isBlocked", out _));
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Portfolio_approval_writes_pending_to_approved()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Approval Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await PhotoApi.UploadPortfolioPhotoAsync(app.Client, barber.Profile.Id);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);
        await PhotoApi.ApproveAsync(admin, barber.Profile.Id, photoId);

        var row = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "portfolio_photo"
                && candidate.EntityId == photoId
                && candidate.Action == "portfolio_photo.updated");

        Assert.Equal(admin.Admin.Id, row.ActorUserId);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var moderationStatus = changes.GetProperty("moderationStatus");
        Assert.Equal("Pending", moderationStatus.GetProperty("from").GetString());
        Assert.Equal("Approved", moderationStatus.GetProperty("to").GetString());

        Assert.False(changes.TryGetProperty("reviewedAt", out _));
        Assert.False(changes.TryGetProperty("reviewedByUserId", out _));
    }

    [Fact(Timeout = 180_000)]
    public async Task Portfolio_rejection_writes_pending_to_rejected_without_the_reason()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Rejection Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await PhotoApi.UploadPortfolioPhotoAsync(app.Client, barber.Profile.Id);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);

        using (var rejection = await PhotoApi.SendRejectionAsync(
                   admin,
                   barber.Profile.Id,
                   photoId,
                   "Out of focus"))
        {
            Assert.Equal(HttpStatusCode.NoContent, rejection.StatusCode);
        }

        var row = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "portfolio_photo"
                && candidate.EntityId == photoId
                && candidate.Action == "portfolio_photo.updated");

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var moderationStatus = changes.GetProperty("moderationStatus");
        Assert.Equal("Pending", moderationStatus.GetProperty("from").GetString());
        Assert.Equal("Rejected", moderationStatus.GetProperty("to").GetString());

        // Причина — свободный текст администратора: на кадре она есть, в payload — нет.
        Assert.DoesNotContain("Out of focus", row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("rejectionReason", row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("reviewedAt", row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("reviewedByUserId", row.Payload, StringComparison.Ordinal);
    }

    [Fact(Timeout = 180_000)]
    public async Task Portfolio_repeated_decision_409_writes_no_new_row()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Repeat Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await PhotoApi.UploadPortfolioPhotoAsync(app.Client, barber.Profile.Id);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);
        await PhotoApi.ApproveAsync(admin, barber.Profile.Id, photoId);

        using (var repeat = await PhotoApi.SendApprovalAsync(admin, barber.Profile.Id, photoId))
        {
            Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
            Assert.Contains(
                "portfolioPhoto.moderation.invalidStatus",
                await repeat.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        var updated = (await data.ReadAuditRowsAsync())
            .Count(candidate => candidate.EntityType == "portfolio_photo"
                && candidate.EntityId == photoId
                && candidate.Action == "portfolio_photo.updated");

        Assert.Equal(1, updated);
    }

    [Fact(Timeout = 180_000)]
    public async Task Portfolio_delete_writes_deleted_row_that_keeps_the_url()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Delete Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await PhotoApi.UploadPortfolioPhotoAsync(
            app.Client,
            barber.Profile.Id,
            "To be removed");

        var created = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "portfolio_photo"
                && candidate.EntityId == photoId
                && candidate.Action == "portfolio_photo.created");

        using var createdPayload = JsonDocument.Parse(created.Payload);
        var url = createdPayload.RootElement
            .GetProperty("values")
            .GetProperty("url")
            .GetString();

        await PhotoApi.RemoveAsync(app.Client, barber.Profile.Id, photoId);

        var deleted = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "portfolio_photo"
                && candidate.EntityId == photoId
                && candidate.Action == "portfolio_photo.deleted");

        using var payload = JsonDocument.Parse(deleted.Payload);
        var values = payload.RootElement.GetProperty("values");

        Assert.Equal(url, values.GetProperty("url").GetString());
        Assert.Equal(
            barber.Profile.Id.ToString(),
            values.GetProperty("barberProfileId").GetString());
        Assert.Equal("Pending", values.GetProperty("moderationStatus").GetString());
    }

    [Fact(Timeout = 180_000)]
    public async Task Portfolio_filter_returns_created_updated_and_deleted_rows_only()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Filter Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var photoId = await PhotoApi.UploadPortfolioPhotoAsync(
            app.Client,
            barber.Profile.Id,
            "Filtered");

        await PhotoApi.UpdateDescriptionAsync(
            app.Client,
            barber.Profile.Id,
            photoId,
            "Filtered again");
        await PhotoApi.RemoveAsync(app.Client, barber.Profile.Id, photoId);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);

        var photoRows = await ReadPageAsync(admin.Client, $"{ENDPOINT}?entityType=portfolio_photo");

        Assert.Equal(3, photoRows.Count);
        Assert.All(photoRows, row => Assert.Equal("portfolio_photo", row.EntityType));
        Assert.All(photoRows, row => Assert.Equal(photoId, row.EntityId));
        Assert.Contains(photoRows, row => row.Action == "portfolio_photo.created");
        Assert.Contains(photoRows, row => row.Action == "portfolio_photo.updated");
        Assert.Contains(photoRows, row => row.Action == "portfolio_photo.deleted");

        // Строка пережила удаление кадра и находится фильтром; под user её нет.
        var userRows = await ReadPageAsync(admin.Client, $"{ENDPOINT}?entityType=user");
        Assert.DoesNotContain(userRows, row => row.EntityId == photoId);

        var allRows = await ReadPageAsync(admin.Client, $"{ENDPOINT}?entityType=all");
        Assert.Contains(allRows, row => row.EntityId == photoId);
    }

    [Fact(Timeout = 180_000)]
    public async Task Avatar_upload_approval_and_deletion_write_user_updated_rows()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Audit Avatar Barber");

        await app.AuthenticateAsync(barber.User.Email);

        var uploaded = await PhotoApi.UploadAvatarAsync(app.Client);

        var uploadRow = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "user"
                && candidate.EntityId == barber.User.Id);

        Assert.Equal("user.updated", uploadRow.Action);
        Assert.Equal(barber.User.Id, uploadRow.ActorUserId);

        using (var payload = JsonDocument.Parse(uploadRow.Payload))
        {
            var changes = payload.RootElement.GetProperty("changes");

            Assert.Equal(uploaded.AvatarUrl, changes.GetProperty("avatarUrl").GetProperty("to").GetString());
            Assert.Equal("Pending", changes.GetProperty("avatarModerationStatus").GetProperty("to").GetString());
        }

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);
        await AvatarModerationApi.ApproveAsync(admin, barber.User.Id);

        var approveRow = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "user"
                && candidate.EntityId == barber.User.Id
                && candidate.Action == "user.updated"
                && candidate.ActorUserId == admin.Admin.Id);

        using (var payload = JsonDocument.Parse(approveRow.Payload))
        {
            var changes = payload.RootElement.GetProperty("changes");

            Assert.Single(changes.EnumerateObject());
            Assert.Equal(
                "Pending",
                changes.GetProperty("avatarModerationStatus").GetProperty("from").GetString());
            Assert.Equal(
                "Approved",
                changes.GetProperty("avatarModerationStatus").GetProperty("to").GetString());
        }

        await PhotoApi.DeleteAvatarAsync(app.Client);

        var deleteRow = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "user"
                && candidate.EntityId == barber.User.Id
                && candidate.ActorUserId == barber.User.Id
                && candidate.Action == "user.updated"
                && HasAvatarUrlToNull(candidate.Payload));

        using (var payload = JsonDocument.Parse(deleteRow.Payload))
        {
            var changes = payload.RootElement.GetProperty("changes");

            var avatarUrl = changes.GetProperty("avatarUrl");
            Assert.Equal(uploaded.AvatarUrl, avatarUrl.GetProperty("from").GetString());
            Assert.Equal(JsonValueKind.Null, avatarUrl.GetProperty("to").ValueKind);
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task Avatar_rejection_writes_only_moderation_status_without_the_reason()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);

        var owner = await app.CreateUserAsync(
            $"audit-avatar-reject-{Guid.NewGuid():N}@barbersearch.test",
            UserRole.Barber);

        await app.AuthenticateAsync(owner.Email);
        await PhotoApi.UploadAvatarAsync(app.Client);

        await using var admin = await AvatarModerationApi.StartAdminSessionAsync(app);
        await AvatarModerationApi.RejectAsync(admin, owner.Id, "Too blurry to publish");

        var row = Assert.Single(
            await data.ReadAuditRowsAsync(),
            candidate => candidate.EntityType == "user"
                && candidate.EntityId == owner.Id
                && candidate.ActorUserId == admin.Admin.Id);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var moderationStatus = changes.GetProperty("avatarModerationStatus");
        Assert.Equal("Pending", moderationStatus.GetProperty("from").GetString());
        Assert.Equal("Rejected", moderationStatus.GetProperty("to").GetString());

        Assert.DoesNotContain("Too blurry to publish", row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("avatarRejectionReason", row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("avatarUrl", row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("avatarReviewedAt", row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("avatarReviewedByUserId", row.Payload, StringComparison.Ordinal);
    }

    [Fact(Timeout = 180_000)]
    public async Task Portfolio_upload_without_permission_403_writes_no_row()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var owner = await data.CreateBookableBarberAsync("Audit Owner Barber");
        var stranger = await data.CreateBookableBarberAsync("Audit Stranger Barber");

        await app.AuthenticateAsync(stranger.User.Email);

        using (var response = await PhotoApi.SendPortfolioUploadAsync(
                   app.Client,
                   owner.Profile.Id))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // Упавшая до SaveChanges загрузка не оставляет «осиротевшей» строки аудита.
        Assert.Equal(0, await data.CountAuditRowsAsync("portfolio_photo"));
    }

    private static async Task<IReadOnlyList<AdminAuditEntryResponse>> ReadPageAsync(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<PagedResponse<AdminAuditEntryResponse>>(TestApp.JsonOptions);

        Assert.NotNull(page);

        return page.Items;
    }

    /// <summary>
    /// Признак строки удаления аватара: в diff есть <c>avatarUrl</c> с <c>to = null</c>.
    /// Разбор через <see cref="JsonDocument"/>, а не поиск подстроки: <c>jsonb</c> меняет
    /// порядок ключей и пробелы, поэтому сравнение текста было бы хрупким.
    /// </summary>
    private static bool HasAvatarUrlToNull(string payload)
    {
        using var document = JsonDocument.Parse(payload);

        return document.RootElement
            .GetProperty("changes")
            .TryGetProperty("avatarUrl", out var avatarUrl)
            && avatarUrl.TryGetProperty("to", out var to)
            && to.ValueKind == JsonValueKind.Null;
    }
}
