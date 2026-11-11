using BarberSearch.Domain.EmailChanges;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Маппинг таблицы <c>email_change_tokens</c>: имена колонок, check-ограничения нового адреса
/// и хеша, частичный уникальный индекс активных строк и каскад по пользователю — без
/// подключения к базе.
/// </summary>
/// <remarks>
/// Стиль повторяет <c>EmailVerificationTokenModelTests</c>: модель строится на неоткрытом
/// контексте, проверяется design-time модель. Применение миграции и поведение ограничений
/// на реальной PostgreSQL проверяют интеграционные тесты.
/// </remarks>
public class EmailChangeTokenModelTests
{
    private const string ActiveIndexFilter = "consumed_at IS NULL AND revoked_at IS NULL";

    [Fact]
    public void Entity_IsMappedToEmailChangeTokensTable()
    {
        using var context = CreateContext();

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EmailChangeToken));

        Assert.NotNull(entity);
        Assert.Equal("email_change_tokens", entity.GetTableName());
        Assert.Equal("pk_email_change_tokens", entity.GetKeys().Single().GetName());
    }

    [Fact]
    public void TokenHashCheck_RequiresSixtyFourCharacters()
    {
        using var context = CreateContext();

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EmailChangeToken));

        Assert.NotNull(entity);

        var check = Assert.Single(
            entity.GetCheckConstraints(),
            candidate => candidate.Name == "ck_email_change_tokens_token_hash");

        Assert.Equal("char_length(token_hash) = 64", check.Sql);
    }

    [Fact]
    public void NewEmailCheck_RequiresAtSignAfterFirstCharacter()
    {
        using var context = CreateContext();

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EmailChangeToken));

        Assert.NotNull(entity);

        var check = Assert.Single(
            entity.GetCheckConstraints(),
            candidate => candidate.Name == "ck_email_change_tokens_new_email");

        // Формат повторяет ck_users_email: тот же предикат, тот же смысл.
        Assert.Equal("position('@' in new_email) > 1", check.Sql);
    }

    [Fact]
    public void NewEmailLengthCheck_RequiresThreeTo254Characters()
    {
        using var context = CreateContext();

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EmailChangeToken));

        Assert.NotNull(entity);

        var check = Assert.Single(
            entity.GetCheckConstraints(),
            candidate => candidate.Name == "ck_email_change_tokens_new_email_length");

        Assert.Equal("char_length(new_email) BETWEEN 3 AND 254", check.Sql);
    }

    [Fact]
    public void NewEmail_IsRequiredAndLimitedTo254Characters()
    {
        using var context = CreateContext();

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EmailChangeToken));

        Assert.NotNull(entity);

        var property = Assert.Single(
            entity.GetProperties(),
            candidate => candidate.Name == nameof(EmailChangeToken.NewEmail));

        Assert.Equal("new_email", property.GetColumnName());
        Assert.False(property.IsNullable);
        Assert.Equal(254, property.GetMaxLength());
    }

    [Fact]
    public void HashIndex_IsUniqueOnTokenHash()
    {
        using var context = CreateContext();

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EmailChangeToken));

        Assert.NotNull(entity);

        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ux_email_change_tokens_hash");

        Assert.True(index.IsUnique);
        Assert.Null(index.GetFilter());
        Assert.Collection(
            index.Properties,
            property =>
            {
                Assert.Equal("TokenHash", property.Name);
                Assert.Equal("token_hash", property.GetColumnName());
            });
    }

    [Fact]
    public void ActiveIndex_IsPartialUniqueOnUserId()
    {
        using var context = CreateContext();

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EmailChangeToken));

        Assert.NotNull(entity);

        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ux_email_change_tokens_active");

        Assert.True(index.IsUnique);
        Assert.Equal(ActiveIndexFilter, index.GetFilter());
        Assert.Collection(
            index.Properties,
            property =>
            {
                Assert.Equal("UserId", property.Name);
                Assert.Equal("user_id", property.GetColumnName());
            });
    }

    [Fact]
    public void ExpiresAtIndex_IsNonUnique()
    {
        using var context = CreateContext();

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EmailChangeToken));

        Assert.NotNull(entity);

        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ix_email_change_tokens_expires_at");

        Assert.False(index.IsUnique);
        Assert.Equal("ExpiresAt", Assert.Single(index.Properties).Name);
    }

    [Fact]
    public void UserForeignKey_CascadesOnDelete()
    {
        using var context = CreateContext();

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EmailChangeToken));

        Assert.NotNull(entity);

        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(User), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal(
            "fk_email_change_tokens_users_user_id",
            foreignKey.GetConstraintName());
    }

    [Fact]
    public void BusinessTimeColumns_AreMappedByTheirOwnNames()
    {
        using var context = CreateContext();

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(EmailChangeToken));

        Assert.NotNull(entity);

        Assert.Equal("issued_at", entity.FindProperty("IssuedAt")!.GetColumnName());
        Assert.Equal("expires_at", entity.FindProperty("ExpiresAt")!.GetColumnName());
        Assert.Equal("consumed_at", entity.FindProperty("ConsumedAt")!.GetColumnName());
        Assert.Equal("revoked_at", entity.FindProperty("RevokedAt")!.GetColumnName());
        Assert.Equal("created_at", entity.FindProperty("CreatedAt")!.GetColumnName());
        Assert.Equal("updated_at", entity.FindProperty("UpdatedAt")!.GetColumnName());
    }

    [Fact]
    public void Migration_AddEmailChangeTokensExistsAndSnapshotMatchesModel()
    {
        using var context = CreateContext();

        Assert.Contains(
            "20261111164843_AddEmailChangeTokens",
            context.Database.GetMigrations());

        // Снимок модели обновлён вместе с миграцией: следующая миграция не должна
        // неожиданно «находить» незакоммиченные изменения.
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=59999;Database=unused;Username=test;Password=test")
            .Options);
}
