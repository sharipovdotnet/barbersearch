namespace BarberSearch.Contracts.Addresses.SuggestCities;

/// <summary>
/// Подсказка города для поля «Город» в форме салона.
/// </summary>
/// <remarks>
/// <para>
/// Город — первый шаг ввода адреса: выбранная строка заполняет поле «Город», а
/// <c>providerId</c> клиент запоминает в состоянии формы и передаёт в запрос адресных
/// подсказок. Без него улицы искались бы по всей стране.
/// </para>
/// <para>
/// <c>providerId</c> — непрозрачный токен провайдера, а не <c>Guid</c>: сервер его не
/// сохраняет и не разбирает. <c>timeZoneId</c> у города намеренно отсутствует: городу он
/// не нужен, а обещать поле в контракте раньше времени нельзя.
/// </para>
/// </remarks>
/// <param name="ProviderId">
/// Непрозрачный идентификатор подсказки у провайдера; <c>null</c>, если провайдер его не вернул.
/// </param>
/// <param name="Display">Строка, которую видит пользователь в выпадающем списке.</param>
/// <param name="City">Название города.</param>
/// <param name="Region">Регион; <c>null</c>, если провайдер его не вернул.</param>
/// <param name="Country">Страна.</param>
/// <param name="Latitude">Широта; <c>null</c>, если провайдер её не вернул.</param>
/// <param name="Longitude">Долгота; <c>null</c>, если провайдер её не вернул.</param>
public record CitySuggestionResponse(
    string? ProviderId,
    string Display,
    string City,
    string? Region,
    string Country,
    double? Latitude,
    double? Longitude);
