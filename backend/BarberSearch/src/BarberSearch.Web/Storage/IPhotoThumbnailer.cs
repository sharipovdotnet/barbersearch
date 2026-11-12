using BarberSearch.Application.PortfolioPhotos;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Web.Storage;

/// <summary>
/// Генератор производных вариантов изображения: WebP шириной <c>sm/md/lg</c>.
/// </summary>
/// <remarks>
/// <para>
/// Абстракция живёт рядом с адаптером, а не в Application: декодирование — это host-деталь
/// (нативная библиотека, память, потоки), а слою Application нужен только результат в байтах.
/// Хранилища (<see cref="LocalPhotoStorage"/> и <see cref="S3PhotoStorage"/>) — вызывающие: они
/// решают, куда писать производные файлы и что отдавать, если вариант невозможен.
/// </para>
/// <para>
/// Исходник не перекодируется и не кропается: производные сохраняют пропорции кадра.
/// Апскейла нет: если исходник уже не шире целевой ширины, вариант не создаётся.
/// </para>
/// </remarks>
public interface IPhotoThumbnailer
{
    /// <summary>
    /// Создаёт доступные производные варианты по исходному файлу.
    /// </summary>
    /// <remarks>
    /// Возвращаются только те варианты, которые уже исходника: ширина считается по
    /// <see cref="PhotoVariantWidths"/>. Пустой список — «вариантов не будет» (теоретический
    /// случай: инспектор требует не меньше 300 px, а самый маленький вариант — 256 px),
    /// а <c>Failure</c> — «декодировать не удалось»; в обоих случаях вызывающий отдаёт исходник.
    /// </remarks>
    /// <param name="original">Байты исходного файла.</param>
    /// <param name="extension">Расширение исходника: <c>jpg</c>, <c>png</c> или <c>webp</c>.</param>
    /// <param name="ownerId">Профиль барбера либо пользователь — только для журнала.</param>
    /// <param name="mediaId">Идентификатор медиафайла — только для журнала.</param>
    /// <param name="isAvatar"><c>true</c> — аватар, <c>false</c> — фотография портфолио.</param>
    /// <param name="cancellationToken">Токен отмены: отмена пробрасывается как исключение.</param>
    Task<Result<IReadOnlyList<PhotoThumbnail>, Failure>> GenerateAsync(
        ReadOnlyMemory<byte> original,
        string extension,
        Guid ownerId,
        Guid mediaId,
        bool isAvatar,
        CancellationToken cancellationToken);
}
