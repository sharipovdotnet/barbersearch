namespace BarberSearch.Application.PortfolioPhotos;

/// <summary>
/// Открытый файл фотографии: поток, MIME-тип, длина и ETag.
/// </summary>
/// <remarks>
/// <para>
/// Поток принадлежит вызывающему коду: контроллер раздачи отдаёт его в ответ и закрывает
/// вместе с ответом. Хранилище не кэширует и не запоминает содержимое.
/// </para>
/// <para>
/// <see cref="EntityTag"/> приходит из хранилища, потому что только оно знает, что именно
/// отдало: исходник или производный вариант. Строка уже в кавычках (<c>"…"</c>) и пригодна
/// и для заголовка <c>ETag</c>, и для сравнения с <c>If-None-Match</c>.
/// </para>
/// </remarks>
/// <param name="Stream">Поток с содержимым файла.</param>
/// <param name="ContentType">MIME-тип, определённый по расширению файла.</param>
/// <param name="Length">Длина файла в байтах.</param>
/// <param name="EntityTag">Сильный ETag отданного файла в кавычках.</param>
public sealed record PhotoContent(Stream Stream, string ContentType, long Length, string EntityTag);
