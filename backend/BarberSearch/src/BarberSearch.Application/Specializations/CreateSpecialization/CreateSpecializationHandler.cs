using BarberSearch.Application.Abstractions;
using BarberSearch.Domain.Specializations;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Specializations.CreateSpecialization;

/// <summary>
/// Обработчик команды <see cref="CreateSpecializationCommand"/>.
/// </summary>
public class CreateSpecializationHandler : ICommandHandler<Guid, CreateSpecializationCommand>
{
    private readonly ISpecializationRepository _repository;
    private readonly ILogger<CreateSpecializationHandler> _logger;

    public CreateSpecializationHandler(
        ISpecializationRepository repository,
        ILogger<CreateSpecializationHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт специализацию и возвращает её идентификатор.
    /// </summary>
    /// <remarks>
    /// Имя уникально без учёта регистра и пробелов: проверка здесь даёт понятную ошибку,
    /// окончательная защита — уникальный индекс <c>ux_specializations_name</c> по колонке
    /// <c>citext</c>. Имя занимает и неактивная запись, иначе после её активации в справочнике
    /// оказались бы два одинаковых тега.
    /// </remarks>
    public async Task<Result<Guid, Failure>> Handle(
        CreateSpecializationCommand command,
        CancellationToken cancellationToken)
    {
        var specialization = Specialization.Create(
            command.Name,
            command.Description);
        if (specialization.IsFailure)
        {
            return specialization.Error;
        }

        var existing = await _repository.GetByNameAsync(
            specialization.Value.Name,
            cancellationToken);

        if (existing.IsSuccess)
        {
            return new Failure(Error.Conflict(
                "specialization.create.nameTaken",
                $"Specialization '{specialization.Value.Name.Trim()}' already exists (including inactive ones)."));
        }

        // Об отсутствии записи репозиторий сообщает ошибкой NOT_FOUND — для нас это
        // «имя свободно». Любая другая ошибка — сбой, её нужно вернуть наверх.
        if (existing.Error.Errors.All(error => error.Type != ErrorType.NOT_FOUND))
        {
            return existing.Error;
        }

        var added = await _repository.AddAsync(specialization.Value, cancellationToken);
        if (added.IsFailure)
        {
            return added.Error;
        }

        _logger.LogInformation(
            "Specialization {SpecializationId} has been created.",
            specialization.Value.Id);

        return specialization.Value.Id;
    }
}
