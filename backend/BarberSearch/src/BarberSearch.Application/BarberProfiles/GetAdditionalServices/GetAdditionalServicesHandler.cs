using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberProfiles.GetAdditionalServices;

/// <summary>
/// Обработчик чтения подсказок фильтра «дополнительные услуги».
/// </summary>
/// <remarks>
/// Порядок и лимит задаёт репозиторий, обработчик результат не пересобирает: подсказки
/// должны быть стабильны между запросами, иначе выпадающий список «прыгает».
/// Сбой репозитория пробрасывается как есть — экран витрины деградирует до поля без подсказок.
/// </remarks>
public sealed class GetAdditionalServicesHandler
    : IQueryHandler<IReadOnlyList<string>, GetAdditionalServicesQuery>
{
    private readonly IBarberProfileRepository _repository;

    public GetAdditionalServicesHandler(IBarberProfileRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    /// <summary>
    /// Возвращает различные дополнительные услуги видимых барберов.
    /// </summary>
    public Task<Result<IReadOnlyList<string>, Failure>> Handle(
        GetAdditionalServicesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return _repository.GetAdditionalServicesAsync(cancellationToken);
    }
}
