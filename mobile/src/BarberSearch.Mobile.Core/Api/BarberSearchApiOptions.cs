namespace BarberSearch.Mobile.Core.Api;

/// <summary>
/// Адрес API, к которому ходит мобильный клиент.
/// </summary>
/// <remarks>
/// Адрес зависит от платформы: эмулятор Android видит хост как <c>10.0.2.2</c>, симулятор iOS —
/// как <c>localhost</c>. Значение приходит из композиционного корня приложения, поэтому Core
/// не знает ни про платформу, ни про конфигурацию.
/// </remarks>
/// <param name="BaseAddress">Базовый адрес API с завершающим слэшем.</param>
public sealed record BarberSearchApiOptions(Uri BaseAddress)
{
    /// <summary>
    /// Собирает настройки из строки, добавляя завершающий слэш.
    /// </summary>
    /// <remarks>
    /// Слэш обязателен: без него <see cref="Uri"/> считает последний сегмент адреса файлом
    /// и отбрасывает его при склейке с относительным путём (<c>/api/barbers</c> съел бы порт).
    /// </remarks>
    /// <param name="baseAddress">Базовый адрес, например <c>http://10.0.2.2:5243</c>.</param>
    /// <exception cref="ArgumentException">Адрес пуст или не является абсолютным URI.</exception>
    public static BarberSearchApiOptions FromBaseAddress(string baseAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseAddress);

        var normalized = baseAddress.EndsWith('/') ? baseAddress : string.Concat(baseAddress, "/");

        return new BarberSearchApiOptions(new Uri(normalized, UriKind.Absolute));
    }
}
