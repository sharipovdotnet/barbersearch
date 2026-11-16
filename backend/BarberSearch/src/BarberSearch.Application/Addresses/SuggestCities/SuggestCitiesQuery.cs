using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Addresses.SuggestCities;

namespace BarberSearch.Application.Addresses.SuggestCities;

/// <summary>
/// Запрос подсказок города для поля «Город» в форме салона.
/// </summary>
/// <remarks>
/// Города приходят от провайдера, а не из нашей БД: у салонов нет <c>fias</c>, без которого
/// нельзя точно отфильтровать улицы, и справочник не знал бы городов, где салонов ещё нет.
/// Минимальная длина запроса у городов своя (<see cref="AddressSuggestionsOptions.MinCityQueryLength"/>),
/// потому что «Уфа» короче порога адреса.
/// </remarks>
/// <param name="Query">Сырая строка поиска; нормализуется в обработчике.</param>
/// <param name="Limit">Сколько подсказок вернуть; <c>null</c> — значение по умолчанию из настроек.</param>
public sealed record SuggestCitiesQuery(string? Query, int? Limit)
    : IQuery<IReadOnlyList<CitySuggestionResponse>>;
