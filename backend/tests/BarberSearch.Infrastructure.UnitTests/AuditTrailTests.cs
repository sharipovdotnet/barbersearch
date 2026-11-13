using System.Text.Json;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Audit.Admin;
using BarberSearch.Domain.BarberProfiles;
using BarberSearch.Domain.BarberServices;
using BarberSearch.Domain.Bookings;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.HairServices;
using BarberSearch.Domain.PortfolioPhotos;
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
/// сущности allowlist (<c>User</c>, <c>BarberProfile</c>, <c>PortfolioPhoto</c>,
/// <c>Review</c>, <c>Salon</c>, <c>HairService</c>, <c>Specialization</c>, <c>Booking</c>
/// и <c>BarberService</c>).
/// Строки аудита кладутся в тот же контекст, поэтому проверяются через
/// <c>context.AuditLogs.Local</c>: <c>SaveChanges</c> в тестах не вызывается, соединение
/// с базой не открывается.
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
    public void Capture_WhenBarberProfileDisplayNameChanged_WritesNoRowBecauseOwnedSnapshotIsLost()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchanged(context, CreateBarberProfile(), context.BarberProfiles);

        Assert.True(profile.UpdateProfile(
            displayName: "Petr",
            yearsOfExperience: 5,
            bio: null,
            languages: null,
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: null,
            additionalServices: null).IsSuccess);

        // Решение #96 (D4): DisplayName — owned-тип, а не свойство. При замене инстанса
        // в UpdateProfile EF удаляет старую owned-запись и создаёт новую в состоянии Added
        // с OriginalValue == CurrentValue: снимок старого имени теряется, diff from/to
        // построить нечем. Поэтому имя намеренно не входит в allowlist, и строка
        // не появляется. Тест фиксирует известное ограничение, а не тихий пропуск: факт
        // и причина записаны в §14 #30 и в отчёте среза. Если EF научится сохранять
        // снимок owned-типа, тест упадёт — это приглашение добавить поле, а не регресс.
        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_WhenBarberProfileDisplayNameAndBioChanged_WritesOnlyBioChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchanged(context, CreateBarberProfile(), context.BarberProfiles);

        Assert.True(profile.UpdateProfile(
            displayName: "Petr",
            yearsOfExperience: 5,
            bio: "Стригу 10 лет",
            languages: null,
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: null,
            additionalServices: null).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        // Диff не содержит displayName: owned-путь не пишется (D4), и имя CLR-типа
        // в payload тоже не попадает.
        Assert.Single(changes.EnumerateObject());
        Assert.True(changes.TryGetProperty("bio", out _));
        Assert.False(changes.TryGetProperty("displayName", out _));
        Assert.DoesNotContain("ValueObjects", row.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_WhenBarberProfileBioLongerThanLimit_TruncatesWithMarker()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchanged(context, CreateBarberProfile(), context.BarberProfiles);
        var bio = new string('a', AuditLogEntry.MAX_PAYLOAD_STRING_LENGTH + 100);

        Assert.True(profile.UpdateProfile(
            displayName: "Ivan",
            yearsOfExperience: 5,
            bio: bio,
            languages: null,
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: null,
            additionalServices: null).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        using var payload = JsonDocument.Parse(row.Payload);
        var change = payload.RootElement.GetProperty("changes").GetProperty("bio");
        var stored = change.GetProperty("to").GetString();

        Assert.Equal(JsonValueKind.Null, change.GetProperty("from").ValueKind);
        Assert.NotNull(stored);
        Assert.Equal(AuditLogEntry.MAX_PAYLOAD_STRING_LENGTH, stored.Length);
        Assert.EndsWith("...", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(bio, row.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_WhenBarberProfileBioAtDomainLimit_TruncatesWithSameLimit()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchanged(context, CreateBarberProfile(), context.BarberProfiles);

        // Домен разрешает 2000 символов, а payload обрезает всё длиннее 500: предел
        // один на весь журнал, полный текст остаётся в barber_profiles.bio.
        Assert.True(profile.UpdateProfile(
            displayName: "Ivan",
            yearsOfExperience: 5,
            bio: new string('b', 2000),
            languages: null,
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: null,
            additionalServices: null).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        using var payload = JsonDocument.Parse(row.Payload);
        var stored = payload.RootElement
            .GetProperty("changes")
            .GetProperty("bio")
            .GetProperty("to")
            .GetString();

        Assert.NotNull(stored);
        Assert.Equal(AuditLogEntry.MAX_PAYLOAD_STRING_LENGTH, stored.Length);
        Assert.EndsWith("...", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_WhenBarberProfileLatePolicyChanged_WritesChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchanged(context, CreateBarberProfile(), context.BarberProfiles);

        Assert.True(profile.UpdateProfile(
            displayName: "Ivan",
            yearsOfExperience: 5,
            bio: null,
            languages: null,
            certificatesAndTraining: null,
            achievements: null,
            paymentMethods: null,
            communicationFormat: null,
            goodies: null,
            latePolicy: "Жду 10 минут",
            additionalServices: null).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        using var payload = JsonDocument.Parse(row.Payload);
        var latePolicy = payload.RootElement
            .GetProperty("changes")
            .GetProperty("latePolicy");

        Assert.Equal(string.Empty, latePolicy.GetProperty("from").GetString());
        Assert.Equal("Жду 10 минут", latePolicy.GetProperty("to").GetString());
    }

    [Fact]
    public void Capture_WhenBarberProfileOnlyListsChanged_WritesNoRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchanged(context, CreateBarberProfile(), context.BarberProfiles);

        // Списки, достижения, специализации и стаж в allowlist не входят: diff массива
        // нечитаем, а payload раздувается. Имя, текст и правила остаются прежними.
        Assert.True(profile.UpdateProfile(
            displayName: "Ivan",
            yearsOfExperience: 9,
            bio: null,
            languages: [BarberSearch.Domain.BarberProfiles.Enums.Language.En],
            certificatesAndTraining: ["Certified"],
            achievements: ["Top-10"],
            paymentMethods: null,
            communicationFormat: ["Silent mode"],
            goodies: ["Espresso"],
            latePolicy: null,
            additionalServices: ["Камуфляж седины"]).IsSuccess);

        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
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
    public void Capture_WhenBookingPriceChanged_WritesPriceChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var booking = TrackUnchanged(context, CreateBooking(), context.Bookings);

        Assert.True(booking.ChangePrice(2000m).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("booking.updated", row.Action);
        Assert.Equal("booking", row.EntityType);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");
        var price = changes.GetProperty("price");

        // Цена — число, не строка: payload журнала обязан совпадать с типом колонки,
        // иначе админский журнал не отформатирует её в рублях.
        Assert.Equal(1500m, price.GetProperty("from").GetDecimal());
        Assert.Equal(2000m, price.GetProperty("to").GetDecimal());
        Assert.Equal(JsonValueKind.Number, price.GetProperty("from").ValueKind);
        Assert.Equal(JsonValueKind.Number, price.GetProperty("to").ValueKind);

        // Меняется только сумма: остальные allowlist-поля брони не попадают в diff.
        Assert.Single(changes.EnumerateObject());
    }

    [Fact]
    public void Capture_WhenBookingPriceSetToSameValue_WritesNoRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var booking = TrackUnchanged(context, CreateBooking(), context.Bookings);

        // Повтор с той же суммой — no-op: MarkUpdated не вызывается, и строка журнала
        // не должна появляться (D6). EF тоже не пометил бы Price изменённым.
        Assert.True(booking.ChangePrice(1500m).IsSuccess);

        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
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
    public void Capture_WhenBarberProfileSalonAttached_WritesNoRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchanged(context, CreateBarberProfile(), context.BarberProfiles);

        // Салон, расписание и отпуска — по-прежнему вне журнала: их добавление помечает
        // корень обновлённым, но allowlist-свойств не меняет. У прайса (#96) и портфолио
        // (#91) собственные типы, поэтому они строку пишут — см. тесты barber_service.
        Assert.True(profile.AddSalon(Guid.CreateVersion7()).IsSuccess);

        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_WhenBarberServiceAdded_WritesCreatedRowWithPrice()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchanged(context, CreateBarberProfile(), context.BarberProfiles);
        var hairServiceId = Guid.CreateVersion7();

        Assert.True(profile.AddService(hairServiceId, 3000m, 60).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("barber_service.created", row.Action);
        Assert.Equal("barber_service", row.EntityType);
        Assert.Equal(ActorId, row.ActorUserId);

        using var payload = JsonDocument.Parse(row.Payload);
        var values = payload.RootElement.GetProperty("values");

        Assert.Equal(hairServiceId.ToString(), values.GetProperty("hairServiceId").GetString());
        Assert.Equal(profile.Id.ToString(), values.GetProperty("barberProfileId").GetString());
        Assert.Equal(3000m, values.GetProperty("price").GetDecimal());
        Assert.Equal(60, values.GetProperty("durationMinutes").GetInt32());
        Assert.True(values.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public void Capture_WhenBarberServicePriceAndDurationUpdated_WritesTwoChanges()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var (profile, _) = TrackUnchangedProfileWithService(context);

        Assert.True(profile.UpdateService(
            Assert.Single(profile.Services).HairServiceId,
            2100m,
            60).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("barber_service.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        var price = changes.GetProperty("price");
        Assert.Equal(2000m, price.GetProperty("from").GetDecimal());
        Assert.Equal(2100m, price.GetProperty("to").GetDecimal());

        var duration = changes.GetProperty("durationMinutes");
        Assert.Equal(45, duration.GetProperty("from").GetInt32());
        Assert.Equal(60, duration.GetProperty("to").GetInt32());

        // Цена и длительность меняются одной операцией, но isActive не трогается.
        Assert.Equal(2, changes.EnumerateObject().Count());
    }

    [Fact]
    public void Capture_WhenBarberServiceDeactivated_WritesIsActiveChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var (profile, service) = TrackUnchangedProfileWithService(context);

        Assert.True(profile.DeactivateService(service.HairServiceId).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("barber_service.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var isActive = payload.RootElement
            .GetProperty("changes")
            .GetProperty("isActive");

        Assert.True(isActive.GetProperty("from").GetBoolean());
        Assert.False(isActive.GetProperty("to").GetBoolean());
    }

    [Fact]
    public void Capture_WhenBarberServiceActivated_WritesIsActiveChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = CreateBarberProfile();
        var hairServiceId = Guid.CreateVersion7();

        Assert.True(profile.AddService(hairServiceId, 2000m, 45).IsSuccess);
        Assert.True(profile.DeactivateService(hairServiceId).IsSuccess);

        profile = TrackUnchanged(context, profile, context.BarberProfiles);
        var service = profile.Services.Single();
        context.Entry(service).State = EntityState.Unchanged;

        Assert.True(service.Activate().IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("barber_service.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var isActive = payload.RootElement
            .GetProperty("changes")
            .GetProperty("isActive");

        Assert.False(isActive.GetProperty("from").GetBoolean());
        Assert.True(isActive.GetProperty("to").GetBoolean());
    }

    [Fact]
    public void Capture_WhenBarberServiceAddedOverHiddenRow_WritesUpdatedWithoutCreated()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = CreateBarberProfile();
        var hairServiceId = Guid.CreateVersion7();

        // Скрытая строка — та же услуга, которую возвращают в прайс: повторное добавление
        // реактивирует её и перезаписывает цену с длительностью, а не создаёт новую.
        Assert.True(profile.AddService(hairServiceId, 2000m, 45).IsSuccess);
        Assert.True(profile.DeactivateService(hairServiceId).IsSuccess);

        profile = TrackUnchanged(context, profile, context.BarberProfiles);
        context.Entry(profile.Services.Single()).State = EntityState.Unchanged;

        Assert.True(profile.AddService(hairServiceId, 2500m, 90).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("barber_service.updated", row.Action);
        Assert.DoesNotContain("barber_service.created", row.Payload, StringComparison.Ordinal);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Equal(3, changes.EnumerateObject().Count());
        Assert.True(changes.GetProperty("isActive").GetProperty("to").GetBoolean());
        Assert.Equal(2500m, changes.GetProperty("price").GetProperty("to").GetDecimal());
    }

    [Fact]
    public void Capture_WhenBarberProfileRatingRecalculated_WritesNoBarberServiceRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var (profile, _) = TrackUnchangedProfileWithService(context);

        // Пересчёт рейтинга пишет barber_profile.updated (rating и reviewsCount в allowlist),
        // но строки прайса не добавляет: услуга не менялась.
        Assert.True(profile.UpdateRating(4.5m, 10).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("barber_profile.updated", row.Action);
        Assert.DoesNotContain(context.AuditLogs.Local, entry => entry.EntityType == "barber_service");
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
    public void Capture_WhenUserAvatarBlocked_WritesOnlyTheFlagChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;

        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);

        user = TrackUnchanged(context, user, context.Users);

        Assert.True(user.BlockAvatar().IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("user.updated", row.Action);
        Assert.Equal("user", row.EntityType);
        Assert.Equal(user.Id, row.EntityId);
        Assert.Equal(ActorId, row.ActorUserId);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        // Только флаг: URL аватара не менялся, поэтому в diff его нет, хотя AvatarUrl
        // в allowlist входит — блокировка не трогает файл.
        Assert.Single(changes.EnumerateObject());

        var blocked = changes.GetProperty("isAvatarBlocked");

        Assert.False(blocked.GetProperty("from").GetBoolean());
        Assert.True(blocked.GetProperty("to").GetBoolean());
        Assert.False(changes.TryGetProperty("avatarUrl", out _));
    }

    [Fact]
    public void Capture_WhenUserAvatarUnblocked_WritesTrueToFalse()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;

        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);
        Assert.True(user.BlockAvatar().IsSuccess);

        user = TrackUnchanged(context, user, context.Users);

        Assert.True(user.UnblockAvatar().IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("user.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var blocked = payload.RootElement
            .GetProperty("changes")
            .GetProperty("isAvatarBlocked");

        Assert.True(blocked.GetProperty("from").GetBoolean());
        Assert.False(blocked.GetProperty("to").GetBoolean());
    }

    [Fact]
    public void Capture_WhenAvatarBlockedAgain_WritesNoSecondRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;

        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);
        Assert.True(user.BlockAvatar().IsSuccess);

        // Снимок уже содержит флаг true: повторная блокировка не меняет UpdatedAt,
        // поэтому change tracker не видит Modified и строка не пишется.
        user = TrackUnchanged(context, user, context.Users);

        Assert.True(user.BlockAvatar().IsSuccess);

        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_WhenAvatarUploaded_WritesAvatarUrlAndStatus()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;

        user = TrackUnchanged(context, user, context.Users);

        // Загрузка — действие с контентом: строка называет файл и переход
        // Approved → Pending, поэтому без URL журнал не ответил бы «какой кадр».
        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("user.updated", row.Action);
        Assert.Equal("user", row.EntityType);
        Assert.Equal(user.Id, row.EntityId);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Equal(2, changes.EnumerateObject().Count());

        var avatarUrl = changes.GetProperty("avatarUrl");
        Assert.Equal(JsonValueKind.Null, avatarUrl.GetProperty("from").ValueKind);
        Assert.Equal(
            "https://cdn.example.com/avatar.png",
            avatarUrl.GetProperty("to").GetString());

        var moderationStatus = changes.GetProperty("avatarModerationStatus");
        Assert.Equal("Approved", moderationStatus.GetProperty("from").GetString());
        Assert.Equal("Pending", moderationStatus.GetProperty("to").GetString());
    }

    [Fact]
    public void Capture_WhenAvatarReplaced_WritesNewUrlAndReturnsToPending()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;

        Assert.True(user.SetAvatar("https://cdn.example.com/first.png").IsSuccess);
        Assert.True(user.ApproveAvatar(ActorId, Now).IsSuccess);

        user = TrackUnchanged(context, user, context.Users);

        // Замена — новый файл: прошлое решение не переезжает на него.
        Assert.True(user.SetAvatar("https://cdn.example.com/second.png").IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("user.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Equal(2, changes.EnumerateObject().Count());

        var avatarUrl = changes.GetProperty("avatarUrl");
        Assert.Equal(
            "https://cdn.example.com/first.png",
            avatarUrl.GetProperty("from").GetString());
        Assert.Equal(
            "https://cdn.example.com/second.png",
            avatarUrl.GetProperty("to").GetString());

        var moderationStatus = changes.GetProperty("avatarModerationStatus");
        Assert.Equal("Approved", moderationStatus.GetProperty("from").GetString());
        Assert.Equal("Pending", moderationStatus.GetProperty("to").GetString());
    }

    [Fact]
    public void Capture_WhenAvatarDeleted_WritesNullUrlAndApprovedStatus()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;

        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);

        user = TrackUnchanged(context, user, context.Users);

        // Удаление: файла больше нет, проверять нечего — статус возвращается в Approved.
        Assert.True(user.SetAvatar(null).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("user.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        var avatarUrl = changes.GetProperty("avatarUrl");
        Assert.Equal(
            "https://cdn.example.com/avatar.png",
            avatarUrl.GetProperty("from").GetString());
        Assert.Equal(JsonValueKind.Null, avatarUrl.GetProperty("to").ValueKind);

        var moderationStatus = changes.GetProperty("avatarModerationStatus");
        Assert.Equal("Pending", moderationStatus.GetProperty("from").GetString());
        Assert.Equal("Approved", moderationStatus.GetProperty("to").GetString());

        Assert.False(changes.TryGetProperty("avatarSubmittedAt", out _));
        Assert.False(changes.TryGetProperty("avatarReviewedAt", out _));
        Assert.False(changes.TryGetProperty("avatarReviewedByUserId", out _));
    }

    [Fact]
    public void Capture_WhenSameAvatarUrlSetAgain_WritesNoRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;

        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);

        user = TrackUnchanged(context, user, context.Users);

        // Ранний возврат домена: та же строка — не изменение и не событие журнала.
        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);

        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_WhenAvatarClearedWithoutAvatar_WritesNoRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;

        user = TrackUnchanged(context, user, context.Users);

        Assert.True(user.SetAvatar(null).IsSuccess);

        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_WhenAvatarApproved_WritesOnlyModerationStatus()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;

        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);

        user = TrackUnchanged(context, user, context.Users);

        Assert.True(user.ApproveAvatar(ActorId, Now).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("user.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var moderationStatus = changes.GetProperty("avatarModerationStatus");
        Assert.Equal("Pending", moderationStatus.GetProperty("from").GetString());
        Assert.Equal("Approved", moderationStatus.GetProperty("to").GetString());

        // Reviewed-поля дублируют occurred_at/actor_user_id, а URL не менялся.
        Assert.False(changes.TryGetProperty("avatarUrl", out _));
        Assert.False(changes.TryGetProperty("avatarReviewedAt", out _));
        Assert.False(changes.TryGetProperty("avatarReviewedByUserId", out _));
        Assert.False(changes.TryGetProperty("avatarRejectionReason", out _));
    }

    [Fact]
    public void Capture_WhenAvatarRejected_WritesOnlyModerationStatusWithoutReason()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;
        const string reason = "Too blurry to publish";

        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);

        user = TrackUnchanged(context, user, context.Users);

        Assert.True(user.RejectAvatar(ActorId, reason, Now).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("user.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var moderationStatus = changes.GetProperty("avatarModerationStatus");
        Assert.Equal("Pending", moderationStatus.GetProperty("from").GetString());
        Assert.Equal("Rejected", moderationStatus.GetProperty("to").GetString());

        // Причина — свободный текст администратора: она живёт на кадре, а не в payload.
        Assert.DoesNotContain(reason, row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("avatarRejectionReason", row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("avatarUrl", row.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_WhenAvatarModeratedTwice_WritesNoSecondRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var user = User.Create("ivan@example.com", PasswordHash, "Ivan", "Petrov").Value;

        Assert.True(user.SetAvatar("https://cdn.example.com/avatar.png").IsSuccess);
        Assert.True(user.ApproveAvatar(ActorId, Now).IsSuccess);

        user = TrackUnchanged(context, user, context.Users);

        // Повторное решение — 409 и никаких изменений: писать в журнал нечего.
        Assert.True(user.ApproveAvatar(ActorId, Now).IsFailure);

        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoUploaded_WritesCreatedRowWithValues()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = CreateBarberProfile();
        var url = PortfolioMediaUrl.Build(profile.Id, Guid.CreateVersion7(), "png");

        Assert.True(profile.AddUploadedPhoto(url, "Fresh cut").IsSuccess);

        var photo = Assert.Single(profile.PortfolioPhotos);

        // Только фотография: профиль уже существует и в этом сценарии не менялся.
        context.PortfolioPhotos.Add(photo);
        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("portfolio_photo.created", row.Action);
        Assert.Equal("portfolio_photo", row.EntityType);
        Assert.Equal(photo.Id, row.EntityId);

        using var payload = JsonDocument.Parse(row.Payload);
        var values = payload.RootElement.GetProperty("values");

        Assert.Equal(profile.Id.ToString(), values.GetProperty("barberProfileId").GetString());
        Assert.Equal(url, values.GetProperty("url").GetString());
        Assert.Equal("Fresh cut", values.GetProperty("description").GetString());
        Assert.Equal(0, values.GetProperty("sortOrder").GetInt32());
        Assert.True(values.GetProperty("isPublished").GetBoolean());
        Assert.False(values.GetProperty("isBlocked").GetBoolean());
        Assert.Equal("Pending", values.GetProperty("moderationStatus").GetString());

        // Миниатюра — производная файла; причина и reviewed-поля не аудируются.
        Assert.False(values.TryGetProperty("thumbnailUrl", out _));
        Assert.False(values.TryGetProperty("rejectionReason", out _));
        Assert.False(values.TryGetProperty("reviewedAt", out _));
        Assert.False(values.TryGetProperty("reviewedByUserId", out _));
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoDescriptionUpdated_WritesOnlyDescription()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchangedProfileWithPhotos(context);
        var photo = Assert.Single(profile.PortfolioPhotos);

        Assert.True(photo.UpdateDescription("New caption").IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("portfolio_photo.updated", row.Action);
        Assert.Equal("portfolio_photo", row.EntityType);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var description = changes.GetProperty("description");
        Assert.Equal("Photo 0", description.GetProperty("from").GetString());
        Assert.Equal("New caption", description.GetProperty("to").GetString());
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoDescriptionUnchanged_WritesNoRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchangedProfileWithPhotos(context);
        var photo = Assert.Single(profile.PortfolioPhotos);

        // Та же подпись — не изменение: UpdatedAt в allowlist не входит.
        Assert.True(photo.UpdateDescription("Photo 0").IsSuccess);

        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoReordered_WritesSortOrder()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchangedProfileWithPhotos(context);
        var photo = Assert.Single(profile.PortfolioPhotos);

        Assert.True(profile.ReorderPhoto(photo.Id, 7).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("portfolio_photo.updated", row.Action);
        Assert.Equal(photo.Id, row.EntityId);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var sortOrder = changes.GetProperty("sortOrder");
        Assert.Equal(0, sortOrder.GetProperty("from").GetInt32());
        Assert.Equal(7, sortOrder.GetProperty("to").GetInt32());
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoReorderKeepsSameValue_WritesNoRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchangedProfileWithPhotos(context);
        var photo = Assert.Single(profile.PortfolioPhotos);

        Assert.True(profile.ReorderPhoto(photo.Id, photo.SortOrder).IsSuccess);

        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_WhenPortfolioPhotosReordered_WritesSortOrderForEachMovedPhoto()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchangedProfileWithPhotos(context, photoCount: 2);
        var photos = profile.PortfolioPhotos.ToList();

        // Обратный порядок реально сдвигает оба кадра: 0 → 1 и 1 → 0.
        Assert.True(profile.ReorderPhotos([photos[1].Id, photos[0].Id]).IsSuccess);

        audit.Capture(context);

        Assert.Equal(2, context.AuditLogs.Local.Count);

        foreach (var row in context.AuditLogs.Local)
        {
            Assert.Equal("portfolio_photo.updated", row.Action);

            using var payload = JsonDocument.Parse(row.Payload);
            var changes = payload.RootElement.GetProperty("changes");

            Assert.Single(changes.EnumerateObject());
            Assert.True(changes.TryGetProperty("sortOrder", out _));
        }
    }

    [Fact]
    public void Capture_WhenPortfolioPhotosReorderedToSameOrder_WritesNoRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchangedProfileWithPhotos(context, photoCount: 2);
        var photos = profile.PortfolioPhotos.ToList();

        Assert.True(profile.ReorderPhotos([photos[0].Id, photos[1].Id]).IsSuccess);

        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoUnpublished_WritesIsPublishedChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchangedProfileWithPhotos(context);
        var photo = Assert.Single(profile.PortfolioPhotos);

        Assert.True(photo.Unpublish().IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("portfolio_photo.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var isPublished = changes.GetProperty("isPublished");
        Assert.True(isPublished.GetProperty("from").GetBoolean());
        Assert.False(isPublished.GetProperty("to").GetBoolean());
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoPublished_WritesIsPublishedChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = CreateBarberProfile();
        var url = PortfolioMediaUrl.Build(profile.Id, Guid.CreateVersion7(), "png");

        Assert.True(profile.AddUploadedPhoto(url, "Photo 0").IsSuccess);

        var photo = Assert.Single(profile.PortfolioPhotos);

        // Снимок — уже снятый с публикации кадр; возврат даёт false → true.
        Assert.True(photo.Unpublish().IsSuccess);

        context.BarberProfiles.Add(profile);
        context.Entry(profile).State = EntityState.Unchanged;
        context.Entry(photo).State = EntityState.Unchanged;

        Assert.True(photo.Publish().IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        using var payload = JsonDocument.Parse(row.Payload);
        var isPublished = payload.RootElement
            .GetProperty("changes")
            .GetProperty("isPublished");

        Assert.False(isPublished.GetProperty("from").GetBoolean());
        Assert.True(isPublished.GetProperty("to").GetBoolean());
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoBlocked_WritesIsBlockedChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchangedProfileWithPhotos(context);
        var photo = Assert.Single(profile.PortfolioPhotos);

        Assert.True(photo.Block().IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("portfolio_photo.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var isBlocked = changes.GetProperty("isBlocked");
        Assert.False(isBlocked.GetProperty("from").GetBoolean());
        Assert.True(isBlocked.GetProperty("to").GetBoolean());
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoUnblocked_WritesIsBlockedChange()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = CreateBarberProfile();
        var url = PortfolioMediaUrl.Build(profile.Id, Guid.CreateVersion7(), "png");

        Assert.True(profile.AddUploadedPhoto(url, "Photo 0").IsSuccess);

        var photo = Assert.Single(profile.PortfolioPhotos);

        Assert.True(photo.Block().IsSuccess);

        context.BarberProfiles.Add(profile);
        context.Entry(profile).State = EntityState.Unchanged;
        context.Entry(photo).State = EntityState.Unchanged;

        Assert.True(photo.Unblock().IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        using var payload = JsonDocument.Parse(row.Payload);
        var isBlocked = payload.RootElement
            .GetProperty("changes")
            .GetProperty("isBlocked");

        Assert.True(isBlocked.GetProperty("from").GetBoolean());
        Assert.False(isBlocked.GetProperty("to").GetBoolean());
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoBlockedAgain_WritesNoRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = CreateBarberProfile();
        var url = PortfolioMediaUrl.Build(profile.Id, Guid.CreateVersion7(), "png");

        Assert.True(profile.AddUploadedPhoto(url, "Photo 0").IsSuccess);

        var photo = Assert.Single(profile.PortfolioPhotos);

        Assert.True(photo.Block().IsSuccess);

        context.BarberProfiles.Add(profile);
        context.Entry(profile).State = EntityState.Unchanged;
        context.Entry(photo).State = EntityState.Unchanged;

        // Идемпотентная повторная блокировка: состояние не меняется, строки нет.
        Assert.True(photo.Block().IsSuccess);

        audit.Capture(context);

        Assert.Empty(context.AuditLogs.Local);
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoApproved_WritesOnlyModerationStatus()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchangedProfileWithPhotos(context);
        var photo = Assert.Single(profile.PortfolioPhotos);

        Assert.True(photo.Approve(ActorId, Now).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("portfolio_photo.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var moderationStatus = changes.GetProperty("moderationStatus");
        Assert.Equal("Pending", moderationStatus.GetProperty("from").GetString());
        Assert.Equal("Approved", moderationStatus.GetProperty("to").GetString());

        Assert.False(changes.TryGetProperty("reviewedAt", out _));
        Assert.False(changes.TryGetProperty("reviewedByUserId", out _));
        Assert.False(changes.TryGetProperty("rejectionReason", out _));
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoRejected_WritesOnlyModerationStatusWithoutReason()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchangedProfileWithPhotos(context);
        var photo = Assert.Single(profile.PortfolioPhotos);
        const string reason = "Out of focus";

        Assert.True(photo.Reject(ActorId, reason, Now).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("portfolio_photo.updated", row.Action);

        using var payload = JsonDocument.Parse(row.Payload);
        var changes = payload.RootElement.GetProperty("changes");

        Assert.Single(changes.EnumerateObject());

        var moderationStatus = changes.GetProperty("moderationStatus");
        Assert.Equal("Pending", moderationStatus.GetProperty("from").GetString());
        Assert.Equal("Rejected", moderationStatus.GetProperty("to").GetString());

        // Причина уже живёт на кадре и показывается владельцу; в журнал не дублируется.
        Assert.DoesNotContain(reason, row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("rejectionReason", row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("reviewedAt", row.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("reviewedByUserId", row.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoReviewedTwice_WritesNoSecondRow()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchangedProfileWithPhotos(context);
        var photo = Assert.Single(profile.PortfolioPhotos);

        Assert.True(photo.Approve(ActorId, Now).IsSuccess);

        // После сохранения повторное решение — 409: домен не меняет ни одного поля.
        Assert.True(photo.Approve(ActorId, Now).IsFailure);

        audit.Capture(context);

        // Обе попытки в одном контексте: строка одна — от первого решения.
        Assert.Single(context.AuditLogs.Local);

        using var payload = JsonDocument.Parse(Assert.Single(context.AuditLogs.Local).Payload);
        Assert.Single(payload.RootElement.GetProperty("changes").EnumerateObject());
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoRemoved_WritesDeletedRowWithUrl()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = TrackUnchangedProfileWithPhotos(context);
        var photo = Assert.Single(profile.PortfolioPhotos);
        var url = photo.Url;

        Assert.True(profile.RemovePhoto(photo.Id).IsSuccess);

        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        Assert.Equal("portfolio_photo.deleted", row.Action);
        Assert.Equal("portfolio_photo", row.EntityType);
        Assert.Equal(photo.Id, row.EntityId);

        using var payload = JsonDocument.Parse(row.Payload);
        var values = payload.RootElement.GetProperty("values");

        // URL переживает удаление кадра — иначе строку не связать с файлом.
        Assert.Equal(url, values.GetProperty("url").GetString());
        Assert.Equal(profile.Id.ToString(), values.GetProperty("barberProfileId").GetString());
        Assert.Equal("Pending", values.GetProperty("moderationStatus").GetString());
    }

    [Fact]
    public void Capture_WhenPortfolioPhotoDescriptionLongerThanLimit_TruncatesWithMarker()
    {
        using var context = CreateContext();
        var audit = CreateAudit();
        var profile = CreateBarberProfile();
        var url = PortfolioMediaUrl.Build(profile.Id, Guid.CreateVersion7(), "png");
        var description = new string('a', AuditLogEntry.MAX_PAYLOAD_STRING_LENGTH + 100);

        Assert.True(profile.AddUploadedPhoto(url, description).IsSuccess);

        context.PortfolioPhotos.Add(Assert.Single(profile.PortfolioPhotos));
        audit.Capture(context);

        var row = Assert.Single(context.AuditLogs.Local);

        using var payload = JsonDocument.Parse(row.Payload);
        var stored = payload.RootElement
            .GetProperty("values")
            .GetProperty("description")
            .GetString();

        Assert.NotNull(stored);
        Assert.Equal(AuditLogEntry.MAX_PAYLOAD_STRING_LENGTH, stored.Length);
        Assert.EndsWith("...", stored, StringComparison.Ordinal);
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

    /// <summary>
    /// Профиль с загруженными кадрами, присоединённый как <c>Unchanged</c>: снимок
    /// фиксирует текущие значения, поэтому последующий доменный метод даёт настоящий
    /// <c>Modified</c> ребёнка, а не отсоединённый граф.
    /// </summary>
    private static BarberProfile TrackUnchangedProfileWithPhotos(
        ApplicationDbContext context,
        int photoCount = 1)
    {
        var profile = CreateBarberProfile();

        for (var index = 0; index < photoCount; index++)
        {
            var url = PortfolioMediaUrl.Build(profile.Id, Guid.CreateVersion7(), "png");
            Assert.True(profile.AddUploadedPhoto(url, $"Photo {index}").IsSuccess);
        }

        context.BarberProfiles.Add(profile);
        context.Entry(profile).State = EntityState.Unchanged;

        foreach (var photo in profile.PortfolioPhotos)
        {
            context.Entry(photo).State = EntityState.Unchanged;
        }

        return profile;
    }

    /// <summary>
    /// Профиль с одной активной услугой, присоединённый как <c>Unchanged</c> вместе
    /// с ребёнком: снимок фиксирует исходные цену и длительность, поэтому последующая
    /// правка даёт настоящий <c>Modified</c> услуги.
    /// </summary>
    private static (BarberProfile Profile, BarberService Service) TrackUnchangedProfileWithService(
        ApplicationDbContext context,
        decimal price = 2000m,
        int durationMinutes = 45)
    {
        var profile = CreateBarberProfile();

        Assert.True(profile.AddService(Guid.CreateVersion7(), price, durationMinutes).IsSuccess);

        context.BarberProfiles.Add(profile);
        context.Entry(profile).State = EntityState.Unchanged;

        var service = Assert.Single(profile.Services);
        context.Entry(service).State = EntityState.Unchanged;

        return (profile, service);
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
