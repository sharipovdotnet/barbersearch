using System.Text.RegularExpressions;
using BarberSearch.Application.Bookings.Admin;
using BarberSearch.Domain.Bookings;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Админская выборка броней должна собираться одним SQL-запросом: левый join по клиенту
/// (у гостевой брони <c>user_id = NULL</c>), фильтр статуса, поиск через <c>ILIKE</c>
/// и keyset-лимит. Аватары приходят из двух разных join'ов: клиентский
/// <c>users.avatar_url</c> и второй join по <c>barber_profiles.user_id</c>.
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: проверяется трансляция EF Core / Npgsql,
/// в том числе условные поля клиента после <c>DefaultIfEmpty</c>. База не нужна.
/// </remarks>
public class AdminBookingQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void BuildAdminBookingsQuery_LeftJoinsCustomersAndBothAvatars()
    {
        using var context = CreateContext();

        var sql = BookingRepository.BuildAdminBookingsQuery(
                context,
                new AdminBookingFilters(Status: null, Search: null),
                cursor: null,
                limit: null)
            .ToQueryString();

        Assert.Contains("from bookings", sql, StringComparison.OrdinalIgnoreCase);

        // Гостевая бронь без user_id обязана остаться в админском списке: внутреннее
        // соединение скрыло бы её, поэтому клиент присоединяется левым join'ом.
        Assert.Contains("LEFT JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("users", sql, StringComparison.OrdinalIgnoreCase);

        // Два аватара: клиент и владелец профиля барбера. Клиентский join и join владельца
        // профиля — разные соединения по разным ключам, поэтому строк с LEFT JOIN две.
        Assert.Equal(2, Regex.Count(sql, "avatar_url", RegexOptions.IgnoreCase));
        Assert.Equal(2, Regex.Count(sql, "LEFT JOIN", RegexOptions.IgnoreCase));
        Assert.Contains("barber_profiles", sql, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DESC", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAdminBookingsQuery_WithStatusSearchAndCursor_IsTranslatable()
    {
        using var context = CreateContext();
        var cursor = new AdminBookingCursor(
            new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero),
            Guid.CreateVersion7());

        var sql = BookingRepository.BuildAdminBookingsQuery(
                context,
                new AdminBookingFilters(BookingStatus.Confirmed, "100% Ivan_"),
                cursor,
                limit: 21)
            .ToQueryString();

        // Прежние условия и порядок не изменились.
        Assert.Contains("status", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);

        // Оба аватара остаются в проекции и на отфильтрованной странице.
        Assert.Equal(2, Regex.Count(sql, "avatar_url", RegexOptions.IgnoreCase));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;

        return new ApplicationDbContext(options);
    }
}
