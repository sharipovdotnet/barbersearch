using BarberSearch.Application.Photos.Admin;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Query-билдеры админских очередей фотографий: только <c>Pending</c>, FIFO-порядок,
/// keyset-курсор, поиск через <c>ILIKE … ESCAPE</c> и лимит <c>pageSize + 1</c>.
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: проверяется трансляция EF Core / Npgsql
/// без базы (AGENT.md §5.4). Счётчики сводки используют те же предикаты, поэтому условие
/// <c>moderation_status = 1</c> здесь — часть контракта счётчика.
/// </remarks>
public class AdminPhotoQueueQueryTests
{
    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void BuildAdminAvatarPhotosQuery_SelectsOnlyPendingQueueInFifoOrder()
    {
        using var context = CreateContext();

        var sql = UserRepository.BuildAdminAvatarPhotosQuery(context).ToQueryString();

        Assert.Contains("from users", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avatar_moderation_status = 1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avatar_url IS NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avatar_submitted_at IS NOT NULL", sql, StringComparison.OrdinalIgnoreCase);

        // Очередь FIFO: старые сверху, второй ключ — id для стабильности страниц.
        Assert.Contains(
            "ORDER BY u.avatar_submitted_at, u.id",
            sql,
            StringComparison.OrdinalIgnoreCase);

        // Сырой URL и статус отдаются администратору: публичной маски в этой проекции нет.
        Assert.Contains("avatar_url", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("is_avatar_blocked", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAdminAvatarPhotosQuery_WithSearchCursorAndLimit_IsTranslatable()
    {
        using var context = CreateContext();

        var cursor = new AdminPhotoCursor(SubmittedAt, Guid.CreateVersion7());

        var sql = UserRepository
            .BuildAdminAvatarPhotosQuery(context, "100% Артём_", cursor, 21)
            .ToQueryString();

        Assert.Contains("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);

        // Keyset-продолжение — строго после пары (submitted_at, id).
        Assert.Contains("avatar_submitted_at > @", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id > @", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAdminPortfolioPhotosQuery_SelectsOnlyPendingQueueInFifoOrder()
    {
        using var context = CreateContext();

        var sql = BarberProfileRepository
            .BuildAdminPortfolioPhotosQuery(context)
            .ToQueryString();

        Assert.Contains("from portfolio_photos", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("join barber_profiles", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("join users", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("moderation_status = 1", sql, StringComparison.OrdinalIgnoreCase);

        // Отдельной метки поступления у портфолио нет: FIFO по created_at, второй ключ — id.
        Assert.Contains(
            "ORDER BY p.created_at, p.id",
            sql,
            StringComparison.OrdinalIgnoreCase);

        // В фильтре нет ни скрытия владельцем, ни блокировки: они статус не меняют.
        // is_published приходит только в проекции строки, а не в WHERE.
        Assert.DoesNotContain("is_blocked", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAdminPortfolioPhotosQuery_WithSearchCursorAndLimit_IsTranslatable()
    {
        using var context = CreateContext();

        var cursor = new AdminPhotoCursor(SubmittedAt, Guid.CreateVersion7());

        var sql = BarberProfileRepository
            .BuildAdminPortfolioPhotosQuery(context, "100% Артём_", cursor, 21)
            .ToQueryString();

        Assert.Contains("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("created_at > @", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id > @", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WherePendingAvatar_IsTheSamePredicateAsTheQueueAndCounter()
    {
        using var context = CreateContext();

        var sql = UserRepository
            .WherePendingAvatar(context.Users.AsNoTracking())
            .ToQueryString();

        Assert.Contains("avatar_moderation_status = 1", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avatar_url IS NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avatar_submitted_at IS NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WherePendingPortfolioPhoto_IsTheSamePredicateAsTheQueueAndCounter()
    {
        using var context = CreateContext();

        var sql = BarberProfileRepository
            .WherePendingPortfolioPhoto(context.PortfolioPhotos.AsNoTracking())
            .ToQueryString();

        Assert.Contains("moderation_status = 1", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;

        return new ApplicationDbContext(options);
    }
}
