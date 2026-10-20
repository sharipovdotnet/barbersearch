namespace BarberSearch.Application.Users;

/// <summary>
/// Проверка и вычисление хеша пароля.
/// </summary>
/// <remarks>
/// Абстракция объявлена в Application, реализация — в Web поверх <c>IPasswordHasher&lt;User&gt;</c>:
/// вход в систему остаётся командой Application, а конкретный алгоритм хеширования не протекает
/// ни в домен, ни в слой приложения. Открытый пароль дальше Application не уходит.
/// </remarks>
public interface IPasswordService
{
    /// <summary>
    /// Вычисляет хеш пароля.
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Сверяет пароль с сохранённым хешем.
    /// </summary>
    PasswordVerification Verify(string passwordHash, string password);

    /// <summary>
    /// Выполняет проверку по фиктивному хешу.
    /// </summary>
    /// <remarks>
    /// Нужна, когда адрес электронной почты не найден: без неё ответ на несуществующий адрес
    /// возвращался бы заметно быстрее, и по времени можно было бы перебрать зарегистрированные адреса.
    /// </remarks>
    PasswordVerification VerifyDummy(string password);
}
