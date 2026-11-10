using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.RemoveBarberProfileService;

/// <summary>
/// Команда скрытия услуги из прайса барбера.
/// </summary>
/// <remarks>
/// Название повторяет имя ручки (<c>DELETE</c>), но удаления строки нет: услуга
/// деактивируется (<c>IsActive = false</c>) и остаётся в БД ради истории записей.
/// Повторное скрытие идемпотентно.
/// </remarks>
public record RemoveBarberProfileServiceCommand(
    Guid BarberProfileId,
    Guid HairServiceId) : ICommand;
