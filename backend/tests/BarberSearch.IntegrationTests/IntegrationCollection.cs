using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Одна collection на весь интеграционный прогон: контейнер PostgreSQL поднимается один раз,
/// тесты внутри collection xUnit выполняет последовательно, поэтому общая база не пересекается.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>
    /// Имя collection для <c>[Collection(...)]</c> в тестовых классах.
    /// </summary>
    public const string Name = "postgres-integration";
}
