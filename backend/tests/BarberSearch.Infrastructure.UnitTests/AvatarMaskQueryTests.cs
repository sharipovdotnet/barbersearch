using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Маска скрытого администратором аватара в публичных чтениях: поиск и карточка барбера.
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: проверяется, что тернарник попал в SQL
/// (<c>CASE … WHEN … THEN NULL</c>), а не вычисляется после материализации. Иначе на большой
/// странице поиска маска стала бы клиентской фильтрацией и изменила бы план выдачи.
/// </remarks>
public class AvatarMaskQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void BuildSearchAvatarsQuery_MasksBlockedAvatarInSqlWithoutJoins()
    {
        using var context = CreateContext();

        var sql = BarberProfileRepository
            .BuildSearchAvatarsQuery(context, [Guid.CreateVersion7(), Guid.CreateVersion7()])
            .ToQueryString();

        // Второй запрос остаётся узким чтением users по набору id: без join'а с профилем.
        Assert.Contains("from users", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avatar_url", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_avatar_blocked", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCardAvatarQuery_MasksBlockedAvatarInSqlByPrimaryKey()
    {
        using var context = CreateContext();

        var sql = BarberProfileRepository
            .BuildCardAvatarQuery(context, Guid.CreateVersion7())
            .ToQueryString();

        Assert.Contains("from users", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avatar_url", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_avatar_blocked", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;

        return new ApplicationDbContext(options);
    }
}
