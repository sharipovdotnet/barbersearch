using Shared;
using Xunit;

namespace BarberSearch.Application.UnitTests;

/// <summary>
/// Контракт набора ошибок <see cref="Failure"/> (R-39): read-only view создаётся один раз
/// в конструкторе, содержимое и порядок ошибок сохраняются, снаружи коллекция неизменяема.
/// </summary>
/// <remarks>
/// <see cref="Failure"/> живёт в <c>Shared</c>, поэтому тесты лежат в Application-проекте:
/// он уже ссылается на <c>Shared</c>, отдельный тестовый проект ради одного класса не нужен.
/// </remarks>
public class FailureTests
{
    [Fact]
    public void Errors_ReturnsSameInstance_OnEveryAccess()
    {
        var multiple = new Failure(
        [
            Error.Validation("validation.first", "First."),
            Error.Conflict("conflict.second", "Second.")
        ]);
        var single = new Failure(Error.NotFound("record.missing", "Missing."));
        var empty = new Failure(Array.Empty<Error>());

        Assert.Same(multiple.Errors, multiple.Errors);
        Assert.Same(single.Errors, single.Errors);
        Assert.Same(empty.Errors, empty.Errors);
    }

    [Fact]
    public void Errors_PreserveContentAndOrder_FromEnumerable()
    {
        Error[] errors =
        [
            Error.Validation("validation.first", "First.", "displayName"),
            Error.Conflict("conflict.second", "Second."),
            Error.Forbidden("forbidden.third", "Third.")
        ];

        var failure = new Failure(errors);

        Assert.Equal(errors, failure.Errors);
        Assert.Equal(errors, failure);
    }

    [Fact]
    public void Errors_ReturnSingleError_FromSingleErrorConstructor()
    {
        var error = Error.Validation("validation.only", "Only.", "email");

        var failure = new Failure(error);

        var only = Assert.Single(failure.Errors);
        Assert.Same(error, only);
        Assert.Same(only, Assert.Single(failure));
    }

    [Fact]
    public void Errors_AreReadOnly()
    {
        var failure = new Failure([Error.Validation("validation.first", "First."), Error.Conflict("conflict.second", "Second.")]);

        var list = Assert.IsAssignableFrom<IList<Error>>(failure.Errors);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(Error.Failure("failure.third", "Third.")));
        Assert.Equal(2, failure.Errors.Count);
    }

    [Fact]
    public void Errors_DoNotChange_WhenSourceCollectionIsMutated()
    {
        var source = new List<Error> { Error.Validation("validation.first", "First.") };
        var failure = new Failure(source);

        source.Add(Error.Conflict("conflict.second", "Second."));
        source.Clear();

        var only = Assert.Single(failure.Errors);
        Assert.Equal("validation.first", only.Code);
    }

    [Fact]
    public void Errors_EmptySetIsEmpty()
    {
        var failure = new Failure(Array.Empty<Error>());

        Assert.Empty(failure.Errors);
        Assert.Empty(failure);
    }
}
