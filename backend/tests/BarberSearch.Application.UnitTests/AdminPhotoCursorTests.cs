using System.Text;
using BarberSearch.Application.Photos.Admin;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Курсор админских очередей фотографий: round-trip, битая строка, чужая версия формата,
/// пустой <see cref="Guid"/> и неверное число частей.
/// </summary>
public class AdminPhotoCursorTests
{
    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 11, 11, 9, 15, 30, TimeSpan.Zero);

    [Fact]
    public void Encode_Decode_RoundTrips()
    {
        var cursor = new AdminPhotoCursor(SubmittedAt, Guid.CreateVersion7());

        var decoded = AdminPhotoCursor.Decode(cursor.Encode());

        Assert.True(decoded.IsSuccess);
        Assert.Equal(cursor, decoded.Value);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData("!!!")]
    public void Decode_WithBrokenBase64_ReturnsValidation(string value)
    {
        var decoded = AdminPhotoCursor.Decode(value);

        Assert.True(decoded.IsFailure);
        Assert.Equal("admin.photos.cursor", decoded.Error.Errors[0].Code);
    }

    [Fact]
    public void Decode_WithOldFormatVersion_ReturnsValidation()
    {
        var encoded = EncodePayload(
            "0",
            SubmittedAt.ToString("O"),
            Guid.CreateVersion7().ToString("D"));

        var decoded = AdminPhotoCursor.Decode(encoded);

        Assert.True(decoded.IsFailure);
        Assert.Equal("admin.photos.cursor", decoded.Error.Errors[0].Code);
    }

    [Fact]
    public void Decode_WithEmptyGuid_ReturnsValidation()
    {
        var encoded = EncodePayload(
            "1",
            SubmittedAt.ToString("O"),
            Guid.Empty.ToString("D"));

        var decoded = AdminPhotoCursor.Decode(encoded);

        Assert.True(decoded.IsFailure);
        Assert.Equal("admin.photos.cursor", decoded.Error.Errors[0].Code);
    }

    [Fact]
    public void Decode_WithWrongPartCount_ReturnsValidation()
    {
        var encoded = EncodePayload("1", SubmittedAt.ToString("O"));

        var decoded = AdminPhotoCursor.Decode(encoded);

        Assert.True(decoded.IsFailure);
        Assert.Equal("admin.photos.cursor", decoded.Error.Errors[0].Code);
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
