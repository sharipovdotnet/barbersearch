using BarberSearch.Application.Users.Passwords;
using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Правила пароля: длина, словарь частых паролей и совпадение с данными профиля.
/// </summary>
public class PasswordPolicyTests
{
    private const string Email = "maximilianus.rosenberg@example.com";
    private const string FirstName = "Konstantinos";
    private const string LastName = "Schwarzenegger";
    private const string StrongPassword = "aB3!xY9#kL2@";

    private static readonly PasswordPolicy Policy = new();

    [Fact]
    public void Validate_WithElevenCharacters_ReturnsTooShort()
    {
        var failure = Validate(new string('x', PasswordPolicy.MIN_LENGTH - 1));

        AssertValidationFailure(failure, "auth.password.tooShort");
    }

    [Fact]
    public void Validate_WithMinimumLength_Passes()
    {
        Assert.Equal(PasswordPolicy.MIN_LENGTH, StrongPassword.Length);

        Assert.Null(ValidateWithOwner(StrongPassword));
    }

    [Fact]
    public void Validate_WithMaximumLength_Passes()
    {
        Assert.Null(ValidateWithOwner(new string('x', PasswordPolicy.MAX_LENGTH)));
    }

    [Fact]
    public void Validate_WithOneCharacterOverMaximum_ReturnsTooLong()
    {
        var failure = Validate(new string('x', PasswordPolicy.MAX_LENGTH + 1));

        AssertValidationFailure(failure, "auth.password.tooLong");
    }

    [Fact]
    public void Validate_WithEmptyPassword_ReturnsRequired()
    {
        AssertValidationFailure(Validate(string.Empty), "auth.password.required");
    }

    [Fact]
    public void Validate_WithWhitespaceOnlyPassword_ReturnsRequired()
    {
        var failure = Validate(new string(' ', PasswordPolicy.MIN_LENGTH));

        AssertValidationFailure(failure, "auth.password.required");
    }

    [Fact]
    public void Validate_WithNullPassword_ReturnsRequired()
    {
        AssertValidationFailure(Validate(null), "auth.password.required");
    }

    [Fact]
    public void Validate_WithCommonPassword_ReturnsCommon()
    {
        // Словарь загружается из embedded-ресурса: без него эти кейсы прошли бы как
        // обычный сильный пароль.
        AssertValidationFailure(Validate("password"), "auth.password.common");
    }

    [Fact]
    public void Validate_WithCommonNumericPassword_ReturnsCommon()
    {
        AssertValidationFailure(Validate("123456"), "auth.password.common");
    }

    [Fact]
    public void Validate_WithCommonPasswordInDifferentCase_ReturnsCommon()
    {
        AssertValidationFailure(Validate("PASSWORD"), "auth.password.common");
    }

    [Fact]
    public void Validate_WithPasswordEqualToEmail_ReturnsEqualsEmail()
    {
        var failure = ValidateWithOwner(Email.ToUpperInvariant());

        AssertValidationFailure(failure, "auth.password.equalsEmail");
    }

    [Fact]
    public void Validate_WithPasswordEqualToEmailLocalPart_ReturnsEqualsEmail()
    {
        var localPart = Email[..Email.LastIndexOf('@')];
        var failure = ValidateWithOwner(localPart.ToUpperInvariant());

        AssertValidationFailure(failure, "auth.password.equalsEmail");
    }

    [Fact]
    public void Validate_WithPasswordEqualToFirstName_ReturnsEqualsName()
    {
        var failure = ValidateWithOwner(FirstName.ToUpperInvariant());

        AssertValidationFailure(failure, "auth.password.equalsName");
    }

    [Fact]
    public void Validate_WithPasswordEqualToLastName_ReturnsEqualsName()
    {
        var failure = ValidateWithOwner(LastName.ToLowerInvariant());

        AssertValidationFailure(failure, "auth.password.equalsName");
    }

    [Fact]
    public void Validate_WithStrongPassword_Passes()
    {
        Assert.Null(ValidateWithOwner(StrongPassword));
    }

    [Fact]
    public void Validate_DoesNotTrimSurroundingSpaces()
    {
        // Ровно 12 символов: если бы пароль срезался, получился бы словарный "password"
        // и проверка вернула бы auth.password.common.
        const string padded = "  password  ";

        Assert.Equal(PasswordPolicy.MIN_LENGTH, padded.Length);
        Assert.Null(ValidateWithOwner(padded));
    }

    private static Failure? Validate(string? password)
        => Policy.Validate(password, "password");

    private static Failure? ValidateWithOwner(string? password)
        => Policy.Validate(password, Email, FirstName, LastName, "password");

    private static void AssertValidationFailure(Failure? failure, string expectedCode)
    {
        Assert.NotNull(failure);
        Assert.Equal(ErrorType.VALIDATION, failure!.Errors[0].Type);
        Assert.Equal(expectedCode, failure.Errors[0].Code);
        Assert.Equal("password", failure.Errors[0].InvalidField);
    }
}
