using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles.GetBarberProfile;
using BarberSearch.Application.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Web.Common;
using Microsoft.AspNetCore.Mvc;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Публичное чтение барберов: поиск и карточка.
/// </summary>
/// <remarks>
/// Доступ закрыт fallback-политикой, как и весь API: анонимных read-эндпоинтов пока нет.
/// Поиск отдаёт только активных и подтверждённых барберов; неподтверждённые и неактивные
/// профили не видны и в карточке (для них возвращается 404).
/// </remarks>
[ApiController]
[Route("api/barbers")]
public class BarbersController : ApiController
{
    /// <summary>
    /// Ищет барберов с фильтрами и keyset-пагинацией.
    /// </summary>
    /// <param name="city">Город салона. Пустая строка означает «без фильтра».</param>
    /// <param name="specializationId">Специализация. Пустой Guid означает «без фильтра».</param>
    /// <param name="serviceId">Услуга каталога. Пустой Guid означает «без фильтра».</param>
    /// <param name="date">Дата, на которую у барбера есть рабочий день.</param>
    /// <param name="cursor">Непрозрачный курсор следующей страницы из предыдущего ответа.</param>
    /// <param name="pageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<BarberSearchItemResponse>>> Search(
        [FromQuery] string? city,
        [FromQuery] Guid? specializationId,
        [FromQuery] Guid? serviceId,
        [FromQuery] DateOnly? date,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromServices] IQueryHandler<PagedResponse<BarberSearchItemResponse>, SearchBarbersQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new SearchBarbersQuery(
                city,
                specializationId,
                serviceId,
                date,
                cursor,
                pageSize),
            cancellationToken));

    /// <summary>
    /// Возвращает карточку барбера: профиль, услуги, салоны, специализации и рейтинг.
    /// </summary>
    /// <param name="barberProfileId">Идентификатор профиля барбера.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("{barberProfileId:guid}")]
    public async Task<ActionResult<BarberProfileResponse>> Get(
        Guid barberProfileId,
        [FromServices] IQueryHandler<BarberProfileResponse, GetBarberProfileQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetBarberProfileQuery(barberProfileId),
            cancellationToken));
}
