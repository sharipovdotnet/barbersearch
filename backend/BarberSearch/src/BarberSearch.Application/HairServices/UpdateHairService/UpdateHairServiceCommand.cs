using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.HairServices.UpdateHairService;

/// <summary>
/// Команда изменения услуги каталога.
/// </summary>
/// <remarks>
/// Специализацию можно менять: категория услуги вычисляется из категории специализации,
/// поэтому прежний запрет смены категории сюда не переносится. Существование
/// <paramref name="SpecializationId"/> проверяет обработчик.
/// </remarks>
/// <param name="HairServiceId">Изменяемая услуга.</param>
/// <param name="Name">Название услуги.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию, минуты.</param>
/// <param name="SpecializationId">Специализация, к которой относится услуга.</param>
public record UpdateHairServiceCommand(
    Guid HairServiceId,
    string Name,
    string? Description,
    int DefaultDurationMinutes,
    Guid SpecializationId) : ICommand;
