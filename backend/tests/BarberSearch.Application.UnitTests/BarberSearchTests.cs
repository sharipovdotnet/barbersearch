using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Application.Common;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Domain.BarberProfiles.Enums;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Поиск барберов: пустая выдача, фильтры, границы страниц и битый курсор.
/// </summary>
public class BarberSearchTests
{
    [Fact]
    public async Task Search_WithoutMatches_ReturnsEmptyPageAndNoCursor()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Null(result.Value.NextCursor);
        Assert.Equal(Pagination.DEFAULT_PAGE_SIZE + 1, repository.LastSearchFilter!.Limit);
    }

    [Fact]
    public async Task Search_ForwardsEveryFilterAndTrimsCity()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);
        var specializationId = Guid.CreateVersion7();
        var serviceId = Guid.CreateVersion7();
        var date = new DateOnly(2026, 12, 1);

        var result = await handler.Handle(
            new SearchBarbersQuery(" Berlin ", specializationId, serviceId, date, null, 5),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.LastSearchFilter);
        Assert.Equal("Berlin", repository.LastSearchFilter!.City);
        Assert.Equal(specializationId, repository.LastSearchFilter.SpecializationId);
        Assert.Equal(serviceId, repository.LastSearchFilter.ServiceId);
        Assert.Equal(date, repository.LastSearchFilter.Date);
        Assert.Null(repository.LastSearchFilter.Cursor);
        Assert.Equal(6, repository.LastSearchFilter.Limit);
    }

    [Fact]
    public async Task Search_EmptyFilters_AreTreatedAsAbsent()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery("   ", Guid.Empty, Guid.Empty, null, "  ", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.LastSearchFilter);
        Assert.Null(repository.LastSearchFilter!.City);
        Assert.Null(repository.LastSearchFilter.SpecializationId);
        Assert.Null(repository.LastSearchFilter.ServiceId);
        Assert.Null(repository.LastSearchFilter.Cursor);
        Assert.Equal(Pagination.DEFAULT_PAGE_SIZE + 1, repository.LastSearchFilter.Limit);
    }

    [Fact]
    public async Task Search_FirstPage_ReturnsCursorForLastReturnedItem()
    {
        var repository = new FakeBarberProfileRepository();
        AddOrderedItems(repository, 3);
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, null, 2),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.NotNull(result.Value.NextCursor);

        var cursor = BarberSearchCursor.Decode(result.Value.NextCursor!);
        Assert.True(cursor.IsSuccess);
        Assert.Equal(result.Value.Items[1].Rating, cursor.Value.Rating);
        Assert.Equal(result.Value.Items[1].Id, cursor.Value.Id);
    }

    [Fact]
    public async Task Search_AcrossPages_HasNoDuplicatesAndNoGaps()
    {
        var repository = new FakeBarberProfileRepository();
        var items = AddOrderedItems(repository, 5);
        var handler = new SearchBarbersHandler(repository);

        var collected = new List<BarberSearchItemResponse>();
        string? cursor = null;

        do
        {
            var page = await handler.Handle(
                new SearchBarbersQuery(null, null, null, null, cursor, 2),
                CancellationToken.None);

            Assert.True(page.IsSuccess);
            Assert.True(page.Value.Items.Count <= 2);
            collected.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(items.Select(item => item.Id), collected.Select(item => item.Id));
        Assert.Equal(collected.Count, collected.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public async Task Search_WithRatingTie_OrdersByIdAscendingAcrossPageBoundary()
    {
        var repository = new FakeBarberProfileRepository();
        AddItem(repository, 3, 4.00m);
        AddItem(repository, 4, 4.00m);
        var handler = new SearchBarbersHandler(repository);

        var first = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, null, 1),
            CancellationToken.None);
        var second = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, first.Value.NextCursor, 1),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(first.Value.Items[0].Id.CompareTo(second.Value.Items[0].Id) < 0);
    }

    [Fact]
    public async Task Search_WithNullRatings_PutsThemLastAndPaginatesInsideGroup()
    {
        var repository = new FakeBarberProfileRepository();
        AddItem(repository, 1, null);
        AddItem(repository, 2, 5.00m);
        AddItem(repository, 3, null);
        var handler = new SearchBarbersHandler(repository);

        var first = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, null, 2),
            CancellationToken.None);
        var second = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, first.Value.NextCursor, 2),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(5.00m, first.Value.Items[0].Rating);
        Assert.Null(first.Value.Items[1].Rating);
        Assert.True(second.IsSuccess);
        Assert.Single(second.Value.Items);
        Assert.Null(second.Value.Items[0].Rating);
        Assert.Null(second.Value.NextCursor);
    }

    [Fact]
    public async Task Search_WithBrokenCursor_ReturnsValidationNotServerError()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, "broken", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Null(repository.LastSearchFilter);
    }

    [Fact]
    public async Task Search_WithPageSizeOutsideBounds_ReturnsValidation()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var tooSmall = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, null, 0),
            CancellationToken.None);
        var tooLarge = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, null, Pagination.MAX_PAGE_SIZE + 1),
            CancellationToken.None);

        Assert.True(tooSmall.IsFailure);
        Assert.True(tooLarge.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, tooSmall.Error.Errors[0].Type);
        Assert.Equal(ErrorType.VALIDATION, tooLarge.Error.Errors[0].Type);
        Assert.Null(repository.LastSearchFilter);
    }

    private static List<BarberSearchItemResponse> AddOrderedItems(
        FakeBarberProfileRepository repository,
        int count)
    {
        var items = new List<BarberSearchItemResponse>();

        for (var index = 0; index < count; index++)
        {
            var item = AddItem(repository, index + 1, 5.00m - index);
            items.Add(item);
        }

        return items;
    }

    private static BarberSearchItemResponse AddItem(
        FakeBarberProfileRepository repository,
        int idSuffix,
        decimal? itemRating)
    {
        var item = new BarberSearchItemResponse(
            Guid.Parse($"00000000-0000-0000-0000-{idSuffix:D12}"),
            $"Barber {idSuffix}",
            Grade.MiddleBarber,
            5,
            itemRating,
            itemRating is null ? 0 : 1,
            true,
            null,
            null,
            null);

        repository.AddSearchItem(item);

        return item;
    }
}
