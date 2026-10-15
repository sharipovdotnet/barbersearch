using System;
using System.Linq;
using System.Reflection;
using BarberSearch.Domain.Common;
using Shared;
using Failure = Shared.Failure;
using Xunit;

namespace BarberSearch.Domain.UnitTests;

internal static class EntityTestHelpers
{
    /// <summary>
    /// Проставляет идентификатор в обход приватного сеттера — так же, как это делает EF Core.
    /// </summary>
    public static T WithId<T>(this T entity, Guid id) where T : Entity
    {
        typeof(Entity)
            .GetProperty(nameof(Entity.Id))!
            .SetValue(entity, id);

        return entity;
    }
}

/// <summary>
/// Проверки базовой сущности: идентификаторы, временные метки, сравнение.
/// </summary>
public class EntityTests
{
    private class FirstEntity : Entity;

    private sealed class DerivedEntity : FirstEntity;

    private static void AssertIsVersion7(Guid id)
    {
        var version = id.ToByteArray()[7] >> 4;

        Assert.Equal(7, version);
    }

    [Fact]
    public void NewEntity_GetsVersion7Identifier()
    {
        var entity = new FirstEntity();

        Assert.NotEqual(Guid.Empty, entity.Id);
        AssertIsVersion7(entity.Id);
    }

    [Fact]
    public void NewEntity_SetsUtcTimestamps()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var entity = new FirstEntity();

        Assert.True(entity.CreatedAt >= before, "CreatedAt must be set on construction.");
        Assert.Equal(entity.CreatedAt.UtcTicks, entity.UpdatedAt.UtcTicks);
        Assert.Equal(TimeSpan.Zero, entity.CreatedAt.Offset);
    }

    [Fact]
    public void Entities_WithDifferentIds_AreNotEqual()
    {
        var left = new FirstEntity();
        var right = new FirstEntity();

        Assert.False(left == right);
        Assert.True(left != right);
    }

    [Fact]
    public void Entity_IsEqualToItself()
    {
        var entity = new FirstEntity();

        // Сравнение переменной с самой собой здесь намеренно: проверяются операторы == и !=.
#pragma warning disable CS1718
        Assert.True(entity == entity);
        Assert.False(entity != entity);
#pragma warning restore CS1718
    }

    [Fact]
    public void Entities_WithSameId_AreEqual()
    {
        var id = Guid.CreateVersion7();

        var left = new FirstEntity().WithId(id);
        var right = new FirstEntity().WithId(id);

        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Entities_WithSameIdAndDifferentRuntimeTypes_AreEqual()
    {
        // Ровно так выглядит прокси EF Core: тип-наследник с тем же идентификатором.
        // Сравнение по GetType() здесь ломается, сравнение по Id — нет.
        var id = Guid.CreateVersion7();

        var entity = new FirstEntity().WithId(id);
        var proxy = new DerivedEntity().WithId(id);

        Assert.True(entity.Equals(proxy));
        Assert.Equal(entity.GetHashCode(), proxy.GetHashCode());
    }

    [Fact]
    public void Entity_IsNotEqualToNull()
    {
        var entity = new FirstEntity();

        Assert.False(entity.Equals(null));
        Assert.False(Equals(entity, null));
        Assert.True(entity != null);
    }

    [Fact]
    public void FailedResult_ExposesAllErrors()
    {
        var failure = new Failure([
            Error.Validation("first", "First error."),
            Error.Conflict("second", "Second error.")
        ]);

        Assert.Equal(2, failure.Errors.Count);
        Assert.Equal("first", failure.Errors[0].Code);
        Assert.Equal(ErrorType.CONFLICT, failure.Errors[1].Type);
        Assert.Equal(2, failure.Count());
    }

    [Fact]
    public void Error_FromSingleInstance_ProducesSingleErrorFailure()
    {
        Failure failure = Error.NotFound("record.not.found", "Not found.", Guid.Empty);

        Assert.Single(failure.Errors);
        Assert.Equal(ErrorType.NOT_FOUND, failure.Errors[0].Type);
        Assert.Equal(Guid.Empty.ToString(), failure.Errors[0].InvalidField);
    }

    [Fact]
    public void Error_Conflict_KeepsMessage()
    {
        var error = Error.Conflict("code", "Conflict message.");

        Assert.Equal("Conflict message.", error.Message);
        Assert.Equal(ErrorType.CONFLICT, error.Type);
    }
}
