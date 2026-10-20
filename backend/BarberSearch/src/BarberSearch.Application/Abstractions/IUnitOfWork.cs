using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.Abstractions;

/// <summary>
/// Граница транзакции для операций, которые меняют несколько агрегатов.
/// </summary>
/// <remarks>
/// <para>
/// Реализация открывает транзакцию базы данных, выполняет операцию и фиксирует её только
/// при успехе. Любой <c>Failure</c> или исключение откатывают транзакцию целиком: частично
/// применённое изменение (например, сохранённый отзыв без пересчитанного рейтинга) наружу
/// не попадает.
/// </para>
/// <para>
/// Репозитории внутри операции должны работать с тем же scoped <c>DbContext</c>, что и
/// реализация <see cref="IUnitOfWork"/>: иначе их <c>SaveChanges</c> уйдёт в другую транзакцию
/// или в её отсутствие.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Выполняет операцию без возвращаемого значения в одной транзакции.
    /// </summary>
    /// <param name="operation">Работа, которую нужно выполнить атомарно.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат операции; <c>Failure</c> означает откат транзакции.</returns>
    Task<UnitResult<Failure>> ExecuteAsync(
        Func<CancellationToken, Task<UnitResult<Failure>>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Выполняет операцию с возвращаемым значением в одной транзакции.
    /// </summary>
    /// <typeparam name="T">Тип успешного результата.</typeparam>
    /// <param name="operation">Работа, которую нужно выполнить атомарно.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат операции; <c>Failure</c> означает откат транзакции.</returns>
    Task<Result<T, Failure>> ExecuteAsync<T>(
        Func<CancellationToken, Task<Result<T, Failure>>> operation,
        CancellationToken cancellationToken = default);
}
