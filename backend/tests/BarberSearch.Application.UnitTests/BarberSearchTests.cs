using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Application.Common;
using BarberSearch.Application.UnitTests.Fakes;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
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
            new SearchBarbersQuery(null, null, null, null, null, null, null),
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
        var hairServiceId = Guid.CreateVersion7();
        var date = new DateOnly(2026, 12, 1);

        var result = await handler.Handle(
            new SearchBarbersQuery(" Berlin ", specializationId, hairServiceId, date, null, null, 5),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.LastSearchFilter);
        Assert.Equal("Berlin", repository.LastSearchFilter!.City);
        Assert.Equal(specializationId, repository.LastSearchFilter.SpecializationId);
        Assert.Equal(hairServiceId, repository.LastSearchFilter.HairServiceId);
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
            new SearchBarbersQuery("   ", Guid.Empty, Guid.Empty, null, null, "  ", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.LastSearchFilter);
        Assert.Null(repository.LastSearchFilter!.City);
        Assert.Null(repository.LastSearchFilter.SpecializationId);
        Assert.Null(repository.LastSearchFilter.HairServiceId);
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
            new SearchBarbersQuery(null, null, null, null, null, null, 2),
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
                new SearchBarbersQuery(null, null, null, null, null, cursor, 2),
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
            new SearchBarbersQuery(null, null, null, null, null, null, 1),
            CancellationToken.None);
        var second = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, null, first.Value.NextCursor, 1),
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
            new SearchBarbersQuery(null, null, null, null, null, null, 2),
            CancellationToken.None);
        var second = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, null, first.Value.NextCursor, 2),
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
            new SearchBarbersQuery(null, null, null, null, null, "broken", null),
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
            new SearchBarbersQuery(null, null, null, null, null, null, 0),
            CancellationToken.None);
        var tooLarge = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, null, null, Pagination.MAX_PAGE_SIZE + 1),
            CancellationToken.None);

        Assert.True(tooSmall.IsFailure);
        Assert.True(tooLarge.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, tooSmall.Error.Errors[0].Type);
        Assert.Equal(ErrorType.VALIDATION, tooLarge.Error.Errors[0].Type);
        Assert.Null(repository.LastSearchFilter);
    }

    [Theory]
    [InlineData("ru")]
    [InlineData(" RU ")]
    [InlineData("En")]
    public async Task Search_WithLanguage_ParsesEnumIgnoringCaseAndTrims(string language)
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, language, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.LastSearchFilter);
        Assert.Equal(
            Enum.Parse<Language>(language.Trim(), ignoreCase: true),
            repository.LastSearchFilter!.Language);
    }

    [Fact]
    public async Task Search_ForwardsAdditionalServiceAndTrimsIt()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "  бород  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("бород", repository.LastSearchFilter!.AdditionalService);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_WithBlankAdditionalService_DoesNotFilter(string? additionalService)
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                additionalService),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(repository.LastSearchFilter!.AdditionalService);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_WithBlankLanguage_DoesNotFilter(string? language)
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, language, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.LastSearchFilter);
        Assert.Null(repository.LastSearchFilter!.Language);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("xx")]
    [InlineData("Ru,En")]
    public async Task Search_WithUnknownLanguage_ReturnsValidationNotServerError(string language)
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, language, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.Common.BarberSearchLanguageInvalid,
            result.Error.Errors[0].Code);
        Assert.Null(repository.LastSearchFilter);
    }

    [Fact]
    public async Task Search_WithoutCoordinates_PassesNullLocation()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.LastSearchFilter);
        Assert.Null(repository.LastSearchFilter!.Location);
    }

    [Fact]
    public async Task Search_WithCoordinates_PassesLocationToRepository()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                Latitude: 55.7558,
                Longitude: 37.6173),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.LastSearchFilter!.Location);
        Assert.Equal(55.7558, repository.LastSearchFilter.Location!.Latitude);
        Assert.Equal(37.6173, repository.LastSearchFilter.Location.Longitude);
    }

    [Fact]
    public async Task Search_WithOnlyLatitude_ReturnsLocationValidation()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                Latitude: 55.7558),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.Common.BarberSearchLocationInvalid,
            result.Error.Errors[0].Code);
        Assert.Equal("longitude", result.Error.Errors[0].InvalidField);
        Assert.Null(repository.LastSearchFilter);
    }

    [Fact]
    public async Task Search_WithOnlyLongitude_ReturnsLocationValidation()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                Longitude: 37.6173),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.Common.BarberSearchLocationInvalid,
            result.Error.Errors[0].Code);
        Assert.Equal("latitude", result.Error.Errors[0].InvalidField);
        Assert.Null(repository.LastSearchFilter);
    }

    [Theory]
    [InlineData(90.0001, 37.6173, "latitude")]
    [InlineData(-90.0001, 37.6173, "latitude")]
    [InlineData(55.7558, 180.0001, "longitude")]
    [InlineData(55.7558, -180.0001, "longitude")]
    [InlineData(double.NaN, 37.6173, "latitude")]
    [InlineData(double.PositiveInfinity, 37.6173, "latitude")]
    [InlineData(55.7558, double.NaN, "longitude")]
    [InlineData(55.7558, double.NegativeInfinity, "longitude")]
    public async Task Search_WithCoordinatesOutsideRangeOrNonFinite_ReturnsLocationValidation(
        double latitude,
        double longitude,
        string invalidField)
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                latitude,
                longitude),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal(
            ApiErrorCodes.Common.BarberSearchLocationInvalid,
            result.Error.Errors[0].Code);
        Assert.Equal(invalidField, result.Error.Errors[0].InvalidField);
        Assert.Null(repository.LastSearchFilter);
    }

    [Fact]
    public async Task Search_WithGeoLocation_BuildsCursorFromDistance()
    {
        var repository = new FakeBarberProfileRepository();
        var nearest = AddItem(repository, 1, 4.00m, distanceKm: 1.5);
        AddItem(repository, 2, 5.00m, distanceKm: 9.5);
        var handler = new SearchBarbersHandler(repository);

        var result = await handler.Handle(
            new SearchBarbersQuery(
                null,
                null,
                null,
                null,
                null,
                null,
                1,
                null,
                Latitude: 55.7558,
                Longitude: 37.6173),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal(nearest.Id, result.Value.Items[0].Id);
        Assert.NotNull(result.Value.NextCursor);

        var cursor = BarberSearchCursor.Decode(result.Value.NextCursor!, isGeoSearch: true);
        Assert.True(cursor.IsSuccess);
        Assert.Equal(nearest.Id, cursor.Value.Id);
        Assert.Equal(nearest.DistanceKm, cursor.Value.DistanceKm);
    }

    [Fact]
    public async Task Search_WithGeoLocation_AcrossPages_HasNoDuplicatesAndNoGaps()
    {
        var repository = new FakeBarberProfileRepository();
        AddItem(repository, 1, 5.00m, distanceKm: 0.4);
        AddItem(repository, 2, 4.00m, distanceKm: 2.2);
        AddItem(repository, 3, 3.00m, distanceKm: 7.9);
        var handler = new SearchBarbersHandler(repository);

        var collected = new List<BarberSearchItemResponse>();
        string? cursor = null;

        do
        {
            var page = await handler.Handle(
                new SearchBarbersQuery(
                    null,
                    null,
                    null,
                    null,
                    null,
                    cursor,
                    2,
                    null,
                    Latitude: 55.7558,
                    Longitude: 37.6173),
                CancellationToken.None);

            Assert.True(page.IsSuccess);
            collected.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(3, collected.Count);
        Assert.Equal(
            collected.Select(item => item.DistanceKm),
            collected.Select(item => item.DistanceKm).OrderBy(distance => distance));
        Assert.Equal(collected.Count, collected.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public async Task Search_WithGeoLocation_PaginatesThroughTheNoSalonGroup()
    {
        var repository = new FakeBarberProfileRepository();
        AddItem(repository, 1, 5.00m, distanceKm: 1.0);
        AddItem(repository, 2, 5.00m, distanceKm: null);
        AddItem(repository, 3, 5.00m, distanceKm: null);
        var handler = new SearchBarbersHandler(repository);

        var first = await handler.Handle(
            new SearchBarbersQuery(
                null,
                null,
                null,
                null,
                null,
                null,
                1,
                null,
                Latitude: 55.7558,
                Longitude: 37.6173),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(1.0, first.Value.Items[0].DistanceKm);
        Assert.NotNull(first.Value.NextCursor);

        var second = await handler.Handle(
            new SearchBarbersQuery(
                null,
                null,
                null,
                null,
                null,
                first.Value.NextCursor,
                1,
                null,
                Latitude: 55.7558,
                Longitude: 37.6173),
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Single(second.Value.Items);
        Assert.Null(second.Value.Items[0].DistanceKm);
        Assert.NotNull(second.Value.NextCursor);

        // Курсор группы «без активного салона» несёт пустое расстояние: без этого
        // следующая страница отвергла бы собственный курсор как чужой порядок.
        var third = await handler.Handle(
            new SearchBarbersQuery(
                null,
                null,
                null,
                null,
                null,
                second.Value.NextCursor,
                1,
                null,
                Latitude: 55.7558,
                Longitude: 37.6173),
            CancellationToken.None);

        Assert.True(third.IsSuccess);
        Assert.Single(third.Value.Items);
        Assert.Null(third.Value.Items[0].DistanceKm);
        Assert.Null(third.Value.NextCursor);
        Assert.Equal(3, first.Value.Items.Count + second.Value.Items.Count + third.Value.Items.Count);
    }

    [Fact]
    public async Task Search_WithGeoLocationAndRatingCursor_ReturnsCursorValidation()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        // Курсор старого формата: в нём нет расстояния, значит, он из рейтинговой выдачи.
        var oldCursor = EncodeCursorPayload($"1|4.5|{Guid.CreateVersion7():D}");

        var result = await handler.Handle(
            new SearchBarbersQuery(
                null,
                null,
                null,
                null,
                null,
                oldCursor,
                null,
                null,
                Latitude: 55.7558,
                Longitude: 37.6173),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("barberSearch.cursor.invalid", result.Error.Errors[0].Code);
        Assert.Equal("cursor", result.Error.Errors[0].InvalidField);
        Assert.Null(repository.LastSearchFilter);
    }

    [Fact]
    public async Task Search_WithoutLocationAndDistanceCursor_ReturnsCursorValidation()
    {
        var repository = new FakeBarberProfileRepository();
        var handler = new SearchBarbersHandler(repository);

        // Курсор с расстоянием принадлежит гео-порядку: продолжать по нему рейтинговую
        // выдачу нельзя, иначе страницы поедут.
        var geoCursor = new BarberSearchCursor(4.5m, Guid.CreateVersion7(), 3.25).Encode();

        var result = await handler.Handle(
            new SearchBarbersQuery(null, null, null, null, null, geoCursor, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Errors[0].Type);
        Assert.Equal("barberSearch.cursor.invalid", result.Error.Errors[0].Code);
        Assert.Null(repository.LastSearchFilter);
    }

    /// <summary>
    /// Кодирует произвольную полезную нагрузку курсора так же, как <c>CursorCodec</c>:
    /// base64url без завершающих символов <c>=</c>. Нужно, чтобы подсунуть обработчику
    /// курсор старого формата версии 1.
    /// </summary>
    private static string EncodeCursorPayload(string payload)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

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
        decimal? itemRating,
        double? distanceKm = null)
    {
        var item = new BarberSearchItemResponse(
            Guid.Parse($"00000000-0000-0000-0000-{idSuffix:D12}"),
            $"Barber {idSuffix}",
            Grade.MiddleBarber,
            5,
            [Language.Ru],
            itemRating,
            itemRating is null ? 0 : 1,
            true,
            null,
            null,
            null,
            null,
            [],
            null,
            distanceKm);

        repository.AddSearchItem(item);

        return item;
    }
}
