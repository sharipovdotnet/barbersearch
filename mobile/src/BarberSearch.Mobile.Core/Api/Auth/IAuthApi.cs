using BarberSearch.Contracts.Auth.Login;

namespace BarberSearch.Mobile.Core.Api.Auth;

/// <summary>
/// Вход, продление и выход по паре токенов.
/// </summary>
/// <remarks>
/// Отдельно от <see cref="IAccountApi"/>: эти запросы идут на «чистом» <see cref="HttpClient"/>
/// без авторизационного обработчика. Иначе refresh, получивший <c>401</c>, вызывал бы сам себя,
/// а выход отправлял бы уже отозванный access-токен.
/// </remarks>
public interface IAuthApi
{
    /// <summary>
    /// Меняет email и пароль на пару токенов.
    /// </summary>
    /// <remarks>
    /// Сервер отвечает <c>200</c> с <c>refreshToken</c> только внешнему клиенту: браузер получает
    /// refresh в HttpOnly-cookie. Ошибка входа — <see cref="BarberSearchApiException"/>
    /// с публичным кодом (<c>auth.login.invalidCredentials</c> и соседние) или <c>429</c>.
    /// </remarks>
    /// <param name="email">Адрес пользователя.</param>
    /// <param name="password">Пароль; в памяти не сохраняется.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Новая пара токенов.</returns>
    Task<TokenResponse> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Обменивает refresh-токен на новую пару.
    /// </summary>
    /// <remarks>
    /// Ротация обязательна: сервер гасит предъявленный refresh и возвращает следующий.
    /// Повтор использованного токена — <c>401 auth.refresh.reused</c> и отзыв всей цепочки,
    /// поэтому параллельные refresh недопустимы (см. <see cref="BarberSearch.Mobile.Core.Auth.AuthenticatedHttpHandler"/>).
    /// </remarks>
    /// <param name="refreshToken">Действующий refresh-токен.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Новая пара токенов.</returns>
    Task<TokenResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отзывает refresh-токен на сервере.
    /// </summary>
    /// <remarks>
    /// Идемпотентно: повторный выход с уже отозванным токеном отвечает <c>204</c>. Пустой токен
    /// допустим — сервер гасит cookie, если она есть; мобильному клиенту просить нечего.
    /// </remarks>
    /// <param name="refreshToken">Refresh-токен или <c>null</c>, если его уже нет.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task LogoutAsync(
        string? refreshToken,
        CancellationToken cancellationToken = default);
}
