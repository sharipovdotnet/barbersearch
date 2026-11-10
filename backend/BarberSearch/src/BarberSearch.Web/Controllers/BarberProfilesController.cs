using BarberSearch.Application.Abstractions;
using BarberSearch.Application.BarberProfiles.ActivateBarberProfile;
using BarberSearch.Application.BarberProfiles.AddBarberProfilePhoto;
using BarberSearch.Application.BarberProfiles.AddBarberProfileSalon;
using BarberSearch.Application.BarberProfiles.AddBarberProfileService;
using BarberSearch.Application.BarberProfiles.AddBarberProfileSpecialization;
using BarberSearch.Application.BarberProfiles.AddBarberProfileTimeOff;
using BarberSearch.Application.BarberProfiles.ChangeBarberProfileGrade;
using BarberSearch.Application.BarberProfiles.CreateBarberProfile;
using BarberSearch.Application.BarberProfiles.DeactivateBarberProfile;
using BarberSearch.Application.BarberProfiles.FindBarberProfileSalon;
using BarberSearch.Application.BarberProfiles.GetBarberProfileSalons;
using BarberSearch.Application.BarberProfiles.GetOwnBarberProfile;
using BarberSearch.Application.BarberProfiles.RemoveBarberProfilePhoto;
using BarberSearch.Application.BarberProfiles.RemoveBarberProfileSalon;
using BarberSearch.Application.BarberProfiles.RemoveBarberProfileService;
using BarberSearch.Application.BarberProfiles.RemoveBarberProfileSpecialization;
using BarberSearch.Application.BarberProfiles.RemoveBarberProfileTimeOff;
using BarberSearch.Application.BarberProfiles.ReorderBarberProfilePhoto;
using BarberSearch.Application.BarberProfiles.ReorderBarberProfilePhotos;
using BarberSearch.Application.BarberProfiles.SetBarberProfilePrimarySalon;
using BarberSearch.Application.BarberProfiles.UnverifyBarberProfile;
using BarberSearch.Application.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Application.BarberProfiles.UpdateBarberProfileRating;
using BarberSearch.Application.BarberProfiles.UpdateBarberProfileService;
using BarberSearch.Application.BarberProfiles.VerifyBarberProfile;
using BarberSearch.Application.PortfolioPhotos.UploadPortfolioPhoto;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfilePhoto;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileSalon;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileService;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileTimeOff;
using BarberSearch.Contracts.BarberProfiles.ChangeBarberProfileGrade;
using BarberSearch.Contracts.BarberProfiles.CreateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.FindBarberProfileSalon;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfileSalons;
using BarberSearch.Contracts.BarberProfiles.ReorderBarberProfilePhoto;
using BarberSearch.Contracts.BarberProfiles.ReorderBarberProfilePhotos;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfileRating;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfileService;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain;
using BarberSearch.Web.Auth;
using BarberSearch.Web.Common;
using BarberSearch.Web.Controllers.PortfolioPhotos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;

namespace BarberSearch.Web.Controllers;

/// <summary>
/// Профиль барбера — центр агрегата: сам профиль, грейд, рейтинг, верификация,
/// портфолио, салоны, услуги, специализации и отпуска.
/// </summary>
/// <remarks>
/// Дочерние сущности адресуются через профиль (<c>/api/barber-profiles/{id}/...</c>):
/// они правятся только через агрегат-корень, и маршрут это отражает.
/// </remarks>
[ApiController]
[Route("api/barber-profiles")]
public class BarberProfilesController : ApiController
{
    /// <summary>
    /// Предел всего multipart-запроса: 6 МиБ, чтобы обвязка формы помещалась вокруг
    /// файла в 5 МиБ (<c>Constants.MAX_PHOTO_BYTES</c>). Kestrel отвечает <c>413</c> до
    /// действия; клиент обрабатывает и его, и код <c>tooLarge</c>.
    /// </summary>
    private const int MAX_UPLOAD_REQUEST_BYTES = 6 * 1024 * 1024;

    /// <summary>
    /// Кусок чтения потока формы: файл читается порциями, а не целиком по <c>IFormFile.Length</c>.
    /// </summary>
    private const int UPLOAD_READ_CHUNK_BYTES = 64 * 1024;

    private const string UPLOAD_FILE_REQUIRED_CODE = "barberProfile.uploadPhoto.fileRequired";
    private const string UPLOAD_TOO_LARGE_CODE = "barberProfile.uploadPhoto.tooLarge";

    private const string UPLOAD_FILE_REQUIRED_MESSAGE = "A photo file is required.";

    private static readonly string UploadTooLargeMessage =
        $"The image file cannot be larger than {Constants.MAX_PHOTO_BYTES} bytes.";

    /// <summary>
    /// Создаёт профиль барбера для текущего пользователя. Доступно только роли <c>Barber</c>:
    /// администратор выдаёт её через <c>PUT api/users/{userId}/role</c>.
    /// </summary>
    /// <remarks>
    /// Единственный create-эндпоинт, который выставляет <c>Location</c> (R-42 пересмотрен):
    /// у созданного профиля есть читаемый <c>GET api/barber-profiles/{id}</c> — собственный
    /// профиль владельца под <c>BarberOnly</c>. Остальные create-эндпоинты по-прежнему
    /// отвечают <c>201</c> без заголовка.
    /// </remarks>
    [Authorize(Policy = AuthorizationPolicies.BarberOnly)]
    [HttpPost]
    public async Task<ActionResult<CreatedResourceResponse>> Create(
        [FromBody] BarberProfileCreateRequest request,
        [FromServices] ICommandHandler<Guid, CreateBarberProfileCommand> handler,
        CancellationToken cancellationToken)
    {
        var created = await handler.Handle(
            new CreateBarberProfileCommand(request),
            cancellationToken);

        return created.IsSuccess
            ? CreatedAtAction(
                nameof(Get),
                new
                {
                    barberProfileId = created.Value
                },
                new CreatedResourceResponse(created.Value))
            : FailureProblemDetails.CreateResult(created.Error);
    }

    /// <summary>
    /// Собственная карточка барбера — в том числе до подтверждения.
    /// </summary>
    /// <remarks>
    /// Публичная <c>GET api/barbers/{id}</c> для кабинета не годится: она отдаёт только
    /// <c>IsActive &amp;&amp; IsVerified</c> и новому барберу отвечает <c>404</c>. Здесь профиль
    /// читается любого статуса, а услуги приходят вместе с выключенными — иначе скрытая строка
    /// исчезла бы из прайса, и вернуть её было бы нечем. Тип ответа тот же
    /// <see cref="BarberProfileResponse"/>, что у публичной карточки.
    /// <para>
    /// Владение проверяет обработчик: неизвестный профиль — <c>404 barberProfile.notFound</c>,
    /// чужой — <c>403 auth.forbidden</c>. Политика <c>BarberOnly</c> отсекает анонима
    /// (<c>401</c>) и остальные роли (<c>403</c>).
    /// </para>
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля барбера.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [Authorize(Policy = AuthorizationPolicies.BarberOnly)]
    [HttpGet("{barberProfileId:guid}")]
    public async Task<ActionResult<BarberProfileResponse>> Get(
        Guid barberProfileId,
        [FromServices] IQueryHandler<BarberProfileResponse, GetOwnBarberProfileQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetOwnBarberProfileQuery(barberProfileId),
            cancellationToken));

    /// <summary>
    /// Обновляет профиль барбера. Грейда в теле нет: его меняет только административный сценарий.
    /// </summary>
    [HttpPut("{barberProfileId:guid}")]
    public async Task<ActionResult> Update(
        Guid barberProfileId,
        [FromBody] BarberProfileUpdateRequest request,
        [FromServices] ICommandHandler<UpdateBarberProfileCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateBarberProfileCommand(barberProfileId, request),
            cancellationToken));

    /// <summary>
    /// Меняет грейд барбера. Только администратор.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{barberProfileId:guid}/grade")]
    public async Task<ActionResult> ChangeGrade(
        Guid barberProfileId,
        [FromBody] ChangeBarberProfileGradeRequest request,
        [FromServices] ICommandHandler<ChangeBarberProfileGradeCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ChangeBarberProfileGradeCommand(barberProfileId, request.Grade),
            cancellationToken));

    /// <summary>
    /// Пересчитывает рейтинг барбера по отзывам. Только администратор.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPut("{barberProfileId:guid}/rating")]
    public async Task<ActionResult> UpdateRating(
        Guid barberProfileId,
        [FromBody] UpdateBarberProfileRatingRequest request,
        [FromServices] ICommandHandler<UpdateBarberProfileRatingCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateBarberProfileRatingCommand(barberProfileId, request.Rating, request.ReviewsCount),
            cancellationToken));

    /// <summary>
    /// Активирует профиль барбера.
    /// </summary>
    [HttpPost("{barberProfileId:guid}/activation")]
    public async Task<ActionResult> Activate(
        Guid barberProfileId,
        [FromServices] ICommandHandler<ActivateBarberProfileCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ActivateBarberProfileCommand(barberProfileId),
            cancellationToken));

    /// <summary>
    /// Деактивирует профиль барбера.
    /// </summary>
    [HttpDelete("{barberProfileId:guid}/activation")]
    public async Task<ActionResult> Deactivate(
        Guid barberProfileId,
        [FromServices] ICommandHandler<DeactivateBarberProfileCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new DeactivateBarberProfileCommand(barberProfileId),
            cancellationToken));

    /// <summary>
    /// Подтверждает профиль барбера. Только администратор.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpPost("{barberProfileId:guid}/verification")]
    public async Task<ActionResult> Verify(
        Guid barberProfileId,
        [FromServices] ICommandHandler<VerifyBarberProfileCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new VerifyBarberProfileCommand(barberProfileId),
            cancellationToken));

    /// <summary>
    /// Снимает подтверждение профиля барбера. Только администратор.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [HttpDelete("{barberProfileId:guid}/verification")]
    public async Task<ActionResult> Unverify(
        Guid barberProfileId,
        [FromServices] ICommandHandler<UnverifyBarberProfileCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UnverifyBarberProfileCommand(barberProfileId),
            cancellationToken));

    /// <summary>
    /// Добавляет фотографию в портфолио барбера.
    /// </summary>
    [HttpPost("{barberProfileId:guid}/photos")]
    public async Task<ActionResult> AddPhoto(
        Guid barberProfileId,
        [FromBody] AddBarberProfilePhotoRequest request,
        [FromServices] ICommandHandler<AddBarberProfilePhotoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddBarberProfilePhotoCommand(
                barberProfileId,
                request.Url,
                request.ThumbnailUrl,
                request.Description),
            cancellationToken));

    /// <summary>
    /// Загружает файл фотографии в портфолио барбера (<c>multipart/form-data</c>, одно поле).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Принимает только JPEG, PNG и WebP; тип определяется по сигнатуре в
    /// <c>ImageFileInspector</c>, а не по имени файла или <c>Content-Type</c> клиента.
    /// Поток формы читается в память порциями с жёстким пределом
    /// <c>Constants.MAX_PHOTO_BYTES + 1</c> байт: <c>IFormFile.Length</c> — это лишь
    /// подсказка от клиента, и доверять ей как пределу нельзя. Пустой файл —
    /// <c>400 barberProfile.uploadPhoto.fileRequired</c>, превышение —
    /// <c>400 barberProfile.uploadPhoto.tooLarge</c>.
    /// </para>
    /// <para>
    /// Остальные проверки (формат, размеры, лимит 10 фотографий, права владельца) выполняет
    /// обработчик Application; ответ — <c>201 { "id": "&lt;photoId&gt;" }</c> без
    /// <c>Location</c>: у фотографии нет читаемого self-URI (R-42). Multipart-запрос —
    /// cookie-мутация, поэтому глобальный antiforgery-фильтр продолжает требовать
    /// <c>X-CSRF-TOKEN</c>; отдельной политики rate limiter нет — хватает глобальных
    /// 30 записей/мин и доменного лимита 10 фото.
    /// </para>
    /// </remarks>
    /// <param name="barberProfileId">Профиль, в портфолио которого добавляется фотография.</param>
    /// <param name="form">Файл и необязательная подпись.</param>
    /// <param name="handler">Обработчик команды загрузки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPost("{barberProfileId:guid}/photos/upload")]
    [RequestSizeLimit(MAX_UPLOAD_REQUEST_BYTES)]
    [RequestFormLimits(MultipartBodyLengthLimit = MAX_UPLOAD_REQUEST_BYTES)]
    public async Task<ActionResult<CreatedResourceResponse>> UploadPhoto(
        Guid barberProfileId,
        [FromForm] UploadPortfolioPhotoForm form,
        [FromServices] ICommandHandler<Guid, UploadPortfolioPhotoCommand> handler,
        CancellationToken cancellationToken)
    {
        // form?.File: при запросе без form-content биндер оставляет параметр пустым,
        // и это тот же «файла нет», а не 500.
        var file = form?.File;

        if (file is null || file.Length == 0)
        {
            return UploadFileRequired();
        }

        await using var source = file.OpenReadStream();

        // Начальная ёмкость ограничена пределом: даже вредоносный Length не раздует буфер.
        using var buffer = new MemoryStream(
            capacity: (int)Math.Min(file.Length, Constants.MAX_PHOTO_BYTES));

        var chunk = new byte[UPLOAD_READ_CHUNK_BYTES];
        var totalBytes = 0;

        while (true)
        {
            var read = await source.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;

            // Останавливаемся на первом же байте сверх предела: целиком 6 МиБ не читаем.
            if (totalBytes > Constants.MAX_PHOTO_BYTES)
            {
                return UploadTooLarge();
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        if (totalBytes == 0)
        {
            return UploadFileRequired();
        }

        var uploaded = await handler.Handle(
            new UploadPortfolioPhotoCommand(
                barberProfileId,
                buffer.ToArray(),
                form!.Description),
            cancellationToken);

        return ToCreatedResult(uploaded);
    }

    /// <summary>
    /// Меняет порядок фотографий портфолио одним запросом: тело — полный список
    /// идентификаторов в новом порядке.
    /// </summary>
    /// <remarks>
    /// Отдельный <c>PUT .../photos/{photoId}/sort-order</c> остаётся для совместимости, но
    /// кабинет меняет порядок только здесь: два одиночных запроса на обмен местами при
    /// частичном сбое оставили бы дубли <c>SortOrder</c>. Неполный список или дубли —
    /// <c>400 barberProfile.reorderPhotos.invalid</c>; владение проверяет обработчик.
    /// </remarks>
    /// <param name="barberProfileId">Профиль, чьё портфолио переупорядочивается.</param>
    /// <param name="request">Полный список фотографий в новом порядке.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPut("{barberProfileId:guid}/photos/order")]
    public async Task<ActionResult> ReorderPhotos(
        Guid barberProfileId,
        [FromBody] ReorderBarberProfilePhotosRequest request,
        [FromServices] ICommandHandler<ReorderBarberProfilePhotosCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ReorderBarberProfilePhotosCommand(barberProfileId, request.PhotoIds),
            cancellationToken));

    /// <summary>
    /// Удаляет фотографию из портфолио барбера.
    /// </summary>
    [HttpDelete("{barberProfileId:guid}/photos/{photoId:guid}")]
    public async Task<ActionResult> RemovePhoto(
        Guid barberProfileId,
        Guid photoId,
        [FromServices] ICommandHandler<RemoveBarberProfilePhotoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RemoveBarberProfilePhotoCommand(barberProfileId, photoId),
            cancellationToken));

    /// <summary>
    /// Меняет порядок фотографии в портфолио барбера (порядок задаёт агрегат-корень).
    /// </summary>
    [HttpPut("{barberProfileId:guid}/photos/{photoId:guid}/sort-order")]
    public async Task<ActionResult> ReorderPhoto(
        Guid barberProfileId,
        Guid photoId,
        [FromBody] ReorderBarberProfilePhotoRequest request,
        [FromServices] ICommandHandler<ReorderBarberProfilePhotoCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new ReorderBarberProfilePhotoCommand(barberProfileId, photoId, request.SortOrder),
            cancellationToken));

    /// <summary>
    /// Добавляет салон в профиль барбера.
    /// </summary>
    [HttpPost("{barberProfileId:guid}/salons")]
    public async Task<ActionResult> AddSalon(
        Guid barberProfileId,
        [FromBody] AddBarberProfileSalonRequest request,
        [FromServices] ICommandHandler<AddBarberProfileSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddBarberProfileSalonCommand(barberProfileId, request.SalonId, request.IsPrimary),
            cancellationToken));

    /// <summary>
    /// Удаляет салон из профиля барбера вместе с его расписанием.
    /// </summary>
    [HttpDelete("{barberProfileId:guid}/salons/{salonId:guid}")]
    public async Task<ActionResult> RemoveSalon(
        Guid barberProfileId,
        Guid salonId,
        [FromServices] ICommandHandler<RemoveBarberProfileSalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RemoveBarberProfileSalonCommand(barberProfileId, salonId),
            cancellationToken));

    /// <summary>
    /// Назначает салон основным. Признак снимается с прежнего основного — правило домена.
    /// </summary>
    [HttpPut("{barberProfileId:guid}/salons/{salonId:guid}/primary")]
    public async Task<ActionResult> SetPrimarySalon(
        Guid barberProfileId,
        Guid salonId,
        [FromServices] ICommandHandler<SetBarberProfilePrimarySalonCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new SetBarberProfilePrimarySalonCommand(barberProfileId, salonId),
            cancellationToken));

    /// <summary>
    /// Возвращает салоны, привязанные к профилю барбера.
    /// </summary>
    /// <remarks>
    /// Доступ — владелец профиля или администратор, как у <c>GET .../schedule</c>.
    /// Работает и для неподтверждённого профиля: салоны выбирают до верификации.
    /// Ответ — все привязки, основной салон первым; без пагинации.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля барбера.</param>
    /// <param name="handler">Обработчик запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpGet("{barberProfileId:guid}/salons")]
    public async Task<ActionResult<IReadOnlyList<BarberSalonSummaryResponse>>> GetSalons(
        Guid barberProfileId,
        [FromServices] IQueryHandler<IReadOnlyList<BarberSalonSummaryResponse>, GetBarberProfileSalonsQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new GetBarberProfileSalonsQuery(barberProfileId),
            cancellationToken));

    /// <summary>
    /// Возвращает салон барбера вместе с расписанием пары (барбер, салон).
    /// </summary>
    [HttpGet("{barberProfileId:guid}/salons/{salonId:guid}")]
    public async Task<ActionResult<BarberSalonResponse>> FindSalon(
        Guid barberProfileId,
        Guid salonId,
        [FromServices] IQueryHandler<BarberSalonResponse, FindBarberProfileSalonQuery> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new FindBarberProfileSalonQuery(barberProfileId, salonId),
            cancellationToken));

    /// <summary>
    /// Добавляет услугу в профиль барбера: цена и длительность задаются для этого барбера.
    /// </summary>
    [HttpPost("{barberProfileId:guid}/services")]
    public async Task<ActionResult> AddService(
        Guid barberProfileId,
        [FromBody] AddBarberProfileServiceRequest request,
        [FromServices] ICommandHandler<AddBarberProfileServiceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddBarberProfileServiceCommand(
                barberProfileId,
                request.HairServiceId,
                request.Price,
                request.DurationMinutes),
            cancellationToken));

    /// <summary>
    /// Меняет цену и длительность услуги в прайсе барбера.
    /// </summary>
    /// <remarks>
    /// Правка не меняет видимость услуги: скрытая строка остаётся скрытой, активная —
    /// активной. Отдельный <c>PUT</c> нужен, чтобы барбер не правил прайс парой
    /// «скрыть и добавить заново» — та меняла бы состояние и историю услуги.
    /// Неизвестная услуга — <c>404 barberProfile.updateService.notFound</c>.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля барбера.</param>
    /// <param name="hairServiceId">Услуга справочника (<c>hair_services.id</c>).</param>
    /// <param name="request">Новая цена и длительность.</param>
    /// <param name="handler">Обработчик команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [HttpPut("{barberProfileId:guid}/services/{hairServiceId:guid}")]
    public async Task<ActionResult> UpdateService(
        Guid barberProfileId,
        Guid hairServiceId,
        [FromBody] UpdateBarberProfileServiceRequest request,
        [FromServices] ICommandHandler<UpdateBarberProfileServiceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new UpdateBarberProfileServiceCommand(
                barberProfileId,
                hairServiceId,
                request.Price,
                request.DurationMinutes),
            cancellationToken));

    /// <summary>
    /// Скрывает услугу из прайса барбера: строка остаётся ради истории записей.
    /// </summary>
    /// <remarks>
    /// Ответ <c>204</c>, повторный вызов идемпотентен. Физического удаления нет специально:
    /// списки записей клиента, барбера и админки строятся внутренними join'ами
    /// <c>bookings → barber_services → hair_services</c>, и удалённая строка уносила бы чужие
    /// брони из выдачи. Неизвестная услуга — <c>404 barberProfile.removeService.notFound</c>.
    /// Вернуть услугу в прайс можно повторным <c>POST .../services</c> с той же услугой
    /// справочника — строка реактивируется с новой ценой и длительностью.
    /// </remarks>
    [HttpDelete("{barberProfileId:guid}/services/{hairServiceId:guid}")]
    public async Task<ActionResult> RemoveService(
        Guid barberProfileId,
        Guid hairServiceId,
        [FromServices] ICommandHandler<RemoveBarberProfileServiceCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RemoveBarberProfileServiceCommand(barberProfileId, hairServiceId),
            cancellationToken));

    /// <summary>
    /// Добавляет специализацию в профиль барбера.
    /// </summary>
    [HttpPost("{barberProfileId:guid}/specializations/{specializationId:guid}")]
    public async Task<ActionResult> AddSpecialization(
        Guid barberProfileId,
        Guid specializationId,
        [FromServices] ICommandHandler<AddBarberProfileSpecializationCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddBarberProfileSpecializationCommand(barberProfileId, specializationId),
            cancellationToken));

    /// <summary>
    /// Удаляет специализацию из профиля барбера.
    /// </summary>
    [HttpDelete("{barberProfileId:guid}/specializations/{specializationId:guid}")]
    public async Task<ActionResult> RemoveSpecialization(
        Guid barberProfileId,
        Guid specializationId,
        [FromServices] ICommandHandler<RemoveBarberProfileSpecializationCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RemoveBarberProfileSpecializationCommand(barberProfileId, specializationId),
            cancellationToken));

    /// <summary>
    /// Добавляет нерабочий период (отпуск) барбера.
    /// </summary>
    [HttpPost("{barberProfileId:guid}/time-offs")]
    public async Task<ActionResult> AddTimeOff(
        Guid barberProfileId,
        [FromBody] AddBarberProfileTimeOffRequest request,
        [FromServices] ICommandHandler<AddBarberProfileTimeOffCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new AddBarberProfileTimeOffCommand(
                barberProfileId,
                request.StartAt,
                request.EndAt,
                request.Reason),
            cancellationToken));

    /// <summary>
    /// Удаляет нерабочий период (отпуск) барбера.
    /// </summary>
    [HttpDelete("{barberProfileId:guid}/time-offs/{timeOffId:guid}")]
    public async Task<ActionResult> RemoveTimeOff(
        Guid barberProfileId,
        Guid timeOffId,
        [FromServices] ICommandHandler<RemoveBarberProfileTimeOffCommand> handler,
        CancellationToken cancellationToken)
        => ToActionResult(await handler.Handle(
            new RemoveBarberProfileTimeOffCommand(barberProfileId, timeOffId),
            cancellationToken));

    /// <summary>
    /// <c>400 barberProfile.uploadPhoto.fileRequired</c>: файла нет или он пуст.
    /// </summary>
    private static ObjectResult UploadFileRequired()
        => FailureProblemDetails.CreateResult(
            new Failure(Error.Validation(
                UPLOAD_FILE_REQUIRED_CODE,
                UPLOAD_FILE_REQUIRED_MESSAGE,
                "file")));

    /// <summary>
    /// <c>400 barberProfile.uploadPhoto.tooLarge</c>: файл больше 5 МиБ.
    /// </summary>
    private static ObjectResult UploadTooLarge()
        => FailureProblemDetails.CreateResult(
            new Failure(Error.Validation(
                UPLOAD_TOO_LARGE_CODE,
                UploadTooLargeMessage,
                "file")));
}
