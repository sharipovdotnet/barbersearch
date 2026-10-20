namespace BarberSearch.Contracts.BarberProfiles.GetBarberProfile;

/// <summary>
/// Специализация барбера в карточке.
/// </summary>
/// <param name="Id">Идентификатор записи «специализация барбера».</param>
/// <param name="SpecializationId">Идентификатор специализации справочника.</param>
/// <param name="Name">Название специализации.</param>
/// <param name="Description">Описание специализации.</param>
public record BarberProfileSpecializationResponse(
    Guid Id,
    Guid SpecializationId,
    string Name,
    string? Description);
