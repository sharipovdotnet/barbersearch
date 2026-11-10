using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.UpdateBarberProfileService;

/// <summary>
/// Команда правки цены и длительности услуги в прайсе барбера.
/// </summary>
/// <remarks>
/// Услуга адресуется идентификатором справочника (<c>hair_services.id</c>), как и в
/// добавлении: строка <c>barber_services</c> у профиля одна на услугу. Допустимые значения
/// проверяет домен (<c>barberService.updatePrice</c> / <c>barberService.updateDuration</c>).
/// </remarks>
public record UpdateBarberProfileServiceCommand(
    Guid BarberProfileId,
    Guid HairServiceId,
    decimal Price,
    int DurationMinutes) : ICommand;
