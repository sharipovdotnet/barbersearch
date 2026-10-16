using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.AddBarberProfileService;

/// <summary>
/// Команда добавления услуги в профиль барбера.
/// </summary>
/// <remarks>
/// Допустимые цену и длительность проверяет домен; дубликат определяется по
/// <see cref="HairServiceId"/>.
/// </remarks>
public record AddBarberProfileServiceCommand(
    Guid BarberProfileId,
    Guid HairServiceId,
    decimal Price,
    int DurationMinutes) : ICommand;
