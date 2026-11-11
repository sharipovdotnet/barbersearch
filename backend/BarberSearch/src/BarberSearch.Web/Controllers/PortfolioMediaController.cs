using System.Text.RegularExpressions;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Domain.PortfolioPhotos;
using BarberSearch.Web.Common;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shared;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Раздача файлов фотографий портфолио по стабильному публичному URL.
/// </summary>
/// <remarks>
/// <para>
/// URL относительный: <c>/api/media/portfolio/{profileId:D}/{mediaId:N}.{jpg|png|webp}</c>.
/// Браузер разрешает его от origin SPA, а <c>/api</c> проксируется на API; абсолютные внешние
/// ссылки старых фотографий этим маршрутом не раздаются.
/// </para>
/// <para>
/// Видимость проверяется по БД на каждый запрос (<c>GetPortfolioPhotoAccessAsync</c>), а не
/// по одному URL: скрытая или заблокированная фотография отвечает <c>404</c> анониму и любому
/// чужому пользователю, даже если ссылка уже известна. Владельцу профиля и администратору
/// файл отдаётся — иначе превью скрытых работ в кабинете были бы битыми, — но с
/// <c>Cache-Control: private, no-store</c>. Отказ всегда <c>404</c>, никогда <c>403</c> или
/// <c>410</c>: существование кадра не раскрывается.
/// </para>
/// <para>
/// Bearer-клиент для скрытых фото не поддерживается осознанно: UI всегда ходит с cookie,
/// а публичные фото доступны анонимно.
/// </para>
/// </remarks>
[ApiController]
[Route("api/media/portfolio")]
public sealed partial class PortfolioMediaController : ControllerBase
{
    /// <summary>
    /// Код <c>404</c> для всех отказов раздачи: неверное имя, чужая/скрытая фотография,
    /// отсутствующий файл. Один код, чтобы ответ не различал причины.
    /// </summary>
    private const string NOT_FOUND_CODE = "portfolioMedia.notFound";

    private const string NOT_FOUND_MESSAGE = "The portfolio photo file was not found.";

    private const string CACHE_CONTROL_HEADER = "Cache-Control";
    private const string CACHE_CONTROL_PUBLIC = "public, max-age=86400";
    private const string CACHE_CONTROL_PRIVATE = "private, no-store";
    private const string NOSNIFF_HEADER = "X-Content-Type-Options";
    private const string NOSNIFF_VALUE = "nosniff";

    private readonly IBarberProfileRepository _repository;
    private readonly IPhotoStorage _storage;
    private readonly ICurrentUser _currentUser;

    /// <summary>
    /// Создаёт контроллер раздачи.
    /// </summary>
    /// <param name="repository">Проверка видимости и владельца фотографии по БД.</param>
    /// <param name="storage">Открытие файла по media-URL.</param>
    /// <param name="currentUser">Текущий пользователь: владелец или администратор.</param>
    public PortfolioMediaController(
        IBarberProfileRepository repository,
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
    /// Отдаёт файл фотографии, если он существует и доступен вызывающему.
    /// </summary>
    /// <param name="barberProfileId">Профиль барбера из URL.</param>
    /// <param name="fileName">Имя файла: 32 hex-символа и разрешённое расширение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Файл с <c>Cache-Control</c> и <c>nosniff</c> либо <c>404</c>.</returns>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.ReadsPolicyName)]
    [HttpGet("{barberProfileId:guid}/{fileName}")]
    public async Task<IActionResult> Get(
        Guid barberProfileId,
        string fileName,
        CancellationToken cancellationToken)
    {
        // Имя проверяется регуляркой, а не «разбором как получится»: через имя файла
        // в путь к диску не должны попасть ни разделители, ни точки, ни кодирование.
        if (string.IsNullOrEmpty(fileName) || !FileNamePattern().IsMatch(fileName))
        {
            return MediaNotFound();
        }

        var mediaId = Guid.ParseExact(fileName[..32], "N");
        var extension = fileName[33..];
        var url = PortfolioMediaUrl.Build(barberProfileId, mediaId, extension);

        var access = await _repository.GetPortfolioPhotoAccessAsync(
            barberProfileId,
            url,
            cancellationToken);
        if (access is null)
        {
            return MediaNotFound();
        }

        var visible = access.IsPublished && !access.IsBlocked;
        var allowed = visible
            || _currentUser.IsAdmin
            || (_currentUser.TryGetUserId(out var userId) && userId == access.OwnerUserId);
        if (!allowed)
        {
            return MediaNotFound();
        }

        var content = await _storage.OpenReadAsync(url, cancellationToken);
        if (content.IsFailure)
        {
            return MediaNotFound();
        }

        // Публичный кадр кэшируется на сутки; скрытый и заблокированный — нет: после
        // разблокировки/публикации клиент не должен показывать устаревший «нет файла».
        Response.Headers[CACHE_CONTROL_HEADER] = visible ? CACHE_CONTROL_PUBLIC : CACHE_CONTROL_PRIVATE;
        Response.Headers[NOSNIFF_HEADER] = NOSNIFF_VALUE;

        var photo = content.Value;

        return File(photo.Stream, photo.ContentType, enableRangeProcessing: true);
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
