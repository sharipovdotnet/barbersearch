using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Infrastructure.Postgresql;
using BarberSearch.Infrastructure.Postgresql.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Фильтр по дополнительным услугам и подсказки для него обязаны транслироваться в SQL.
/// </summary>
/// <remarks>
/// <c>ToQueryString()</c> не открывает соединение: проверяется трансляция EF Core / Npgsql.
/// Подстрока должна стать <c>EXISTS (SELECT 1 FROM unnest(additional_services) … ILIKE …)</c>,
/// а не клиентской фильтрацией: иначе keyset-пагинация вернула бы неполную выдачу.
/// Подсказки — <c>unnest</c> + <c>DISTINCT</c> + <c>LIMIT</c>, база не нужна.
/// </remarks>
public class AdditionalServiceSearchQueryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=59999;Database=model_only;Username=test;Password=test";

    [Fact]
    public void AdditionalServiceFilter_UsesUnnestIlikeAndEscape()
    {
        using var context = CreateContext();
        var repository = new BarberProfileRepository(context);
        var filter = new BarberSearchFilter(
            City: null,
            SpecializationId: null,
            HairServiceId: null,
            Date: null,
            Language: null,
            AdditionalService: "  бород  ",
            Cursor: null,
            Limit: 21);

        var sql = repository
            .ApplySearchFilters(context.BarberProfiles.AsNoTracking(), filter)
            .ToQueryString();

        Assert.Contains("additional_services", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unnest", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_verified", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdditionalServiceFilter_WithoutValue_DoesNotTouchTheCollection()
    {
        using var context = CreateContext();
        var repository = new BarberProfileRepository(context);
        var filter = new BarberSearchFilter(
            City: null,
            SpecializationId: null,
            HairServiceId: null,
            Date: null,
            Language: null,
            AdditionalService: "   ",
            Cursor: null,
            Limit: 21);

        var sql = repository
            .ApplySearchFilters(context.BarberProfiles.AsNoTracking(), filter)
            .ToQueryString();

        Assert.DoesNotContain("unnest", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdditionalServicesSuggestions_UseUnnestDistinctAndLimit()
    {
        using var context = CreateContext();

        var sql = BarberProfileRepository
            .BuildAdditionalServicesQuery(context.BarberProfiles.AsNoTracking())
            .ToQueryString();

        Assert.Contains("additional_services", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unnest", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DISTINCT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DateFilter_UsesTheEffectiveDayAndExceptionOverTheTemplate()
    {
        using var context = CreateContext();
        var repository = new BarberProfileRepository(context);
        var filter = new BarberSearchFilter(
            City: null,
            SpecializationId: null,
            HairServiceId: null,
            Date: new DateOnly(2026, 11, 20),
            Language: null,
            AdditionalService: null,
            Cursor: null,
            Limit: 21);

        var sql = repository
            .ApplySearchFilters(context.BarberProfiles.AsNoTracking(), filter)
            .ToQueryString();

        // Эффективный день — «исключение важнее шаблона»: рабочий день шаблона обязан
        // перекрываться отсутствием исключения на дату.
        Assert.Contains("schedule_overrides", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("working_schedules", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("day_of_week", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_day_off", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("barber_salons", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_verified", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DateFilter_WithBlockedProfiles_AddsTheInPredicate()
    {
        using var context = CreateContext();
        var repository = new BarberProfileRepository(context);
        var filter = new BarberSearchFilter(
            City: null,
            SpecializationId: null,
            HairServiceId: null,
            Date: new DateOnly(2026, 11, 20),
            Language: null,
            AdditionalService: null,
            Cursor: null,
            Limit: 21);

        var sql = repository
            .ApplySearchFilters(
                context.BarberProfiles.AsNoTracking(),
                filter,
                [Guid.CreateVersion7(), Guid.CreateVersion7()])
            .ToQueryString();

        // Отпуск основного салона на весь локальный день: `NOT (… = ANY (@blockedProfileIds))`.
        Assert.Contains("blockedProfileIds", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ANY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DateFilter_WithEmptyBlockedProfiles_AddsNoPredicate()
    {
        using var context = CreateContext();
        var repository = new BarberProfileRepository(context);
        var filter = new BarberSearchFilter(
            City: null,
            SpecializationId: null,
            HairServiceId: null,
            Date: new DateOnly(2026, 11, 20),
            Language: null,
            AdditionalService: null,
            Cursor: null,
            Limit: 21);

        var sql = repository
            .ApplySearchFilters(context.BarberProfiles.AsNoTracking(), filter, [])
            .ToQueryString();

        // Ловушка #4: пустой список не должен добавлять `NOT (… ANY (пусто))`.
        Assert.DoesNotContain("blockedProfileIds", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimeOffDayQuery_JoinsThePrimarySalonAndFiltersTheWindow()
    {
        using var context = CreateContext();

        var windowStart = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);
        var windowEnd = windowStart.AddDays(3);

        var sql = BarberProfileRepository
            .BuildDayBlockedTimeOffsQuery(context, windowStart, windowEnd)
            .ToQueryString();

        Assert.Contains("time_offs", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("barber_salons", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_primary", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("time_zone_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("start_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("end_at", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options);
}
