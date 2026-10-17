namespace BarberSearch.Contracts.Specializations.CreateSpecialization;

/// <summary>
/// Данные запроса на создание специализации.
/// </summary>
/// <remarks>
/// Уникальность имени (без учёта регистра и окружающих пробелов) обеспечивают обработчик
/// и уникальный индекс <c>ux_specializations_name</c>; сам запрос ничего не проверяет.
/// </remarks>
public record CreateSpecializationRequest(
    string Name,
    string? Description);
