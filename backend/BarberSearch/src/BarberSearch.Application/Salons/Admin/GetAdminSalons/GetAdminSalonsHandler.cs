using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Salons.Admin;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Salons.Admin.GetAdminSalons;

/// <summary>
/// Обработчик админского справочника салонов.
/// </summary>
/// <remarks>
/// Доступ проверяет контроллер политикой <c>AdminOnly</c>. Страницы нет: словарь маленький
/// и отдаётся целиком — админ должен видеть строку, чтобы отредактировать её или включить.
/// </remarks>
public sealed class GetAdminSalonsHandler
    : IQueryHandler<IReadOnlyList<AdminSalonResponse>, GetAdminSalonsQuery>
{
    private readonly ISalonRepository _salonRepository;

    public GetAdminSalonsHandler(ISalonRepository salonRepository)
    {
        ArgumentNullException.ThrowIfNull(salonRepository);

        _salonRepository = salonRepository;
    }

    /// <summary>
    /// Возвращает полный справочник салонов выбранного статуса.
    /// </summary>
    public async Task<Result<IReadOnlyList<AdminSalonResponse>, Failure>> Handle(
        GetAdminSalonsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var status = AdminCatalogueFilters.ParseStatus(query.Status);

        if (status is null)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminCatalogue.StatusInvalid,
                "Status must be one of 'all', 'active' or 'hidden'.",
                nameof(query.Status)));
        }

        var search = query.Search?.Trim();

        if (search is { Length: > AdminCatalogueFilters.MAX_CATALOGUE_SEARCH_LENGTH })
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminCatalogue.SearchTooLong,
                $"Search must not be longer than {AdminCatalogueFilters.MAX_CATALOGUE_SEARCH_LENGTH} characters.",
                nameof(query.Search)));
        }

        return await _salonRepository.GetAdminListAsync(
            status.Value,
            string.IsNullOrEmpty(search) ? null : search,
            cancellationToken);
    }
}
