namespace BarberSearch.Contracts.Specializations.RenameSpecialization;

/// <summary>
/// Данные запроса на переименование специализации и смену её описания.
/// </summary>
/// <remarks>
/// <see cref="Description"/> = <c>null</c> означает «убрать описание». Имя проверяется
/// на уникальность теми же правилами, что и при создании.
/// </remarks>
public record RenameSpecializationRequest(
    string Name,
    string? Description);
