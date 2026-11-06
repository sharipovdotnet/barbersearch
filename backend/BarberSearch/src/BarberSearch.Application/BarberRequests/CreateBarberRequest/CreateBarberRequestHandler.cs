using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.BarberRequests;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberRequests;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.BarberRequests.CreateBarberRequest;

/// <summary>
/// Обработчик команды <see cref="CreateBarberRequestCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// Заявку подаёт только клиент: барбер и администратор получают
/// <c>403 barberRequest.create.forbidden</c>. Роль берётся из базы, а не из claim'а: пока
/// заявка ждала решения, роль могла измениться, и устаревший токен не должен открывать
/// повторную подачу.
/// </para>
/// <para>
/// Одна активная заявка на пользователя обеспечена двумя механизмами: предпроверка
/// <see cref="IBarberRequestRepository.HasPendingAsync"/> на обычном пути и частичный
/// уникальный индекс <c>ux_barber_requests_pending_user</c> на гонке. Оба дают один и тот же
/// публичный код <c>barberRequest.create.alreadyPending</c>, поэтому одновременная подача
/// не превращается в <c>500</c>.
/// </para>
/// </remarks>
public sealed class CreateBarberRequestHandler
    : ICommandHandler<BarberRequestResponse, CreateBarberRequestCommand>
{
    /// <summary>
    /// Текст ошибки «заявку может подать только клиент».
    /// </summary>
    private const string FORBIDDEN_MESSAGE = "Only a customer can submit a barber request.";

    /// <summary>
    /// Текст ошибки «активная заявка уже есть».
    /// </summary>
    private const string ALREADY_PENDING_MESSAGE =
        "A barber request is already pending for this user.";

    private readonly IBarberRequestRepository _repository;
    private readonly IUserRepository _users;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CreateBarberRequestHandler> _logger;

    public CreateBarberRequestHandler(
        IBarberRequestRepository repository,
        IUserRepository users,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ILogger<CreateBarberRequestHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _users = users;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт заявку от текущего пользователя и возвращает её в статусе <c>Pending</c>.
    /// </summary>
    public async Task<Result<BarberRequestResponse, Failure>> Handle(
        CreateBarberRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userIdResult = _currentUser.RequireUserId();
        if (userIdResult.IsFailure)
        {
            return userIdResult.Error;
        }

        var userResult = await _users.GetByIdAsync(userIdResult.Value, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        if (user.Role != UserRole.Customer)
        {
            return new Failure(Error.Forbidden(
                ApiErrorCodes.BarberRequest.CreateForbidden,
                FORBIDDEN_MESSAGE));
        }

        var request = command.Request;

        var created = BarberRequest.Create(
            user.Id,
            request.DisplayName,
            request.YearsOfExperience,
            request.City,
            request.About,
            request.HasSalon,
            _timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        var pending = await _repository.HasPendingAsync(user.Id, cancellationToken);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        if (pending.Value)
        {
            return AlreadyPending();
        }

        var added = await _repository.AddAsync(created.Value, cancellationToken);
        if (added.IsFailure)
        {
            // Частичный уникальный индекс ловит гонку двух подач: клиент должен получить
            // тот же 409 barberRequest.create.alreadyPending, а не инфраструктурный код.
            if (added.Error.Errors.Any(error =>
                    error.Code == ApiErrorCodes.BarberRequest.AddUniqueViolation))
            {
                return AlreadyPending();
            }

            return added.Error;
        }

        // В лог попадает только идентификатор: город и «о себе» — персональные данные.
        _logger.LogInformation(
            "Barber request {BarberRequestId} has been created.",
            created.Value.Id);

        return BarberRequestResponseMapper.Map(created.Value);
    }

    private static Failure AlreadyPending()
        => new(Error.Conflict(
            ApiErrorCodes.BarberRequest.CreateAlreadyPending,
            ALREADY_PENDING_MESSAGE));
}
