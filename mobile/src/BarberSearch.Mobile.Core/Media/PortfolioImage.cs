namespace BarberSearch.Mobile.Core.Media;

/// <summary>
/// Подготовленное приложением изображение портфолио.
/// </summary>
/// <remarks>
/// <para>
/// Пикер отдаёт уже перекодированный JPEG: уменьшенный до 1600 px по длинной стороне,
/// очищенный от метаданных и уложенный в 5 МиБ. Core не декодирует картинку повторно —
/// на телефоне это лишняя память и второй шанс разойтись с браузерной подготовкой.
/// </para>
/// <para>
/// <see cref="FileName"/> нужен только multipart-части: сервер хранит файл по своему
/// идентификатору и исходное имя не использует.
/// </para>
/// </remarks>
/// <param name="Bytes">Байты подготовленного файла.</param>
/// <param name="FileName">Имя файла для multipart-части.</param>
/// <param name="ContentType">MIME-тип содержимого.</param>
public sealed record PortfolioImage(byte[] Bytes, string FileName, string ContentType);
