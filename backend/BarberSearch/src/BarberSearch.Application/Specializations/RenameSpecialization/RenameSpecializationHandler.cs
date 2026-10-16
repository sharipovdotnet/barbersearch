using BarberSearch.Application.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Application.Specializations.RenameSpecialization;

/// <summary>
/// Обработчик команды <see cref="RenameSpecializationCommand"/>.
/// </summary>
public class RenameSpecializationHandler : ICommandHandler<RenameSpecializationCommand>
{
    private readonly ISpecializationRepository _repository;
    private readonly ILogger<RenameSpecializationHandler> _logger;

    public RenameSpecializationHandler(
        ISpecializationRepository repository,
        ILogger<RenameSpecializationHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Загружает специализацию, применяет к ней переименование и сохраняет агрегат.
    /// </summary>
    /// <remarks>
    /// Новое имя уникально без учёта регистра и пробелов. Смена только регистра
    /// переименованием не считается — иначе запись конфликтовала бы сама с собой.
    /// Окончательная защита — уникальный индекс <c>ux_specializations_name</c>.
    /// </remarks>
    public async Task<UnitResult<Failure>> Handle(
        RenameSpecializationCommand command,
        CancellationToken cancellationToken)
    {
        var specializationResult = await _repository.GetByIdAsync(
            command.SpecializationId,
            cancellationToken);
        if (specializationResult.IsFailure)
        {
            return specializationResult.Error;
        }

        var specialization = specializationResult.Value;

        // Смена только регистра переименованием не считается: лишний запрос не нужен,
        // а запись не должна конфликтовать сама с собой.
        var nameChanged = !string.Equals(
            specialization.Name,
            command.Name,
            StringComparison.OrdinalIgnoreCase);

        if (nameChanged)
        {
            var existing = await _repository.GetByNameAsync(command.Name, cancellationToken);

            // Найдено чужое имя — конфликт. Своё же (например, изменён только регистр
            // или добавлен пробел) переименованию не мешает.
            if (existing.IsSuccess && existing.Value.Id != specialization.Id)
            {
                return new Failure(Error.Conflict(
                    "specialization.rename.nameTaken",
                    $"Specialization '{command.Name.Trim()}' already exists (including inactive ones)."));
            }

            // Отсутствие записи (NOT_FOUND) значит «имя свободно». Пустое имя репозиторий
            // отвергает Validation-ошибкой, не обращаясь к БД, — она уходит наверх как есть.
            if (existing.IsFailure && existing.Error.Errors.All(error => error.Type != ErrorType.NOT_FOUND))
            {
                return existing.Error;
            }
        }

        var updated = specialization.Rename(
            command.Name,
            command.Description);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var saved = await _repository.UpdateAsync(specialization, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        _logger.LogInformation("Specialization {SpecializationId} has been renamed.", specialization.Id);

        return UnitResult.Success<Failure>();
    }
}
