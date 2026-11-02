using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.Users.Me;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Users.GetMyProfile;

/// <summary>
/// Обработчик профиля текущего пользователя.
/// </summary>
/// <remarks>
/// Идентификатор берётся из claim'а <c>sub</c>, а не из запроса: иначе клиент мог бы
/// прочитать чужой профиль, подставив id. Аноним до обработчика не доходит — эндпоинт
/// закрыт fallback-политикой, но проверка остаётся и здесь, чтобы ослабление политики
/// не сделало чтение анонимным.
/// </remarks>
public sealed class GetMyProfileHandler
    : IQueryHandler<MyProfileResponse, GetMyProfileQuery>
{
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUser _currentUser;

    public GetMyProfileHandler(
        IUserRepository userRepository,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(currentUser);

        _userRepository = userRepository;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Возвращает профиль вызывающего пользователя.
    /// </summary>
    public async Task<Result<MyProfileResponse, Failure>> Handle(
        GetMyProfileQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userIdResult = _currentUser.RequireUserId();
        if (userIdResult.IsFailure)
        {
            return userIdResult.Error;
        }

        var profileResult = await _userRepository.GetMyProfileAsync(
            userIdResult.Value,
            cancellationToken);
        if (profileResult.IsFailure)
        {
            return profileResult.Error;
        }

        return profileResult.Value;
    }
}
