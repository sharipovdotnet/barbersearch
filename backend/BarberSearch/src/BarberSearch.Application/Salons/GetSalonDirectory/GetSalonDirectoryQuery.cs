using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Salons.GetSalonDirectory;

namespace BarberSearch.Application.Salons.GetSalonDirectory;

/// <summary>
/// Публичный каталог активных салонов для выбора в кабинете барбера.
/// </summary>
/// <remarks>
/// Справочник маленький и отдаётся целиком: пагинации нет, как у
/// <c>GET api/specializations</c>. Оба фильтра необязательны, пустая строка означает
/// «без фильтра».
/// </remarks>
/// <param name="City">Город: точное совпадение после обрезки пробелов.</param>
/// <param name="Search">Поиск по названию и адресу; <c>null</c> — без фильтра.</param>
public record GetSalonDirectoryQuery(
    string? City,
    string? Search) : IQuery<IReadOnlyList<SalonDirectoryItemResponse>>;
