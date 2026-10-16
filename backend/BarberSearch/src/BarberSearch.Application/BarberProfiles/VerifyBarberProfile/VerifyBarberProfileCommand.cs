using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.VerifyBarberProfile;

/// <summary>
/// Команда верификации профиля барбера.
/// </summary>
/// <remarks>
/// Неактивного барбера верифицировать нельзя — это правило домена, а не обработчика.
/// </remarks>
public record VerifyBarberProfileCommand(Guid BarberProfileId) : ICommand;
