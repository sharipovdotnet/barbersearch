using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки дописывания кода страны в поле телефона: национальная запись получает
/// <c>+7</c>, локальная восьмёрка становится кодом страны, а уже начатый номер не
/// переформатируется. Нормализация поля на телефоне дополнительно оставляет скелет
/// <c>+79</c>, отбрасывает разделители и пересчитывает курсор.
/// </summary>
public sealed class PhoneInputTests
{
    [Theory]
    [InlineData("9991234567", "+79991234567")]
    [InlineData("999 123-45-67", "+7999 123-45-67")]
    [InlineData("8 (999) 123-45-67", "+7 (999) 123-45-67")]
    [InlineData("89991234567", "+79991234567")]
    [InlineData("7 999 123-45-67", "+7 999 123-45-67")]
    [InlineData("79991234567", "+79991234567")]
    [InlineData("(999) 123-45-67", "+7(999) 123-45-67")]
    public void EnsureCountryCode_NationalNumber_AddsRussianCode(string input, string expected)
        => Assert.Equal(expected, PhoneInput.EnsureCountryCode(input));

    [Theory]
    [InlineData("+7 999 123-45-67")]
    [InlineData("+79991234567")]
    [InlineData("+7")]
    public void EnsureCountryCode_NumberWithCode_KeepsInput(string input)
        => Assert.Equal(input, PhoneInput.EnsureCountryCode(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureCountryCode_EmptyValue_StaysEmpty(string input)
        => Assert.Equal(input, PhoneInput.EnsureCountryCode(input));

    [Fact]
    public void EnsureCountryCode_RepeatedCall_IsIdempotent()
    {
        var once = PhoneInput.EnsureCountryCode("9991234567");

        Assert.Equal(once, PhoneInput.EnsureCountryCode(once));
    }

    [Fact]
    public void EnsureCountryCode_LeadingSpaces_AreDroppedBeforePrefix()
        => Assert.Equal("+79991234567", PhoneInput.EnsureCountryCode("   9991234567"));

    [Theory]
    [InlineData("", 0, "+79", 3)]
    [InlineData("+7", 2, "+79", 3)]
    [InlineData("9991234567", 10, "+79991234567", 12)]
    [InlineData("89991234567", 11, "+79991234567", 12)]
    [InlineData("8 (999) 123-45-67", 18, "+79991234567", 12)]
    [InlineData("+7 (999) 123-45-67", 18, "+79991234567", 12)]
    [InlineData("+799", 4, "+799", 4)]
    [InlineData("+79a", 4, "+79", 3)]
    [InlineData("+799912345678", 13, "+79991234567", 12)]
    [InlineData("   ", 3, "+79", 3)]
    public void NormalizeMobile_Value_ReturnsTextAndCaret(
        string input,
        int caret,
        string expectedText,
        int expectedCaret)
    {
        var edit = PhoneInput.NormalizeMobile(input, caret);

        Assert.Equal(new PhoneInputEdit(expectedText, expectedCaret), edit);
    }

    [Fact]
    public void NormalizeMobile_NullValue_ReturnsPrefixAndCaret()
        => Assert.Equal(new PhoneInputEdit("+79", 3), PhoneInput.NormalizeMobile(null, 0));

    [Theory]
    [InlineData("")]
    [InlineData("+7")]
    [InlineData("9991234567")]
    [InlineData("89991234567")]
    [InlineData("8 (999) 123-45-67")]
    [InlineData("+7 (999) 123-45-67")]
    [InlineData("+799")]
    [InlineData("+79a")]
    [InlineData("+799912345678")]
    [InlineData("   ")]
    public void NormalizeMobile_RepeatedCall_IsIdempotent(string input)
    {
        var edit = PhoneInput.NormalizeMobile(input, input.Length);
        var again = PhoneInput.NormalizeMobile(edit.Text, edit.CaretPosition);

        Assert.Equal(edit, again);
    }

    [Theory]
    [InlineData("+79991234567")]
    [InlineData("9991234567")]
    [InlineData("89991234567")]
    [InlineData("+7 (999) 123-45-67")]
    [InlineData("+799")]
    public void HasNationalDigits_NumberWithNationalPart_ReturnsTrue(string input)
        => Assert.True(PhoneInput.HasNationalDigits(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+79")]
    [InlineData("+7")]
    [InlineData("7")]
    [InlineData("8")]
    [InlineData("9")]
    public void HasNationalDigits_PrefixOnly_ReturnsFalse(string? input)
        => Assert.False(PhoneInput.HasNationalDigits(input));
}
