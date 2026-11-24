using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.HairServices.GetHairServices;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.HairServices.GetHairServices;

/// <summary>
/// Обработчик чтения публичного справочника услуг.
/// </summary>
/// <remarks>
/// Порядок задаёт репозиторий (категория специализации, затем имя) — обработчик его
/// сохраняет, чтобы фильтр на витрине не переставлял строки между запросами. Категория
/// и специализация приходят из join'а с <c>specializations</c>: у услуги своего поля
/// категории больше нет.
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
            .Select(row => new HairServiceResponse(
                row.HairService.Id,
                row.HairService.Name,
                row.Category,
                row.HairService.DefaultDurationMinutes,
                row.HairService.Description,
                row.HairService.SpecializationId))
            .ToList();
    }
}
