using System.Net.Mail;
using Shared;

namespace BarberSearch.Domain.Users;

/// <summary>
/// Канонический адрес электронной почты: нормализация, синтаксическая проверка и код ошибки.
/// </summary>
/// <remarks>
/// <para>
/// Одно место на все сценарии: регистрация и правка профиля (<c>user.email</c>) и смена
/// адреса с подтверждением (<c>user.changeEmail.newEmail</c>). Код ошибки — часть публичного
/// контракта, поэтому правила живут здесь, а не дублируются в агрегате.
/// </para>
/// <para>
/// Канонический вид — часть контракта хранения: по нему строится уникальный индекс
/// <c>ux_users_email</c>, и в нём адрес сравнивается при входе. Без нормализации
/// <c>User@x</c> и <c>user@x</c> становились двумя пользователями, а аккаунт с пробелом
/// по краям нельзя было открыть для входа (R-03).
/// </para>
/// </remarks>
public static class EmailAddress
{
    /// <summary>
    /// Приводит адрес к каноническому виду: срезает пробелы по краям и переводит в нижний регистр.
    /// </summary>
    /// <param name="email">Исходное значение; <c>null</c> даёт пустую строку.</param>
    public static string Normalize(string? email)
        => email?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>
    /// Синтаксическая проверка канонического адреса.
    /// </summary>
    /// <remarks>
    /// <see cref="MailAddress.TryCreate(string, out MailAddress)"/> разбирает адрес по RFC,
    /// а не по регулярному выражению. Сравнение <c>Address</c> с исходной строкой отсекает
    /// форму с отображаемым именем (<c>Иван &lt;ivan@example.com&gt;</c>), которую он тоже
    /// принимает. Домен обязан содержать точку: <c>a@b</c> синтаксически допустим для
    /// <see cref="MailAddress"/>, но не является адресом публичного сервиса.
    /// </remarks>
    /// <param name="email">Нормализованный адрес.</param>
    public static bool IsValid(string email)
    {
        if (email.Length is < Constants.MIN_EMAIL_LENGTH or > Constants.MAX_EMAIL_LENGTH)
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
    /// Проверяет адрес и возвращает ошибку валидации либо <c>null</c>, если адрес допустим.
    /// </summary>
    /// <param name="email">Нормализованный адрес.</param>
    /// <param name="code">Код ошибки — свой у каждого сценария (<c>user.email</c>,
    /// <c>user.changeEmail.newEmail</c>).</param>
    /// <param name="invalidField">Имя поля запроса для <c>invalidField</c> в ответе.</param>
    public static Error? Validate(string email, string code, string invalidField)
    {
        if (IsValid(email))
        {
            return null;
        }

        return Error.Validation(
            code,
            $"Email must be a valid address between {Constants.MIN_EMAIL_LENGTH} " +
            $"and {Constants.MAX_EMAIL_LENGTH} characters.",
            invalidField);
    }
}
