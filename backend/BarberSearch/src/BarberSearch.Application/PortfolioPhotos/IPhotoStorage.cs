using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.PortfolioPhotos;

/// <summary>
/// Хранилище файлов изображений пользователей: фотографий портфолио и аватаров.
/// </summary>
/// <remarks>
/// Абстракция живёт в Application и не знает ни про ASP.NET, ни про файловую систему:
/// реализация — host-адаптер в слое Web. Интерфейс не знает про БД и публикацию: он только
/// сохраняет, отдаёт и удаляет файл. Загруженная фотография адресуется по
/// <c>/api/media/portfolio/{profileId:D}/{mediaId:N}.{ext}</c>, аватар — по
/// <c>/api/media/avatars/{userId:D}/{mediaId:N}.{ext}</c>.
/// <para>
/// Один адаптер, один корень, один диск обслуживают <b>два вида файлов</b>. Имя интерфейса
/// осталось прежним, потому что различается только корневой каталог
/// (<c>portfolio/</c> и <c>avatars/</c>): отдельная абстракция хранилища аватаров завела бы
/// второй экземпляр настроек, вторую проверку «путь внутри корня» и второй код ошибки записи
/// без единой выгоды. <see cref="OpenReadAsync"/> и <see cref="DeleteAsync"/> поэтому разбирают
/// оба префикса — сначала portfolio, затем avatars.
/// </para>
/// <para>
/// <see cref="DeleteAsync"/> идемпотентен: отсутствие файла — не ошибка, чужие и внешние URL
/// (абсолютные ссылки старых фотографий) молча игнорируются. Ошибки ввода-вывода при удалении
/// не пробрасываются наружу: строка в БД уже удалена, а файл-сирота не ломает UX.
/// </para>
/// </remarks>
public interface IPhotoStorage
{
    /// <summary>
    /// Сохраняет файл фотографии портфолио и возвращает его публичный URL.
    /// </summary>
    /// <remarks>
    /// После записи исходника хранилище пытается сразу записать производные варианты
    /// (<c>?size=sm|md|lg</c>). Это <b>best effort</b>: сбой генерации пишется <c>Warning</c>'ом,
    /// но загрузку не роняет — кадр живёт с исходником, а вариант догенерируется на первом
    /// чтении.
    /// </remarks>
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
    /// Сохраняет файл аватара пользователя и возвращает его публичный URL.
    /// </summary>
    /// <remarks>
    /// Путь — <c>{RootPath}/avatars/{userId:N}/{mediaId:N}.{ext}</c>. Запись атомарная, как у
    /// фотографии портфолио: временный файл в том же каталоге и <c>File.Move</c>. Ошибка
    /// записи — <c>500 user.uploadAvatar.storage</c> без пути и имени файла в тексте.
    /// </remarks>
    /// <param name="userId">Пользователь — первый сегмент пути.</param>
    /// <param name="mediaId">Идентификатор медиафайла (UUIDv7).</param>
    /// <param name="extension">Расширение <c>jpg</c>, <c>png</c> или <c>webp</c>.</param>
    /// <param name="content">Содержимое файла.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<StoredPhoto, Failure>> SaveAvatarAsync(
        Guid userId,
        Guid mediaId,
        string extension,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Открывает файл по публичному URL для раздачи.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Разбирает оба префикса: <c>/api/media/portfolio/…</c> и <c>/api/media/avatars/…</c>.
    /// Для аватара отказ приходит с кодом <c>avatarMedia.notFound</c>, для фотографии — с
    /// прежним <c>portfolioMedia.notFound</c>.
    /// </para>
    /// <para>
    /// <paramref name="variant"/> — представление того же файла. <see cref="PhotoVariant.Original"/>
    /// отдаёт исходник как есть. Производный вариант отдаётся, если он уже на диске либо его
    /// удалось сгенерировать на этом чтении (ленивая генерация: файлы, загруженные до среза,
    /// и кадры, чья генерация на загрузке упала). Если вариант невозможен — исходник меньше
    /// целевой ширины, инспектор отказал, декодер упал, — отдаётся <b>исходник</b> с исходным
    /// <c>Content-Type</c> и исходным ETag: сломанной картинки в <c>srcset</c> быть не может.
    /// </para>
    /// </remarks>
    /// <param name="url">Относительный media-URL без query-параметров.</param>
    /// <param name="variant">Запрошенное представление файла.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<Result<PhotoContent, Failure>> OpenReadAsync(
        string url,
        PhotoVariant variant,
        CancellationToken cancellationToken);

    /// <summary>
    /// Удаляет файл по публичному URL, если он существует. Идемпотентно.
    /// </summary>
    /// <remarks>
    /// Как и чтение, разбирает оба префикса. Удаляет исходник <b>и все производные варианты</b>
    /// кадра: перечисление идёт по маске <c>{mediaId:N}-*.webp</c> в каталоге исходника, поэтому
    /// вариант не может прийти из query. Отсутствующий файл, чужой и внешний URL —
    /// не ошибка: вызывающий не должен различать «нечего удалять» и «удалено».
    /// </remarks>
    /// <param name="url">Относительный media-URL.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task DeleteAsync(string url, CancellationToken cancellationToken);
}
