using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Users.Admin;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Users.Admin.GetAdminUser;

/// <summary>
/// Обработчик админской карточки пользователя.
/// </summary>
/// <remarks>
/// Возвращает любого пользователя, включая заблокированного: карточка нужна как раз для
/// того, чтобы снять блокировку. Доступ проверяет контроллер политикой <c>AdminOnly</c>.
/// </remarks>
public sealed class GetAdminUserHandler
    : IQueryHandler<AdminUserDetailResponse, GetAdminUserQuery>
{
    private readonly IUserRepository _userRepository;

    public GetAdminUserHandler(IUserRepository userRepository)
    {
        ArgumentNullException.ThrowIfNull(userRepository);

        _userRepository = userRepository;
    }

    /// <summary>
    /// Возвращает пользователя либо <c>404 admin.users.notFound</c>.
    /// </summary>
    public async Task<Result<AdminUserDetailResponse, Failure>> Handle(
        GetAdminUserQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.UserId == Guid.Empty)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.AdminUser.InvalidId,
                "User id must not be empty.",
                nameof(query.UserId)));
        }

        return await _userRepository.GetAdminByIdAsync(query.UserId, cancellationToken);
    }
}
