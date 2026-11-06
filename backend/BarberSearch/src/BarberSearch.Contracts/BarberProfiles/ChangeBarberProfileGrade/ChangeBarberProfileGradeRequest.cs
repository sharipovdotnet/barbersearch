using BarberSearch.Domain.BarberProfiles.Enums;

namespace BarberSearch.Contracts.BarberProfiles.ChangeBarberProfileGrade;

/// <summary>
/// Данные запроса на смену грейда барбера.
/// </summary>
/// <remarks>
/// Грейд — административное действие, поэтому запрос отдельный от обновления профиля:
/// сам барбер выбирает грейд только при создании, но не меняет его в <c>UpdateProfile</c>.
/// </remarks>
public record ChangeBarberProfileGradeRequest(Grade Grade);
