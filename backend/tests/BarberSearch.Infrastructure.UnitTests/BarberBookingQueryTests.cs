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
/// </summary>
/// <remarks>
/// Соединение указывает на закрытый порт. EF оборачивает сбой подключения в
/// <see cref="InvalidOperationException"/> с вложенным <see cref="NpgsqlException"/>: это
/// доказывает, что SQL построен. Непереводимое выражение дало бы исключение без вложенного
/// <see cref="NpgsqlException"/> и с текстом «could not be translated» (AGENT.md §5.4).
/// База не нужна.
/// </remarks>
public class BarberBookingQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public async Task GetBarberPageAsync_QueryWithGuestLeftJoinIsTranslatable()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        var repository = new BookingRepository(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.GetBarberPageAsync(
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
}
