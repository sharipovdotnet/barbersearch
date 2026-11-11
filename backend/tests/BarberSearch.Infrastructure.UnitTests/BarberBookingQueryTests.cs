using BarberSearch.Application.Bookings;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Список записей барбера должен транслироваться в SQL с левым join по клиенту:
/// у гостевой брони (walk-in) <c>user_id = NULL</c>, и внутреннее соединение скрыло бы её.
/// Аватар клиента приходит из того же левого join'а (<c>users.avatar_url</c>).
/// </summary>
/// <remarks>
/// Соединение указывает на закрытый порт. EF оборачивает сбой подключения в
/// <see cref="InvalidOperationException"/> с вложенным <see cref="NpgsqlException"/>: это
/// доказывает, что SQL построен. Непереводимое выражение дало бы исключение без вложенного
/// <see cref="NpgsqlException"/> и с текстом «could not be translated» (AGENT.md §5.4).
/// Колонки проверяются через <c>ToQueryString()</c> у вынесенного builder'а: база не нужна.
/// </remarks>
public class BarberBookingQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public async Task GetBarberPageAsync_QueryWithGuestLeftJoinIsTranslatable()
    {
        await using var context = CreateContext();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new BookingRepository(context).GetBarberPageAsync(
                Guid.CreateVersion7(),
                BarberBookingScope.Upcoming,
                DateTimeOffset.UtcNow,
                cursor: null,
                limit: 21));

        Assert.IsType<NpgsqlException>(exception.InnerException);
        Assert.DoesNotContain(
            "could not be translated",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildBarberBookingsQuery_SelectsCustomerAvatarAndKeepsScopeAndOrder()
    {
        using var context = CreateContext();

        var sql = BookingRepository.BuildBarberBookingsQuery(
                context,
                Guid.CreateVersion7(),
                BarberBookingScope.Upcoming,
                DateTimeOffset.UtcNow,
                cursor: null,
                limit: 21)
            .ToQueryString();

        // Аватар клиента — из уже существующего LEFT JOIN users, без нового соединения.
        Assert.Contains("avatar_url", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFT JOIN", sql, StringComparison.OrdinalIgnoreCase);

        // Скоуп «ближайшие» и порядок keyset не изменились.
        Assert.Contains("status", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("end_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("start_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DESC", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;

        return new ApplicationDbContext(options);
    }
}
