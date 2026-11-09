using BarberSearch.Domain.EmailOutbox;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Маппинг таблицы <c>email_outbox</c>: имена колонок, уникальный ключ дедупликации,
/// индекс диспетчера и check-ограничения — без подключения к базе.
/// </summary>
/// <remarks>
/// Модель строится на неоткрытом контексте (design-time), как в
/// <c>EmailVerificationTokenModelTests</c>. Применение миграции и блокировки строк на
/// реальной PostgreSQL проверяют интеграционные тесты.
/// </remarks>
public class EmailOutboxModelTests
{
    [Fact]
    public void Entity_IsMappedToEmailOutboxTable()
    {
        using var context = CreateContext();

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EmailOutboxMessage));

        Assert.NotNull(entity);
        Assert.Equal("email_outbox", entity.GetTableName());
        Assert.Equal("pk_email_outbox", entity.GetKeys().Single().GetName());
    }

    [Fact]
    public void DeduplicationKeyIndex_IsUnique()
    {
        using var context = CreateContext();

        var entity = RequireEntity(context);

        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ux_email_outbox_deduplication_key");

        Assert.True(index.IsUnique);
        Assert.Null(index.GetFilter());
        Assert.Equal("DeduplicationKey", Assert.Single(index.Properties).Name);
    }

    [Fact]
    public void DispatchIndex_CoversTheQueuePredicate()
    {
        using var context = CreateContext();

        var entity = RequireEntity(context);

        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ix_email_outbox_dispatch");

        Assert.False(index.IsUnique);
        Assert.Collection(
            index.Properties,
            property => Assert.Equal("sent_at", property.GetColumnName()),
            property => Assert.Equal("failed_at", property.GetColumnName()),
            property => Assert.Equal("next_attempt_at", property.GetColumnName()));
    }

    [Fact]
    public void CheckConstraints_GuardAttemptsAndState()
    {
        using var context = CreateContext();

        var entity = RequireEntity(context);

        var attempts = Assert.Single(
            entity.GetCheckConstraints(),
            candidate => candidate.Name == "ck_email_outbox_attempts");
        Assert.Equal("attempts >= 0", attempts.Sql);

        var state = Assert.Single(
            entity.GetCheckConstraints(),
            candidate => candidate.Name == "ck_email_outbox_state");
        Assert.Equal("NOT (sent_at IS NOT NULL AND failed_at IS NOT NULL)", state.Sql);
    }

    [Fact]
    public void ColumnsAndMaxLengths_AreMappedExplicitly()
    {
        using var context = CreateContext();

        var entity = RequireEntity(context);

        Assert.Equal(
            "deduplication_key",
            entity.FindProperty(nameof(EmailOutboxMessage.DeduplicationKey))!.GetColumnName());
        Assert.Equal(
            EmailOutboxMessage.MAX_DEDUPLICATION_KEY_LENGTH,
            entity.FindProperty(nameof(EmailOutboxMessage.DeduplicationKey))!.GetMaxLength());

        Assert.Equal(
            EmailOutboxMessage.MAX_EMAIL_LENGTH,
            entity.FindProperty(nameof(EmailOutboxMessage.ToEmail))!.GetMaxLength());

        Assert.Equal(
            EmailOutboxMessage.MAX_SUBJECT_LENGTH,
            entity.FindProperty(nameof(EmailOutboxMessage.Subject))!.GetMaxLength());

        Assert.Equal(
            EmailOutboxMessage.MAX_LAST_ERROR_LENGTH,
            entity.FindProperty(nameof(EmailOutboxMessage.LastError))!.GetMaxLength());

        Assert.Equal("html_body", entity.FindProperty(nameof(EmailOutboxMessage.HtmlBody))!.GetColumnName());
        Assert.Equal("text_body", entity.FindProperty(nameof(EmailOutboxMessage.TextBody))!.GetColumnName());
        Assert.Equal("next_attempt_at", entity.FindProperty(nameof(EmailOutboxMessage.NextAttemptAt))!.GetColumnName());
        Assert.Equal("sent_at", entity.FindProperty(nameof(EmailOutboxMessage.SentAt))!.GetColumnName());
        Assert.Equal("failed_at", entity.FindProperty(nameof(EmailOutboxMessage.FailedAt))!.GetColumnName());
        Assert.Equal("created_at", entity.FindProperty(nameof(EmailOutboxMessage.CreatedAt))!.GetColumnName());
        Assert.Equal("updated_at", entity.FindProperty(nameof(EmailOutboxMessage.UpdatedAt))!.GetColumnName());
    }

    [Fact]
    public void TableHasNoForeignKeys()
    {
        using var context = CreateContext();

        var entity = RequireEntity(context);

        // Outbox — инфраструктурная таблица: письмо переживает удаление пользователя.
        Assert.Empty(entity.GetForeignKeys());
    }

    private static IEntityType RequireEntity(ApplicationDbContext context)
    {
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EmailOutboxMessage));

        Assert.NotNull(entity);

        return entity;
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=59999;Database=unused;Username=test;Password=test")
            .Options);
}
