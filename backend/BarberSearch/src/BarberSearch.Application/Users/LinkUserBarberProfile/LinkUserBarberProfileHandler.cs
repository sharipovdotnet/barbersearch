using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Users.LinkUserBarberProfile;

/// <summary>
/// Обработчик команды <see cref="LinkUserBarberProfileCommand"/>.
/// </summary>
/// <remarks>
/// Существование самого профиля барбера здесь не проверяется: это граница другого агрегата,
/// ссылочную целостность держит БД.
/// </remarks>
public class LinkUserBarberProfileHandler : ICommandHandler<LinkUserBarberProfileCommand>
{
    private readonly IUserRepository _repository;
    private readonly ILogger<LinkUserBarberProfileHandler> _logger;

    public LinkUserBarberProfileHandler(IUserRepository repository, ILogger<LinkUserBarberProfileHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает пользователя, привязывает профиль барбера и сохраняет агрегат.
    /// </summary>
    public async Task<UnitResult<Failure>> Handle(
        LinkUserBarberProfileCommand command,
        CancellationToken cancellationToken)
    {
        var userResult = await _repository.GetByIdAsync(command.UserId, cancellationToken);
        if (userResult.IsFailure)
        {
            return userResult.Error;
        }

        var user = userResult.Value;

        var linked = user.LinkBarberProfile(command.BarberProfileId);
        if (linked.IsFailure)
        {
            return linked.Error;
        }

        var saved = await _repository.UpdateAsync(user, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation(
            "Barber profile {BarberProfileId} has been linked to user {UserId}.",
            command.BarberProfileId,
            user.Id);

        return UnitResult.Success<Failure>();
    }
}
