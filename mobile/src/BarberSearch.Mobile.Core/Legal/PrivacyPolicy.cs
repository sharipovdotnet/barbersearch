using System.Globalization;

namespace BarberSearch.Mobile.Core.Legal;

/// <summary>
/// Редакция Политики обработки персональных данных и адрес её публичной страницы.
/// </summary>
/// <remarks>
/// <para>
/// Значения повторяют <c>frontend/src/lib/legal.ts</c>: форма регистрации подписывает ту
/// редакцию, которая указана здесь, а сервер сверяет её со своей <c>PrivacyPolicy.Version</c>.
/// Правка текста Политики поднимает обе константы и серверную версию; приложение с прежней
/// редакцией получает <c>400 auth.register.consentVersion</c> и просит обновиться.
/// </para>
/// <para>
/// Сам текст Политики в приложении не рисуется: это документ Оператора (152-ФЗ), и ссылка
/// должна вести на ту же страницу, что и в вебе. Core про платформу не знает, поэтому здесь
/// только версия и путь, а базовый адрес сайта приходит из <see cref="LegalOptions"/>.
/// </para>
/// </remarks>
public static class PrivacyPolicy
{
    /// <summary>
    /// Редакция Политики, которую подписывает форма регистрации (ISO-дата).
    /// </summary>
    public const string Version = "2026-11-11";

    /// <summary>
    /// Путь страницы Политики на публичном сайте.
    /// </summary>
    public const string Path = "privacy";

    /// <summary>
    /// Дата редакции: подпись в интерфейсе строится из неё, а не из второго литерала.
    /// </summary>
    public static DateOnly EditionDate { get; } = DateOnly.FromDateTime(
        DateTime.ParseExact(Version, "yyyy-MM-dd", CultureInfo.InvariantCulture));
}
