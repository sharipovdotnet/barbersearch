namespace BarberSearch.Mobile.Core.Auth;

/// <summary>
/// Место хранения пары токенов между запусками приложения.
/// </summary>
/// <remarks>
/// Интерфейс живёт в Core, реализация — в приложении: <c>SecureStorage</c> принадлежит MAUI,
/// а ViewModels проверяются обычным xunit без платформы. Набор методов намеренно короткий:
/// сессия либо прочитана, либо сохранена целиком, либо стёрта — частичных состояний нет.
/// </remarks>
public interface ITokenStore
{
    /// <summary>
    /// Читает сохранённую пару.
    /// </summary>
    /// <remarks>
    /// Повреждённое значение — это «сессии нет», а не ошибка: реализация обязана вернуть
    /// <c>null</c>, а не бросить исключение, иначе приложение не запустится после
    /// восстановления из бэкапа.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Пара токенов или <c>null</c>, если её нет.</returns>
    Task<StoredTokens?> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Записывает пару целиком, заменяя прежнюю.
    /// </summary>
    /// <param name="tokens">Пара от сервера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task SaveAsync(StoredTokens tokens, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет сохранённую пару.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
