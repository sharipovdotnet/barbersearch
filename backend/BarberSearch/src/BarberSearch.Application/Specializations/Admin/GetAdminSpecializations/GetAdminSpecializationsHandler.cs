using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Specializations.Admin;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Specializations.Admin.GetAdminSpecializations;

/// <summary>
/// Обработчик админского справочника специализаций.
/// </summary>
/// <remarks>
/// Доступ проверяет контроллер политикой <c>AdminOnly</c>. Страницы нет: словарь маленький
/// и отдаётся целиком, как и публичное чтение активных строк.
/// </remarks>
public sealed class GetAdminSpecializationsHandler
    : IQueryHandler<IReadOnlyList<AdminSpecializationResponse>, GetAdminSpecializationsQuery>
{
    private readonly ISpecializationRepository _specializationRepository;

    public GetAdminSpecializationsHandler(ISpecializationRepository specializationRepository)
    {
        ArgumentNullException.ThrowIfNull(specializationRepository);

        _specializationRepository = specializationRepository;
    }

    /// <summary>
    /// Возвращает полный справочник специализаций выбранного статуса.
    /// </summary>
    public async Task<Result<IReadOnlyList<AdminSpecializationResponse>, Failure>> Handle(
        GetAdminSpecializationsQuery query,
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

        return await _specializationRepository.GetAdminListAsync(
            status.Value,
            string.IsNullOrEmpty(search) ? null : search,
            cancellationToken);
    }
}
