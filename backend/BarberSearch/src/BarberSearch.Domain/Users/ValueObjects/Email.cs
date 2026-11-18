using System.Net.Mail;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Users.ValueObjects;

/// <summary>
/// Адрес электронной почты пользователя в каноническом виде.
/// </summary>
/// <remarks>
/// <para>
/// Объект-значение хранит только канонический вид: без пробелов по краям и в нижнем регистре.
/// Канонический вид — часть контракта хранения: по нему построен уникальный индекс
/// <c>ux_users_email</c>, и в нём адрес сравнивается при входе. Без нормализации
/// <c>User@x</c> и <c>user@x</c> становились бы двумя разными пользователями, а аккаунт
/// с пробелом по краям нельзя было бы открыть для входа (R-03).
/// </para>
/// <para>
/// Синтаксис проверяется разбором по RFC через <see cref="MailAddress.TryCreate"/>, а не
/// регулярным выражением. Сравнение <c>Address</c> с исходной строкой отсекает форму
/// с отображаемым именем (<c>Иван &lt;ivan@example.com&gt;</c>), которую он тоже принимает.
/// Домен обязан содержать точку: <c>a@b</c> синтаксически допустим для
/// <see cref="MailAddress"/>, но не является адресом публичного сервиса.
/// </para>
/// <para>
/// Коды ошибок не зашиты в правила: каждый сценарий передаёт свой код
/// (<c>user.email</c>, <c>user.changeEmail.newEmail</c>, <c>emailChangeToken.create.newEmail</c>),
/// потому что код ошибки — часть публичного контракта.
/// </para>
/// </remarks>
public sealed class Email : IEquatable<Email>
{
    /// <summary>
    /// Код ошибки по умолчанию для недопустимого адреса.
    /// </summary>
    public const string DEFAULT_ERROR_CODE = "user.email";

    private Email(string value) => Value = value;

    /// <summary>
    /// Канонический адрес: без пробелов по краям, в нижнем регистре.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Создаёт адрес из произвольной записи: нормализует и проверяет синтаксис.
    /// </summary>
    /// <param name="value">Исходное значение.</param>
    /// <param name="code">Код ошибки — часть контракта вызывающего сценария.</param>
    /// <param name="invalidField">Имя поля запроса для <c>invalidField</c> в ответе.</param>
    public static Result<Email, Error> Create(
        string? value,
        string code = DEFAULT_ERROR_CODE,
        string invalidField = "email")
    {
        var error = Validate(value, code, invalidField, out var email);

        if (error is not null)
        {
            return error;
        }

        return email!;
    }

    /// <summary>
    /// Проверяет адрес и возвращает ошибку валидации либо <c>null</c>, если адрес допустим.
    /// </summary>
    /// <remarks>
    /// Нормализация входит в проверку: значение с пробелами по краям или в верхнем регистре
    /// допустимо и возвращается в <paramref name="email"/> уже каноническим.
    /// </remarks>
    /// <param name="value">Исходное значение.</param>
    /// <param name="code">Код ошибки — часть контракта вызывающего сценария.</param>
    /// <param name="invalidField">Имя поля запроса для <c>invalidField</c> в ответе.</param>
    /// <param name="email">
    /// Канонический адрес при успехе; <c>null</c>, если значение недопустимо.
    /// </param>
    /// <returns>Ошибка валидации либо <c>null</c>, если значение допустимо.</returns>
    public static Error? Validate(
        string? value,
        string code,
        string invalidField,
        out Email? email)
    {
        email = null;

        var normalized = Normalize(value);

        if (!IsValid(normalized))
        {
            return Error.Validation(
                code,
                $"Email must be a valid address between {Constants.MIN_EMAIL_LENGTH} " +
                $"and {Constants.MAX_EMAIL_LENGTH} characters.",
                invalidField);
        }

        email = new Email(normalized);

        return null;
    }

    /// <summary>
    /// Проверяет синтаксис уже нормализованного адреса.
    /// </summary>
    /// <param name="email">Нормализованный адрес; <c>null</c> и пустая строка — не адрес.</param>
    public static bool IsValid(string? email)
    {
        if (email is null ||
            email.Length is < Constants.MIN_EMAIL_LENGTH or > Constants.MAX_EMAIL_LENGTH)
        {
            return false;
        }

        if (!MailAddress.TryCreate(email, out var parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.Ordinal))
        {
            return false;
        }

        var at = email.LastIndexOf('@');
        var dot = email.IndexOf('.', at + 1);

        return at > 0 &&
               dot > at + 1 &&
               dot < email.Length - 1 &&
               !email.Contains("..", StringComparison.Ordinal) &&
               !email.EndsWith('.');
    }

    /// <summary>
    /// Приводит адрес к каноническому виду: срезает пробелы по краям и переводит
    /// в нижний регистр.
    /// </summary>
    /// <param name="email">Исходное значение; <c>null</c> даёт пустую строку.</param>
    public static string Normalize(string? email)
        => email?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>
    /// Восстанавливает адрес из канонического вида без проверки синтаксиса.
    /// </summary>
    /// <remarks>
    /// Два места использования: материализация значения из колонки <c>users.email</c>
    /// (старые строки не должны ронять чтение) и аргумент типизированного сравнения
    /// в запросах к БД — EF применяет конвертер к параметру, поэтому сравнение идёт
    /// по <c>citext</c>, а не по приведённой строке.
    /// </remarks>
    /// <param name="value">Канонический адрес из хранилища.</param>
    public static Email FromStorage(string value) => new(value);

    /// <inheritdoc />
    public bool Equals(Email? other)
        => other is not null &&
           string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is Email other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>
    /// Отдаёт каноническую строку явным приведением.
    /// </summary>
    /// <remarks>
    /// Нужно там, где SQL-функция или проекция требует именно строку, а не объект-значение:
    /// поиск <c>ILIKE</c> по адресу. Неявного преобразования нет намеренно — оно позволило бы
    /// сравнивать адрес со строкой в LINQ и получить регистрозависимое сравнение
    /// (<c>email::text</c>) вместо регистронезависимого <c>citext</c> (R-03).
    /// </remarks>
    /// <param name="email">Адрес.</param>
    public static explicit operator string(Email email) => email.Value;

    public static bool operator ==(Email? left, Email? right)
        => object.Equals(left, right);

    public static bool operator !=(Email? left, Email? right)
        => !object.Equals(left, right);
}
