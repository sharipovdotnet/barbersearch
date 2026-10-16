using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfileSpecialization;

/// <summary>
/// Команда добавления специализации в профиль барбера.
/// </summary>
/// <remarks>
/// Дубликат определяется по <see cref="SpecializationId"/> — это правило домена.
/// </remarks>
public record AddBarberProfileSpecializationCommand(
    Guid BarberProfileId,
    Guid SpecializationId) : ICommand;
