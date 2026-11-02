using System.Text;
using BarberSearch.Application.Bookings.Admin;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Курсор админского списка броней: round-trip, битая строка, старая версия формата,
/// пустой <see cref="Guid"/> и курсор чужого списка.
/// </summary>
public class AdminBookingCursorTests
{
    private static readonly DateTimeOffset StartAt =
        new(2026, 9, 12, 18, 30, 15, TimeSpan.Zero);

    [Fact]
    public void Encode_Decode_RoundTrips()
    {
        var cursor = new AdminBookingCursor(StartAt, Guid.CreateVersion7());

        var decoded = AdminBookingCursor.Decode(cursor.Encode());

        Assert.True(decoded.IsSuccess);
        Assert.Equal(cursor, decoded.Value);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData("!!!")]
    public void Decode_WithBrokenBase64_ReturnsValidation(string value)
    {
        var decoded = AdminBookingCursor.Decode(value);

        Assert.True(decoded.IsFailure);
        Assert.Equal("admin.bookings.cursor.invalid", decoded.Error.Errors[0].Code);
    }

    [Fact]
    public void Decode_WithOldFormatVersion_ReturnsValidation()
    {
        var encoded = EncodePayload(
            "0",
            "admin.bookings",
            StartAt.ToString("O"),
            Guid.CreateVersion7().ToString("D"));

        var decoded = AdminBookingCursor.Decode(encoded);

        Assert.True(decoded.IsFailure);
        Assert.Equal("admin.bookings.cursor.invalid", decoded.Error.Errors[0].Code);
    }

    [Fact]
    public void Decode_WithForeignScope_ReturnsValidation()
    {
        // Курсор журнала: та же форма (4 части), другая область.
        var encoded = EncodePayload(
            "1",
            "admin.audit",
            StartAt.ToString("O"),
            Guid.CreateVersion7().ToString("D"));

        var decoded = AdminBookingCursor.Decode(encoded);

        Assert.True(decoded.IsFailure);
        Assert.Equal("admin.bookings.cursor.invalid", decoded.Error.Errors[0].Code);
    }

    [Fact]
    public void Decode_WithEmptyGuid_ReturnsValidation()
    {
        var encoded = EncodePayload(
            "1",
            "admin.bookings",
            StartAt.ToString("O"),
            Guid.Empty.ToString("D"));

        var decoded = AdminBookingCursor.Decode(encoded);

        Assert.True(decoded.IsFailure);
        Assert.Equal("admin.bookings.cursor.invalid", decoded.Error.Errors[0].Code);
    }

    /// <summary>
    /// Кодирует payload так же, как общий <c>CursorCodec</c>: base64url без padding.
    /// Тест не может позвать внутренний кодек, поэтому повторяет формат явно — это и есть
    /// контракт, который курсор обязан отвергать.
    /// </summary>
    private static string EncodePayload(params string[] parts)
        => Convert
            .ToBase64String(Encoding.UTF8.GetBytes(string.Join('|', parts)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
