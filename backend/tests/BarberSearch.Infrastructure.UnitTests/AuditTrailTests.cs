using System.Text.Json;
using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Reviews;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Audit;
using BarberSearch.Infrastructure.UnitTests.Fakes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет сборку строк аудита из change tracker (R-34) — без подключения к базе.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AuditTrail.Capture"/> обходит только добавленные, изменённые и удалённые
/// сущности allowlist (<c>User</c>, <c>BarberProfile</c>, <c>Review</c>). Строки аудита
/// кладутся в тот же контекст, поэтому проверяются через <c>context.AuditLogs.Local</c>:
/// <c>SaveChanges</c> в тестах не вызывается, соединение с базой не открывается.
/// </para>
/// <para>
/// Для изменения сущности сначала присоединяем её как <c>Unchanged</c>: EF снимает снимок
/// исходных значений, и последующий доменный метод даёт настоящий <c>Modified</c>
/// с <c>OriginalValue</c>, а не отсоединённый граф, где все свойства «изменены».
/// </para>
/// </remarks>
public class AuditTrailTests
{
    private const string PasswordHash = "pbkdf2-sha256-secret-password-hash";

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ActorId = Guid.CreateVersion7();

    [Fact]
    public void Capture_WhenUserCreated_WritesCreatedRowWithRedactedSecrets()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;

        context.Users.Add(user);
        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("user.created", row.Action);
        Assert.Equal("user", row.EntityType);
        Assert.Equal(user.Id, row.EntityId);
        Assert.Equal(ActorId, row.ActorUserId);
        Assert.Equal(Now, row.OccurredAt);

        using var payload = JsonDocument.Parse(row.Payload);
        var values = payload.RootElement.GetProperty("values");

        Assert.Equal("ivan@example.com", values.GetProperty("email").GetString());
        Assert.Equal("Customer", values.GetProperty("role").GetString());
        Assert.Equal("[redacted]", values.GetProperty("passwordHash").GetString());
        Assert.Equal("[redacted]", values.GetProperty("securityStamp").GetString());
    }

    [Fact]
    public void Capture_WhenUserCreated_DoesNotStorePasswordHashInPlainText()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;

        context.Users.Add(user);
        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.DoesNotContain(PasswordHash, row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("pbkdf2", row.Payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capture_WhenUserRoleChanged_WritesChangesWithFromAndTo()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = TrackUnchanged(context, User.Create(
            "ivan@example.com",
            PasswordHash,
            "Ivan",
            "Petrov").Value, context.Users);

        Assert.True(user.ChangeRole(UserRole.Admin).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("user.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var role = payload.RootElement.GetProperty("changes").GetProperty("role");

        Assert.Equal("Customer", role.GetProperty("from").GetString());
        Assert.Equal("Admin", role.GetProperty("to").GetString());

        // SecurityStamp меняется вместе с ролью, но в открытом виде в аудит не попадает.
        var stamp = payload.RootElement.GetProperty("changes").GetProperty("securityStamp");

        Assert.Equal("[redacted]", stamp.GetProperty("from").GetString());
        Assert.Equal("[redacted]", stamp.GetProperty("to").GetString());
    }

    [Fact]
    public void Capture_WhenBarberProfileVerified_WritesIsVerifiedChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = BarberProfile.Create(
            Guid.CreateVersion7(),
            "Ivan",
            5,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null).Value;

        profile = TrackUnchanged(context, profile, context.BarberProfiles);

        Assert.True(profile.Verify().IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("barber_profile.updated", row.Action);
        Assert.Equal("barber_profile", row.EntityType);

        using var payload = JsonDocument.Parse(row.Payload);
        var isVerified = payload.RootElement
            .GetProperty("changes")
            .GetProperty("isVerified");

        Assert.False(isVerified.GetProperty("from").GetBoolean());
        Assert.True(isVerified.GetProperty("to").GetBoolean());
    }

    [Fact]
    public void Capture_WhenReviewHidden_WritesIsPublishedChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var review = CreateReview(text: "Great cut");

        // R-35: Create оставляет отзыв скрытым, поэтому для перехода true→false
        // публикуем его до снятия снимка состояния.
        Assert.True(review.Publish().IsSuccess);

        review = TrackUnchanged(context, review, context.Reviews);

        Assert.True(review.Hide().IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("review.updated", row.Action);
        Assert.Equal("review", row.EntityType);

        using var payload = JsonDocument.Parse(row.Payload);
        var isPublished = payload.RootElement
            .GetProperty("changes")
            .GetProperty("isPublished");

        Assert.True(isPublished.GetProperty("from").GetBoolean());
        Assert.False(isPublished.GetProperty("to").GetBoolean());
    }

    [Fact]
    public void Capture_WhenReviewTextLongerThanLimit_TruncatesWithMarker()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var text = new string('a', AuditLogEntry.MAX_PAYLOAD_STRING_LENGTH + 100);
        var review = CreateReview(text);

        context.Reviews.Add(review);
        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        using var payload = JsonDocument.Parse(row.Payload);
        var stored = payload.RootElement
            .GetProperty("values")
            .GetProperty("text")
            .GetString();

        Assert.NotNull(stored);
        Assert.Equal(AuditLogEntry.MAX_PAYLOAD_STRING_LENGTH, stored.Length);
        Assert.EndsWith("...", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_WhenOnlyLoginRecorded_WritesNothing()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = TrackUnchanged(context, User.Create(
            "ivan@example.com",
            PasswordHash,
            "Ivan",
            "Petrov").Value, context.Users);

        // LastLoginAt и UpdatedAt вне allowlist: вход — техническое событие, не аудит.
        Assert.True(user.RecordLogin().IsSuccess);

        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_WhenEntityOutsideAllowlist_WritesNothing()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var salon = Salon.Create(
            "Salon",
            "Street 1",
            "Moscow",
            "Russia",
            "UTC",
            55.7558,
            37.6173).Value;

        context.Salons.Add(salon);
        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    private static AuditTrail CreateAudit()
        => new(new FakeCurrentUser(ActorId), new FakeTimeProvider(Now));

    private static TEntity TrackUnchanged<TEntity>(
        ApplicationDbContext context,
        TEntity entity,
        DbSet<TEntity> set)
        where TEntity : class
    {
        set.Add(entity);
        context.Entry(entity).State = EntityState.Unchanged;

        return entity;
    }

    private static Review CreateReview(string? text)
    {
        var startAt = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);
        var booking = Booking.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddMinutes(30),
            1500m,
            $"key-{Guid.NewGuid()}").Value;

        return Review.Create(booking, Guid.CreateVersion7(), 5, text).Value;
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=59999;Database=unused;Username=test;Password=test")
            .Options);

    /// <summary>
    /// Текущий пользователь с заданным идентификатором: actor строки аудита.
    /// </summary>
    private sealed class FakeCurrentUser : ICurrentUser
    {
        private readonly Guid _userId;

        public FakeCurrentUser(Guid userId)
        {
            _userId = userId;
        }

        public bool IsAdmin => false;

        public bool TryGetUserId(out Guid userId)
        {
            userId = _userId;

            return true;
        }
    }
}
