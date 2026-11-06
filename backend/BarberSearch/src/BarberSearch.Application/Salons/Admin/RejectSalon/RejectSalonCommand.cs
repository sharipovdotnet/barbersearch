using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.Salons.Admin.RejectSalon;

/// <summary>
/// Команда отказа салону в модерации.
/// </summary>
/// <param name="SalonId">Идентификатор салона.</param>
/// <param name="Reason">
/// Причина отказа. Обязательна: пустая после <c>Trim</c> — <c>400 salon.reject.reasonRequired</c>,
/// длиннее 500 символов — <c>400 salon.reject.reasonTooLong</c>.
/// </param>
public record RejectSalonCommand(Guid SalonId, string? Reason) : ICommand;
