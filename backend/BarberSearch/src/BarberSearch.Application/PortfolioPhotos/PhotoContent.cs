namespace BarberSearch.Application.PortfolioPhotos;

/// <summary>
/// Открытый файл фотографии: поток, MIME-тип и длина.
/// </summary>
/// <remarks>
/// Поток принадлежит вызывающему коду: контроллер раздачи отдаёт его в ответ и закрывает
/// вместе с ответом. Хранилище не кэширует и не запоминает содержимое.
/// </remarks>
/// <param name="Stream">Поток с содержимым файла.</param>
/// <param name="ContentType">MIME-тип, определённый по расширению.</param>
/// <param name="Length">Длина файла в байтах.</param>
public sealed record PhotoContent(Stream Stream, string ContentType, long Length);
