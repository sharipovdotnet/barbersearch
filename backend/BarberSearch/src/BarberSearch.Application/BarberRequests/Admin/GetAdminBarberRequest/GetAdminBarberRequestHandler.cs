using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.BarberRequests.Admin;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberRequests.Admin.GetAdminBarberRequest;

/// <summary>
/// Обработчик администраторской карточки заявки в барберы.
/// </summary>
/// <remarks>
/// Карточка читается по прямому URL, поэтому неизвестный идентификатор — <c>404
/// barberRequest.notFound</c>. Это второй администраторский контракт с PII заявителя;
/// публичного чтения по идентификатору нет.
/// </remarks>
public sealed class GetAdminBarberRequestHandler
    : IQueryHandler<AdminBarberRequestResponse, GetAdminBarberRequestQuery>
{
    private readonly IBarberRequestRepository _repository;

    public GetAdminBarberRequestHandler(IBarberRequestRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    /// <summary>
    /// Возвращает заявку любого статуса либо <c>404 barberRequest.notFound</c>.
    /// </summary>
    public async Task<Result<AdminBarberRequestResponse, Failure>> Handle(
        GetAdminBarberRequestQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await _repository.GetAdminByIdAsync(query.RequestId, cancellationToken);
    }
}
