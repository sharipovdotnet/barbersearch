using System.Text.RegularExpressions;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Shared;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Узкое чтение профиля барбера по брони — ключ advisory-блокировки для переноса визита.
/// </summary>
/// <remarks>
/// <para>
/// Перенос обязан взять блокировку барбера <b>до</b> чтения самой брони, иначе контекст
/// вернёт отслеживаемую сущность с устаревшими данными. Поэтому репозиторий отдаёт только
/// идентификатор профиля: проекция одного столбца без отслеживания (AGENT.md §15.6).
/// </para>
/// <para>
/// <c>ToQueryString()</c> не открывает соединение: проверяется трансляция EF Core / Npgsql.
/// Исполнение (404 на отсутствующей брони) проверяет интеграционный тест; здесь — форма
/// запроса и маппинг отсутствия в <c>booking.notFound</c>, чтобы код не разошёлся с контрактом.
/// </para>
/// </remarks>
public class BookingBarberProfileIdQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void BuildBarberProfileIdQuery_SelectsOnlyTheProfileIdOfOneBooking()
    {
        using var context = CreateContext();
        var bookingId = Guid.CreateVersion7();

        var sql = BookingRepository
            .BuildBarberProfileIdQuery(context, bookingId)
            .ToQueryString();

        // Ровно один столбец и ровно одна строка: ключ advisory-блокировки.
        Assert.True(
            Regex.IsMatch(
                sql,
                @"select\s+b\.barber_profile_id\s+from\s+bookings\s+as\s+b\s+where\s+b\.id",
                RegexOptions.IgnoreCase),
            sql);

        // Ни одного другого столбца брони: чтению нужен только ключ блокировки.
        Assert.DoesNotContain("b.status", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("b.price", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("b.start_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("b.end_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Regex.Matches(sql, @"\bfrom\s+bookings\b", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void MapBarberProfileId_WhenBookingIsMissing_ReturnsBookingNotFound()
    {
        var bookingId = Guid.CreateVersion7();

        var result = BookingRepository.MapBarberProfileId(bookingId, null);

        Assert.True(result.IsFailure);

        var error = Assert.Single(result.Error.Errors);
        Assert.Equal(ErrorType.NOT_FOUND, error.Type);
        Assert.Equal("booking.notFound", error.Code);
        Assert.Equal(bookingId.ToString(), error.InvalidField);
    }

    [Fact]
    public void MapBarberProfileId_WithProjectedRow_ReturnsTheProfileId()
    {
        var barberProfileId = Guid.CreateVersion7();

        var result = BookingRepository.MapBarberProfileId(Guid.CreateVersion7(), barberProfileId);

        Assert.True(result.IsSuccess);
        Assert.Equal(barberProfileId, result.Value);
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options);
}
