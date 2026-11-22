namespace BarberSearch.Mobile.Core.ViewModels.Barber;

/// <summary>
/// Часовые пояса для выбора в форме создания салона.
/// </summary>
/// <remarks>
/// <para>
/// Веб рисует <c>&lt;select&gt;</c> из <c>Intl.supportedValuesOf('timeZone')</c> — это сотни
/// строк, и браузер ищет по ним набором с клавиатуры. Нативный MAUI <c>Picker</c> поиска не
/// имеет: длинный список пришлось бы листать, а поиск по нему — писать заново. Поэтому
/// мобильный список курированный: сначала пояс устройства, затем все российские зоны с
/// запада на восток, ближайшие соседи, несколько частых мировых зон и <c>UTC</c>. Человек,
/// который создаёт салон, почти всегда выбирает свой пояс или соседний; остальные остаются
/// доступными, но не превращают выбор в прокрутку на сотни пунктов.
/// </para>
/// <para>
/// Пояс устройства стоит первым, потому что форма подставляет <c>TimeZones[0]</c> и это
/// должен быть местный пояс, а не <c>Europe/Moscow</c> на телефоне в Екатеринбурге. Пустой
/// идентификатор устройства даёт <c>Europe/Moscow</c> — продуктовый город по умолчанию, как
/// и в вебе при неизвестном поясе браузера.
/// </para>
/// <para>
/// Список только со значениями IANA: сервер разрешает и Windows-идентификаторы, но телефон
/// на Android и iOS отдаёт IANA, и такой список одинаково читается в обоих API.
/// </para>
/// </remarks>
public static class SalonTimeZones
{
    /// <summary>
    /// Пояс по умолчанию, когда устройство не сообщило свой.
    /// </summary>
    private const string FallbackTimeZoneId = "Europe/Moscow";

    /// <summary>
    /// Курированный список зон с запада на восток: Россия, соседи, частые мировые зоны.
    /// </summary>
    private static readonly string[] Curated =
    [
        // Россия, запад → восток.
        "Europe/Kaliningrad",
        "Europe/Moscow",
        "Europe/Volgograd",
        "Europe/Saratov",
        "Europe/Astrakhan",
        "Europe/Ulyanovsk",
        "Europe/Samara",
        "Asia/Yekaterinburg",
        "Asia/Omsk",
        "Asia/Novosibirsk",
        "Asia/Barnaul",
        "Asia/Tomsk",
        "Asia/Novokuznetsk",
        "Asia/Krasnoyarsk",
        "Asia/Irkutsk",
        "Asia/Chita",
        "Asia/Yakutsk",
        "Asia/Vladivostok",
        "Asia/Ust-Nera",
        "Asia/Magadan",
        "Asia/Sakhalin",
        "Asia/Srednekolymsk",
        "Asia/Kamchatka",
        "Asia/Anadyr",

        // Соседи: Беларусь, Казахстан, Кавказ и Средняя Азия.
        "Europe/Minsk",
        "Asia/Almaty",
        "Asia/Tbilisi",
        "Asia/Yerevan",
        "Asia/Baku",
        "Asia/Tashkent",
        "Asia/Bishkek",

        // Частые мировые зоны, куда ездят или откуда приезжают клиенты.
        "Europe/Berlin",
        "Europe/Amsterdam",
        "Europe/London",
        "Europe/Paris",
        "Europe/Warsaw",
        "Europe/Prague",
        "Europe/Istanbul",
        "Asia/Dubai",

        // Нейтральный пояс для мест без собственной зоны.
        "UTC",
    ];

    /// <summary>
    /// Строит список зон для пикера: пояс устройства первым, затем курированный список.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Пустой или пробельный <paramref name="deviceTimeZoneId"/> заменяется на
    /// <c>Europe/Moscow</c>. Значение тримится: идентификатор зоны не содержит пробелов по
    /// краям, и такой же очищенный уходит на сервер.
    /// </para>
    /// <para>
    /// Пояс устройства не повторяется в хвосте списка: <c>Picker</c> с двумя одинаковыми
    /// строками выглядит как ошибка. Порядок остальных зон не меняется — он запад → восток.
    /// </para>
    /// </remarks>
    /// <param name="deviceTimeZoneId">
    /// <see cref="TimeZoneInfo.Local"/>.<see cref="TimeZoneInfo.Id"/> устройства.
    /// </param>
    /// <returns>Уникальные идентификаторы IANA; первый — пояс устройства или Москва.</returns>
    public static IReadOnlyList<string> Build(string deviceTimeZoneId)
    {
        var device = string.IsNullOrWhiteSpace(deviceTimeZoneId)
            ? FallbackTimeZoneId
            : deviceTimeZoneId.Trim();

        var zones = new List<string>(Curated.Length + 1)
        {
            device,
        };

        foreach (var zone in Curated)
        {
            if (!string.Equals(zone, device, StringComparison.Ordinal))
            {
                zones.Add(zone);
            }
        }

        return zones;
    }
}
