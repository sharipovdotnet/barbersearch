using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.HairServices;

namespace BarberSearch.Application.Specializations.RenameSpecialization;

/// <summary>
/// Команда переименования специализации, смены её описания и категории.
/// </summary>
/// <remarks>
/// Категория меняется свободно: услуги получают категорию из специализации, поэтому
/// перевод направления в другую категорию не требует правки самих услуг.
/// </remarks>
/// <param name="SpecializationId">Изменяемая специализация.</param>
/// <param name="Name">Новое название.</param>
/// <param name="Category">Категория услуг направления.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
public record RenameSpecializationCommand(
    Guid SpecializationId,
    string Name,
    ServiceCategory Category,
    string? Description) : ICommand;
