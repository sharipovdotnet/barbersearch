using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Abstractions;

/// <summary>
/// Маркер запроса — операции, читающей состояние и ничего не меняющей.
/// </summary>
public interface IQuery<TResponse>;

/// <summary>
/// Обработчик запроса.
/// </summary>
public interface IQueryHandler<TResponse, in TQuery>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse, Failure>> Handle(TQuery query, CancellationToken cancellationToken);
}
