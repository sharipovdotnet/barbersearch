namespace BarberSearch.Application.Addresses;

/// <summary>
/// Запрос подсказок к провайдеру: что искать, в каких границах и сколько строк вернуть.
/// </summary>
/// <remarks>
/// <para>
/// Поля уже нормализованы валидатором Application: <see cref="Query"/> обрезан и со
/// схлопнутыми пробелами, <see cref="CityProviderId"/> и <see cref="City"/> — либо
/// непустые значения, либо <c>null</c>. Адаптер не повторяет эти проверки.
/// </para>
/// <para>
/// Приоритет фильтра города задаёт провайдер: <see cref="CityProviderId"/> точнее свободного
/// текста <see cref="City"/>. Оба поля передаются как есть, чтобы адаптер мог выбрать
/// <c>city_fias_id</c>, затем <c>city</c>, а без них — запрос без фильтра.
/// </para>
/// </remarks>
/// <param name="Query">Нормализованный поисковый запрос.</param>
/// <param name="Bound">Что подсказывать: город или адрес внутри города.</param>
/// <param name="CityProviderId">
/// Непрозрачный идентификатор выбранного города у провайдера; <c>null</c>, если город
/// не выбран из подсказок.
/// </param>
/// <param name="City">
/// Свободный текст города — фолбэк, если человек ввёл город руками; <c>null</c>, если
/// фильтр по городу не нужен.
/// </param>
/// <param name="Limit">Сколько подсказок вернуть; уже зажат в допустимый диапазон.</param>
public sealed record AddressSuggestionRequest(
    string Query,
    AddressSuggestionBound Bound,
    string? CityProviderId,
    string? City,
    int Limit);
