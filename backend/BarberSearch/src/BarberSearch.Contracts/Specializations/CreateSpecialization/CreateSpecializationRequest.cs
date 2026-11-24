using BarberSearch.Domain.HairServices;

namespace BarberSearch.Contracts.Specializations.CreateSpecialization;

/// <summary>
/// Данные запроса на создание специализации.
/// </summary>
/// <remarks>
/// У специализации ровно одна категория услуг (<see cref="ServiceCategory"/>): категория
/// услуги каталога берётся из категории её специализации. Уникальность имени (без учёта
/// регистра и окружающих пробелов) обеспечивают обработчик и уникальный индекс
/// <c>ux_specializations_name</c>; сам запрос ничего не проверяет.
/// </remarks>
/// <param name="Name">Название специализации.</param>
/// <param name="Category">Категория услуг направления.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
public record CreateSpecializationRequest(
    string Name,
    ServiceCategory Category,
    string? Description);
