using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberProfiles.GetOwnBarberProfile;

/// <summary>
/// Обработчик запроса <see cref="GetOwnBarberProfileQuery"/>.
/// </summary>
/// <remarks>
/// Читает карточку любого статуса — без фильтра <c>IsActive &amp;&amp; IsVerified</c>, как
/// <c>GET .../schedule</c> и <c>GET .../salons</c>: кабинет открыт до подтверждения. Услуги
/// приходят вместе с выключенными: иначе скрытая строка исчезла бы из прайса, и барбер не смог
/// бы вернуть её в работу. Владение проверяется после чтения, по <c>UserId</c> карточки:
/// неизвестный профиль — <c>404</c>, чужой — <c>403 auth.forbidden</c>.
/// </remarks>
public sealed class GetOwnBarberProfileHandler
    : IQueryHandler<BarberProfileResponse, GetOwnBarberProfileQuery>
{
    private readonly IBarberProfileRepository _repository;
    private readonly ICurrentUser _currentUser;

    public GetOwnBarberProfileHandler(
        IBarberProfileRepository repository,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(currentUser);

        _repository = repository;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Возвращает собственную карточку либо <c>404</c> / <c>403</c>.
    /// </summary>
    public async Task<Result<BarberProfileResponse, Failure>> Handle(
        GetOwnBarberProfileQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var cardResult = await _repository.GetOwnCardByIdAsync(
            query.BarberProfileId,
            cancellationToken);
        if (cardResult.IsFailure)
        {
            return cardResult.Error;
        }

        var card = cardResult.Value;

        var allowed = _currentUser.EnsureCanManage(card.UserId);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        return card;
    }
}
