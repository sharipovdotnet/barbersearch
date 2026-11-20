using BarberSearch.Contracts.BarberProfiles.AddBarberProfileSalon;
using BarberSearch.Contracts.BarberProfiles.AddBarberProfileService;
using BarberSearch.Contracts.BarberProfiles.CreateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfileSalons;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfileService;

namespace BarberSearch.Mobile.Core.Api.Barber;

/// <summary>
/// Собственный профиль мастера в кабинете: карточка, салоны, услуги и специализации.
/// </summary>
/// <remarks>
/// <para>
/// Клиент ходит через <see cref="HttpClient"/> с <c>AuthenticatedHttpHandler</c>: владельца
/// профиля сервер берёт из токена, <c>X-CSRF-TOKEN</c> не нужен.
/// </para>
/// <para>
/// <c>GET api/barber-profiles/{id}</c> — не публичная карточка: она отдаёт профиль любого
/// статуса и услуги вместе с выключенными, потому что кабинет заполняют до подтверждения.
/// Поэтому интерфейс отдельный от <see cref="IBarberSearchApi"/>.
/// </para>
/// <para>
/// Расписания здесь нет: недельный шаблон и исключения живут в <see cref="IBarberScheduleApi"/>,
/// чтобы у одного ресурса был один владелец.
/// </para>
/// </remarks>
public interface IBarberProfileApi
{
    /// <summary>
    /// Читает собственную карточку мастера — в том числе до подтверждения.
    /// </summary>
    /// <remarks>
    /// В отличие от публичной карточки, услуги приходят и выключенными
    /// (<see cref="BarberProfileServiceResponse.IsActive"/> = <c>false</c>): иначе скрытая
    /// строка исчезла бы из прайса, и вернуть её было бы нечем.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Карточка профиля любого статуса.</returns>
    /// <exception cref="BarberSearchApiException">Профиль не найден или чужой.</exception>
    Task<BarberProfileResponse> GetOwnAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Создаёт профиль мастера для текущего пользователя.
    /// </summary>
    /// <remarks>
    /// Роль <c>Barber</c> выдаёт администратор, поэтому до неё сервер ответит <c>403</c>.
    /// Идентификатор профиля генерирует домен, и клиент узнаёт его только из ответа.
    /// </remarks>
    /// <param name="request">Поля профиля; грейд необязателен.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор созданного профиля.</returns>
    /// <exception cref="BarberSearchApiException">Профиль уже существует или запрос отклонён.</exception>
    Task<Guid> CreateAsync(
        BarberProfileCreateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Обновляет профиль мастера.
    /// </summary>
    /// <remarks>
    /// Тело заменяет профиль целиком: грейда в нём нет, его меняет только администратор.
    /// Успех — <c>204</c> без тела.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="request">Новое состояние профиля.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Профиль не найден или поля отклонены.</exception>
    Task UpdateAsync(
        Guid barberProfileId,
        BarberProfileUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает салоны, привязанные к профилю.
    /// </summary>
    /// <remarks>
    /// Ответ — все привязки, основной салон первым, без пагинации. Салон на проверке и
    /// отклонённый тоже видны: это рабочий список кабинета, а не витрина.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Салоны профиля в порядке сервера.</returns>
    /// <exception cref="BarberSearchApiException">Профиль не найден или чужой.</exception>
    Task<IReadOnlyList<BarberSalonSummaryResponse>> GetSalonsAsync(
        Guid barberProfileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Привязывает салон к профилю.
    /// </summary>
    /// <remarks>
    /// <paramref name="isPrimary"/> = <c>true</c> делает салон основным и снимает признак
    /// с прежнего — это правило домена, клиент его не повторяет. Успех — <c>204</c>.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="salonId">Привязываемый салон.</param>
    /// <param name="isPrimary">Сделать салон основным.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Салон уже привязан или недоступен.</exception>
    Task AddSalonAsync(
        Guid barberProfileId,
        Guid salonId,
        bool isPrimary,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отвязывает салон от профиля вместе с расписанием пары.
    /// </summary>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="salonId">Отвязываемый салон.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Привязка не найдена или API ответил ошибкой.</exception>
    Task RemoveSalonAsync(
        Guid barberProfileId,
        Guid salonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Назначает салон основным.
    /// </summary>
    /// <remarks>
    /// Признак снимается с прежнего основного на сервере: отдельного «снять основной» нет.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="salonId">Салон, который станет основным.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Салон не привязан или API ответил ошибкой.</exception>
    Task SetPrimarySalonAsync(
        Guid barberProfileId,
        Guid salonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет услугу в прайс мастера.
    /// </summary>
    /// <remarks>
    /// Цена и длительность задаются для этого мастера, услуга справочника приходит
    /// идентификатором. Повторное добавление уже скрытой услуги реактивирует её.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="request">Услуга справочника, цена и длительность.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Услуга уже в прайсе или поля отклонены.</exception>
    Task AddServiceAsync(
        Guid barberProfileId,
        AddBarberProfileServiceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Меняет цену и длительность услуги в прайсе.
    /// </summary>
    /// <remarks>
    /// Правка не меняет видимость услуги: скрытая строка остаётся скрытой. Отдельный
    /// <c>PUT</c> нужен, чтобы не править прайс парой «скрыть и добавить заново» — та
    /// меняла бы состояние и историю услуги.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="hairServiceId">Услуга справочника.</param>
    /// <param name="request">Новая цена и длительность.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Услуга не найдена или поля отклонены.</exception>
    Task UpdateServiceAsync(
        Guid barberProfileId,
        Guid hairServiceId,
        UpdateBarberProfileServiceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Скрывает услугу из прайса: строка остаётся ради истории записей.
    /// </summary>
    /// <remarks>
    /// Физического удаления нет специально — иначе удалённая строка уносила бы чужие брони
    /// из списков. Повторный вызов идемпотентен, вернуть услугу можно повторным
    /// <see cref="AddServiceAsync"/>.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="hairServiceId">Услуга справочника.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Услуга не найдена или API ответил ошибкой.</exception>
    Task RemoveServiceAsync(
        Guid barberProfileId,
        Guid hairServiceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет специализацию мастеру из справочника.
    /// </summary>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="specializationId">Специализация справочника.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Специализация уже добавлена или не найдена.</exception>
    Task AddSpecializationAsync(
        Guid barberProfileId,
        Guid specializationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Убирает специализацию у мастера.
    /// </summary>
    /// <remarks>
    /// Удаление физическое: истории за специализацией нет.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="specializationId">Специализация справочника.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Специализация не найдена или API ответил ошибкой.</exception>
    Task RemoveSpecializationAsync(
        Guid barberProfileId,
        Guid specializationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Загружает фотографию в портфолио мастера.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Файл уходит <c>multipart/form-data</c> двумя частями: <c>file</c> (байты, имя и
    /// <c>Content-Type</c>) и <c>description</c> (подпись либо пустая строка). Пустую
    /// подпись сервер сам превращает в <c>null</c>; отдельного «без подписи» в запросе нет.
    /// </para>
    /// <para>
    /// Файл приходит уже подготовленным
    /// (<see cref="BarberSearch.Mobile.Core.Media.IPortfolioPhotoPicker"/>):
    /// JPEG до 1600 px по длинной стороне и до 5 МиБ. Клиент не перекодирует его повторно,
    /// но и не доверяет себе: те же пределы проверяет домен, а его коды разбирает
    /// <see cref="PortfolioPhotoErrors"/>.
    /// </para>
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="photo">Подготовленный файл и подпись.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор созданной фотографии из ответа <c>201</c>.</returns>
    /// <exception cref="BarberSearchApiException">
    /// Профиль чужой, лимит исчерпан, файл отклонён или хранилище недоступно.
    /// </exception>
    Task<Guid> UploadPhotoAsync(
        Guid barberProfileId,
        PortfolioPhotoUpload photo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет фотографию из портфолио.
    /// </summary>
    /// <remarks>
    /// Удаление физическое и необратимое: скрытая фотография остаётся в портфолио и
    /// возвращается публикацией, а удалённую восстановить нельзя. Успех — <c>204</c>.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="photoId">Удаляемая фотография.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Фотография не найдена или чужая.</exception>
    Task RemovePhotoAsync(
        Guid barberProfileId,
        Guid photoId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Меняет подпись фотографии.
    /// </summary>
    /// <remarks>
    /// <c>null</c> и пустая строка одинаково стирают подпись: сервер хранит отсутствие
    /// текста как <c>null</c>. Пробелы по краям обрезаются до запроса, чтобы «   » не
    /// сохранялось подписью из одних пробелов. Успех — <c>204</c>.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="photoId">Фотография, чья подпись меняется.</param>
    /// <param name="description">Новая подпись либо <c>null</c>, чтобы стереть её.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Фотография не найдена или подпись отклонена.</exception>
    Task UpdatePhotoDescriptionAsync(
        Guid barberProfileId,
        Guid photoId,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет новый порядок фотографий одним запросом.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Отправляется полный список идентификаторов в новом порядке: индекс элемента
    /// становится <c>SortOrder</c>. Частичный список или дубли сервер отклоняет — иначе
    /// порядок поехал бы, а два одиночных <c>PUT</c> при сбое посередине дали бы дубли
    /// номеров. Поэтому обмен местами двух соседей тоже уходит целым списком.
    /// </para>
    /// <para>
    /// Успех — <c>204</c>; после него экран перечитывает карточку, а не переставляет
    /// строки сам.
    /// </para>
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="photoIds">Все фотографии профиля в новом порядке.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Список не совпал с портфолио или фотография чужая.</exception>
    Task ReorderPhotosAsync(
        Guid barberProfileId,
        IReadOnlyList<Guid> photoIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает фотографию в публикацию.
    /// </summary>
    /// <remarks>
    /// Публичность решает владелец, но кадр показывается клиентам только после одобрения
    /// модератором: публикация отклонённой или ожидающей проверки фотографии не обходит
    /// допуск. Успех — <c>204</c>.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="photoId">Публикуемая фотография.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Фотография не найдена или чужая.</exception>
    Task PublishPhotoAsync(
        Guid barberProfileId,
        Guid photoId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Скрывает фотографию из публикации, оставляя её в портфолио.
    /// </summary>
    /// <remarks>
    /// Это не удаление: кадр остаётся в кабинете вместе с подписью и порядком и
    /// возвращается повторной публикацией. Успех — <c>204</c>.
    /// </remarks>
    /// <param name="barberProfileId">Идентификатор профиля мастера.</param>
    /// <param name="photoId">Скрываемая фотография.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="BarberSearchApiException">Фотография не найдена или чужая.</exception>
    Task UnpublishPhotoAsync(
        Guid barberProfileId,
        Guid photoId,
        CancellationToken cancellationToken = default);
}
