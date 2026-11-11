using System.Text.RegularExpressions;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Узкое чтение аватара для раздачи: один столбец <c>avatar_url</c> по первичному ключу,
/// без <c>Include(FavoriteBarbers)</c> и без join'ов.
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: проверяется трансляция EF Core / Npgsql.
/// Поведение метода (URL, <c>null</c>, <c>NotFound</c>) проверяется интеграционным тестом на
/// реальной PostgreSQL: в unit-проекте нет провайдера для базы в памяти, а добавлять пакет
/// ради этого нельзя. Здесь фиксируется форма SQL — то, ради чего метод узкий.
/// </remarks>
public class UserAvatarUrlQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void BuildAvatarUrlQuery_SelectsOnlyAvatarUrlByPrimaryKeyWithoutJoins()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;
        using var context = new ApplicationDbContext(options);
        var userId = Guid.CreateVersion7();

        var sql = UserRepository.BuildAvatarUrlQuery(context, userId).ToQueryString();

        // Один столбец: проекция не тянет агрегат и избранное.
        Assert.Matches(
            new Regex(
                @"select\s+[a-z]\.avatar_url\s+from\s+users\s+as\s+[a-z]",
                RegexOptions.IgnoreCase),
            sql);
        Assert.DoesNotContain("favorite_barbers", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);

        // Фильтр — первичный ключ, а не поиск по URL или пользователю из другого столбца.
        Assert.Contains("id =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
