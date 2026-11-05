using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.BarberRequests.Admin;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberRequests.Admin.RejectBarberRequest;

/// <summary>
/// Обработчик команды <see cref="RejectBarberRequestCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Отклонение — та же транзакция, что у одобрения, но без смены роли и без отзыва сессий:
/// заявитель остаётся <c>Customer</c> и продолжает работать в сервисе как клиент. Причина
/// сохраняется в заявке, чтобы он видел её в кабинете.
/// </para>
/// <para>
/// Повторное решение по уже решённой заявке — <c>409 barberRequest.review.invalidStatus</c>;
/// пустая причина — <c>400 barberRequest.rejection.reasonRequired</c>, длиннее 500 символов —
/// <c>400 barberRequest.rejection.reasonTooLong</c>. Заявка не попадает в <c>audit_log</c>:
/// смена роли (у одобрения) пишется как <c>user.updated</c>, а тексты заявки — PII.
/// </para>
/// </remarks>
public sealed class RejectBarberRequestHandler
    : ICommandHandler<AdminBarberRequestResponse, RejectBarberRequestCommand>
{
    private readonly IBarberRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RejectBarberRequestHandler> _logger;

    public RejectBarberRequestHandler(
        IBarberRequestRepository requests,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<RejectBarberRequestHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _requests = requests;
        _users = users;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Отклоняет заявку с причиной, не меняя роль заявителя.
    /// </summary>
    public async Task<Result<AdminBarberRequestResponse, Failure>> Handle(
        RejectBarberRequestCommand command,
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

            // Статус, причина и её длина проверяются доменом до любой записи: транзакция
            // откатится, и заявка останется в прежнем состоянии.
            var rejected = request.Reject(adminId, command.Reason, now);
            if (rejected.IsFailure)
            {
                return rejected.Error;
            }

            // Заявитель нужен только для администраторского ответа; роль не проверяется
            // и не меняется — отказ безопасен при любой роли.
            var userResult = await _users.GetByIdAsync(request.UserId, token);
            if (userResult.IsFailure)
            {
                return userResult.Error;
            }

            var saved = await _requests.UpdateAsync(request, token);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            return BarberRequestResponseMapper.MapAdmin(request, userResult.Value);
        }, cancellationToken);

        if (result.IsSuccess)
        {
            // Причина отказа — текст заявителя, поэтому в журнал попадает только идентификатор.
            _logger.LogInformation(
                "Barber request {BarberRequestId} has been rejected by user {AdminId}.",
                command.RequestId,
                adminId);
        }

        return result;
    }
}
