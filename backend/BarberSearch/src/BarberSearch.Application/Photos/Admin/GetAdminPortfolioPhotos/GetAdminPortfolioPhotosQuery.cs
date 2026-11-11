using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Admin;
using BarberSearch.Contracts.Common;

namespace BarberSearch.Application.Photos.Admin.GetAdminPortfolioPhotos;

/// <summary>
/// Запрос очереди «Портфолио» на проверку: поиск, курсор продолжения и размер страницы.
/// </summary>
/// <remarks>
/// Очередь — только <c>Pending</c>-кадры, FIFO по <c>created_at, id</c>. Поиск идёт по имени
/// барбера и email владельца профиля; пустая строка — без фильтра. Ошибки параметров
/// отклоняет обработчик публичными кодами <c>admin.photos.cursor</c>,
/// <c>admin.photos.search</c> и <c>admin.photos.pageSize</c>.
/// </remarks>
/// <param name="Search">Поиск по имени барбера или email владельца; <c>null</c>/пусто — без фильтра.</param>
/// <param name="Cursor">Непрозрачный курсор следующей страницы.</param>
/// <param name="PageSize">Размер страницы; по умолчанию 20, максимум 100.</param>
public record GetAdminPortfolioPhotosQuery(
    string? Search,
    string? Cursor,
    int? PageSize) : IQuery<PagedResponse<AdminPortfolioPhotoResponse>>;
