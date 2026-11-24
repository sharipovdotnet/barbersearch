namespace BarberSearch.Contracts.HairServices.CreateHairService;

/// <summary>
/// Данные запроса на создание услуги каталога.
/// </summary>
/// <remarks>
/// Категории в теле нет: услуга принадлежит специализации, и категория услуги — это
/// категория её специализации. Существование <see cref="SpecializationId"/> проверяет
/// Application до вставки. Длительность по умолчанию барбер может переопределить у себя.
/// </remarks>
/// <param name="Name">Название услуги.</param>
/// <param name="SpecializationId">Специализация, к которой относится услуга.</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию, минуты.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
public record CreateHairServiceRequest(
    string Name,
    Guid SpecializationId,
    int DefaultDurationMinutes,
    string? Description);
