namespace BarberSearch.Application.PortfolioPhotos;

/// <summary>
/// Представление файла изображения, которое хранилище отдаёт по media-URL.
/// </summary>
/// <remarks>
/// <para>
/// Вариант — это <b>тот же ресурс в другом представлении</b>, а не новый маршрут: путь и имя
/// файла исходника не меняются, меняется только query-параметр <c>size</c> и <c>Content-Type</c>.
/// Поэтому вариант не попадает в домен: <c>PortfolioMediaUrl</c> и <c>UserAvatarMediaUrl</c>
/// по-прежнему знают ровно один адрес файла, а ширины и имена производных живут в
/// <see cref="PhotoVariantWidths"/>.
/// </para>
/// <para>
/// <see cref="Original"/> — единственный вариант, который всегда существует: он записан
/// загрузкой и никогда не перекодируется. Остальные три — производные WebP-файлы; их может
/// не быть (исходник меньше целевой ширины, генерация упала, файл загружен до среза).
/// </para>
/// </remarks>
public enum PhotoVariant
{
    /// <summary>
    /// Исходный файл как есть: то, что записала загрузка.
    /// </summary>
    Original,

    /// <summary>
    /// Маленький вариант: <c>?size=sm</c>.
    /// </summary>
    Small,

    /// <summary>
    /// Средний вариант: <c>?size=md</c>.
    /// </summary>
    Medium,

    /// <summary>
    /// Большой вариант: <c>?size=lg</c>.
    /// </summary>
    Large
}
