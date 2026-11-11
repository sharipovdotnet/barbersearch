using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Узкое чтение аватара для раздачи: <c>avatar_url</c>, <c>is_avatar_blocked</c> и
/// <c>avatar_moderation_status</c> по первичному ключу, без <c>Include(FavoriteBarbers)</c>
/// и без join'ов.
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: проверяется трансляция EF Core / Npgsql.
/// Поведение метода (URL, флаг, <c>null</c>, <c>NotFound</c>) проверяется интеграционным тестом
/// на реальной PostgreSQL: в unit-проекте нет провайдера для базы в памяти, а добавлять пакет
/// ради этого нельзя. Здесь фиксируется форма SQL — то, ради чего метод узкий.
/// </remarks>
public class UserAvatarUrlQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void BuildAvatarUrlQuery_SelectsAvatarUrlAndBlockedFlagByPrimaryKeyWithoutJoins()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;
        using var context = new ApplicationDbContext(options);
        var userId = Guid.CreateVersion7();

        var sql = UserRepository.BuildAvatarUrlQuery(context, userId).ToQueryString();

        // Три столбца: проекция не тянет агрегат, избранное и маску (маску применяет раздача).
        Assert.Contains("avatar_url", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_avatar_blocked", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avatar_moderation_status", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("favorite_barbers", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CASE", sql, StringComparison.OrdinalIgnoreCase);

        // Фильтр — первичный ключ, а не поиск по URL или пользователю из другого столбца.
        Assert.Contains("id =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
