using System.Text.RegularExpressions;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Application.Users;
using BarberSearch.Domain.Common;
using BarberSearch.Domain.Users;
using BarberSearch.Web.Common;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shared;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Раздача файлов аватаров по стабильному публичному URL.
/// </summary>
/// <remarks>
/// <para>
/// URL относительный: <c>/api/media/avatars/{userId:D}/{mediaId:N}.{jpg|png|webp}</c>. Браузер
/// разрешает его от origin SPA, а <c>/api</c> проксируется на API; абсолютные внешние ссылки
/// легаси-пути администратора этим маршрутом не раздаются.
/// </para>
/// <para>
/// Доступ проверяется по БД на каждый запрос (<c>GetAvatarAccessAsync</c>): файл отдаётся,
/// только если сохранённая строка <b>совпадает</b> с запрошенным URL (Ordinal). Поэтому замена
/// или удаление аватара сразу делает старую ссылку <c>404</c>, даже если файл ещё лежит на диске.
/// Публично кадр виден только при <c>AvatarModerationStatus = Approved</c> и
/// <c>IsAvatarBlocked = false</c>; неодобренный (<c>Pending</c>/<c>Rejected</c>) файл отдаётся
/// <b>владельцу</b> — иначе превью в кабинете было бы битым, — а скрытый администратором
/// (<c>IsAvatarBlocked</c>) не отдаётся даже владельцу: его видит только администратор.
/// Всем остальным — тот же <c>404 avatarMedia.notFound</c>. Отказа с <c>403</c> или <c>410</c>
/// нет: существование кадра не раскрывается.
/// </para>
/// <para>
/// <c>Cache-Control: public, max-age=0, must-revalidate</c> + сильный <c>ETag</c> — только у
/// публичного кадра: он обязан перепроверяться, чтобы скрытый или заменённый аватар исчезал
/// сразу после перезагрузки страницы, а повторный показ давал <c>304</c> вместо повторной
/// загрузки файла. Неодобренный (владельцу) и скрытый (администратору) отдаются с
/// <c>private, no-store</c>. Политику кэша, <c>ETag</c> и <c>304</c> реализует
/// <see cref="MediaDelivery"/> — один код на оба маршрута раздачи.
/// </para>
/// <para>
/// <c>?size=sm|md|lg</c> — вариант того же файла (WebP шириной 96/192/384 px). Проверка
/// доступа идёт по исходному URL без query, поэтому вариант подчиняется тому же правилу, что
/// и исходник; если вариант невозможен, отдаётся исходник.
/// </para>
/// </remarks>
[ApiController]
[Route("api/media/avatars")]
public sealed partial class AvatarMediaController : ControllerBase
{
    /// <summary>
    /// Код <c>404</c> для всех отказов раздачи: неверное имя, чужой/заменённый URL,
    /// отсутствующий файл. Один код, чтобы ответ не различал причины.
    /// </summary>
    private const string NOT_FOUND_CODE = "avatarMedia.notFound";

    private const string NOT_FOUND_MESSAGE = "The avatar file was not found.";

    private readonly IUserRepository _repository;
    private readonly IPhotoStorage _storage;
    private readonly ICurrentUser _currentUser;

    /// <summary>
    /// Создаёт контроллер раздачи.
    /// </summary>
    /// <param name="repository">Проверка сохранённого URL и блокировки аватара по БД.</param>
    /// <param name="storage">Открытие файла по media-URL.</param>
    /// <param name="currentUser">Текущий пользователь: администратор видит скрытый файл.</param>
    public AvatarMediaController(
        IUserRepository repository,
        IPhotoStorage storage,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(currentUser);

        _repository = repository;
        _storage = storage;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Отдаёт файл аватара, если он существует, сохранённый URL совпадает с запрошенным
    /// и кадр доступен вызывающему: публично — только одобренный и не скрытый, владельцу —
    /// ещё и неодобренный, администратору — любой.
    /// </summary>
    /// <remarks>
    /// <c>?size=sm|md|lg</c> выбирает вариант того же файла; отсутствие параметра — исходник.
    /// Недопустимое значение (пустое, неизвестное, повторённое) — тот же <c>404</c>, что и
    /// отсутствующий файл.
    /// </remarks>
    /// <param name="userId">Пользователь из URL.</param>
    /// <param name="fileName">Имя файла: 32 hex-символа и разрешённое расширение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Файл с <c>ETag</c>, <c>Cache-Control</c> и <c>nosniff</c>, <c>304</c> при совпадении
    /// <c>If-None-Match</c> либо <c>404</c> с <c>no-store</c>.
    /// </returns>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.ReadsPolicyName)]
    [HttpGet("{userId:guid}/{fileName}")]
    public async Task<IActionResult> Get(
        Guid userId,
        string fileName,
        CancellationToken cancellationToken)
    {
        // Представление разбирается до обращения к диску и БД: неизвестный размер — это
        // невалидный запрос, а не повод читать файл.
        if (!MediaDelivery.TryParseVariant(Request.Query, out var variant))
        {
            return MediaNotFound();
        }

        // Имя проверяется регуляркой, а не «разбором как получится»: через имя файла
        // в путь к диску не должны попасть ни разделители, ни точки, ни кодирование.
        if (userId == Guid.Empty
            || string.IsNullOrEmpty(fileName)
            || !FileNamePattern().IsMatch(fileName))
        {
            return MediaNotFound();
        }

        var mediaId = Guid.ParseExact(fileName[..32], "N");
        var extension = fileName[33..];
        var url = UserAvatarMediaUrl.Build(userId, mediaId, extension);

        var access = await _repository.GetAvatarAccessAsync(userId, cancellationToken);
        if (access.IsFailure)
        {
            return MediaNotFound();
        }

        // Точное сравнение с сохранённой строкой: заменённый или удалённый аватар —
        // это новый URL (или null), поэтому старая ссылка перестаёт работать сразу.
        if (!string.Equals(access.Value.AvatarUrl, url, StringComparison.Ordinal))
        {
            return MediaNotFound();
        }

        // Публичный кадр — только одобренный и не скрытый администратором. Блокировка
        // проверяется после точного совпадения URL, иначе старая ссылка скрытого фото
        // могла бы отдаться после замены.
        var avatar = access.Value;
        var publicVisible = avatar.ModerationStatus == PhotoModerationStatus.Approved
            && !avatar.IsAvatarBlocked;

        // Владелец видит превью неодобренного кадра в кабинете (Pending/Rejected), но никогда
        // не видит скрытый: административная маска сильнее владения.
        var isOwner = _currentUser.TryGetUserId(out var callerId) && callerId == userId;
        var ownerPreview = isOwner
            && !avatar.IsAvatarBlocked
            && (avatar.ModerationStatus == PhotoModerationStatus.Pending
                || avatar.ModerationStatus == PhotoModerationStatus.Rejected);

        if (!publicVisible && !_currentUser.IsAdmin && !ownerPreview)
        {
            return MediaNotFound();
        }

        // Проверка доступа идёт по исходному URL: query не участвует, поэтому вариант
        // подчиняется тому же правилу, что и исходник.
        var content = await _storage.OpenReadAsync(url, variant, cancellationToken);
        if (content.IsFailure)
        {
            return MediaNotFound();
        }

        // Публичный кадр перепроверяется (max-age=0 + ETag), неодобренный и скрытый
        // не кэшируются: после одобрения или возврата фото клиент не покажет устаревший ответ.
        return MediaDelivery.CreateFileResult(Request, Response, content.Value, publicVisible);
    }

    /// <summary>
    /// Единый <c>404</c> раздачи: причина не раскрывается, тело — обычный ProblemDetails,
    /// а ответ не кэшируется.
    /// </summary>
    private ObjectResult MediaNotFound()
    {
        MediaDelivery.MarkNotFound(Response);

        return FailureProblemDetails.CreateResult(
            new Failure(Error.NotFound(NOT_FOUND_CODE, NOT_FOUND_MESSAGE)));
    }

    /// <summary>
    /// Формат имени файла: 32 hex-символа нижнего регистра, точка и <c>jpg|png|webp</c>.
    /// </summary>
    [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png|webp)$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();
}
