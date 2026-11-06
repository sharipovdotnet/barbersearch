using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Salons.ActivateSalon;
using BarberSearch.Application.Salons.AddSalonBarber;
using BarberSearch.Application.Salons.Admin.ApproveSalon;
using BarberSearch.Application.Salons.Admin.RejectSalon;
using BarberSearch.Application.Salons.CreateOwnSalon;
using BarberSearch.Application.Salons.CreateSalon;
using BarberSearch.Application.Salons.DeactivateSalon;
using BarberSearch.Application.Salons.GetSalonBarbers;
using BarberSearch.Application.Salons.GetSalonDirectory;
using BarberSearch.Application.Salons.RemoveSalonBarber;
using BarberSearch.Application.Salons.SearchSalonBarberCandidates;
using BarberSearch.Application.Salons.UpdateSalonContactInfo;
using BarberSearch.Application.Salons.UpdateSalonLocation;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.AddSalonBarber;
using BarberSearch.Contracts.Salons.CreateSalon;
using BarberSearch.Contracts.Salons.GetSalonBarbers;
using BarberSearch.Contracts.Salons.GetSalonDirectory;
using BarberSearch.Contracts.Salons.RejectSalon;
using BarberSearch.Contracts.Salons.UpdateSalonContactInfo;
using BarberSearch.Contracts.Salons.UpdateSalonLocation;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using BarberSearch.Web.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Салоны: публичный каталог для выбора и админское управление.
/// </summary>
/// <remarks>
/// Расписание и услуги салона здесь не живут: слоты считаются по паре (барбер, салон),
/// поэтому расписание адресуется через профиль барбера. Чтение каталога открыто анонимно
/// (барбер выбирает салон до входа в кабинет, а витрина — без входа).
/// <para>
/// Создание: администратор — <c>POST /api/salons</c> (сразу активный салон), барбер —
/// <c>POST /api/salons/own</c> (салон уходит на модерацию). Адрес и контакты правят
/// администратор или владелец; активация и модерация — только администратор.
/// </para>
/// <para>
/// Состав салона: владелец (или администратор) читает прикреплённых барберов, ищет
/// кандидатов по имени/email и прикрепляет или открепляет их. Привязка — та же строка
/// <c>barber_salons</c>, что и у выбора салона самим барбером; владельца салона убрать
/// нельзя.
/// </para>
/// </remarks>
[ApiController]
[Route("api/salons")]
public class SalonsController : ApiController
{
    /// <summary>
    /// Публичный каталог активных салонов: фильтр по городу и поиск по названию и адресу.
    /// </summary>
    /// <remarks>
    /// Анонимное действие с лимитом <c>reads</c> 120 запросов в минуту на адрес клиента.
    /// Ответ — голый JSON-массив без пагинации: справочник для выбора, а не пейджер.
    /// </remarks>
    /// <param name="city">Город; пустая строка означает «без фильтра».</param>
    /// <param name="search">Поиск по названию и адресу; пустая строка — без фильтра.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitSettings.ReadsPolicyName)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SalonDirectoryItemResponse>>> GetDirectory(
        [FromQuery] string? city,
        [FromQuery] string? search,
        [FromServices] IQueryHandler<IReadOnlyList<SalonDirectoryItemResponse>, GetSalonDirectoryQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetSalonDirectoryQuery(city, search),
            cancellationToken));

    /// <summary>
    /// Создаёт салон.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost]
    public async Task<ActionResult<CreatedResourceResponse>> Create(
        [FromBody] CreateSalonRequest request,
        [FromServices] ICommandHandler<Guid, CreateSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToCreatedResult(await handler.Handle(new CreateSalonCommand(request), cancellationToken));

    /// <summary>
    /// Создаёт салон от имени барбера: салон уходит на модерацию.
    /// </summary>
    /// <remarks>
    /// Политика <c>BarberOnly</c>: салон создаёт барбер с профилем; профиль берётся из
    /// claim'а, тело его не содержит. Салон создаётся в статусе <c>Pending</c> и сразу
    /// привязывается к профилю в одной транзакции. Ответ — <c>201 { id }</c>; барбер без
    /// профиля получает <c>403 barberProfile.notFound</c>. Повторная отправка на модерацию
    /// после отказа в этом срезе не поддерживается.
    /// </remarks>
    /// <param name="request">Поля салона.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize(Policy = AuthorizationPolicies.BarberOnly)]
    [HttpPost("own")]
    public async Task<ActionResult<CreatedResourceResponse>> CreateOwn(
        [FromBody] CreateSalonRequest request,
        [FromServices] ICommandHandler<Guid, CreateOwnSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToCreatedResult(await handler.Handle(new CreateOwnSalonCommand(request), cancellationToken));

    /// <summary>
    /// Меняет адрес и координаты салона.
    /// </summary>
    /// <remarks>
    /// Доступно администратору и владельцу салона; чужой салон — <c>403 auth.forbidden</c>.
    /// Проверка владения живёт в обработчике: он уже загрузил агрегат.
    /// </remarks>
    [Authorize]
    [HttpPut("{salonId:guid}/location")]
    public async Task<ActionResult> UpdateLocation(
        Guid salonId,
        [FromBody] UpdateSalonLocationRequest request,
        [FromServices] ICommandHandler<UpdateSalonLocationCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateSalonLocationCommand(
                salonId,
                request.Address,
                request.City,
                request.Country,
                request.TimeZoneId,
                request.Latitude,
                request.Longitude,
                request.PostalCode),
            cancellationToken));

    /// <summary>
    /// Меняет контактные данные салона.
    /// </summary>
    /// <remarks>
    /// Доступно администратору и владельцу салона; чужой салон — <c>403 auth.forbidden</c>.
    /// </remarks>
    [Authorize]
    [HttpPut("{salonId:guid}/contact-info")]
    public async Task<ActionResult> UpdateContactInfo(
        Guid salonId,
        [FromBody] UpdateSalonContactInfoRequest request,
        [FromServices] ICommandHandler<UpdateSalonContactInfoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateSalonContactInfoCommand(salonId, request.PhoneNumber, request.WebsiteUrl),
            cancellationToken));

    /// <summary>
    /// Возвращает барберов, прикреплённых к салону.
    /// </summary>
    /// <remarks>
    /// Доступно администратору и владельцу салона; чужой салон — <c>403 auth.forbidden</c>,
    /// аноним — <c>401</c>. Ответ — все привязки, включая выключенные профили и
    /// неподтверждённых мастеров: владелец управляет составом и до публикации.
    /// <c>userId</c> позволяет экрану отличить владельца салона.
    /// </remarks>
    /// <param name="salonId">Идентификатор салона.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize]
    [HttpGet("{salonId:guid}/barbers")]
    public async Task<ActionResult<IReadOnlyList<SalonBarberResponse>>> GetBarbers(
        Guid salonId,
        [FromServices] IQueryHandler<IReadOnlyList<SalonBarberResponse>, GetSalonBarbersQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetSalonBarbersQuery(salonId),
            cancellationToken));

    /// <summary>
    /// Ищет активных барберов, ещё не прикреплённых к салону, по имени или email.
    /// </summary>
    /// <remarks>
    /// Доступно администратору и владельцу салона. Строка короче трёх символов —
    /// <c>400 salon.barbers.search.tooShort</c>, длиннее ста — <c>400
    /// salon.barbers.search.tooLong</c>. Ответ ограничен десятью строками, пагинации нет:
    /// это подсказка формы. Уже прикреплённые профили не возвращаются.
    /// </remarks>
    /// <param name="salonId">Идентификатор салона.</param>
    /// <param name="search">Имя или email барбера.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize]
    [HttpGet("{salonId:guid}/barber-candidates")]
    public async Task<ActionResult<IReadOnlyList<SalonBarberResponse>>> SearchBarberCandidates(
        Guid salonId,
        [FromQuery] string? search,
        [FromServices] IQueryHandler<IReadOnlyList<SalonBarberResponse>, SearchSalonBarberCandidatesQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new SearchSalonBarberCandidatesQuery(salonId, search),
            cancellationToken));

    /// <summary>
    /// Прикрепляет барбера к салону.
    /// </summary>
    /// <remarks>
    /// Доступно администратору и владельцу салона; чужой салон — <c>403 auth.forbidden</c>.
    /// Профиль должен существовать (<c>404 barberProfile.notFound</c>) и быть активным
    /// (<c>409 salon.barbers.inactive</c>); повторная привязка — <c>409</c>
    /// <c>barberProfile.addSalon.duplicate</c>. Первый салон профиля становится основным.
    /// Ответ — <c>204</c>.
    /// </remarks>
    /// <param name="salonId">Идентификатор салона.</param>
    /// <param name="request">Прикрепляемый профиль барбера.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize]
    [HttpPost("{salonId:guid}/barbers")]
    public async Task<ActionResult> AddBarber(
        Guid salonId,
        [FromBody] AddSalonBarberRequest request,
        [FromServices] ICommandHandler<AddSalonBarberCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddSalonBarberCommand(salonId, request.BarberProfileId),
            cancellationToken));

    /// <summary>
    /// Открепляет барбера от салона.
    /// </summary>
    /// <remarks>
    /// Доступно администратору и владельцу салона. Владельца салона убрать нельзя —
    /// <c>409 salon.barbers.owner</c>; отсутствующая привязка — <c>404
    /// barberProfile.removeSalon.notFound</c>. Расписание пары (барбер, салон) уходит
    /// вместе с привязкой; прошлые записи клиентов остаются историей.
    /// </remarks>
    /// <param name="salonId">Идентификатор салона.</param>
    /// <param name="barberProfileId">Идентификатор открепляемого профиля.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize]
    [HttpDelete("{salonId:guid}/barbers/{barberProfileId:guid}")]
    public async Task<ActionResult> RemoveBarber(
        Guid salonId,
        Guid barberProfileId,
        [FromServices] ICommandHandler<RemoveSalonBarberCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RemoveSalonBarberCommand(salonId, barberProfileId),
            cancellationToken));

    /// <summary>
    /// Активирует салон.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{salonId:guid}/activation")]
    public async Task<ActionResult> Activate(
        Guid salonId,
        [FromServices] ICommandHandler<ActivateSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new ActivateSalonCommand(salonId), cancellationToken));

    /// <summary>
    /// Деактивирует салон.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpDelete("{salonId:guid}/activation")]
    public async Task<ActionResult> Deactivate(
        Guid salonId,
        [FromServices] ICommandHandler<DeactivateSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new DeactivateSalonCommand(salonId), cancellationToken));

    /// <summary>
    /// Одобряет салон на модерации: салон появляется в поиске и доступен для записи.
    /// </summary>
    /// <remarks>
    /// Только администратор. Повторное решение — <c>409 salon.review.invalidStatus</c>;
    /// неизвестный салон — <c>404 salon.notFound</c>. Решение пишется в журнал как
    /// <c>salon.updated</c> с изменением <c>moderationStatus</c>.
    /// </remarks>
    /// <param name="salonId">Идентификатор салона.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{salonId:guid}/approval")]
    public async Task<ActionResult> Approve(
        Guid salonId,
        [FromServices] ICommandHandler<ApproveSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(new ApproveSalonCommand(salonId), cancellationToken));

    /// <summary>
    /// Отклоняет салон на модерации с обязательной причиной.
    /// </summary>
    /// <remarks>
    /// Только администратор. Пустая причина — <c>400 salon.reject.reasonRequired</c>,
    /// длиннее 500 символов — <c>400 salon.reject.reasonTooLong</c>, повторное решение —
    /// <c>409 salon.review.invalidStatus</c>. Отклонённый салон остаётся невидимым.
    /// </remarks>
    /// <param name="salonId">Идентификатор салона.</param>
    /// <param name="request">Причина отказа.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{salonId:guid}/rejection")]
    public async Task<ActionResult> Reject(
        Guid salonId,
        [FromBody] RejectSalonRequest request,
        [FromServices] ICommandHandler<RejectSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RejectSalonCommand(salonId, request.Reason),
            cancellationToken));
}
