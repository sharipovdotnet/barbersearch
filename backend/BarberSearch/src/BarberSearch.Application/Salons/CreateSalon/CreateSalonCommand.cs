using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Salons.CreateSalon;

namespace BarberSearch.Application.Salons.CreateSalon;

/// <summary>
/// Команда создания салона.
/// </summary>
/// <remarks>
/// Состав полей описан контрактом <see cref="CreateSalonRequest"/> и здесь не дублируется:
/// команда только помечает операцию, данные приходят одним объектом — так же, как
/// <c>CreateBarberProfileCommand</c> с <c>BarberProfileCreateRequest</c>.
/// </remarks>
public record CreateSalonCommand(CreateSalonRequest Request) : ICommand;
