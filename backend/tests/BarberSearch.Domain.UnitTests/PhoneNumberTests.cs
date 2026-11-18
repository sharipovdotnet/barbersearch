using BarberSearch.Domain.Users.ValueObjects;
using Shared;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Телефон как объект-значение: канонизация привычных записей, отказ на чужие коды и
/// городские номера, необязательность в <see cref="PhoneNumber.Validate"/> и равенство по
/// каноническому значению.
/// </summary>
/// <remarks>
/// Канонический вид <c>+7XXXXXXXXXX</c> — часть контракта хранения: в агрегате
/// <c>User</c> лежит только он, поэтому два разных написания одного номера обязаны быть
/// равны, а любой недопустимый ввод — отклонён с кодом вызывающего сценария.
/// </remarks>
public class PhoneNumberTests
{
    private const string CANONICAL = "+79991234567";

    private const string CALLER_CODE = "user.updateProfile.phoneNumber";

    private const string FIELD = "phoneNumber";

    [Theory]
    [InlineData("+7 999 123-45-67")]
    [InlineData("8 (999) 123-45-67")]
    [InlineData("7-999-123-45-67")]
    [InlineData("9991234567")]
    [InlineData(" +7(999)1234567 ")]
    public void Create_WithSupportedSpelling_ReturnsCanonicalValue(string input)
    {
        var result = PhoneNumber.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(CANONICAL, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+49 170 1234567")]
    [InlineData("+375 29 123-45-67")]
    [InlineData("+7 495 123-45-67")]
    [InlineData("+7 999 123-45-6")]
    [InlineData("+7 999 123-45-678")]
    [InlineData("abc")]
    [InlineData("+7 999 123+45-67")]
    [InlineData("8 999 123 45 67 89")]
    public void Create_WithInvalidValue_ReturnsDefaultErrorCode(string? input)
    {
        var result = PhoneNumber.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal(PhoneNumber.DEFAULT_ERROR_CODE, result.Error.Code);
    }

    [Fact]
    public void Create_WithInvalidValue_UsesCallerCodeAndField()
    {
        var result = PhoneNumber.Create("+49 170 1234567", CALLER_CODE, FIELD);

        Assert.True(result.IsFailure);
        Assert.Equal(CALLER_CODE, result.Error.Code);
        Assert.Equal(FIELD, result.Error.InvalidField);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Type);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithNoPhone_ReturnsNoErrorAndNullValue(string? input)
    {
        var error = PhoneNumber.Validate(input, CALLER_CODE, FIELD, out var phoneNumber);

        Assert.Null(error);
        Assert.Null(phoneNumber);
    }

    [Fact]
    public void Validate_WithSupportedSpelling_ReturnsCanonicalPhone()
    {
        var error = PhoneNumber.Validate("8 (999) 123-45-67", CALLER_CODE, FIELD, out var phoneNumber);

        Assert.Null(error);
        Assert.Equal(CANONICAL, phoneNumber?.Value);
    }

    [Fact]
    public void Validate_WithInvalidValue_ReturnsCallerCodeAndNullValue()
    {
        var error = PhoneNumber.Validate("+7 495 123-45-67", CALLER_CODE, FIELD, out var phoneNumber);

        Assert.NotNull(error);
        Assert.Equal(CALLER_CODE, error.Code);
        Assert.Equal(FIELD, error.InvalidField);
        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Null(phoneNumber);
    }

    [Theory]
    [InlineData("+7 999 123-45-67")]
    [InlineData("8 (999) 123-45-67")]
    [InlineData("79991234567")]
    [InlineData("9991234567")]
    public void IsValid_WithRussianMobile_ReturnsTrue(string input)
    {
        Assert.True(PhoneNumber.IsValid(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+49 170 1234567")]
    [InlineData("+375 29 123-45-67")]
    [InlineData("+7 495 123-45-67")]
    [InlineData("+7 812 123-45-67")]
    [InlineData("+7 999 123-45-6")]
    [InlineData("+8 999 123-45-67")]
    [InlineData("abc")]
    [InlineData("+7 999 123+45-67")]
    public void IsValid_WithInvalidValue_ReturnsFalse(string? input)
    {
        Assert.False(PhoneNumber.IsValid(input));
    }

    [Fact]
    public void Equality_DifferentSpellingsOfSameNumber_AreEqual()
    {
        var formatted = PhoneNumber.Create("+7 999 123-45-67").Value;
        var local = PhoneNumber.Create("8 (999) 123-45-67").Value;
        var bare = PhoneNumber.Create("9991234567").Value;

        Assert.Equal(formatted, local);
        Assert.Equal(local, bare);
        Assert.True(formatted == local);
        Assert.False(formatted != local);
        Assert.Equal(formatted.GetHashCode(), local.GetHashCode());
        Assert.Equal(formatted.GetHashCode(), bare.GetHashCode());
    }

    [Fact]
    public void Equality_WithItselfAndNull_BehavesAsReferenceValue()
    {
        var phone = PhoneNumber.Create(CANONICAL).Value;

        Assert.True(phone.Equals(phone));
        Assert.False(phone.Equals((PhoneNumber?)null));
        Assert.False(phone.Equals("+79991234567"));
        Assert.False(phone == null);
        Assert.True(phone != null);
        Assert.True((PhoneNumber?)null == null);
    }

    [Fact]
    public void ToString_ReturnsCanonicalValue()
    {
        var phone = PhoneNumber.Create("8 (999) 123-45-67").Value;

        Assert.Equal(CANONICAL, phone.ToString());
        Assert.Equal(CANONICAL, phone.Value);
        Assert.Equal(CANONICAL, $"{phone}");
    }

    [Fact]
    public void Constants_MatchCanonicalContract()
    {
        Assert.Equal("+7", PhoneNumber.PREFIX);
        Assert.Equal(12, PhoneNumber.VALUE_LENGTH);
        Assert.Equal(32, PhoneNumber.MAX_INPUT_LENGTH);
        Assert.Equal(PhoneNumber.VALUE_LENGTH, CANONICAL.Length);
    }
}
