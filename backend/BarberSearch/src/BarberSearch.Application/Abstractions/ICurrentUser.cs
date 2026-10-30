namespace BarberSearch.Application.Abstractions;

/// <summary>
/// Текущий пользователь, от имени которого выполняется запрос.
/// </summary>
/// <remarks>
/// Абстракция живёт в Application, а реализация — в Web: слой Application не ссылается
/// на ASP.NET Core и не знает ни про <c>HttpContext</c>, ни про схемы аутентификации.
/// Обработчики используют её для проверки владения ресурсом.
/// <para>
/// Идентификатор пользователя читается только через <see cref="TryGetUserId"/>: на анонимном
/// запросе он не бросает исключение, а возвращает <c>false</c>. Обработчики, которым нужен
/// пользователь, получают <c>401 auth.unauthorized</c> через
/// <c>CurrentUserExtensions.RequireUserId()</c> или проверки владения (R-29).
/// </para>
/// </remarks>
public interface ICurrentUser
{
    /// <summary>
    /// Пытается получить идентификатор пользователя из claim'а <c>sub</c>.
    /// </summary>
    /// <param name="userId">
    /// Идентификатор пользователя, если запрос аутентифицирован; иначе <see cref="Guid.Empty"/>.
    /// </param>
    /// <returns>
    /// <c>true</c>, если запрос аутентифицирован; <c>false</c> для анонимного запроса.
    /// </returns>
    bool TryGetUserId(out Guid userId);

    /// <summary>
    /// Пытается получить идентификатор профиля барбера из claim'а <c>barber_profile_id</c>.
    /// </summary>
    /// <remarks>
    /// Claim заполняется при входе, если профиль уже привязан к аккаунту. Профиль из claim'а,
    /// а не из query, — единственный способ открыть барберу его собственные записи: параметра
    /// с идентификатором профиля у чтения нет, поэтому чужой список запросить нечем.
    /// </remarks>
    /// <param name="barberProfileId">
    /// Идентификатор профиля, если claim есть и разбирается; иначе <see cref="Guid.Empty"/>.
    /// </param>
    /// <returns>
    /// <c>true</c>, если профиль привязан к текущей сессии; иначе <c>false</c>.
    /// </returns>
    bool TryGetBarberProfileId(out Guid barberProfileId);

    /// <summary>
    /// Пришёл ли запрос от администратора.
    /// </summary>
    bool IsAdmin { get; }
}
