namespace BarberSearch.Application.Abstractions;

/// <summary>
/// Текущий пользователь, от имени которого выполняется запрос.
/// </summary>
/// <remarks>
/// Абстракция живёт в Application, а реализация — в Web: слой Application не ссылается
/// на ASP.NET Core и не знает ни про <c>HttpContext</c>, ни про схемы аутентификации.
/// Обработчики используют её для проверки владения ресурсом.
/// </remarks>
public interface ICurrentUser
{
    /// <summary>
    /// Идентификатор пользователя из claim'а <c>sub</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Запрос не аутентифицирован. Для действий, до которых доходит обработчик, это означает
    /// ошибку конфигурации: fallback-политика требует входа для всего, кроме <c>[AllowAnonymous]</c>.
    /// </exception>
    Guid UserId { get; }

    /// <summary>
    /// Пришёл ли запрос от администратора.
    /// </summary>
    bool IsAdmin { get; }
}
