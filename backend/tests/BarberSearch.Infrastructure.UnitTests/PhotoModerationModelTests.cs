using System.Globalization;
using BarberSearch.Domain;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Маппинг премодерации фото без подключения к базе: числовые enum-колонки <c>smallint</c>
/// с дефолтом <c>Approved</c>, check-ограничения статуса и причины, FK на автора решения
/// с <c>ON DELETE SET NULL</c> и индексы очередей.
/// </summary>
/// <remarks>
/// Стиль повторяет <c>EmailChangeTokenModelTests</c>: модель строится на неоткрытом
/// контексте, проверяется design-time модель. Применение миграции и поведение ограничений
/// на реальной PostgreSQL проверяют интеграционные тесты.
/// </remarks>
public class PhotoModerationModelTests
{
    [Fact]
    public void PortfolioPhoto_ModerationColumns_AreMappedAndNullable()
    {
        using var context = CreateContext();

        var entity = RequireEntity(context, typeof(PortfolioPhoto));

        var status = RequireProperty(entity, nameof(PortfolioPhoto.ModerationStatus));

        Assert.Equal("moderation_status", status.GetColumnName());
        Assert.Equal(typeof(PhotoModerationStatus), status.ClrType);
        Assert.False(status.IsNullable);
        Assert.Equal("smallint", status.GetColumnType());
        Assert.Equal(2, Convert.ToInt32(status.GetDefaultValue(), CultureInfo.InvariantCulture));

        var reason = RequireProperty(entity, nameof(PortfolioPhoto.RejectionReason));

        Assert.Equal("rejection_reason", reason.GetColumnName());
        Assert.True(reason.IsNullable);
        Assert.Equal(Constants.MAX_PHOTO_REJECTION_REASON_LENGTH, reason.GetMaxLength());

        Assert.Equal(
            "reviewed_at",
            RequireProperty(entity, nameof(PortfolioPhoto.ReviewedAt)).GetColumnName());
        Assert.Equal(
            "reviewed_by_user_id",
            RequireProperty(entity, nameof(PortfolioPhoto.ReviewedByUserId)).GetColumnName());
    }

    [Fact]
    public void PortfolioPhoto_StatusAndReasonChecks_ArePresent()
    {
        using var context = CreateContext();

        var entity = RequireEntity(context, typeof(PortfolioPhoto));

        Assert.Equal(
            "moderation_status IN (1, 2, 3)",
            RequireCheck(entity, "ck_portfolio_photos_moderation_status").Sql);
        Assert.Equal(
            "rejection_reason IS NULL OR char_length(rejection_reason) BETWEEN 1 AND 500",
            RequireCheck(entity, "ck_portfolio_photos_rejection_reason").Sql);
    }

    [Fact]
    public void PortfolioPhoto_ReviewerForeignKey_SetsNullOnDelete()
    {
        using var context = CreateContext();

        var entity = RequireEntity(context, typeof(PortfolioPhoto));

        var foreignKey = Assert.Single(
            entity.GetForeignKeys(),
            candidate => candidate.GetConstraintName()
                == "fk_portfolio_photos_users_reviewed_by_user_id");

        Assert.Equal(typeof(User), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.SetNull, foreignKey.DeleteBehavior);
        Assert.Equal(
            "reviewed_by_user_id",
            Assert.Single(foreignKey.Properties).GetColumnName());
    }

    [Fact]
    public void PortfolioPhoto_ModerationIndexes_ArePresent()
    {
        using var context = CreateContext();

        var entity = RequireEntity(context, typeof(PortfolioPhoto));

        var queue = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName()
                == "ix_portfolio_photos_moderation_status_created_at");

        Assert.False(queue.IsUnique);
        Assert.Null(queue.GetFilter());
        Assert.Collection(
            queue.Properties,
            property => Assert.Equal("moderation_status", property.GetColumnName()),
            property => Assert.Equal("created_at", property.GetColumnName()),
            property => Assert.Equal("id", property.GetColumnName()));

        var reviewer = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName()
                == "ix_portfolio_photos_reviewed_by_user_id");

        Assert.False(reviewer.IsUnique);
        Assert.Equal("reviewed_by_user_id", Assert.Single(reviewer.Properties).GetColumnName());
    }

    [Fact]
    public void User_AvatarModerationColumns_AreMappedAndNullable()
    {
        using var context = CreateContext();

        var entity = RequireEntity(context, typeof(User));

        var status = RequireProperty(entity, nameof(User.AvatarModerationStatus));

        Assert.Equal("avatar_moderation_status", status.GetColumnName());
        Assert.Equal(typeof(PhotoModerationStatus), status.ClrType);
        Assert.False(status.IsNullable);
        Assert.Equal("smallint", status.GetColumnType());
        Assert.Equal(2, Convert.ToInt32(status.GetDefaultValue(), CultureInfo.InvariantCulture));

        var reason = RequireProperty(entity, nameof(User.AvatarRejectionReason));

        Assert.Equal("avatar_rejection_reason", reason.GetColumnName());
        Assert.True(reason.IsNullable);
        Assert.Equal(Constants.MAX_PHOTO_REJECTION_REASON_LENGTH, reason.GetMaxLength());

        Assert.True(RequireProperty(entity, nameof(User.AvatarSubmittedAt)).IsNullable);
        Assert.True(RequireProperty(entity, nameof(User.AvatarReviewedAt)).IsNullable);
        Assert.True(RequireProperty(entity, nameof(User.AvatarReviewedByUserId)).IsNullable);
    }

    [Fact]
    public void User_AvatarStatusAndReasonChecks_ArePresent()
    {
        using var context = CreateContext();

        var entity = RequireEntity(context, typeof(User));

        Assert.Equal(
            "avatar_moderation_status IN (1, 2, 3)",
            RequireCheck(entity, "ck_users_avatar_moderation_status").Sql);
        Assert.Equal(
            "avatar_rejection_reason IS NULL OR char_length(avatar_rejection_reason) BETWEEN 1 AND 500",
            RequireCheck(entity, "ck_users_avatar_rejection_reason").Sql);
    }

    [Fact]
    public void User_AvatarReviewerForeignKey_SetsNullOnDelete()
    {
        using var context = CreateContext();

        var entity = RequireEntity(context, typeof(User));

        var foreignKey = Assert.Single(
            entity.GetForeignKeys(),
            candidate => candidate.GetConstraintName()
                == "fk_users_users_avatar_reviewed_by_user_id");

        Assert.Equal(typeof(User), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.SetNull, foreignKey.DeleteBehavior);
        Assert.Equal(
            "avatar_reviewed_by_user_id",
            Assert.Single(foreignKey.Properties).GetColumnName());
    }

    [Fact]
    public void User_AvatarModerationIndexes_ArePresent()
    {
        using var context = CreateContext();

        var entity = RequireEntity(context, typeof(User));

        var queue = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName()
                == "ix_users_avatar_moderation_status_submitted_at");

        Assert.False(queue.IsUnique);
        Assert.Equal("avatar_url IS NOT NULL", queue.GetFilter());
        Assert.Collection(
            queue.Properties,
            property => Assert.Equal("avatar_moderation_status", property.GetColumnName()),
            property => Assert.Equal("avatar_submitted_at", property.GetColumnName()),
            property => Assert.Equal("id", property.GetColumnName()));

        var reviewer = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName()
                == "ix_users_avatar_reviewed_by_user_id");

        Assert.False(reviewer.IsUnique);
        Assert.Equal("avatar_reviewed_by_user_id", Assert.Single(reviewer.Properties).GetColumnName());
    }

    [Fact]
    public void Migration_AddPhotoModerationExistsAndSnapshotMatchesModel()
    {
        using var context = CreateContext();

        Assert.Contains(
            "20261111185526_AddPhotoModeration",
            context.Database.GetMigrations());

        // Снимок модели обновлён вместе с миграцией: следующая миграция не должна
        // неожиданно «находить» незакоммиченные изменения.
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static IEntityType RequireEntity(ApplicationDbContext context, Type clrType)
    {
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(clrType);

        Assert.NotNull(entity);

        return entity;
    }

    private static IProperty RequireProperty(IEntityType entity, string name)
    {
        var property = entity.FindProperty(name);

        Assert.NotNull(property);

        return property;
    }

    private static ICheckConstraint RequireCheck(IEntityType entity, string name)
        => Assert.Single(
            entity.GetCheckConstraints(),
            candidate => candidate.Name == name);

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=59999;Database=unused;Username=test;Password=test")
            .Options);
}
