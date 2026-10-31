using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Specializations.GetSpecializations;

/// <summary>
/// Обработчик чтения публичного справочника специализаций.
/// </summary>
/// <remarks>
/// Порядок задаёт репозиторий (по имени) — обработчик его сохраняет, чтобы фильтр
/// на витрине не переставлял строки между запросами.
/// </remarks>
public sealed class GetSpecializationsHandler
    : IQueryHandler<IReadOnlyList<SpecializationResponse>, GetSpecializationsQuery>
{
    private readonly ISpecializationRepository _repository;

    public GetSpecializationsHandler(ISpecializationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    /// <summary>
    /// Возвращает активные специализации; сбой репозитория пробрасывается как есть.
    /// </summary>
    public async Task<Result<IReadOnlyList<SpecializationResponse>, Failure>> Handle(
        GetSpecializationsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await _repository.GetActiveAsync(cancellationToken);
        if (result.IsFailure)
        {
            return result.Error;
        }

        return result.Value
            .Select(specialization => new SpecializationResponse(
                specialization.Id,
                specialization.Name,
                specialization.Description))
            .ToList();
    }
}
