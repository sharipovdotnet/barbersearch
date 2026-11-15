using BarberSearch.Contracts.Auth.Login;
using BarberSearch.Contracts.Auth.Register;

namespace BarberSearch.Mobile.Core.Api.Auth;

/// <summary>
/// Регистрация, вход, запрос ссылки сброса пароля и работа с парой токенов.
/// </summary>
/// <remarks>
/// Отдельно от <see cref="IAccountApi"/>: эти запросы идут на «чистом» <see cref="HttpClient"/>
/// без авторизационного обработчика. Иначе refresh, получивший <c>401</c>, вызывал бы сам себя,
/// а выход отправлял бы уже отозванный access-токен.
/// </remarks>
public interface IAuthApi
{
    /// <summary>
    /// Создаёт аккаунт клиента и возвращает его идентификатор.
    /// </summary>
    /// <remarks>
    /// Регистрация не входит: <c>201</c> несёт только <c>id</c>, а пара выдаётся отдельным
    /// <see cref="LoginAsync"/> тем же паролем — так же поступает веб. Роль в теле не
    /// передаётся: публичная регистрация всегда создаёт <c>Customer</c>.
    /// <c>ConsentVersion</c> по смыслу обязателен — без него сервер отвечает
    /// <c>400 auth.register.consentRequired</c>, а со старой редакцией —
    /// <c>400 auth.register.consentVersion</c>.
    /// </remarks>
    /// <param name="request">Имя, фамилия, email, пароль и редакция Политики.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор созданного пользователя.</returns>
    Task<Guid> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Просит сервер отправить на адрес ссылку для сброса пароля.
    /// </summary>
    /// <remarks>
    /// Ответ <c>204</c> одинаков для существующего, несуществующего и неактивного адреса:
    /// по нему нельзя выяснить, есть ли аккаунт, и экран обязан повторять это. Письмо со
    /// ссылкой открывается в браузере — приложение её не разбирает и новый пароль не
    /// принимает. <c>409 auth.passwordReset.unavailable</c> значит, что почта на сервере
    /// не настроена; <c>429</c> — лимитер авторизации.
    /// </remarks>
    /// <param name="email">Адрес аккаунта; его существование не проверяется.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default);

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
