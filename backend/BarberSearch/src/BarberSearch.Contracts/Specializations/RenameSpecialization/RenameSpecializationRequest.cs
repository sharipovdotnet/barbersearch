using BarberSearch.Domain.HairServices;

namespace BarberSearch.Contracts.Specializations.RenameSpecialization;

/// <summary>
/// Данные запроса на переименование специализации, смену её описания и категории.
/// </summary>
/// <remarks>
/// <see cref="Description"/> = <c>null</c> означает «убрать описание». Имя проверяется
/// на уникальность теми же правилами, что и при создании. Категория меняется свободно:
/// услуги получают её из специализации и правки не требуют.
/// </remarks>
/// <param name="Name">Новое название специализации.</param>
/// <param name="Category">Категория услуг направления.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
public record RenameSpecializationRequest(
    string Name,
    ServiceCategory Category,
    string? Description);
