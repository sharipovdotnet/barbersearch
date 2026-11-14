using BarberSearch.Contracts.Auth.Me;

namespace BarberSearch.Mobile.Core.Api.Account;

/// <summary>
/// Данные текущего пользователя по access-токену.
/// </summary>
/// <remarks>
/// Ходит через клиент с <c>AuthenticatedHttpHandler</c>: заголовок и продление пары —
/// его работа, а не этого интерфейса.
/// </remarks>
public interface IAccountApi
{
    /// <summary>
    /// Возвращает пользователя, которому принадлежит текущий access-токен.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Проекция claims текущей сессии.</returns>
    Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default);
}
