using BarberSearch.Domain;
using BarberSearch.Domain.Users;
using BarberSearch.Domain.Users.ValueObjects;
using Shared;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Объект-значение адреса электронной почты: нормализация, синтаксис и код ошибки сценария.
/// </summary>
/// <remarks>
/// Адрес вынесен из <see cref="User"/> в <see cref="Email"/> и используется регистрацией,
/// правкой профиля и сменой адреса. Правила — <c>MailAddress.TryCreate</c>, обязательная
/// точка в домене и длина <c>3..254</c>; коды ошибок у сценариев разные, поэтому
/// <see cref="Email.Validate"/> принимает их аргументами. Хранится только канонический вид:
/// без пробелов по краям и в нижнем регистре.
/// </remarks>
public class EmailTests
{
    [Fact]
    public void Normalize_TrimsAndLowercases()
    {
        Assert.Equal("user@example.com", Email.Normalize("  User@Example.COM  "));
    }

    [Fact]
    public void Normalize_KeepsCanonicalAddressUnchanged()
    {
        Assert.Equal("user@example.com", Email.Normalize("user@example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_WithBlankInput_ReturnsEmptyString(string? email)
    {
        Assert.Equal(string.Empty, Email.Normalize(email));
    }

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("user.name+tag@sub.example.co.uk")]
    [InlineData("a@b.c")]
    [InlineData("user_name@example-domain.com")]
    public void IsValid_WithCanonicalAddress_ReturnsTrue(string email)
    {
        Assert.True(Email.IsValid(email));
    }

    [Fact]
    public void IsValid_AtMaximumEmailLength_ReturnsTrue()
    {
        // 254 символа — предел RFC 5321 и колонки varchar(254).
        var email = new string('a', 242) + "@example.com";

        Assert.Equal(254, email.Length);
        Assert.True(Email.IsValid(email));
    }

    [Fact]
    public void IsValid_AboveMaximumEmailLength_ReturnsFalse()
    {
        var email = new string('a', 243) + "@example.com";

        Assert.Equal(255, email.Length);
        Assert.False(Email.IsValid(email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a@")]
    [InlineData("@example.com")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a@localhost")]
    [InlineData("a@b..c")]
    [InlineData("a@example.com.")]
    [InlineData("Ivan <ivan@example.com>")]
    [InlineData("a@b@c")]
    [InlineData(" user@example.com")]
    [InlineData("user@example.com ")]
    public void IsValid_WithInvalidAddress_ReturnsFalse(string email)
    {
        Assert.False(Email.IsValid(email));
    }

    [Fact]
    public void Create_WithNonCanonicalAddress_ReturnsCanonicalValue()
    {
        var result = Email.Create("  User@Example.COM  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("user@example.com", result.Value.Value);
    }

    [Fact]
    public void Create_WithCanonicalAddress_ReturnsItUnchanged()
    {
        var result = Email.Create("user@example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal("user@example.com", result.Value.Value);
    }

    [Fact]
    public void Create_WithInvalidAddress_ReturnsValidationWithGivenCodeAndField()
    {
        var result = Email.Create("not-an-email", "user.changeEmail.newEmail", "newEmail");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Type);
        Assert.Equal("user.changeEmail.newEmail", result.Error.Code);
        Assert.Equal("newEmail", result.Error.InvalidField);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
    }

    [Fact]
    public void Create_WithoutExplicitCode_UsesDefaultCodeAndField()
    {
        var result = Email.Create("not-an-email");

        Assert.True(result.IsFailure);
        Assert.Equal(Email.DEFAULT_ERROR_CODE, result.Error.Code);
        Assert.Equal("email", result.Error.InvalidField);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankAddress_ReturnsValidationFailure(string? email)
    {
        var result = Email.Create(email);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.VALIDATION, result.Error.Type);
        Assert.Equal(Email.DEFAULT_ERROR_CODE, result.Error.Code);
    }

    [Fact]
    public void Validate_WithValidAddress_ReturnsNullAndCanonicalOutValue()
    {
        var error = Email.Validate("  User@Example.COM  ", "user.email", "email", out var email);

        Assert.Null(error);
        Assert.NotNull(email);
        Assert.Equal("user@example.com", email!.Value);
    }

    [Fact]
    public void Validate_WithInvalidAddress_ReturnsValidationAndNullOutValue()
    {
        var error = Email.Validate(
            "not-an-email",
            "user.changeEmail.newEmail",
            "newEmail",
            out var email);

        Assert.NotNull(error);
        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal("user.changeEmail.newEmail", error.Code);
        Assert.Equal("newEmail", error.InvalidField);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Null(email);
    }

    [Fact]
    public void Validate_WithAnotherScenarioCode_KeepsThatCode()
    {
        // Один валидатор обслуживает все сценарии: код ошибки — часть публичного контракта
        // и не должен подменяться внутри.
        var error = Email.Validate(
            "not-an-email",
            "emailChangeToken.create.newEmail",
            "newEmail",
            out _);

        Assert.NotNull(error);
        Assert.Equal("emailChangeToken.create.newEmail", error.Code);
        Assert.Equal("newEmail", error.InvalidField);
    }

    [Fact]
    public void FromStorage_WithInvalidValue_DoesNotThrowAndKeepsValue()
    {
        // Старые строки в колонке users.email не должны ронять чтение.
        var email = Email.FromStorage("not-an-email");

        Assert.Equal("not-an-email", email.Value);
    }

    [Fact]
    public void FromStorage_WithCanonicalValue_ReturnsThatValue()
    {
        var email = Email.FromStorage("user@example.com");

        Assert.Equal("user@example.com", email.Value);
    }

    [Fact]
    public void Equals_WithSameValue_ReturnsTrue()
    {
        var first = Email.FromStorage("user@example.com");
        var second = Email.FromStorage("user@example.com");

        Assert.True(first == second);
        Assert.True(first.Equals(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_WithDifferentValue_ReturnsFalse()
    {
        var first = Email.FromStorage("user@example.com");
        var second = Email.FromStorage("other@example.com");

        Assert.False(first == second);
        Assert.True(first != second);
        Assert.False(first.Equals(second));
    }

    [Fact]
    public void ToString_ReturnsCanonicalValue()
    {
        var email = Email.FromStorage("user@example.com");

        Assert.Equal("user@example.com", email.ToString());
        Assert.Equal("user@example.com", (string)email);
    }

    [Fact]
    public void Constants_EmailLengthBoundsMatchValidation()
    {
        Assert.Equal(3, Constants.MIN_EMAIL_LENGTH);
        Assert.Equal(254, Constants.MAX_EMAIL_LENGTH);

        // Ниже нижней границы: "a@" — два символа.
        var belowMinimum = "a@";

        Assert.True(belowMinimum.Length < Constants.MIN_EMAIL_LENGTH);
        Assert.False(Email.IsValid(belowMinimum));
        Assert.True(Email.Create(belowMinimum).IsFailure);

        // Ровно по верхней границе — допустимо, на символ больше — уже нет.
        var atMaximum = new string('a', Constants.MAX_EMAIL_LENGTH - "@example.com".Length)
                        + "@example.com";
        var aboveMaximum = atMaximum + "a";

        Assert.Equal(Constants.MAX_EMAIL_LENGTH, atMaximum.Length);
        Assert.Equal(Constants.MAX_EMAIL_LENGTH + 1, aboveMaximum.Length);
        Assert.True(Email.IsValid(atMaximum));
        Assert.True(Email.Create(atMaximum).IsSuccess);
        Assert.False(Email.IsValid(aboveMaximum));
        Assert.True(Email.Create(aboveMaximum).IsFailure);
    }
}
