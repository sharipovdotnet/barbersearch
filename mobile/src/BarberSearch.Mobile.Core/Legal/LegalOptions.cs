namespace BarberSearch.Mobile.Core.Legal;

/// <summary>
/// Адрес публичного сайта: правовые документы открываются в браузере, а не рисуются в приложении.
/// </summary>
/// <remarks>
/// Core не знает ни про платформу, ни про конфигурацию — адрес приходит из композиционного
/// корня, как <see cref="Api.BarberSearchApiOptions"/>. Политика — документ Оператора
/// (152-ФЗ), и ссылка на него должна вести туда же, куда ведёт веб.
/// </remarks>
/// <param name="SiteBaseAddress">Базовый адрес сайта с завершающим слэшем.</param>
public sealed record LegalOptions(Uri SiteBaseAddress)
{
    /// <summary>
    /// Полный адрес страницы Политики обработки персональных данных.
    /// </summary>
    public Uri PrivacyPolicyUrl => new(SiteBaseAddress, PrivacyPolicy.Path);

    /// <summary>
    /// Собирает настройки из строки, добавляя завершающий слэш.
    /// </summary>
    /// <remarks>
    /// Слэш обязателен по той же причине, что и в <see cref="Api.BarberSearchApiOptions"/>:
    /// без него последний сегмент адреса считается файлом и отбрасывается при склейке
    /// относительного пути.
    /// </remarks>
    /// <param name="baseAddress">Базовый адрес, например <c>http://10.0.2.2:5173</c>.</param>
    /// <exception cref="ArgumentException">Адрес пуст или не является абсолютным URI.</exception>
    public static LegalOptions FromBaseAddress(string baseAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseAddress);

        var normalized = baseAddress.EndsWith('/') ? baseAddress : string.Concat(baseAddress, "/");

        return new LegalOptions(new Uri(normalized, UriKind.Absolute));
    }
}
