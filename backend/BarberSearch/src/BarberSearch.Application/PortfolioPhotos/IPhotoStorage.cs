using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.PortfolioPhotos;

/// <summary>
/// Хранилище файлов фотографий портфолио.
/// </summary>
/// <remarks>
/// Абстракция живёт в Application и не знает ни про ASP.NET, ни про файловую систему:
/// реализация — host-адаптер в слое Web. Интерфейс не знает про БД и публикацию: он только
/// сохраняет, отдаёт и удаляет файл. Загруженный файл адресуется по
/// <c>/api/media/portfolio/{profileId:D}/{mediaId:N}.{ext}</c>.
/// <para>
/// <see cref="DeleteAsync"/> идемпотентен: отсутствие файла — не ошибка, чужие и внешние URL
/// (абсолютные ссылки старых фотографий) молча игнорируются. Ошибки ввода-вывода при удалении
/// не пробрасываются наружу: строка в БД уже удалена, а файл-сирота не ломает UX.
/// </para>
/// </remarks>
public interface IPhotoStorage
{
    /// <summary>
    /// Сохраняет файл и возвращает его публичный URL.
    /// </summary>
    /// <param name="barberProfileId">Профиль барбера — первый сегмент пути.</param>
    /// <param name="mediaId">Идентификатор медиафайла (UUIDv7).</param>
    /// <param name="extension">Расширение <c>jpg</c>, <c>png</c> или <c>webp</c>.</param>
    /// <param name="content">Содержимое файла.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<StoredPhoto, Failure>> SaveAsync(
        Guid barberProfileId,
        Guid mediaId,
        string extension,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Открывает файл по публичному URL для раздачи.
    /// </summary>
    /// <param name="url">Относительный media-URL.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<PhotoContent, Failure>> OpenReadAsync(
        string url,
        CancellationToken cancellationToken);

    /// <summary>
    /// Удаляет файл по публичному URL, если он существует. Идемпотентно.
    /// </summary>
    /// <param name="url">Относительный media-URL.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task DeleteAsync(string url, CancellationToken cancellationToken);
}
