using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.HairServices.Admin;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.HairServices.Admin.GetAdminHairServices;

/// <summary>
/// Обработчик админского справочника услуг.
/// </summary>
/// <remarks>
/// Доступ проверяет контроллер политикой <c>AdminOnly</c>: обработчик не знает, кто его
/// вызвал. Страницы нет: справочник маленький, а созданная строка должна быть видна сразу.
/// </remarks>
public sealed class GetAdminHairServicesHandler
    : IQueryHandler<IReadOnlyList<AdminHairServiceResponse>, GetAdminHairServicesQuery>
{
    private readonly IHairServiceRepository _hairServiceRepository;

    public GetAdminHairServicesHandler(IHairServiceRepository hairServiceRepository)
    {
        ArgumentNullException.ThrowIfNull(hairServiceRepository);

        _hairServiceRepository = hairServiceRepository;
    }

    /// <summary>
    /// Возвращает полный справочник услуг выбранного статуса.
    /// </summary>
    public async Task<Result<IReadOnlyList<AdminHairServiceResponse>, Failure>> Handle(
        GetAdminHairServicesQuery query,
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

        return await _hairServiceRepository.GetAdminListAsync(
            status.Value,
            string.IsNullOrEmpty(search) ? null : search,
            cancellationToken);
    }
}
