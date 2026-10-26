using BarberSearch.Domain.RefreshTokens;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Web.Auth;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Фоновая уборка истёкших refresh-токенов (R-50) на реальной таблице.
/// </summary>
/// <remarks>
/// Проверяется не SQL, а инвариант границы: удаляются строки, истёкшие более 30 дней назад,
/// поэтому у каждой удаляемой строки все потомки по <c>replaced_by</c> уже истекли. Цепочка
/// <c>parent → child</c> удаляется целиком и не обнуляет ссылку живого токена, а недавно
/// истёкшая строка остаётся для reuse-detection.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public sealed class RefreshTokenCleanupTests
{
    private readonly PostgresFixture _database;

    public RefreshTokenCleanupTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 120_000)]
    public async Task Cleanup_removes_expired_rows_and_keeps_recent_and_live_tokens()
    {
        await using var app = await TestApp.StartAsync(_database);

        var user = await app.CreateUserAsync(
            $"refresh-cleanup-{Guid.NewGuid():N}@barbersearch.test");
        var now = DateTimeOffset.UtcNow;

        var oldHash = new string('a', RefreshToken.HASH_LENGTH);
        var recentHash = new string('b', RefreshToken.HASH_LENGTH);
        var liveHash = new string('c', RefreshToken.HASH_LENGTH);
        var childHash = new string('d', RefreshToken.HASH_LENGTH);
        var parentHash = new string('e', RefreshToken.HASH_LENGTH);

        // old: истёк 31 день назад — за границей 30-дневного запаса, удаляется.
        var old = Require(RefreshToken.Create(
            user.Id, oldHash, now.AddDays(-31), now.AddDays(-32)));

        // recent: истёк 1 день назад — ещё в окне хранения, остаётся.
        var recent = Require(RefreshToken.Create(
            user.Id, recentHash, now.AddDays(-1), now.AddDays(-2)));

        // live: действует ещё 14 дней — не трогается.
        var live = Require(RefreshToken.Create(
            user.Id, liveHash, now.AddDays(14), now));

        // Цепочка: parent заменён на child. Оба истекли за границей уборки, поэтому
        // ON DELETE SET NULL не должен обнулить ссылку живого токена — его здесь нет.
        var child = Require(RefreshToken.Create(
            user.Id, childHash, now.AddDays(-31), now.AddDays(-32)));
        var parent = Require(RefreshToken.Create(
            user.Id, parentHash, now.AddDays(-46), now.AddDays(-47)));

        var revoked = parent.Revoke(now.AddDays(-45), child.Id);
        Assert.True(revoked.IsSuccess);

        // parent.replaced_by → child.id: self-FK, поэтому сначала сохраняется потомок.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.RefreshTokens.AddRange(old, recent, live, child);
            await db.SaveChangesAsync();

            db.RefreshTokens.Add(parent);
            await db.SaveChangesAsync();
        }

        var cleanup = app.Services.GetRequiredService<RefreshTokenCleanupService>();

        // old + parent + child: цепочка удаляется вместе, recent и live остаются.
        Assert.Equal(3, await cleanup.CleanupOnceAsync(CancellationToken.None));

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var remaining = await db.RefreshTokens
                .AsNoTracking()
                .Select(token => token.TokenHash)
                .ToListAsync();

            Assert.Equal(
                new[] { liveHash, recentHash }.OrderBy(hash => hash, StringComparer.Ordinal),
                remaining.OrderBy(hash => hash, StringComparer.Ordinal));
        }

        // Повторный прогон ничего не удаляет: DELETE идемпотентен.
        Assert.Equal(0, await cleanup.CleanupOnceAsync(CancellationToken.None));
    }

    private static RefreshToken Require(Result<RefreshToken, Failure> result)
    {
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
