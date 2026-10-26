using System.Text.RegularExpressions;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Пара «услуга барбера — услуга справочника» для слотов читается одной строкой (R-20).
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: проверяется трансляция EF Core / Npgsql.
/// Идентификатор и длительность обязаны выбираться из той же строки <c>barber_services</c>,
/// иначе ответ слотов отдаст <c>barberServiceId</c> одной пары, а длительность — другой.
/// Поведение обработчика без базы — в <c>BarberSearch.Application.UnitTests</c>.
/// </remarks>
public class BarberSlotServiceQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void BuildSlotServiceQuery_SelectsIdAndDurationFromSameFilteredRow()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;
        using var context = new ApplicationDbContext(options);
        var barberProfileId = Guid.CreateVersion7();
        var hairServiceId = Guid.CreateVersion7();

        var sql = BarberProfileRepository
            .BuildSlotServiceQuery(context, barberProfileId, hairServiceId)
            .ToQueryString();

        // Фильтр — услуга справочника и активность у конкретного профиля.
        Assert.Contains("hair_service_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("barber_profile_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);

        // Идентификатор пары и длительность выбираются одним SELECT по barber_services:
        // отдельный запрос длительности мог бы вернуть другую строку (R-20).
        Assert.True(
            Regex.IsMatch(
                sql,
                @"select\s+[\s\S]*?\bid\b[\s\S]*duration_minutes[\s\S]*from\s+barber_services",
                RegexOptions.IgnoreCase),
            sql);
        Assert.Single(Regex.Matches(sql, @"\bfrom\s+barber_services\b", RegexOptions.IgnoreCase));
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }
}
