namespace BarberSearch.Contracts.Addresses.SuggestAddresses;

/// <summary>
/// Подсказка адреса внутри выбранного города.
/// </summary>
/// <remarks>
/// <para>
/// Порядок полей — часть контракта: клиент читает их по именам, но порядок зафиксирован
/// примером из спецификации среза, чтобы форма ответа не менялась незаметно.
/// </para>
/// <para>
/// <see cref="ProviderId"/> — непрозрачный идентификатор подсказки у провайдера (у DaData —
/// <c>fias_id</c>, иначе <c>kladr_id</c>): сервер его не разбирает и никуда не сохраняет
/// (в этом срезе в БД нет <c>fias_id</c>), поэтому клиент не должен строить на его формате
/// логику. <see cref="TimeZoneId"/> в этом срезе всегда <c>null</c>: поле зарезервировано
/// сразу, чтобы следующий срез (офлайн <c>lat</c>/<c>lon</c> → IANA) не менял форму ответа;
/// пока пояс выбирается в форме вручную.
/// </para>
/// </remarks>
/// <param name="ProviderId">
/// Непрозрачный идентификатор подсказки у провайдера; <c>null</c>, если провайдер его не вернул.
/// </param>
/// <param name="Display">Строка, которую видит пользователь в выпадающем списке.</param>
/// <param name="Address">
/// Улица и дом без города; <c>null</c>, если провайдер не дал улицу (тогда адрес — <see cref="Display"/>).
/// </param>
/// <param name="City">Город.</param>
/// <param name="Region">Регион; <c>null</c>, если провайдер его не вернул.</param>
/// <param name="Country">Страна.</param>
/// <param name="PostalCode">Почтовый индекс; <c>null</c>, если его нет.</param>
/// <param name="Latitude">Широта; <c>null</c>, если провайдер её не вернул.</param>
/// <param name="Longitude">Долгота; <c>null</c>, если провайдер её не вернул.</param>
/// <param name="TimeZoneId">
/// Часовой пояс салона (<c>IANA</c>); в этом срезе всегда <c>null</c>.
/// </param>
public record AddressSuggestionResponse(
    string? ProviderId,
    string Display,
    string? Address,
    string City,
    string? Region,
    string Country,
    string? PostalCode,
    double? Latitude,
    double? Longitude,
    string? TimeZoneId);
