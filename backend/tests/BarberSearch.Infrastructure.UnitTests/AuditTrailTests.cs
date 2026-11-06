using System.Text.Json;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Audit.Admin;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.Reviews;
using BarberSearch.Domain.Salons;
using BarberSearch.Domain.Specializations;
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
/// сущности allowlist (<c>User</c>, <c>BarberProfile</c>, <c>Review</c>, <c>Salon</c>,
/// <c>HairService</c>, <c>Specialization</c>, <c>Booking</c>). Строки аудита кладутся
/// в тот же контекст, поэтому проверяются через <c>context.AuditLogs.Local</c>:
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
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;

        Assert.True(user.AddFavorite(Guid.CreateVersion7()).IsSuccess);
        var favorite = Assert.Single(user.FavoriteBarbers);

        // Избранное — дочерняя сущность User, а не отдельный агрегат: строки аудита у неё нет.
        context.FavoriteBarbers.Add(favorite);
        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_WhenSalonCreated_WritesValuesRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var salon = Salon.Create(
            "Best Cuts",
            "Main Street 1",
            "Moscow",
            "Russia",
            "Europe/Moscow",
            55.7558,
            37.6173,
            postalCode: "101000",
            phoneNumber: "+74950000000").Value;

        context.Salons.Add(salon);
        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("salon.created", row.Action);
        Assert.Equal("salon", row.EntityType);
        Assert.Equal(salon.Id, row.EntityId);

        using var payload = JsonDocument.Parse(row.Payload);
        var values = payload.RootElement.GetProperty("values");

        Assert.Equal("Best Cuts", values.GetProperty("name").GetString());
        Assert.Equal("Moscow", values.GetProperty("city").GetString());
        Assert.True(values.GetProperty("isActive").GetBoolean());
        Assert.Equal("Salon", values.GetProperty("kind").GetString());
        Assert.True(values.GetProperty("isListed").GetBoolean());
        Assert.Equal(55.7558, values.GetProperty("latitude").GetDouble());
        Assert.Equal(37.6173, values.GetProperty("longitude").GetDouble());

        // В allowlist салона нет секретных полей: скрывать нечего.
        Assert.DoesNotContain("[redacted]", row.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_WhenSalonContactsChanged_WritesPhoneChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var salon = TrackUnchanged(
            context,
            Salon.Create(
                "Best Cuts",
                "Main Street 1",
                "Moscow",
                "Russia",
                "Europe/Moscow",
                55.7558,
                37.6173,
                phoneNumber: "+74950000000").Value,
            context.Salons);

        Assert.True(salon.UpdateContactInfo("+74959999999", null).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("salon.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");
        var phone = changes.GetProperty("phoneNumber");

        Assert.Equal("+74950000000", phone.GetProperty("from").GetString());
        Assert.Equal("+74959999999", phone.GetProperty("to").GetString());

        // Неизменённые поля в diff не попадают: payload — только реальные изменения.
        Assert.Single(changes.EnumerateObject());
        Assert.False(changes.TryGetProperty("address", out _));
        Assert.False(changes.TryGetProperty("isActive", out _));
    }

    [Fact]
    public void Capture_WhenSalonDeactivated_WritesIsActiveChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var salon = TrackUnchanged(
            context,
            Salon.Create(
                "Best Cuts",
                "Main Street 1",
                "Moscow",
                "Russia",
                "Europe/Moscow",
                55.7558,
                37.6173).Value,
            context.Salons);

        Assert.True(salon.Deactivate().IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("salon.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var isActive = payload.RootElement
            .GetProperty("changes")
            .GetProperty("isActive");

        Assert.True(isActive.GetProperty("from").GetBoolean());
        Assert.False(isActive.GetProperty("to").GetBoolean());
    }

    [Fact]
    public void Capture_WhenHairServiceUpdated_WritesNameAndDuration()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var hairService = TrackUnchanged(
            context,
            HairService.Create("Classic Cut", ServiceCategory.Haircut, 30, "Short").Value,
            context.HairServices);

        Assert.True(hairService.Update("Premium Cut", "Short", 45).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("hair_service.updated", row.Action);
        Assert.Equal("hair_service", row.EntityType);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Equal(
            "Classic Cut",
            changes.GetProperty("name").GetProperty("from").GetString());
        Assert.Equal(
            "Premium Cut",
            changes.GetProperty("name").GetProperty("to").GetString());
        Assert.Equal(
            30,
            changes.GetProperty("defaultDurationMinutes").GetProperty("from").GetInt32());
        Assert.Equal(
            45,
            changes.GetProperty("defaultDurationMinutes").GetProperty("to").GetInt32());

        // Update не меняет категорию: она задаётся только при создании и живёт в values.
        Assert.False(changes.TryGetProperty("category", out _));
        Assert.False(changes.TryGetProperty("description", out _));
    }

    [Fact]
    public void Capture_WhenSpecializationRenamed_WritesNameChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var specialization = TrackUnchanged(
            context,
            Specialization.Create("Fade", "Short fade").Value,
            context.Specializations);

        Assert.True(specialization.Rename("Skin Fade", "Short fade").IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("specialization.updated", row.Action);
        Assert.Equal("specialization", row.EntityType);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Equal("Fade", changes.GetProperty("name").GetProperty("from").GetString());
        Assert.Equal("Skin Fade", changes.GetProperty("name").GetProperty("to").GetString());

        // Описание не менялось — в diff его нет.
        Assert.Single(changes.EnumerateObject());
    }

    [Fact]
    public void Capture_WhenBookingCancelled_WritesStatusChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var booking = TrackUnchanged(context, CreateBooking(), context.Bookings);

        Assert.True(booking.Cancel().IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("booking.updated", row.Action);
        Assert.Equal("booking", row.EntityType);

        using var payload = JsonDocument.Parse(row.Payload);
        var status = payload.RootElement.GetProperty("changes").GetProperty("status");

        Assert.Equal("Pending", status.GetProperty("from").GetString());
        Assert.Equal("Cancelled", status.GetProperty("to").GetString());
    }

    [Fact]
    public void Capture_WhenBookingNoteAdded_WritesBarberNoteChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var booking = TrackUnchanged(context, CreateBooking(), context.Bookings);

        Assert.True(booking.AddBarberNote("Bring clippers").IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("booking.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var barberNote = payload.RootElement
            .GetProperty("changes")
            .GetProperty("barberNote");

        Assert.Equal(JsonValueKind.Null, barberNote.GetProperty("from").ValueKind);
        Assert.Equal("Bring clippers", barberNote.GetProperty("to").GetString());
    }

    [Fact]
    public void Capture_WhenBookingCreated_WritesNoRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();

        // Создание брони не аудируется — это обещание (флаг политики AuditsCreation),
        // а не побочный эффект: бронь уже хранит владельца, время и цену.
        context.Bookings.Add(CreateBooking());
        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_WhenBarberProfileChildChanged_WritesNoRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchanged(context, CreateBarberProfile(), context.BarberProfiles);

        // Дети BarberProfile (услуги, салоны, расписание, портфолио, отпуска) — не агрегаты:
        // их добавление помечает корень обновлённым, но allowlist-свойств не меняет.
        Assert.True(profile.AddService(Guid.CreateVersion7(), 40.00m, 30).IsSuccess);

        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_BookingPayload_DoesNotContainIdempotencyKey()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var idempotencyKey = $"idem-{Guid.NewGuid():N}";
        var booking = TrackUnchanged(context, CreateBooking(idempotencyKey), context.Bookings);

        Assert.True(booking.Confirm().IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("booking.updated", row.Action);

        // Ключ идемпотентности — технический ключ клиента, а не история изменения:
        // в payload его нет даже как «полезное» значение.
        Assert.DoesNotContain(idempotencyKey, row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("idempotencyKey", row.Payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntityTypeNames_MatchTheAdminFilterRegistry()
    {
        var audited = AuditTrail.EntityTypeNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var registry = Enum.GetValues<AdminAuditEntityType>()
            .Where(entityType => entityType != AdminAuditEntityType.All)
            .Select(AdminAuditEntityTypes.ToStorageName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Два реестра — allowlist AuditTrail и фильтр entityType — обязаны совпадать:
        // иначе журнал пишет тип, который фильтр не принимает (или наоборот).
        Assert.Equal(registry, audited);
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
        => Review.Create(
            CreateBooking($"review-key-{Guid.NewGuid()}"),
            Guid.CreateVersion7(),
            5,
            text).Value;

    /// <summary>
    /// Бронь в состоянии <c>Pending</c> без сохранения в БД: домен-хелпер для проверок
    /// изменения статуса и заметки, повторяющий подход <c>BookingTests</c>.
    /// </summary>
    private static Booking CreateBooking(
        string idempotencyKey = "booking-key",
        string? customerNote = null)
    {
        var startAt = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

        return Booking.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            startAt,
            startAt.AddMinutes(30),
            1500m,
            idempotencyKey,
            customerNote).Value;
    }

    /// <summary>
    /// Профиль барбера без детей: нужен, чтобы доказать, что добавление ребёнка
    /// (услуги, салона, расписания) строки аудита не пишет.
    /// </summary>
    private static BarberProfile CreateBarberProfile()
        => BarberProfile.Create(
            userId: Guid.CreateVersion7(),
            displayName: "Ivan",
            yearsOfExperience: 5,
            bio: null,
            languages: null,
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: null).Value;

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

        public bool TryGetBarberProfileId(out Guid barberProfileId)
        {
            barberProfileId = Guid.Empty;

            return false;
        }
    }
}
