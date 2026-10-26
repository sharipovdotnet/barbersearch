using Shared;

namespace BarberSearch.Application.Users.Passwords;

/// <summary>
/// Политика паролей: обязательность, длина, словарь частых паролей и совпадение с данными профиля.
/// </summary>
/// <remarks>
/// Живёт в Application, потому что одно правило нужно трём сценариям: публичной регистрации,
/// созданию пользователя администратором и смене пароля. Проверка выполняется до хеширования
/// PBKDF2 — верхняя граница длины не даёт неограниченному вводу жечь CPU.
/// </remarks>
public interface IPasswordPolicy
{
    /// <summary>
    /// Проверяет состав пароля: обязательность, длину и отсутствие в словаре частых паролей.
    /// </summary>
    /// <param name="password">Открытый пароль; пробелы по краям значимы и не срезаются.</param>
    /// <param name="invalidField">Имя поля запроса для ответа 400.</param>
    /// <returns><see langword="null"/>, если пароль допустим; иначе ошибку валидации.</returns>
    Failure? Validate(string? password, string invalidField = "password");

    /// <summary>
    /// Полная проверка: состав пароля плюс запрет совпадения с email, его local-part, именем и фамилией.
    /// </summary>
    /// <param name="password">Открытый пароль; пробелы по краям значимы и не срезаются.</param>
    /// <param name="email">Адрес пользователя; сравнивается целиком и по local-part.</param>
    /// <param name="firstName">Имя пользователя.</param>
    /// <param name="lastName">Фамилия пользователя.</param>
    /// <param name="invalidField">Имя поля запроса для ответа 400.</param>
    /// <returns><see langword="null"/>, если пароль допустим; иначе ошибку валидации.</returns>
    Failure? Validate(
        string? password,
        string? email,
        string? firstName,
        string? lastName,
        string invalidField = "password");
}
