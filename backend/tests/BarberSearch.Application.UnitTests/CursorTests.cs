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
