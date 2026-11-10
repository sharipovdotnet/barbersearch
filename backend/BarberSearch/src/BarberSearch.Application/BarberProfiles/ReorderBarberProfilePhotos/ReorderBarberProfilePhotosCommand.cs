using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.BarberProfiles.ReorderBarberProfilePhotos;

/// <summary>
/// Команда bulk-изменения порядка фотографий портфолио.
/// </summary>
/// <remarks>
/// <see cref="PhotoIds"/> — полный список фотографий профиля в новом порядке. Одиночная
/// перестановка (<c>ReorderBarberProfilePhotoCommand</c>) остаётся для совместимости,
/// но UI меняет порядок только этим запросом: частичный сбой двух одиночных вызовов
/// оставил бы дубли <c>SortOrder</c>.
/// </remarks>
/// <param name="BarberProfileId">Профиль, чьё портфолио переупорядочивается.</param>
/// <param name="PhotoIds">Идентификаторы фотографий в новом порядке.</param>
public record ReorderBarberProfilePhotosCommand(
    Guid BarberProfileId,
    IReadOnlyList<Guid> PhotoIds) : ICommand;
