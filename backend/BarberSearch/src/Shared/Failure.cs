using System.Collections;
using System.Collections.ObjectModel;

namespace Shared;

/// <summary>
/// Набор ошибок одного доменного сценария. Может содержать несколько ошибок сразу.
/// </summary>
public class Failure : IEnumerable<Error>
{
    private readonly List<Error> _errors;
    private readonly ReadOnlyCollection<Error> _errorsView;

    public Failure(IEnumerable<Error> errors)
    {
        _errors = [.. errors];
        _errorsView = _errors.AsReadOnly();
    }

    /// <summary>
    /// Провал с единственной ошибкой.
    /// </summary>
    public Failure(Error error)
    {
        _errors = [error];
        _errorsView = _errors.AsReadOnly();
    }

    /// <summary>
    /// Ошибки сценария. Коллекция доступна для чтения, менять её нельзя.
    /// </summary>
    /// <remarks>
    /// Обёртка создаётся один раз в конструкторе (R-39): <c>List&lt;T&gt;.AsReadOnly()</c>
    /// на каждом обращении выделял бы новую <see cref="ReadOnlyCollection{T}"/>, а ошибки
    /// читаются в пути каждого неуспешного ответа. После конструктора <c>_errors</c>
    /// не меняется, поэтому обёртка потокобезопасна для параллельного чтения.
    /// </remarks>
    public IReadOnlyList<Error> Errors => _errorsView;

    public IEnumerator<Error> GetEnumerator()
    {
        return _errors.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator Failure(Error[] errors) => new(errors);

    public static implicit operator Failure(Error error) => new([error]);
}
