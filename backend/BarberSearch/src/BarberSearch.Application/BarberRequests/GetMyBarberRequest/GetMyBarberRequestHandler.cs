using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.BarberRequests.GetMyBarberRequest;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberRequests.GetMyBarberRequest;

/// <summary>
/// Обработчик запроса своей заявки на роль барбера.
/// </summary>
/// <remarks>
/// Доступно любой роли: после одобрения пользователь уже <c>Barber</c>, но всё ещё должен
/// видеть статус. Заявки может не быть — ответ <c>200 { request: null }</c>, а не <c>404</c>.
/// </remarks>
public sealed class GetMyBarberRequestHandler
    : IQueryHandler<MyBarberRequestResponse, GetMyBarberRequestQuery>
{
    private readonly IBarberRequestRepository _repository;
    private readonly ICurrentUser _currentUser;

    public GetMyBarberRequestHandler(
        IBarberRequestRepository repository,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(currentUser);

        _repository = repository;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Возвращает последнюю заявку текущего пользователя или <c>null</c>.
    /// </summary>
    public async Task<Result<MyBarberRequestResponse, Failure>> Handle(
        GetMyBarberRequestQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userIdResult = _currentUser.RequireUserId();
        if (userIdResult.IsFailure)
        {
            return userIdResult.Error;
        }

        var latest = await _repository.GetLatestByUserAsync(
            userIdResult.Value,
            cancellationToken);
        if (latest.IsFailure)
        {
            return latest.Error;
        }

        return new MyBarberRequestResponse(latest.Value);
    }
}
