using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Addresses.SuggestAddresses;

namespace BarberSearch.Application.Addresses.SuggestAddresses;

/// <summary>
/// Запрос подсказок адреса внутри выбранного города.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CityProviderId"/> — <c>providerId</c> из ответа подсказок города: точный фильтр
/// по выбранному городу. <see cref="City"/> — фолбэк для города, введённого руками.
/// Приоритет задаёт адаптер: <c>cityProviderId</c>, затем свободный <c>city</c>, затем
/// запрос без фильтра.
/// </para>
/// <para>
/// Оба поля необязательны: форма должна работать и без выбора подсказки города, как раньше.
/// </para>
/// </remarks>
/// <param name="Query">Сырая строка поиска; нормализуется в обработчике.</param>
/// <param name="CityProviderId">Непрозрачный идентификатор выбранного города; <c>null</c>, если город не выбран.</param>
/// <param name="City">Свободный текст города; <c>null</c>, если фильтр по городу не задан.</param>
/// <param name="Limit">Сколько подсказок вернуть; <c>null</c> — значение по умолчанию из настроек.</param>
public sealed record SuggestAddressesQuery(string? Query, string? CityProviderId, string? City, int? Limit)
    : IQuery<IReadOnlyList<AddressSuggestionResponse>>;
