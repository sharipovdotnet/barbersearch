using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.HairServices.CreateHairService;

/// <summary>
/// Команда создания услуги каталога.
/// </summary>
/// <remarks>
/// Категории в команде нет: услуга принадлежит специализации, и категория услуги — это
/// категория её специализации. Существование <paramref name="SpecializationId"/>
/// проверяет обработчик: домен видит только идентификатор чужого агрегата.
/// </remarks>
/// <param name="Name">Название услуги.</param>
/// <param name="SpecializationId">Специализация, к которой относится услуга.</param>
/// <param name="DefaultDurationMinutes">Длительность по умолчанию, минуты.</param>
/// <param name="Description">Описание; <c>null</c>, если его нет.</param>
public record CreateHairServiceCommand(
    string Name,
    Guid SpecializationId,
    int DefaultDurationMinutes,
    string? Description) : ICommand;
