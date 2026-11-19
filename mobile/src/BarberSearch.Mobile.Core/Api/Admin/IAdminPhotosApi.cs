using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Mobile.Core.Api.Admin;

/// <summary>
/// Очереди премодерации фотографий: аватары пользователей и работы портфолио.
/// </summary>
/// <remarks>
/// <para>
/// Два чтения и четыре решения — ровно то, что показывает веб-экран <c>/admin/photos</c>
/// (<c>frontend/src/lib/admin-photos.ts</c>). Очередь отдаёт только кадры <c>Pending</c>
/// в порядке поступления, поэтому параметра статуса у чтений нет: история проверенных
/// кадров живёт в админских карточках пользователя и мастера, а не здесь.
/// </para>
/// <para>
/// Решения — обычные <c>POST</c> без тела либо с причиной отказа; сервер отвечает
/// <c>204</c>, тело не читается. Повторное решение по уже проверенному кадру сервер
/// отклоняет <c>409</c>: экран перечитывает очередь, но не выдумывает успех.
/// </para>
/// <para>
/// Скрытие и возврат кадра (<c>.../block</c>) сюда не входят: на веб-экране очереди этих
/// действий нет, они живут в карточках пользователя и мастера — то есть в других срезах.
/// </para>
/// </remarks>
public interface IAdminPhotosApi
{
    /// <summary>
    /// Читает страницу аватаров, ждущих проверки.
    /// </summary>
    /// <remarks>
    /// Порядок задаёт сервер: <c>avatar_submitted_at ASC, id ASC</c>. Пустой курсор —
    /// первая страница; размер страницы ограничен сервером диапазоном 1..100.
    /// </remarks>
    /// <param name="cursor">Непрозрачный курсор следующей страницы либо <c>null</c>.</param>
    /// <param name="pageSize">Размер страницы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница строк очереди и курсор продолжения.</returns>
    /// <exception cref="BarberSearchApiException">Сессия истекла, роль не администратор или API ответил ошибкой.</exception>
    Task<PagedResponse<AdminAvatarPhotoResponse>> GetAvatarsAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает страницу работ портфолио, ждущих проверки.
    /// </summary>
    /// <remarks>
    /// Порядок задаёт сервер: <c>created_at ASC, id ASC</c> — отдельной метки поступления
    /// у портфолио нет.
    /// </remarks>
    /// <param name="cursor">Непрозрачный курсор следующей страницы либо <c>null</c>.</param>
    /// <param name="pageSize">Размер страницы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница строк очереди и курсор продолжения.</returns>
    /// <exception cref="BarberSearchApiException">Сессия истекла, роль не администратор или API ответил ошибкой.</exception>
    Task<PagedResponse<AdminPortfolioPhotoResponse>> GetPortfolioAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Одобряет аватар пользователя.
    /// </summary>
    /// <remarks>
    /// Одобрение — допуск к публикации, а не публикация: скрытый администратором кадр
    /// остаётся скрытым. Ответ <c>204</c>, тело не читается.
    /// </remarks>
    /// <param name="userId">Пользователь, чей аватар одобряется.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача запроса.</returns>
    /// <exception cref="BarberSearchApiException">Сессия истекла, роль не администратор, кадр уже проверен или API ответил ошибкой.</exception>
    Task ApproveAvatarAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отклоняет аватар пользователя с обязательной причиной.
    /// </summary>
    /// <remarks>
    /// Сервер тримит причину и требует от 1 до 500 символов. Ответ <c>204</c>,
    /// тело не читается.
    /// </remarks>
    /// <param name="userId">Пользователь, чей аватар отклоняется.</param>
    /// <param name="reason">Причина отказа; сервер обрезает её по краям.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача запроса.</returns>
    /// <exception cref="BarberSearchApiException">Сессия истекла, роль не администратор, причина недопустима, кадр уже проверен или API ответил ошибкой.</exception>
    Task RejectAvatarAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Одобряет работу портфолио.
    /// </summary>
    /// <remarks>
    /// Публичность после одобрения решают <c>isPublished</c> владельца и
    /// <c>isBlocked</c>. Ответ <c>204</c>, тело не читается.
    /// </remarks>
    /// <param name="barberProfileId">Профиль, которому принадлежит фотография.</param>
    /// <param name="photoId">Одобряемая фотография.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача запроса.</returns>
    /// <exception cref="BarberSearchApiException">Сессия истекла, роль не администратор, кадр уже проверен или API ответил ошибкой.</exception>
    Task ApprovePortfolioPhotoAsync(
        Guid barberProfileId,
        Guid photoId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отклоняет работу портфолио с обязательной причиной.
    /// </summary>
    /// <remarks>
    /// Сервер тримит причину и требует от 1 до 500 символов. Ответ <c>204</c>,
    /// тело не читается.
    /// </remarks>
    /// <param name="barberProfileId">Профиль, которому принадлежит фотография.</param>
    /// <param name="photoId">Отклоняемая фотография.</param>
    /// <param name="reason">Причина отказа; сервер обрезает её по краям.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача запроса.</returns>
    /// <exception cref="BarberSearchApiException">Сессия истекла, роль не администратор, причина недопустима, кадр уже проверен или API ответил ошибкой.</exception>
    Task RejectPortfolioPhotoAsync(
        Guid barberProfileId,
        Guid photoId,
        string reason,
        CancellationToken cancellationToken = default);
}
