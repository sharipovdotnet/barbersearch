using BarberSearch.Domain.HairServices;

namespace BarberSearch.Contracts.HairServices.CreateHairService;

/// <summary>
/// Данные запроса на создание услуги каталога.
/// </summary>
/// <remarks>
/// Категория берётся из домена (<see cref="ServiceCategory"/>) — это словарь предметной области.
/// <c>SpecializationId</c> — направление, которому принадлежит услуга: на витрине клиент
/// сначала выбирает направление, затем услугу из него. Длительность по умолчанию барбер
/// может переопределить у себя.
/// </remarks>
/// <param name="Name">Название услуги.</param>
/// <param name="Category">Категория услуги.</param>
/// <param name="SpecializationId">Идентификатор направления (специализации).</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию, минуты.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
public record CreateHairServiceRequest(
    string Name,
    ServiceCategory Category,
    Guid SpecializationId,
    int DefaultDurationMinutes,
    string? Description);
