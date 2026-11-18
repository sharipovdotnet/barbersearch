using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using EmailValueObject = BarberSearch.Domain.Users.ValueObjects.Email;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Маппинг адреса пользователя: объект-значение <c>Email</c> хранится в прежней
/// колонке <c>users.email</c> типа <c>citext</c> через конвертер — без подключения к базе.
/// </summary>
/// <remarks>
/// <para>
/// Стиль повторяет <c>EmailVerificationTokenModelTests</c>: модель строится на неоткрытом
/// контексте. Проверяется не только колонка, но и то, что адрес остаётся <b>скалярным</b>
/// свойством сущности: owned-тип EF потерял бы снимок при замене инстанса, и адрес выпал бы
/// из журнала аудита (та же дыра D4, что у <c>BarberProfile.DisplayName</c>).
/// </para>
/// <para>
/// Поведение на реальной PostgreSQL проверяют интеграционные тесты.
/// </para>
/// </remarks>
public class UserEmailModelTests
{
    [Fact]
    public void Email_IsScalarPropertyInCitextColumn()
    {
        using var context = CreateContext();

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(User));

        Assert.NotNull(entity);

        var property = entity.FindProperty("Email");

        Assert.NotNull(property);
        Assert.Equal(typeof(EmailValueObject), property.ClrType);
        Assert.False(property.IsNullable);

        // Owned-тип был бы навигацией, а не свойством: снимок значения потерялся бы.
        Assert.Null(entity.FindNavigation("Email"));

        var column = StoreObjectIdentifier.Table("users", null);

        Assert.Equal("email", property.GetColumnName(column));
        Assert.Equal("citext", property.GetColumnType());
        Assert.Equal(254, property.GetMaxLength());
    }

    [Fact]
    public void Email_KeepsUniqueIndexOnCanonicalValue()
    {
        using var context = CreateContext();

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(User));

        Assert.NotNull(entity);

        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ux_users_email");

        Assert.True(index.IsUnique);

        var indexed = Assert.Single(index.Properties);

        Assert.Equal("Email", indexed.Name);
    }

    [Fact]
    public void ChangeEmail_KeepsOriginalValueForAuditDiff()
    {
        // Аудит строит diff по OriginalValue/CurrentValue того же свойства. Если бы Email
        // стал owned-типом, EF удалил бы старый инстанс и добавил новый без снимка, и строка
        // user.updated перестала бы называть прежний адрес.
        using var context = CreateContext();

        var user = User.Create("old.address@example.com", "hash", "Ivan", "Petrov").Value;
        context.Users.Add(user);
        context.Entry(user).State = EntityState.Unchanged;

        var changed = user.ChangeEmail(" New.Address@Example.COM ");

        Assert.True(changed.IsSuccess);

        var entry = context.Entry(user);

        Assert.Equal(EntityState.Modified, entry.State);

        var property = entry.Property("Email");

        Assert.True(property.IsModified);
        Assert.Equal("old.address@example.com", property.OriginalValue?.ToString());
        Assert.Equal("new.address@example.com", property.CurrentValue?.ToString());
    }

    [Fact]
    public void ChangeEmail_ToSameCanonicalAddress_KeepsEntityUnchanged()
    {
        using var context = CreateContext();

        var user = User.Create("user@example.com", "hash", "Ivan", "Petrov").Value;
        context.Users.Add(user);
        context.Entry(user).State = EntityState.Unchanged;

        var changed = user.ChangeEmail("  User@Example.COM  ");

        Assert.True(changed.IsFailure);
        Assert.Equal(EntityState.Unchanged, context.Entry(user).State);
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=59999;Database=unused;Username=test;Password=test")
            .Options);
}
