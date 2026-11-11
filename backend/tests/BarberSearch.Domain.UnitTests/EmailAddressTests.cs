using BarberSearch.Domain;
using BarberSearch.Domain.Users;
using Shared;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

/// <summary>
/// Общий валидатор адреса электронной почты: нормализация, синтаксис и код ошибки сценария.
/// </summary>
/// <remarks>
/// Синтаксис вынесен из <see cref="User"/> в <see cref="EmailAddress"/> и используется
/// регистрацией, правкой профиля и сменой адреса. Правила — <c>MailAddress.TryCreate</c>,
/// обязательная точка в домене и длина <c>3..254</c>; коды ошибок у сценариев разные, поэтому
/// <see cref="EmailAddress.Validate"/> принимает их аргументами.
/// </remarks>
public class EmailAddressTests
{
    [Fact]
    public void Normalize_TrimsAndLowercases()
    {
        Assert.Equal("user@example.com", EmailAddress.Normalize("  User@Example.COM  "));
    }

    [Fact]
    public void Normalize_KeepsCanonicalAddressUnchanged()
    {
        Assert.Equal("user@example.com", EmailAddress.Normalize("user@example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_WithBlankInput_ReturnsEmptyString(string? email)
    {
        Assert.Equal(string.Empty, EmailAddress.Normalize(email));
    }

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("user.name+tag@sub.example.co.uk")]
    [InlineData("a@b.c")]
    [InlineData("user_name@example-domain.com")]
    public void IsValid_WithCanonicalAddress_ReturnsTrue(string email)
    {
        Assert.True(EmailAddress.IsValid(email));
    }

    [Fact]
    public void IsValid_AtMaximumEmailLength_ReturnsTrue()
    {
        // 254 символа — предел RFC 5321 и колонки varchar(254).
        var email = new string('a', 242) + "@example.com";

        Assert.Equal(254, email.Length);
        Assert.True(EmailAddress.IsValid(email));
    }

    [Fact]
    public void IsValid_AboveMaximumEmailLength_ReturnsFalse()
    {
        var email = new string('a', 243) + "@example.com";

        Assert.Equal(255, email.Length);
        Assert.False(EmailAddress.IsValid(email));
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
        Assert.False(EmailAddress.IsValid(email));
    }

    [Fact]
    public void Validate_WithValidAddress_ReturnsNull()
    {
        Assert.Null(EmailAddress.Validate("user@example.com", "user.email", "email"));
    }

    [Fact]
    public void Validate_WithInvalidAddress_ReturnsValidationWithGivenCodeAndField()
    {
        var error = EmailAddress.Validate("not-an-email", "user.changeEmail.newEmail", "newEmail");

        Assert.NotNull(error);
        Assert.Equal(ErrorType.VALIDATION, error.Type);
        Assert.Equal("user.changeEmail.newEmail", error.Code);
        Assert.Equal("newEmail", error.InvalidField);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void Validate_WithAnotherScenarioCode_KeepsThatCode()
    {
        // Один валидатор обслуживает все сценарии: код ошибки — часть публичного контракта
        // и не должен подменяться внутри.
        var error = EmailAddress.Validate("not-an-email", "user.email", "email");

        Assert.NotNull(error);
        Assert.Equal("user.email", error.Code);
        Assert.Equal("email", error.InvalidField);
    }

    [Fact]
    public void Constants_EmailLengthBoundsMatchValidation()
    {
        Assert.Equal(3, Constants.MIN_EMAIL_LENGTH);
        Assert.Equal(254, Constants.MAX_EMAIL_LENGTH);
    }
}
