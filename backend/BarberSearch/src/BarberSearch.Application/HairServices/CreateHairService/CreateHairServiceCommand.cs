using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.HairServices;

namespace BarberSearch.Application.HairServices.CreateHairService;

/// <summary>
/// Команда создания услуги каталога.
/// </summary>
/// <param name="Name">Название услуги.</param>
/// <param name="Category">Категория услуги; после создания не меняется.</param>
/// <param name="SpecializationId">
/// Направление, которому принадлежит услуга. Существование строки проверяет обработчик
/// до вставки: домен видит только идентификатор чужого агрегата.
/// </param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию, минуты.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
public record CreateHairServiceCommand(
    string Name,
    ServiceCategory Category,
    Guid SpecializationId,
    int DefaultDurationMinutes,
    string? Description) : ICommand;
