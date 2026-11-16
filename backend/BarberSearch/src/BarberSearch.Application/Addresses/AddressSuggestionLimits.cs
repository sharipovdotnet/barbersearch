namespace BarberSearch.Application.Addresses;

/// <summary>
/// Пределы длин запроса подсказок: часть контракта, а не настройка.
/// </summary>
/// <remarks>
/// <para>
/// Это границы полей HTTP-контракта, поэтому они одинаковы для городов и адреса и не
/// вычитываются из конфигурации: клиент не должен зависеть от стенда. Настраиваются только
/// пороги минимальной длины и число подсказок (<see cref="AddressSuggestionsOptions"/>).
/// </para>
/// <para>
/// Верхние границы защищают провайдера и кэш от раздутых ключей, а также дают
/// <c>400 addressSuggestions.query.tooLong</c> / <c>addressSuggestions.city.tooLong</c>
/// вместо молчаливого обрезания того, что человек ввёл.
/// </para>
/// </remarks>
public static class AddressSuggestionLimits
{
    /// <summary>
    /// Максимальная длина запроса адреса или города: <c>120</c> символов.
    /// </summary>
    public const int MAX_QUERY_LENGTH = 120;

    /// <summary>
    /// Максимальная длина свободного текста города: <c>100</c> символов.
    /// </summary>
    public const int MAX_CITY_LENGTH = 100;

    /// <summary>
    /// Максимальная длина <c>cityProviderId</c>: <c>64</c> символа.
    /// </summary>
    /// <remarks>
    /// Это непрозрачный токен провайдера, а не <c>Guid</c>: у DaData это <c>fias_id</c>,
    /// у другого провайдера формат будет иным, поэтому проверяется только длина и отсутствие
    /// пробелов, а не формат GUID.
    /// </remarks>
    public const int MAX_CITY_PROVIDER_ID_LENGTH = 64;
}
