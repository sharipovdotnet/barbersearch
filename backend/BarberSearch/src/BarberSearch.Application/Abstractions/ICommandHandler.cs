using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Abstractions;

/// <summary>
/// Маркер команды — операции, изменяющей состояние.
/// </summary>
public interface ICommand;

/// <summary>
/// Обработчик команды, возвращающий результат (идентификатор созданной сущности и т. п.).
/// </summary>
public interface ICommandHandler<TResponse, in TCommand>
    where TCommand : ICommand
{
    Task<Result<TResponse, Failure>> Handle(TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Обработчик команды без результата.
/// </summary>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<UnitResult<Failure>> Handle(TCommand command, CancellationToken cancellationToken);
}
