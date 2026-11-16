namespace BarberSearch.Application.Addresses;

/// <summary>
/// Провайдер-нейтральная подсказка города или адреса.
/// </summary>
/// <remarks>
/// <para>
/// Модель не знает ни DaData, ни JSON: адаптер в Infrastructure разбирает ответ вендора
/// и заполняет эти поля, а обработчики Application только маппят их в контракт. Поэтому
/// смена провайдера (например, на self-hosted Photon) не меняет Application и HTTP-контракт.
/// </para>
/// <para>
/// <see cref="ProviderId"/> — непрозрачный идентификатор подсказки: клиент возвращает его
/// в запросе адреса как фильтр по городу, но сервер его не сохраняет. <see cref="City"/> и
/// <see cref="Country"/> обязательны в контракте: подсказку без них адаптер отбрасывает,
/// потому что домен требует город и страну у салона.
/// </para>
/// </remarks>
/// <param name="ProviderId">
/// Идентификатор подсказки у провайдера; <c>null</c>, если провайдер его не вернул.
/// </param>
/// <param name="Display">Строка, которую видит пользователь в выпадающем списке.</param>
/// <param name="Address">
/// Улица и дом без города; если улицы нет — отображаемая строка <c>value</c> целиком (так
/// адаптер заполняет подсказку уровня города). <c>null</c>, только если провайдер не вернул
/// ни улицы, ни отображаемой строки.
/// </param>
/// <param name="City">Город.</param>
/// <param name="Region">Регион; <c>null</c>, если провайдер его не вернул.</param>
/// <param name="Country">Страна.</param>
/// <param name="PostalCode">Почтовый индекс; <c>null</c>, если его нет.</param>
/// <param name="Latitude">Широта; <c>null</c>, если провайдер её не вернул или она не разобралась.</param>
/// <param name="Longitude">Долгота; <c>null</c>, если провайдер её не вернул или она не разобралась.</param>
public sealed record AddressSuggestion(
    string? ProviderId,
    string Display,
    string? Address,
    string City,
    string? Region,
    string Country,
    string? PostalCode,
    double? Latitude,
    double? Longitude);
