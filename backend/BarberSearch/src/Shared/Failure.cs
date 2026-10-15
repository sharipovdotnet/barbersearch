using System.Collections;
using System.Collections.Generic;

namespace Shared;

/// <summary>
/// Набор ошибок одного доменного сценария. Может содержать несколько ошибок сразу.
/// </summary>
public class Failure : IEnumerable<Error>
{
    private readonly List<Error> _errors;

    public Failure(IEnumerable<Error> errors)
    {
        _errors = [.. errors];
    }

    /// <summary>
    /// Провал с единственной ошибкой.
    /// </summary>
    public Failure(Error error)
    {
        _errors = [error];
    }

    /// <summary>
    /// Ошибки сценария. Коллекция доступна для чтения, менять её нельзя.
    /// </summary>
    public IReadOnlyList<Error> Errors => _errors.AsReadOnly();

    public IEnumerator<Error> GetEnumerator()
    {
        return _errors.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator Failure(Error[] errors) => new(errors);

    public static implicit operator Failure(Error error) => new([error]);
}
