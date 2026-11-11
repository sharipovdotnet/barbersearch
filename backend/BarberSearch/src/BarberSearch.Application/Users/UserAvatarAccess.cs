namespace BarberSearch.Application.Users;

/// <summary>
/// Доступ к файлу аватара по media-URL: сохранённая ссылка и административная блокировка.
/// </summary>
/// <remarks>
/// <para>
/// Публичный тип живёт в Application рядом с <see cref="IUserRepository"/>: инфраструктурная
/// запись <c>UserAvatarUrlLookup</c> была <c>internal</c> и не могла быть возвращена из
/// публичного интерфейса. Раздаче нужны оба поля сразу — без флага она не отличит
/// «файла нет» от «файл скрыт администратором».
/// </para>
/// <para>
/// <c>AvatarUrl</c> — сырое сохранённое значение; маску применяет вызывающий: публичные
/// чтения отдают <c>null</c>, раздача отвечает <c>404</c> всем, кроме администратора.
/// </para>
/// </remarks>
/// <param name="AvatarUrl">Сохранённый URL аватара либо <c>null</c>, если он не загружен.</param>
/// <param name="IsAvatarBlocked">Скрыт ли аватар администратором.</param>
public sealed record UserAvatarAccess(string? AvatarUrl, bool IsAvatarBlocked);
