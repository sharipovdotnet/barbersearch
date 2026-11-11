using BarberSearch.Domain.Common;

namespace BarberSearch.Application.Users;

/// <summary>
/// Доступ к файлу аватара по media-URL: сохранённая ссылка, административная блокировка
/// и состояние проверки.
/// </summary>
/// <remarks>
/// <para>
/// Публичный тип живёт в Application рядом с <see cref="IUserRepository"/>: инфраструктурная
/// запись <c>UserAvatarUrlLookup</c> была <c>internal</c> и не могла быть возвращена из
/// публичного интерфейса. Раздаче нужны все три поля сразу — без флага она не отличит
/// «файла нет» от «файл скрыт администратором», а без статуса — «кадр не одобрен»
/// от «кадр одобрен».
/// </para>
/// <para>
/// <c>AvatarUrl</c> — сырое сохранённое значение; маску применяет вызывающий: публичные
/// чтения отдают <c>null</c> при блокировке или статусе не <c>Approved</c>, раздача
/// отвечает <c>404</c> всем, кроме администратора (и владельца для непубличного кадра).
/// Точное сравнение URL с запрошенным не ослабляется: файл заменённого аватара должен
/// отвечать <c>404</c>.
/// </para>
/// </remarks>
/// <param name="AvatarUrl">Сохранённый URL аватара либо <c>null</c>, если он не загружен.</param>
/// <param name="IsAvatarBlocked">Скрыт ли аватар администратором.</param>
/// <param name="ModerationStatus">Допуск аватара к публикации.</param>
public sealed record UserAvatarAccess(
    string? AvatarUrl,
    bool IsAvatarBlocked,
    PhotoModerationStatus ModerationStatus);
