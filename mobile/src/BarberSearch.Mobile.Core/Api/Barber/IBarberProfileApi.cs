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
}
