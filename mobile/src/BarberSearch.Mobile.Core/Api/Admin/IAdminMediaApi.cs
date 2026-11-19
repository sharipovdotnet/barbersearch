namespace BarberSearch.Mobile.Core.Api.Admin;

/// <summary>
/// Чтение файлов медиа с Bearer: превью, которые публично ещё не видны.
/// </summary>
/// <remarks>
/// <para>
/// Раздача <c>api/media/avatars</c> и <c>api/media/portfolio</c> отдаёт неодобренный кадр только
/// владельцу и администратору, остальным — <c>404</c> (<c>AvatarMediaController</c>,
/// <c>PortfolioMediaController</c>). Администратором запрос делает роль из токена, а не адрес,
/// поэтому очередь модерации на телефоне обязана читать превью тем же клиентом, что и списки.
/// </para>
/// <para>
/// Голый <c>Image Source="http://…"</c> этого не умеет: загрузчик картинок платформы ходит без
/// заголовков, и вместо превью на карточке остаётся серый прямоугольник. Поэтому байты читает
/// приложение, а не платформа, и картинка собирается из потока в памяти.
/// </para>
/// </remarks>
public interface IAdminMediaApi
{
    /// <summary>
    /// Читает файл по абсолютному адресу из ответа API.
    /// </summary>
    /// <remarks>
    /// Адрес приходит уже склеенным с базой (<c>MediaUrl.Resolve</c>): относительный путь в
    /// <see cref="HttpClient"/> не попадёт. Вариант размера (<c>?size=md</c>) — часть адреса,
    /// его выбирает вызывающий.
    /// </remarks>
    /// <param name="url">Абсолютный адрес файла.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Содержимое файла.</returns>
    /// <exception cref="BarberSearchApiException">Файл не найден, доступ закрыт или API ответил ошибкой.</exception>
    Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken = default);
}
