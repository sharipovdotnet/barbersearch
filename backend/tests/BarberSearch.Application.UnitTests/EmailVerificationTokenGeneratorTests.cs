using BarberSearch.Application.EmailVerifications;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Генератор одноразового токена подтверждения email: длина, base64url-алфавит и SHA-256
/// в нижнем регистре.
/// </summary>
/// <remarks>
/// Формат проверяется до хеширования: мусорная строка не должна тратить SHA-256 и не должна
/// доходить до запроса по хешу. Открытый токен в тестах никуда не сохраняется.
/// </remarks>
public class EmailVerificationTokenGeneratorTests
{
    [Fact]
    public void Generate_ReturnsFortyThreeUrlSafeCharacters()
    {
        var token = EmailVerificationTokenGenerator.Generate();

        Assert.Equal(43, token.Length);
        Assert.True(EmailVerificationTokenGenerator.IsValidToken(token));
        Assert.All(token, character => Assert.True(
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_',
            $"Unexpected base64url character '{character}'."));
    }

    [Fact]
    public void Generate_ProducesDifferentTokens()
    {
        var tokens = Enumerable
            .Range(0, 32)
            .Select(_ => EmailVerificationTokenGenerator.Generate())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(32, tokens.Count);
    }

    [Fact]
    public void ComputeHash_ReturnsSixtyFourLowerCaseHexCharacters()
    {
        var token = EmailVerificationTokenGenerator.Generate();

        var hash = EmailVerificationTokenGenerator.ComputeHash(token);

        Assert.Equal(64, hash.Length);
        Assert.All(hash, character => Assert.True(char.IsAsciiHexDigitLower(character)));
        Assert.Equal(hash, hash.ToLowerInvariant());
    }

    [Fact]
    public void ComputeHash_IsStableForTheSameToken()
    {
        var token = EmailVerificationTokenGenerator.Generate();

        Assert.Equal(
            EmailVerificationTokenGenerator.ComputeHash(token),
            EmailVerificationTokenGenerator.ComputeHash(token));
    }

    [Fact]
    public void ComputeHash_MatchesKnownSha256Vector()
    {
        // SHA-256("abc") — известный вектор: хеш считается по UTF-8 открытого токена.
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            EmailVerificationTokenGenerator.ComputeHash("abc"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc def")]
    [InlineData("abc+def")]
    [InlineData("abc/def")]
    [InlineData("abc=")]
    [InlineData("токен")]
    public void IsValidToken_RejectsForeignCharactersAndEmptyInput(string? token)
    {
        Assert.False(EmailVerificationTokenGenerator.IsValidToken(token));
    }

    [Fact]
    public void IsValidToken_RejectsMoreThanOneHundredTwentyEightCharacters()
    {
        Assert.True(EmailVerificationTokenGenerator.IsValidToken(new string('a', 128)));
        Assert.False(EmailVerificationTokenGenerator.IsValidToken(new string('a', 129)));
    }

    [Fact]
    public void ComputeHash_WithMalformedToken_ThrowsWithoutHashing()
    {
        Assert.Throws<ArgumentException>(
            () => EmailVerificationTokenGenerator.ComputeHash("not a token"));
    }
}
