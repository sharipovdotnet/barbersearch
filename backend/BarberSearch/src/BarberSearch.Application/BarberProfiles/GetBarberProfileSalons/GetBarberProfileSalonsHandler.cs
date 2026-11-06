using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfileSalons;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberProfiles.GetBarberProfileSalons;

/// <summary>
/// Обработчик запроса <see cref="GetBarberProfileSalonsQuery"/>.
/// </summary>
/// <remarks>
/// Доступ тот же, что у расписания и <c>FindBarberProfileSalon</c>: владелец профиля
/// или администратор. Проверка идёт по <c>user_id</c> профиля, поэтому запрос работает
/// и для неподтверждённого профиля — барбер заполняет салоны до верификации.
/// </remarks>
public sealed class GetBarberProfileSalonsHandler
    : IQueryHandler<IReadOnlyList<BarberSalonSummaryResponse>, GetBarberProfileSalonsQuery>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;

    public GetBarberProfileSalonsHandler(
        IBarberProfileRepository repository,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(currentUser);

        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<BarberSalonSummaryResponse>, Failure>> Handle(
        GetBarberProfileSalonsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var ownerResult = await _repository.GetOwnerIdAsync(
            query.BarberProfileId,
            cancellationToken);
        if (ownerResult.IsFailure)
        {
            return ownerResult.Error;
        }

        var allowed = _currentUser.EnsureCanManage(ownerResult.Value);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        return await _repository.GetSalonSummariesAsync(
            query.BarberProfileId,
            cancellationToken);
    }
}
