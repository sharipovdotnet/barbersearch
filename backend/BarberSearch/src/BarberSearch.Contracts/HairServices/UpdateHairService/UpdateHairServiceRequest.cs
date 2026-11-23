namespace BarberSearch.Contracts.HairServices.UpdateHairService;

/// <summary>
/// Данные запроса на изменение услуги каталога.
/// </summary>
/// <remarks>
/// Категории здесь нет: она не меняется, потому что цена и длительность конкретных
/// барберов привязаны к услуге, а перевод услуги в другую категорию менял бы смысл
/// уже сделанных предложений. Направление (<c>SpecializationId</c>) менять можно, но только
/// пока услугу не предлагает барбер без нового направления: такой перевод отклоняется
/// конфликтом, иначе фильтр витрины и прайс барбера разошлись бы.
/// </remarks>
/// <param name="Name">Название услуги.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию, минуты.</param>
/// <param name="SpecializationId">Идентификатор направления (специализации).</param>
public record UpdateHairServiceRequest(
    string Name,
    string? Description,
    int DefaultDurationMinutes,
    Guid SpecializationId);
