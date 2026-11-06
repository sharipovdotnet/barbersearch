using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Salons.CreateSalon;

namespace BarberSearch.Application.Salons.CreateOwnSalon;

/// <summary>
/// Команда создания салона барбером: салон уходит на модерацию и привязывается к профилю.
/// </summary>
/// <remarks>
/// Состав полей тот же, что у админского <c>CreateSalonCommand</c>, поэтому используется
/// общий контракт <see cref="CreateSalonRequest"/>: две формы салона не должны разойтись.
/// </remarks>
public record CreateOwnSalonCommand(CreateSalonRequest Request) : ICommand;
