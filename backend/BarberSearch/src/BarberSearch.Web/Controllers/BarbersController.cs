using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles.GetBarberProfile;
using BarberSearch.Application.BarberProfiles.GetBarberSlots;
using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberSlots;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Web.Common;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Публичное чтение барберов: поиск, карточка и слоты.
/// </summary>
/// <remarks>
/// Все три действия открыты анонимно (<c>[AllowAnonymous]</c>): витрина «найти барбера
/// и записаться» должна работать без входа, иначе fallback-политика закрывает её целиком.
/// Вместо 401 злоупотребление ограничивает политика <c>reads</c> — 120 запросов в минуту
/// на адрес клиента (<c>Connection.RemoteIpAddress</c> после <c>UseForwardedHeaders</c>);
/// лишний запрос получает <c>429</c> с <c>Retry-After</c>. Ответы не содержат PII: поиск
/// отдаёт только активных и подтверждённых барберов, карточка — активные услуги и активные
/// салоны (неактивный или неподтверждённый профиль даёт 404), контактных данных
/// (email/телефон) в response-контрактах нет.
/// </remarks>
[ApiController]
[Route("api/barbers")]
public class BarbersController : ApiController
{
    /// <summary>
    /// Ищет барберов с фильтрами и keyset-пагинацией.
    /// </summary>
    /// <remarks>
    /// Анонимное действие: 401 не возвращается, лимит — <c>reads</c> 120 запросов в минуту
    /// на адрес клиента. Отдаются только активные и подтверждённые профили.
    /// </remarks>
    /// <param name="city">Город салона. Пустая строка означает «без фильтра».</param>
    /// <param name="specializationId">Специализация. Пустой Guid означает «без фильтра».</param>
    /// <param name="hairServiceId">
    /// Услуга справочника (<c>hair_services.id</c>). Пустой Guid означает «без фильтра».
    /// Это не <c>barberServiceId</c> из брони (R-20).
    /// </param>
    /// <param name="date">Дата, на которую у барбера есть рабочий день.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы из предыдущего ответа.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.ReadsPolicyName)]
    [HttpGet]
    public async Task<ActionResult<PagedResponse<BarberSearchItemResponse>>> Search(
        [FromQuery] string? city,
        [FromQuery] Guid? specializationId,
        [FromQuery] Guid? hairServiceId,
        [FromQuery] DateOnly? date,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<PagedResponse<BarberSearchItemResponse>, SearchBarbersQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new SearchBarbersQuery(
                city,
                specializationId,
                hairServiceId,
                date,
                cursor,
                pageSize),
            cancellationToken));

    /// <summary>
    /// Возвращает карточку барбера: профиль, услуги, салоны, специализации и рейтинг.
    /// </summary>
    /// <remarks>
    /// Анонимное действие с лимитом <c>reads</c> 120/мин на IP. Карточка собирается только
    /// из публичных полей и активных услуг/салонов; для неактивного или неподтверждённого
    /// профиля — 404.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля барбера.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.ReadsPolicyName)]
    [HttpGet("{barberProfileId:guid}")]
    public async Task<ActionResult<BarberProfileResponse>> Get(
        Guid barberProfileId,
        [FromServices] IQueryHandler<BarberProfileResponse, GetBarberProfileQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetBarberProfileQuery(barberProfileId),
            cancellationToken));

    /// <summary>
    /// Возвращает свободные слоты барбера на дату.
    /// </summary>
    /// <remarks>
    /// Анонимное действие с лимитом <c>reads</c> 120/мин на IP: слоты — часть витрины,
    /// без них незалогиненный клиент не может выбрать время.
    /// День считается в часовом поясе салона: в ответе есть <c>timeZoneId</c> и границы дня
    /// в UTC. Без <paramref name="salonId"/> берётся основной активный салон; без
    /// <paramref name="hairServiceId"/> шаг сетки — <c>Constants.DEFAULT_SLOT_DURATION_MINUTES</c>
    /// (30 минут), иначе длительность услуги.
    /// <paramref name="hairServiceId"/> — услуга справочника; для брони в ответе есть
    /// <c>barberServiceId</c> той пары, по которой считалась длительность (R-20).
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля барбера.</param>
    /// <param name="date">Локальная дата салона.</param>
    /// <param name="hairServiceId">Идентификатор услуги справочника (<c>hair_services.id</c>).</param>
    /// <param name="salonId">Идентификатор салона.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.ReadsPolicyName)]
    [HttpGet("{barberProfileId:guid}/slots")]
    public async Task<ActionResult<BarberSlotsResponse>> GetSlots(
        Guid barberProfileId,
        [FromQuery] DateOnly? date,
        [FromQuery] Guid? hairServiceId,
        [FromQuery] Guid? salonId,
        [FromServices] IQueryHandler<BarberSlotsResponse, GetBarberSlotsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetBarberSlotsQuery(barberProfileId, date, hairServiceId, salonId),
            cancellationToken));
}
