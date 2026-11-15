using BarberSearch.Application.Common;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Keyset-курсоры: round-trip, null-рейтинг и отказ на битой строке.
/// </summary>
public class CursorTests
{
    [Fact]
    public void BarberSearchCursor_RoundTripsRatingAndId()
    {
        var id = Guid.CreateVersion7();
        var cursor = new BarberSearchCursor(4.25m, id);

        var encoded = cursor.Encode();
        var decoded = BarberSearchCursor.Decode(encoded);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(4.25m, decoded.Value.Rating);
        Assert.Equal(id, decoded.Value.Id);
        Assert.DoesNotContain('=', encoded);
        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
    }

    [Fact]
    public void BarberSearchCursor_RoundTripsNullRating()
    {
        var id = Guid.CreateVersion7();
        var cursor = new BarberSearchCursor(null, id);

        var decoded = BarberSearchCursor.Decode(cursor.Encode());

        Assert.True(decoded.IsSuccess);
        Assert.Null(decoded.Value.Rating);
        Assert.Equal(id, decoded.Value.Id);
    }

    [Fact]
    public void BarberSearchCursor_InvalidBase64_ReturnsValidationFailure()
    {
        var decoded = BarberSearchCursor.Decode("not-a-cursor");

        Assert.True(decoded.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, decoded.Error.Errors[0].Type);
        Assert.Equal("cursor", decoded.Error.Errors[0].InvalidField);
    }

    [Fact]
    public void BarberSearchCursor_UnknownVersion_ReturnsValidationFailure()
    {
        var payload = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"2|4.5|{Guid.CreateVersion7():D}"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var decoded = BarberSearchCursor.Decode(payload);

        Assert.True(decoded.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, decoded.Error.Errors[0].Type);
    }

    [Fact]
    public void BarberSearchCursor_RatingOutsideDomainRange_ReturnsValidationFailure()
    {
        var payload = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"1|99|{Guid.CreateVersion7():D}"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var decoded = BarberSearchCursor.Decode(payload);

        Assert.True(decoded.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, decoded.Error.Errors[0].Type);
    }

    [Fact]
    public void BarberSearchCursor_EmptyId_ReturnsValidationFailure()
    {
        var payload = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes("1|4.5|00000000-0000-0000-0000-000000000000"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var decoded = BarberSearchCursor.Decode(payload);

        Assert.True(decoded.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, decoded.Error.Errors[0].Type);
    }

    [Fact]
    public void BarberSearchCursor_EncodesVersionTwoWithFourParts()
    {
        var cursor = new BarberSearchCursor(4.25m, Guid.CreateVersion7(), 3.5);

        var parts = DecodePayload(cursor.Encode());

        // Версия 2: version|rating|id|distance. Старый формат отверг бы distance.
        Assert.Equal(4, parts.Length);
        Assert.Equal("2", parts[0]);
        Assert.Equal("4.25", parts[1]);
        Assert.Equal("3.5", parts[3]);
    }

    [Fact]
    public void BarberSearchCursor_RoundTripsDistanceInGeoSearch()
    {
        var id = Guid.CreateVersion7();
        var cursor = new BarberSearchCursor(4.25m, id, 12.75);

        var decoded = BarberSearchCursor.Decode(cursor.Encode(), isGeoSearch: true);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(4.25m, decoded.Value.Rating);
        Assert.Equal(id, decoded.Value.Id);
        Assert.Equal(12.75, decoded.Value.DistanceKm);
    }

    [Fact]
    public void BarberSearchCursor_RoundTripsNullDistanceInRatingSearch()
    {
        var id = Guid.CreateVersion7();
        var cursor = new BarberSearchCursor(null, id);

        var decoded = BarberSearchCursor.Decode(cursor.Encode());

        Assert.True(decoded.IsSuccess);
        Assert.Null(decoded.Value.Rating);
        Assert.Null(decoded.Value.DistanceKm);
        Assert.Equal(id, decoded.Value.Id);
    }

    [Fact]
    public void BarberSearchCursor_NullDistanceInGeoSearch_ContinuesTheNoSalonGroup()
    {
        var id = Guid.CreateVersion7();
        var cursor = new BarberSearchCursor(null, id, DistanceKm: null);

        // Пустая distance — законная позиция в гео-выдаче: группа «без активного салона»
        // идёт последней, и обработчик выдаёт для неё именно такой курсор.
        var decoded = BarberSearchCursor.Decode(cursor.Encode(), isGeoSearch: true);

        Assert.True(decoded.IsSuccess);
        Assert.Null(decoded.Value.DistanceKm);
        Assert.Equal(id, decoded.Value.Id);
    }

    [Fact]
    public void BarberSearchCursor_DecodesVersionOneCursorInRatingSearch()
    {
        var id = Guid.CreateVersion7();
        var payload = EncodePayload($"1|4.5|{id:D}");

        var decoded = BarberSearchCursor.Decode(payload);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(4.5m, decoded.Value.Rating);
        Assert.Equal(id, decoded.Value.Id);
        Assert.Null(decoded.Value.DistanceKm);
    }

    [Fact]
    public void BarberSearchCursor_VersionOneCursorInGeoSearch_ReturnsValidationFailure()
    {
        var payload = EncodePayload($"1|4.5|{Guid.CreateVersion7():D}");

        var decoded = BarberSearchCursor.Decode(payload, isGeoSearch: true);

        Assert.True(decoded.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, decoded.Error.Errors[0].Type);
        Assert.Equal("barberSearch.cursor.invalid", decoded.Error.Errors[0].Code);
    }

    [Fact]
    public void BarberSearchCursor_DistanceInRatingSearch_ReturnsValidationFailure()
    {
        var cursor = new BarberSearchCursor(4.5m, Guid.CreateVersion7(), 3.25);

        var decoded = BarberSearchCursor.Decode(cursor.Encode());

        Assert.True(decoded.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, decoded.Error.Errors[0].Type);
        Assert.Equal("barberSearch.cursor.invalid", decoded.Error.Errors[0].Code);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("not-a-number")]
    public void BarberSearchCursor_InvalidDistance_ReturnsValidationFailure(string distance)
    {
        var payload = EncodePayload($"2|4.5|{Guid.CreateVersion7():D}|{distance}");

        var decoded = BarberSearchCursor.Decode(payload, isGeoSearch: true);

        Assert.True(decoded.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, decoded.Error.Errors[0].Type);
        Assert.Equal("cursor", decoded.Error.Errors[0].InvalidField);
    }

    /// <summary>
    /// Кодирует произвольную полезную нагрузку так же, как <c>CursorCodec</c>: base64url
    /// без завершающих <c>=</c>. Нужно для курсоров, которые новый код уже не пишет.
    /// </summary>
    private static string EncodePayload(string payload)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>
    /// Разбирает полезную нагрузку курсора обратно в части.
    /// </summary>
    private static string[] DecodePayload(string cursor)
    {
        var normalized = cursor.Replace('-', '+').Replace('_', '/');
        var remainder = normalized.Length % 4;

        if (remainder != 0)
        {
            normalized += new string('=', 4 - remainder);
        }

        return System.Text.Encoding.UTF8
            .GetString(Convert.FromBase64String(normalized))
            .Split('|');
    }

    [Fact]
    public void Pagination_PageSizeWithinBounds_ReturnsNull()
    {
        Assert.Null(Pagination.ValidatePageSize(1, "test"));
        Assert.Null(Pagination.ValidatePageSize(Pagination.MAX_PAGE_SIZE, "test"));
    }

    [Fact]
    public void Pagination_PageSizeOutsideBounds_ReturnsValidationFailure()
    {
        var tooSmall = Pagination.ValidatePageSize(0, "test");
        var tooLarge = Pagination.ValidatePageSize(Pagination.MAX_PAGE_SIZE + 1, "test");

        Assert.NotNull(tooSmall);
        Assert.NotNull(tooLarge);
        Assert.Equal(ErrorType.VALIDATION, tooSmall!.Errors[0].Type);
        Assert.Equal(ErrorType.VALIDATION, tooLarge!.Errors[0].Type);
    }
}
