using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberClients;

namespace BarberSearch.Application.BarberClients;

/// <summary>
/// Поиск зарегистрированного клиента, которого барбер записывает сам.
/// </summary>
/// <remarks>
/// Строка ищется по email и телефону; минимум и максимум длины проверяет обработчик,
/// а не биндер, чтобы клиент получил доменный код, а не framework-<c>400</c>.
/// </remarks>
/// <param name="Search">Email или телефон клиента; пробелы по краям не значимы.</param>
public record SearchBarberClientsQuery(string? Search)
    : IQuery<IReadOnlyList<BarberClientResponse>>;
