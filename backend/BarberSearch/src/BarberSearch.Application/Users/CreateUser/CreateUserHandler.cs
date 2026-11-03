using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Privacy;
using BarberSearch.Contracts.Common;
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
/// <para>
/// При <c>RequireConsent</c> сервер сам решает, можно ли создать аккаунт: клиентская проверка
/// галочки — только удобство. Время согласия берётся из <see cref="TimeProvider"/>, IP
/// приходит из команды; клиентское время не принимается.
/// </para>
/// </remarks>
public class CreateUserHandler : ICommandHandler<Guid, CreateUserCommand>
{
    /// <summary>
    /// Текст ошибки «согласие не передано».
    /// </summary>
    private const string CONSENT_REQUIRED =
        "Consent to personal data processing is required.";

    /// <summary>
    /// Текст ошибки «редакция Политики устарела».
    /// </summary>
    private const string CONSENT_VERSION_MISMATCH =
        "The privacy policy version has changed. Reload the page and confirm consent again.";

    private readonly IUserRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CreateUserHandler> _logger;

    public CreateUserHandler(
        IUserRepository repository,
        TimeProvider timeProvider,
        ILogger<CreateUserHandler> logger)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт пользователя и возвращает его идентификатор.
    /// </summary>
    public async Task<Result<Guid, Failure>> Handle(
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        UserConsent? consent = null;

        if (command.RequireConsent)
        {
            // Редакция сравнивается после Trim и по ordinal: версия — дата, а не «1.0».
            var requestedVersion = command.ConsentVersion?.Trim();

            if (string.IsNullOrEmpty(requestedVersion))
            {
                return new Failure(Error.Validation(
                    ApiErrorCodes.Auth.RegisterConsentRequired,
                    CONSENT_REQUIRED,
                    nameof(command.ConsentVersion)));
            }

            if (!string.Equals(requestedVersion, PrivacyPolicy.Version, StringComparison.Ordinal))
            {
                return new Failure(Error.Validation(
                    ApiErrorCodes.Auth.RegisterConsentVersion,
                    CONSENT_VERSION_MISMATCH,
                    nameof(command.ConsentVersion)));
            }

            var createdConsent = UserConsent.Create(
                requestedVersion,
                _timeProvider.GetUtcNow(),
                command.ClientIp);
            if (createdConsent.IsFailure)
            {
                return createdConsent.Error;
            }

            consent = createdConsent.Value;
        }

        var user = User.Create(
            command.Email,
            command.PasswordHash,
            command.FirstName,
            command.LastName,
            command.Role,
            consent);
        if (user.IsFailure)
        {
            return user.Error;
        }

        // Проверка занятости идёт по каноническому адресу из агрегата: User.Create уже
        // срезал пробелы и привёл регистр, поэтому проверка и вставка видят одно значение.
        var emailExists = await _repository.EmailExistsAsync(user.Value.Email, cancellationToken);
        if (emailExists.IsFailure)
        {
            return emailExists.Error;
        }

        if (emailExists.Value)
        {
            return new Failure(Error.Conflict(
                "user.create.emailTaken",
                $"Email '{user.Value.Email}' is already registered."));
        }

        var added = await _repository.AddAsync(user.Value, cancellationToken);
        if (added.IsFailure)
        {
            return added.Error;
        }

        // В лог попадает только идентификатор: тело регистрации содержит пароль, а IP
        // согласия — персональные данные, которых в логах быть не должно.
        _logger.LogInformation("User {UserId} has been created.", user.Value.Id);

        return user.Value.Id;
    }
}
