using BarberSearch.Application.Users.Admin;
using BarberSearch.Domain.Users;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Админские выборки пользователей должны собираться одним SQL-запросом: фильтры статуса
/// и роли, поиск через <c>ILIKE</c> и левый join по профилю барбера.
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: проверяется трансляция EF Core / Npgsql,
/// в том числе owned-типы <c>DisplayName</c> в левом join. База не нужна.
/// </remarks>
public class AdminUserQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void BuildAdminUsersQuery_OrdersNewestFirst()
    {
        using var context = CreateContext();

        var sql = UserRepository.BuildAdminUsersQuery(context).ToQueryString();

        Assert.Contains("from users", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DESC", sql, StringComparison.OrdinalIgnoreCase);
        // Список всегда свежими сверху, поэтому лимит без курсора не добавляется.
        Assert.DoesNotContain("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAdminUsersQuery_WithFiltersSearchAndCursor_IsTranslatable()
    {
        using var context = CreateContext();

        var cursor = new AdminUserCursor(
            new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.Zero),
            Guid.CreateVersion7());

        var sql = UserRepository.BuildAdminUsersQuery(
                context,
                AdminUserStatus.Unverified,
                UserRole.Barber,
                search: "100% Артём_",
                cursor: cursor,
                limit: 21)
            .ToQueryString();

        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_email_verified", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("role", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);

        // Поиск регистронезависимый, а служебные символы шаблона экранированы.
        Assert.Contains("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAdminUserDetailQuery_LeftJoinsBarberProfile()
    {
        using var context = CreateContext();

        var sql = UserRepository
            .BuildAdminUserDetailQuery(context, Guid.CreateVersion7())
            .ToQueryString();

        Assert.Contains("from users", sql, StringComparison.OrdinalIgnoreCase);

        // Левый join, иначе клиенты и администраторы без профиля просто исчезли бы из карточки.
        Assert.Contains("LEFT JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("barber_profiles", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("display_name", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;

        return new ApplicationDbContext(options);
    }
}
