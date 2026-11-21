using BarberSearch.Mobile.Core.Formatting;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки дописывания кода страны в поле телефона: национальная запись получает
/// <c>+7</c>, локальная восьмёрка становится кодом страны, а уже начатый номер не
/// переформатируется.
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
}
