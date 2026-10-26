using BarberSearch.Domain.RefreshTokens;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Очистка change tracker после отзыва refresh-токенов в обход EF Core (R-16).
/// </summary>
/// <remarks>
/// <para>
/// <c>RevokeChainAsync</c> и <c>RevokeAllForUserAsync</c> меняют строки сырым <c>UPDATE</c>,
/// поэтому отслеживаемые копии продолжают хранить <c>RevokedAt = null</c>. Тесты проверяют,
/// что очистка снимает именно эти сущности и следующий <c>SaveChanges</c> уже не пишет
/// им прежнее значение; соединение указывает на закрытый порт, поэтому любая запись упала бы
/// <see cref="Npgsql.NpgsqlException"/>.
/// </para>
/// <para>
/// Методы репозитория вызывают тот же <c>DetachTracked</c>; их полный путь вместе с SQL
/// проверен на локальной PostgreSQL 16 (R-16, <c>CODE-REVIEW.md</c>).
/// </para>
/// </remarks>
public class RefreshTokenTrackerTests
{
    [Fact]
    public async Task DetachAll_AfterChainRevoke_RemovesStaleCopiesSoSaveChangesWritesNothing()
    {
        await using var context = new ApplicationDbContext(CreateOptions());
        var repository = new TestRepository(context);
        var first = CreateToken(Guid.CreateVersion7());
        var second = CreateToken(Guid.CreateVersion7());

        context.AttachRange(first, second);

        // Сырой UPDATE уже отозвал строки в базе. Отмечаем отслеживаемую копию изменённой
        // целиком — так её записал бы SaveGraphAsync; вместе с ней в UPDATE попал бы
        // и прежний RevokedAt = null.
        context.Update(first);
        Assert.Equal(EntityState.Modified, context.Entry(first).State);

        var detached = repository.DetachAll();

        Assert.Equal(2, detached);
        Assert.Equal(EntityState.Detached, context.Entry(first).State);
        Assert.Equal(EntityState.Detached, context.Entry(second).State);
        Assert.Empty(context.ChangeTracker.Entries<RefreshToken>());
        Assert.Equal(0, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task DetachForUser_AfterRevokeAll_RemovesOnlyThatUsersTokens()
    {
        await using var context = new ApplicationDbContext(CreateOptions());
        var repository = new TestRepository(context);
        var userId = Guid.CreateVersion7();
        var first = CreateToken(userId);
        var second = CreateToken(userId);
        var foreign = CreateToken(Guid.CreateVersion7());

        context.AttachRange(first, second, foreign);
        context.Update(first);

        var detached = repository.DetachForUser(userId);

        Assert.Equal(2, detached);
        Assert.Equal(EntityState.Detached, context.Entry(first).State);
        Assert.Equal(EntityState.Detached, context.Entry(second).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(foreign).State);

        // Чужой токен не менялся, а своих в трекере не осталось — писать нечего.
        Assert.Equal(0, await context.SaveChangesAsync());
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test")
            .Options;

    private static RefreshToken CreateToken(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;

        return RefreshToken.Create(
            userId,
            new string('a', RefreshToken.HASH_LENGTH),
            now.AddDays(RefreshToken.MIN_LIFETIME_DAYS),
            now).Value;
    }

    private sealed class TestRepository : RepositoryBase<RefreshToken>
    {
        public TestRepository(ApplicationDbContext context)
            : base(context)
        {
        }

        public int DetachAll() => DetachTracked();

        public int DetachForUser(Guid userId) => DetachTracked(token => token.UserId == userId);
    }
}
