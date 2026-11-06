using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Salons.GetSalonBarbers;

namespace BarberSearch.Application.Salons.SearchSalonBarberCandidates;

/// <summary>
/// Поиск барбера, которого владелец может прикрепить к салону.
/// </summary>
/// <remarks>
/// Ищутся только профили, ещё не прикреплённые к этому салону. Строка короче трёх
/// символов и длиннее ста — <c>400</c> с кодом <c>salon.barbers.search.*</c>;
/// проверка живёт в обработчике, чтобы клиент получил доменный код, а не framework-<c>400</c>.
/// </remarks>
/// <param name="SalonId">Салон, к которому идёт прикрепление.</param>
/// <param name="Search">Имя или email барбера; пробелы по краям не значимы.</param>
public record SearchSalonBarberCandidatesQuery(Guid SalonId, string? Search)
    : IQuery<IReadOnlyList<SalonBarberResponse>>;
