using BarberSearch.Contracts.Auth.Me;
using BarberSearch.Contracts.Users.Me;

namespace BarberSearch.Mobile.Core.Api.Account;

/// <summary>
/// Данные текущего пользователя по access-токену: проверка сессии и профиль аккаунта.
/// </summary>
/// <remarks>
/// <para>
/// Ходит через клиент с <c>AuthenticatedHttpHandler</c>: заголовок и продление пары —
/// его работа, а не этого интерфейса. Поэтому здесь же живут смена пароля и заявка на
/// смену email: на сервере они лежат в <c>api/auth</c>, но идут с Bearer, а клиент
/// <see cref="Auth.IAuthApi"/> намеренно без обработчика — иначе refresh, получивший
/// <c>401</c>, вызывал бы сам себя.
/// </para>
/// <para>
/// Два чтения не взаимозаменяемы: <see cref="GetMeAsync"/> отвечает проекцией claims и
/// служит проверкой сессии, <see cref="GetMyProfileAsync"/> отвечает из базы и несёт имя,
/// телефон и подтверждения. Экран профиля читает второе.
/// </para>
/// </remarks>
public interface IAccountApi
{
    /// <summary>
    /// Возвращает пользователя, которому принадлежит текущий access-токен.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Проекция claims текущей сессии.</returns>
    Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает профиль текущего пользователя из базы.
    /// </summary>
    /// <remarks>
    /// Ответ актуальнее токена: имя, телефон и подтверждение email меняются без перевыпуска
    /// access-токена, и старый токен ещё пятнадцать минут показывал бы прежние значения.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Профиль из базы.</returns>
    Task<MyProfileResponse> GetMyProfileAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Заменяет имя, фамилию, телефон и аватар профиля.
    /// </summary>
    /// <remarks>
    /// Тело заменяется целиком, поэтому <paramref name="avatarUrl"/> передаётся обратно как
    /// есть — даже <c>null</c>. Иначе сохранение одного имени стёрло бы фото, загруженное
    /// на вебе: в этом срезе аватар не показывается, но и терять его нельзя.
    /// </remarks>
    /// <param name="userId">Пользователь; сервер отклонит чужой идентификатор.</param>
    /// <param name="firstName">Имя, не длиннее 100 символов.</param>
    /// <param name="lastName">Фамилия, не длиннее 100 символов.</param>
    /// <param name="phoneNumber">Телефон либо <c>null</c>, чтобы очистить поле.</param>
    /// <param name="avatarUrl">Текущая ссылка на аватар либо <c>null</c>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task UpdateProfileAsync(
        Guid userId,
        string firstName,
        string lastName,
        string? phoneNumber,
        string? avatarUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Меняет пароль текущего пользователя.
    /// </summary>
    /// <remarks>
    /// <c>204</c> отзывает все refresh-токены и ротирует <c>SecurityStamp</c>: текущий
    /// access-токен перестаёт проходить проверку сразу, а не через пятнадцать минут. Поэтому
    /// после успеха приложение обязано получить новую пару повторным входом.
    /// </remarks>
    /// <param name="currentPassword">Текущий пароль — подтверждение личности.</param>
    /// <param name="newPassword">Новый пароль, 12..128 символов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Оставляет заявку на смену адреса электронной почты.
    /// </summary>
    /// <remarks>
    /// <c>204</c> значит только «письма поставлены в очередь»: ссылка подтверждения уходит на
    /// новый адрес, уведомление — на текущий, а сам адрес меняется после перехода по ссылке
    /// в браузере. Приложение ссылку не открывает и токен не разбирает.
    /// </remarks>
    /// <param name="newEmail">Запрошенный адрес.</param>
    /// <param name="currentPassword">Текущий пароль — подтверждение личности.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task RequestEmailChangeAsync(
        string newEmail,
        string currentPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Просит отправить письмо подтверждения на текущий адрес.
    /// </summary>
    /// <remarks>
    /// При уже подтверждённом адресе сервер отвечает тем же <c>204</c> и письма не шлёт,
    /// поэтому кнопка в интерфейсе показывается только для неподтверждённого.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task RequestEmailVerificationAsync(CancellationToken cancellationToken = default);
}
