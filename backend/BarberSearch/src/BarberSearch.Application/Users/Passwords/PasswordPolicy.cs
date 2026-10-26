using Shared;

namespace BarberSearch.Application.Users.Passwords;

/// <summary>
/// Проверяет пароль по правилам репозитория: 12..128 символов, не из словаря top-10k,
/// не совпадает с email, его local-part, именем или фамилией.
/// </summary>
/// <remarks>
/// Словарь — встроенный ресурс сборки, читается один раз при первой проверке и живёт
/// в <see cref="HashSet{T}"/> с <see cref="StringComparer.OrdinalIgnoreCase"/>; сети в рантайме нет.
/// Словарь проверяется раньше минимальной длины, поэтому короткий частый пароль получает
/// <c>auth.password.common</c>, а не <c>auth.password.tooShort</c>.
/// </remarks>
public sealed class PasswordPolicy : IPasswordPolicy
{
    /// <summary>
    /// Минимальная длина пароля.
    /// </summary>
    public const int MIN_LENGTH = 12;

    /// <summary>
    /// Максимальная длина пароля: потолок до PBKDF2, чтобы большой ввод не жёг CPU.
    /// </summary>
    public const int MAX_LENGTH = 128;

    private const string COMMON_PASSWORDS_RESOURCE =
        "BarberSearch.Application.Users.Passwords.10k-most-common.txt";

    private static readonly Lazy<HashSet<string>> CommonPasswords = new(LoadCommonPasswords);

    /// <inheritdoc />
    public Failure? Validate(string? password, string invalidField = "password")
    {
        // Пробелы внутри и по краям значимы: сам пароль не срезается. Пробельный пароль
        // при этом не считается заданным.
        if (string.IsNullOrWhiteSpace(password))
        {
            return Reject(
                "auth.password.required",
                "Password is required.",
                invalidField);
        }

        if (password.Length > MAX_LENGTH)
        {
            return Reject(
                "auth.password.tooLong",
                $"Password must be at most {MAX_LENGTH} characters long.",
                invalidField);
        }

        if (CommonPasswords.Value.Contains(password))
        {
            return Reject(
                "auth.password.common",
                "This password is too common.",
                invalidField);
        }

        if (password.Length < MIN_LENGTH)
        {
            return Reject(
                "auth.password.tooShort",
                $"Password must be at least {MIN_LENGTH} characters long.",
                invalidField);
        }

        return null;
    }

    /// <inheritdoc />
    public Failure? Validate(
        string? password,
        string? email,
        string? firstName,
        string? lastName,
        string invalidField = "password")
    {
        var composition = Validate(password, invalidField);
        if (composition is not null)
        {
            return composition;
        }

        if (EqualsEmail(password!, email))
        {
            return Reject(
                "auth.password.equalsEmail",
                "Password must not be the same as the email address.",
                invalidField);
        }

        if (EqualsName(password!, firstName) || EqualsName(password!, lastName))
        {
            return Reject(
                "auth.password.equalsName",
                "Password must not be the same as the first or last name.",
                invalidField);
        }

        return null;
    }

    private static Failure Reject(string code, string message, string invalidField)
        => new(Error.Validation(code, message, invalidField));

    /// <summary>
    /// Сравнивает пароль с адресом целиком и с его local-part без учёта регистра.
    /// </summary>
    private static bool EqualsEmail(string password, string? email)
    {
        var normalized = email?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        if (string.Equals(password, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var at = normalized.LastIndexOf('@');

        return at > 0 &&
               string.Equals(password, normalized[..at], StringComparison.OrdinalIgnoreCase);
    }

    private static bool EqualsName(string password, string? name)
    {
        var normalized = name?.Trim();

        return !string.IsNullOrEmpty(normalized) &&
               string.Equals(password, normalized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Лениво загружает встроенный словарь: строки обрезаются, пустые пропускаются,
    /// сравнение — без учёта регистра.
    /// </summary>
    private static HashSet<string> LoadCommonPasswords()
    {
        using var stream = typeof(PasswordPolicy).Assembly
            .GetManifestResourceStream(COMMON_PASSWORDS_RESOURCE);

        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{COMMON_PASSWORDS_RESOURCE}' was not found.");
        }

        using var reader = new StreamReader(stream);
        var passwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (reader.ReadLine() is { } line)
        {
            var candidate = line.Trim();

            if (candidate.Length != 0)
            {
                passwords.Add(candidate);
            }
        }

        return passwords;
    }
}
