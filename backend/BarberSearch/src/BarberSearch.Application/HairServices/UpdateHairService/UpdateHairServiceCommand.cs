using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.HairServices.UpdateHairService;

/// <summary>
/// Команда изменения услуги каталога.
/// </summary>
/// <remarks>
/// Категория не меняется: цена и длительность конкретных барберов привязаны к услуге,
/// а перевод услуги в другую категорию менял бы смысл уже сделанных предложений.
/// Направление меняется, но только пока ни один барбер не предлагает услугу без нового
/// направления — проверка живёт в обработчике.
/// </remarks>
/// <param name="HairServiceId">Изменяемая услуга.</param>
/// <param name="Name">Название услуги.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию, минуты.</param>
/// <param name="SpecializationId">Направление (специализация), которому принадлежит услуга.</param>
public record UpdateHairServiceCommand(
    Guid HairServiceId,
    string Name,
    string? Description,
    int DefaultDurationMinutes,
    Guid SpecializationId) : ICommand;
