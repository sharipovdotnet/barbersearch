using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.RefreshTokens;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.BarberRequests.Admin;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberRequests.Admin.ApproveBarberRequest;

/// <summary>
/// Обработчик команды <see cref="ApproveBarberRequestCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Одобрение — одна транзакция: заявка переходит в <c>Approved</c>, пользователь получает роль
/// <c>Barber</c>, все его refresh-токены отзываются. <c>User.ChangeRole</c> ротирует
/// security stamp, поэтому выданные access-токен и cookie тоже перестают действовать —
/// заявитель войдёт заново и уже барбером. Это ожидаемое поведение, а не дефект.
/// </para>
/// <para>
/// Перед сменой роли проверяется, что заявитель всё ещё <c>Customer</c>: пока заявка ждала
/// решения, роль могли изменить административным действием. Повторное одобрение уже решённой
/// заявки — <c>409 barberRequest.review.invalidStatus</c>.
/// </para>
/// <para>
/// Профиль барбера здесь не создаётся: <c>BarberProfileId</c> остаётся <c>null</c>, и барбер
/// заполняет профиль онбордингом (#45). Заявка не входит в allowlist <c>audit_log</c>:
/// смена роли уже пишется как <c>user.updated</c>, а город и «о себе» — PII.
/// </para>
/// </remarks>
public sealed class ApproveBarberRequestHandler
    : ICommandHandler<AdminBarberRequestResponse, ApproveBarberRequestCommand>
{
    /// <summary>
    /// Текст ошибки «заявитель уже не клиент».
    /// </summary>
    private const string APPLICANT_ROLE_MESSAGE =
        "The applicant is no longer a customer, so the request cannot be approved.";

    private readonly IBarberRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ApproveBarberRequestHandler> _logger;

    public ApproveBarberRequestHandler(
        IBarberRequestRepository requests,
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<ApproveBarberRequestHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(refreshTokens);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _requests = requests;
        _users = users;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Одобряет заявку: меняет роль, отзывает сессии и фиксирует решение администратора.
    /// </summary>
    public async Task<Result<AdminBarberRequestResponse, Failure>> Handle(
        ApproveBarberRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var adminIdResult = _currentUser.RequireUserId();
        if (adminIdResult.IsFailure)
        {
            return adminIdResult.Error;
        }

        var adminId = adminIdResult.Value;
        var now = _timeProvider.GetUtcNow();

        var result = await _unitOfWork.ExecuteAsync<AdminBarberRequestResponse>(async token =>
        {
            var requestResult = await _requests.GetByIdAsync(command.RequestId, token);
            if (requestResult.IsFailure)
            {
                return requestResult.Error;
            }

            var request = requestResult.Value;

            // Решение принимается один раз: заявка вне Pending уже не может быть одобрена.
            if (request.Status != BarberRequestStatus.Pending)
            {
                return new Failure(Error.Conflict(
                    ApiErrorCodes.BarberRequest.ReviewInvalidStatus,
                    "Only a pending barber request can be reviewed."));
            }

            var userResult = await _users.GetByIdAsync(request.UserId, token);
            if (userResult.IsFailure)
            {
                return userResult.Error;
            }

            var user = userResult.Value;

            // Барбера повторно не повышаем, администратора не понижаем: заявка остаётся
            // открытой, а роль меняет отдельное административное действие.
            if (user.Role != UserRole.Customer)
            {
                return new Failure(Error.Conflict(
                    ApiErrorCodes.BarberRequest.ReviewInvalidApplicant,
                    APPLICANT_ROLE_MESSAGE));
            }

            var changed = user.ChangeRole(UserRole.Barber);
            if (changed.IsFailure)
            {
                return changed.Error;
            }

            var savedUser = await _users.UpdateAsync(user, token);
            if (savedUser.IsFailure)
            {
                return savedUser.Error;
            }

            // Смена роли меняет claims следующего access-токена, поэтому живые refresh-токены
            // пользователя отзываются в той же транзакции, что и запись роли (R-08).
            var revoked = await _refreshTokens.RevokeAllForUserAsync(user.Id, now, token);
            if (revoked.IsFailure)
            {
                return revoked.Error;
            }

            var approved = request.Approve(adminId, now);
            if (approved.IsFailure)
            {
                return approved.Error;
            }

            var savedRequest = await _requests.UpdateAsync(request, token);
            if (savedRequest.IsFailure)
            {
                return savedRequest.Error;
            }

            return BarberRequestResponseMapper.MapAdmin(request, user);
        }, cancellationToken);

        if (result.IsSuccess)
        {
            // Только идентификаторы: данные заявки содержат PII, им не место в журнале.
            _logger.LogInformation(
                "Barber request {BarberRequestId} has been approved by user {AdminId}.",
                command.RequestId,
                adminId);
        }

        return result;
    }
}
