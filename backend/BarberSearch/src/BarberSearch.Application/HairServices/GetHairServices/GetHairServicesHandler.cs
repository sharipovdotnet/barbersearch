using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.HairServices.GetHairServices;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.HairServices.GetHairServices;

/// <summary>
/// Обработчик чтения публичного справочника услуг.
/// </summary>
/// <remarks>
/// Порядок задаёт репозиторий (категория, затем имя) — обработчик его сохраняет,
/// чтобы фильтр на витрине не переставлял строки между запросами.
/// </remarks>
public sealed class GetHairServicesHandler
    : IQueryHandler<IReadOnlyList<HairServiceResponse>, GetHairServicesQuery>
{
    private readonly IHairServiceRepository _repository;

    public GetHairServicesHandler(IHairServiceRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    /// <summary>
    /// Возвращает активные услуги; сбой репозитория пробрасывается как есть.
    /// </summary>
    public async Task<Result<IReadOnlyList<HairServiceResponse>, Failure>> Handle(
        GetHairServicesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await _repository.GetActiveAsync(cancellationToken);
        if (result.IsFailure)
        {
            return result.Error;
        }

        return result.Value
            .Select(hairService => new HairServiceResponse(
                hairService.Id,
                hairService.Name,
                hairService.Category,
                hairService.DefaultDurationMinutes,
                hairService.Description))
            .ToList();
    }
}
