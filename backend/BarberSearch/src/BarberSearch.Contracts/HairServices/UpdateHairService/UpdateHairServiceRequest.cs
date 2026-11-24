namespace BarberSearch.Contracts.HairServices.UpdateHairService;

/// <summary>
/// Данные запроса на изменение услуги каталога.
/// </summary>
/// <remarks>
/// Специализацию можно менять: категория услуги вычисляется из неё, поэтому прежний
/// запрет «категория после создания не меняется» сюда не переносится. Существование
/// <see cref="SpecializationId"/> проверяет Application до сохранения.
/// </remarks>
/// <param name="Name">Название услуги.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию, минуты.</param>
/// <param name="SpecializationId">Специализация, к которой относится услуга.</param>
public record UpdateHairServiceRequest(
    string Name,
    string? Description,
    int DefaultDurationMinutes,
    Guid SpecializationId);
