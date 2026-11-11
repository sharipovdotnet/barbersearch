using System.Text.RegularExpressions;
using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles;
using BarberSearch.Application.PortfolioPhotos;
using BarberSearch.Domain.Common;
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
/// по одному URL: публично кадр виден, только когда он одобрен
/// (<c>ModerationStatus = Approved</c>), опубликован владельцем (<c>IsPublished</c>) и не
/// заблокирован (<c>IsBlocked</c>). Неодобренная, скрытая или заблокированная фотография
/// отвечает <c>404</c> анониму и любому чужому пользователю, даже если ссылка уже известна.
/// Владельцу профиля и администратору файл отдаётся при любом статусе — иначе превью скрытых
/// и ждущих проверки работ в кабинете были бы битыми, — но с
/// <c>Cache-Control: private, no-store</c>. Отказ всегда <c>404</c>, никогда <c>403</c> или
/// <c>410</c>: существование кадра не раскрывается.
/// </para>
/// <para>
/// <c>?size=sm|md|lg</c> — вариант того же файла (WebP шириной 256/512/1024 px), а не новый
/// маршрут: путь и имя файла не меняются. Разбор, <c>ETag</c>, <c>304</c> и политика кэша
/// живут в <see cref="MediaDelivery"/>; здесь остаются только имя файла и проверка доступа.
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
    /// <remarks>
    /// <c>?size=sm|md|lg</c> выбирает вариант того же файла; отсутствие параметра — исходник.
    /// Недопустимое значение (пустое, неизвестное, повторённое) — тот же <c>404</c>, что и
    /// отсутствующий файл: набора размеров не существует, поэтому запрос не «исправляется».
    /// </remarks>
    /// <param name="barberProfileId">Профиль барбера из URL.</param>
    /// <param name="fileName">Имя файла: 32 hex-символа и разрешённое расширение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Файл с <c>ETag</c>, <c>Cache-Control</c> и <c>nosniff</c>, <c>304</c> при совпадении
    /// <c>If-None-Match</c> либо <c>404</c> с <c>no-store</c>.
    /// </returns>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.ReadsPolicyName)]
    [HttpGet("{barberProfileId:guid}/{fileName}")]
    public async Task<IActionResult> Get(
        Guid barberProfileId,
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

        // Публичная видимость — допуск к публикации, намерение владельца и отсутствие
        // административной маски одновременно. Неодобренный кадр не виден никому, кроме
        // владельца профиля и администратора.
        var visible = access.ModerationStatus == PhotoModerationStatus.Approved
            && access.IsPublished
            && !access.IsBlocked;
        var allowed = visible
            || _currentUser.IsAdmin
            || (_currentUser.TryGetUserId(out var userId) && userId == access.OwnerUserId);
        if (!allowed)
        {
            return MediaNotFound();
        }

        // Проверка доступа идёт по исходному URL: query не участвует, поэтому варианты
        // подчиняются тому же правилу, что и исходник.
        var content = await _storage.OpenReadAsync(url, variant, cancellationToken);
        if (content.IsFailure)
        {
            return MediaNotFound();
        }

        // Публичный кадр перепроверяется (max-age=0 + ETag), скрытый и заблокированный
        // не кэшируются: после разблокировки/публикации клиент не покажет устаревший ответ.
        return MediaDelivery.CreateFileResult(Request, Response, content.Value, visible);
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
