using System.Text.RegularExpressions;
using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Application.Users;
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
/// Доступ проверяется по БД на каждый запрос (<c>GetAvatarUrlAsync</c>): файл отдаётся, только
/// если сохранённая строка <b>совпадает</b> с запрошенным URL (Ordinal). Поэтому замена или
/// удаление аватара сразу делает старую ссылку <c>404</c>, даже если файл ещё лежит на диске.
/// Аватар публичен по определению: исключений «владельцу/админу» нет, и отказа с <c>403</c>
/// или <c>410</c> тоже — существование файла не раскрывается, отказ всегда один
/// <c>404 avatarMedia.notFound</c>.
/// </para>
/// <para>
/// <c>Cache-Control: public, max-age=86400</c>: после замены старый кадр может ещё сутки жить
/// в кэше другого браузера — то же ограничение, что у фотографий портфолио, и лечится оно
/// сменой имени файла (новый <c>mediaId</c>), а не <c>no-store</c> для всех.
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

    private const string CACHE_CONTROL_HEADER = "Cache-Control";
    private const string CACHE_CONTROL_PUBLIC = "public, max-age=86400";
    private const string NOSNIFF_HEADER = "X-Content-Type-Options";
    private const string NOSNIFF_VALUE = "nosniff";

    private readonly IUserRepository _repository;
    private readonly IPhotoStorage _storage;

    /// <summary>
    /// Создаёт контроллер раздачи.
    /// </summary>
    /// <param name="repository">Проверка сохранённого URL аватара по БД.</param>
    /// <param name="storage">Открытие файла по media-URL.</param>
    public AvatarMediaController(IUserRepository repository, IPhotoStorage storage)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(storage);

        _repository = repository;
        _storage = storage;
    }

    /// <summary>
    /// Отдаёт файл аватара, если он существует и сохранённый URL совпадает с запрошенным.
    /// </summary>
    /// <param name="userId">Пользователь из URL.</param>
    /// <param name="fileName">Имя файла: 32 hex-символа и разрешённое расширение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Файл с <c>Cache-Control</c> и <c>nosniff</c> либо <c>404</c>.</returns>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.ReadsPolicyName)]
    [HttpGet("{userId:guid}/{fileName}")]
    public async Task<IActionResult> Get(
        Guid userId,
        string fileName,
        CancellationToken cancellationToken)
    {
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

        var stored = await _repository.GetAvatarUrlAsync(userId, cancellationToken);
        if (stored.IsFailure)
        {
            return MediaNotFound();
        }

        // Точное сравнение с сохранённой строкой: заменённый или удалённый аватар —
        // это новый URL (или null), поэтому старая ссылка перестаёт работать сразу.
        if (!string.Equals(stored.Value, url, StringComparison.Ordinal))
        {
            return MediaNotFound();
        }

        var content = await _storage.OpenReadAsync(url, cancellationToken);
        if (content.IsFailure)
        {
            return MediaNotFound();
        }

        Response.Headers[CACHE_CONTROL_HEADER] = CACHE_CONTROL_PUBLIC;
        Response.Headers[NOSNIFF_HEADER] = NOSNIFF_VALUE;

        var avatar = content.Value;

        return File(avatar.Stream, avatar.ContentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// Единый <c>404</c> раздачи: причина не раскрывается, тело — обычный ProblemDetails.
    /// </summary>
    private static ObjectResult MediaNotFound()
        => FailureProblemDetails.CreateResult(
            new Failure(Error.NotFound(NOT_FOUND_CODE, NOT_FOUND_MESSAGE)));

    /// <summary>
    /// Формат имени файла: 32 hex-символа нижнего регистра, точка и <c>jpg|png|webp</c>.
    /// </summary>
    [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png|webp)$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();
}
