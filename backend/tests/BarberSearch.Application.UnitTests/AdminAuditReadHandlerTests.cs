using BarberSearch.Application.Audit.Admin;
using BarberSearch.Application.Audit.Admin.GetAdminAuditLog;
using BarberSearch.Application.Common;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.Audit.Admin;
using BarberSearch.Contracts.Common;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Обработчик админского журнала: разбор типа, автора и периода, размер страницы, курсор,
/// порядок строк и проброс ошибки репозитория.
/// </summary>
public class AdminAuditReadHandlerTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_ReturnsNewestFirstAcrossPages()
    {
        var repository = new FakeAuditLogRepository();
        var oldest = CreateRow(Start);
        var middle = CreateRow(Start.AddMinutes(10));
        var newest = CreateRow(Start.AddMinutes(20));
        repository.Add(oldest);
        repository.Add(middle);
        repository.Add(newest);

        var handler = new GetAdminAuditLogHandler(repository);

        var firstPage = await handler.Handle(
            new GetAdminAuditLogQuery(null, null, null, null, null, 2),
            CancellationToken.None);

        Assert.True(firstPage.IsSuccess);
        Assert.Equal(
            new[] { newest.Id, middle.Id },
            firstPage.Value.Items.Select(row => row.Id));
        Assert.NotNull(firstPage.Value.NextCursor);
        // Репозиторий читает pageSize + 1 строку, чтобы понять, есть ли следующая страница.
        Assert.Equal(3, repository.LastLimit);

        var secondPage = await handler.Handle(
            new GetAdminAuditLogQuery(null, null, null, null, firstPage.Value.NextCursor, 2),
            CancellationToken.None);

        Assert.True(secondPage.IsSuccess);
        Assert.Equal(
            new[] { oldest.Id },
            secondPage.Value.Items.Select(row => row.Id));
        Assert.Null(secondPage.Value.NextCursor);
    }

    [Theory]
    [InlineData(null, AdminAuditEntityType.All)]
    [InlineData("", AdminAuditEntityType.All)]
    [InlineData("all", AdminAuditEntityType.All)]
    [InlineData("User", AdminAuditEntityType.User)]
    [InlineData("REVIEW", AdminAuditEntityType.Review)]
    [InlineData(" barber_profile ", AdminAuditEntityType.BarberProfile)]
    public async Task Get_ParsesEntityTypeCaseInsensitively(
        string? value,
        AdminAuditEntityType expected)
    {
        var repository = new FakeAuditLogRepository();
        var handler = new GetAdminAuditLogHandler(repository);

        var result = await handler.Handle(
            new GetAdminAuditLogQuery(value, null, null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, repository.LastFilters!.EntityType);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("up")]
    [InlineData("0")]
    public async Task Get_WithUnknownEntityType_ReturnsValidation(string value)
    {
        var repository = new FakeAuditLogRepository();
        var handler = new GetAdminAuditLogHandler(repository);

        var result = await handler.Handle(
            new GetAdminAuditLogQuery(value, null, null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.AdminAudit.EntityTypeInvalid, result.Error.Errors[0].Code);
        Assert.Null(repository.LastFilters);
    }

    [Fact]
    public async Task Get_TrimsActorAndPassesItToRepository()
    {
        var repository = new FakeAuditLogRepository();
        var handler = new GetAdminAuditLogHandler(repository);

        await handler.Handle(
            new GetAdminAuditLogQuery(null, "  e2e-admin  ", null, null, null, null),
            CancellationToken.None);

        Assert.Equal("e2e-admin", repository.LastFilters!.Actor);
    }

    [Fact]
    public async Task Get_WithBlankActor_DoesNotFilter()
    {
        var repository = new FakeAuditLogRepository();
        var handler = new GetAdminAuditLogHandler(repository);

        await handler.Handle(
            new GetAdminAuditLogQuery(null, "   ", null, null, null, null),
            CancellationToken.None);

        Assert.Null(repository.LastFilters!.Actor);
    }

    [Fact]
    public async Task Get_WithTooLongActor_ReturnsValidation()
    {
        var repository = new FakeAuditLogRepository();
        var handler = new GetAdminAuditLogHandler(repository);

        var result = await handler.Handle(
            new GetAdminAuditLogQuery(null, new string('a', 101), null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.AdminAudit.ActorTooLong, result.Error.Errors[0].Code);
        Assert.Null(repository.LastFilters);
    }

    [Fact]
    public async Task Get_PeriodBecomesInclusiveUtcDays()
    {
        var repository = new FakeAuditLogRepository();
        var handler = new GetAdminAuditLogHandler(repository);

        await handler.Handle(
            new GetAdminAuditLogQuery(null, null, "2026-09-10", "2026-09-10", null, null),
            CancellationToken.None);

        Assert.Equal(
            new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
            repository.LastFilters!.OccurredFrom);
        // Верхняя граница — начало следующих суток: 23:59:59 последнего дня входит.
        Assert.Equal(
            new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero),
            repository.LastFilters.OccurredToExclusive);
    }

    [Fact]
    public async Task Get_WithEmptyPeriod_DoesNotFilter()
    {
        var repository = new FakeAuditLogRepository();
        var handler = new GetAdminAuditLogHandler(repository);

        await handler.Handle(
            new GetAdminAuditLogQuery(null, null, "", "", null, null),
            CancellationToken.None);

        Assert.Null(repository.LastFilters!.OccurredFrom);
        Assert.Null(repository.LastFilters.OccurredToExclusive);
    }

    [Theory]
    [InlineData("2026-09-11", "2026-09-10")]
    [InlineData("2026-9-1", null)]
    [InlineData(null, "2026-13-40")]
    [InlineData("10.09.2026", null)]
    public async Task Get_WithBadPeriod_ReturnsValidation(string? from, string? to)
    {
        var repository = new FakeAuditLogRepository();
        var handler = new GetAdminAuditLogHandler(repository);

        var result = await handler.Handle(
            new GetAdminAuditLogQuery(null, null, from, to, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApiErrorCodes.AdminAudit.PeriodInvalid, result.Error.Errors[0].Code);
        Assert.Null(repository.LastFilters);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Get_WithPageSizeOutOfRange_ReturnsValidation(int pageSize)
    {
        var repository = new FakeAuditLogRepository();
        var handler = new GetAdminAuditLogHandler(repository);

        var result = await handler.Handle(
            new GetAdminAuditLogQuery(null, null, null, null, null, pageSize),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            $"{ApiErrorCodes.AdminAudit.PageSizePrefix}.pageSize",
            result.Error.Errors[0].Code);
        Assert.Null(repository.LastFilters);
    }

    [Fact]
    public async Task Get_WithoutPageSize_UsesDefault()
    {
        var repository = new FakeAuditLogRepository();
        var handler = new GetAdminAuditLogHandler(repository);

        await handler.Handle(
            new GetAdminAuditLogQuery(null, null, null, null, null, null),
            CancellationToken.None);

        Assert.Equal(Pagination.DEFAULT_PAGE_SIZE + 1, repository.LastLimit);
    }

    [Fact]
    public async Task Get_WithBrokenCursor_ReturnsValidation()
    {
        var repository = new FakeAuditLogRepository();
        var handler = new GetAdminAuditLogHandler(repository);

        var result = await handler.Handle(
            new GetAdminAuditLogQuery(null, null, null, null, "garbage", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin.audit.cursor.invalid", result.Error.Errors[0].Code);
        Assert.Null(repository.LastFilters);
    }

    [Fact]
    public async Task Get_PropagatesRepositoryFailure()
    {
        var repository = new FakeAuditLogRepository
        {
            ForcedFailure = new Failure(Error.Validation(
                "admin.audit.page.limit",
                "The page limit is not positive.",
                "limit"))
        };
        var handler = new GetAdminAuditLogHandler(repository);

        var result = await handler.Handle(
            new GetAdminAuditLogQuery(null, null, null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin.audit.page.limit", result.Error.Errors[0].Code);
    }

    private static AdminAuditEntryResponse CreateRow(DateTimeOffset occurredAt)
        => new(
            Guid.CreateVersion7(),
            occurredAt,
            "user.updated",
            AdminAuditEntityTypes.USER,
            Guid.NewGuid(),
            new AdminAuditActorResponse(Guid.CreateVersion7(), "e2e-admin@barbersearch.test"),
            []);
}
