using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Application.Photos.Admin.GetAdminAvatarPhotos;

/// <summary>
/// Запрос очереди «Аватары» на проверку: поиск, курсор продолжения и размер страницы.
/// </summary>
/// <remarks>
/// Очередь — только <c>Pending</c>-кадры, FIFO по <c>avatar_submitted_at, id</c>.
/// <paramref name="Search"/> ищет по имени, фамилии и email владельца; пустая строка —
/// без фильтра. Неизвестный курсор и слишком длинный поиск отклоняет обработчик: клиенту
/// нужен публичный код ошибки (<c>admin.photos.cursor</c>, <c>admin.photos.search</c>),
/// а не общий ответ биндера.
/// </remarks>
/// <param name="Search">Поиск по имени или email; <c>null</c>/пусто — без фильтра.</param>
/// <param name="Cursor">Непрозрачный курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
public record GetAdminAvatarPhotosQuery(
    string? Search,
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<AdminAvatarPhotoResponse>>;
