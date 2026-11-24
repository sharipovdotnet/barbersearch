using BarberSearch.Domain.HairServices;

namespace BarberSearch.Contracts.HairServices.GetHairServices;

/// <summary>
/// Строка публичного справочника услуг.
/// </summary>
/// <remarks>
/// Ответ — голый JSON-массив без пагинации. Категория не хранится у услуги: она приходит
/// из категории специализации (join), но в ответе остаётся — витрина и мобильное
/// приложение не переписывались. <see cref="SpecializationId"/> добавлен в конец записи
/// с значением по умолчанию, чтобы позиционные конструкции в мобильных тестах
/// компилировались без правок.
/// </remarks>
/// <param name="Id">Идентификатор услуги справочника.</param>
/// <param name="Name">Название для подписи в фильтре.</param>
/// <param name="Category">Категория специализации услуги.</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию, минуты.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
/// <param name="SpecializationId">Специализация, к которой относится услуга.</param>
public record HairServiceResponse(
    Guid Id,
    string Name,
    ServiceCategory Category,
    int DefaultDurationMinutes,
    string? Description,
    Guid SpecializationId = default);
