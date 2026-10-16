using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.Users;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.CreateUser;

/// <summary>
/// Обработчик команды <see cref="CreateUserCommand"/>.
/// </summary>
/// <remarks>
/// Кроме доменной валидации проверяет занятость адреса электронной почты: это правило
/// требует обращения к хранилищу, поэтому в домене его нет (уникальный индекс БД — последняя защита).
/// </remarks>
public class CreateUserHandler : ICommandHandler<Guid, CreateUserCommand>
{
    private readonly IUserRepository _repository;
    private readonly ILogger<CreateUserHandler> _logger;

    public CreateUserHandler(IUserRepository repository, ILogger<CreateUserHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт пользователя и возвращает его идентификатор.
    /// </summary>
    public async Task<Result<Guid, Failure>> Handle(
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        var emailExists = await _repository.EmailExistsAsync(command.Email, cancellationToken);
        if (emailExists.IsFailure)
        {
            return emailExists.Error;
        }

        if (emailExists.Value)
        {
            return new Failure(Error.Conflict(
                "user.create.emailTaken",
                $"Email '{command.Email}' is already registered."));
        }

        var user = User.Create(
            command.Email,
            command.PasswordHash,
            command.FirstName,
            command.LastName,
            command.Role);
        if (user.IsFailure)
        {
            return user.Error;
        }

        var added = await _repository.AddAsync(user.Value, cancellationToken);
        if (added.IsFailure)
        {
            return added.Error;
        }

        _logger.LogInformation("User {UserId} has been created.", user.Value.Id);

        return user.Value.Id;
    }
}
