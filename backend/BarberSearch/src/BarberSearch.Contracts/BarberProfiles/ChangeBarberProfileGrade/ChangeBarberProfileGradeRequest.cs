using BarberSearch.Domain.BarberProfiles.Enums;

namespace BarberSearch.Contracts.BarberProfiles.ChangeBarberProfileGrade;

/// <summary>
/// Данные запроса на смену грейда барбера.
/// </summary>
/// <remarks>
/// Грейд — административное действие, поэтому запрос отдельный от обновления профиля:
/// сам барбер свой грейд не поднимает.
/// </remarks>
public record ChangeBarberProfileGradeRequest(Grade Grade);
