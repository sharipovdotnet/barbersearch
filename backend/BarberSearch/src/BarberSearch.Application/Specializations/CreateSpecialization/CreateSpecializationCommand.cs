using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.HairServices;

namespace BarberSearch.Application.Specializations.CreateSpecialization;

/// <summary>
/// Команда создания специализации.
/// </summary>
/// <param name="Name">Название специализации.</param>
/// <param name="Category">Категория услуг направления.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
public record CreateSpecializationCommand(
    string Name,
    ServiceCategory Category,
    string? Description) : ICommand;
