using BarberSearch.Infrastructure.Postgresql;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Одна PostgreSQL-база на весь прогон: контейнер <c>postgres:18</c> либо внешний сервер.
/// </summary>
/// <remarks>
/// Контейнер поднимается один раз на xUnit-collection, миграции применяются один раз
/// (<see cref="InitializeAsync"/>), а чистое состояние под каждый тест даёт
/// <see cref="ResetAsync"/>: <c>TRUNCATE … RESTART IDENTITY CASCADE</c> по всем таблицам
/// <c>public</c>, кроме истории миграций. Отдельная база на каждый тест была бы медленнее
/// (миграции заново) и не нужна: тесты одной collection идут последовательно.
/// <para>
/// Если задана переменная окружения <c>BARBERSEARCH_TEST_POSTGRES</c> (строка подключения
/// Npgsql к существующему серверу, например <c>Host=127.0.0.1;Port=5432;Username=postgres;
/// Password=postgres;Database=postgres</c>), Docker не нужен: фикстура создаёт уникальную
/// базу <c>barbersearch_it_&lt;guid&gt;</c> на прогон и удаляет её в <see cref="DisposeAsync"/>.
/// Такой режим предназначен для CI без Docker; по умолчанию используется Testcontainers.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>
    /// Переменная окружения для внешней PostgreSQL (CI без Docker).
    /// </summary>
    public const string ExternalServerVariable = "BARBERSEARCH_TEST_POSTGRES";

    private const string DATABASE_PREFIX = "barbersearch_it_";
    private const string ConnectionStringVariable = "ConnectionStrings__Postgres";
    private const string SigningKeyVariable = "Auth__SigningKey";
    private const string KeysPathVariable = "DataProtection__KeysPath";
    private const string EventLogVariable = "Logging__EventLog__LogLevel__Default";

    private PostgreSqlContainer? _container;
    private string _adminConnectionString = string.Empty;
    private string? _databaseName;
    private string? _keysPath;
    private Dictionary<string, string?> _previousEnvironment = [];
    private bool _disposed;

    /// <summary>
    /// Строка подключения к базе, которую используют тестовые хосты.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        try
        {
            await InitializeCoreAsync();
        }
        catch
        {
            // Не оставляем за собой контейнер или уникальную БД, если старт прогона сорвался.
            await DisposeAsync();
            throw;
        }
    }

    private async Task InitializeCoreAsync()
    {
        var external = Environment.GetEnvironmentVariable(ExternalServerVariable);

        if (!string.IsNullOrWhiteSpace(external))
        {
            _adminConnectionString = BuildAdminConnectionString(external);
            _databaseName = $"{DATABASE_PREFIX}{Guid.NewGuid():N}";
            await CreateDatabaseAsync(_adminConnectionString, _databaseName);
            ConnectionString = BuildDatabaseConnectionString(_adminConnectionString, _databaseName);
        }
        else
        {
            // postgres:18 — единственный образ, гарантированно лежащий в локальном кэше Docker;
            // другие теги не тянем, чтобы прогон не зависел от Docker Hub.
            _container = new PostgreSqlBuilder("postgres:18")
                .WithDatabase("barbersearch_it")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        await MigrateAsync(ConnectionString);

        _keysPath = Path.Combine(
            Path.GetTempPath(),
            "barbersearch-integration-keys",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_keysPath);

        // Minimal hosting читает конфигурацию в WebApplication.CreateBuilder, то есть раньше,
        // чем WebApplicationFactory применит ConfigureAppConfiguration. Переменные окружения
        // видит уже сам CreateBuilder — так же поднимает хост deploy/smoke.ps1.
        _previousEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ConnectionStringVariable] = Environment.GetEnvironmentVariable(ConnectionStringVariable),
            [SigningKeyVariable] = Environment.GetEnvironmentVariable(SigningKeyVariable),
            [KeysPathVariable] = Environment.GetEnvironmentVariable(KeysPathVariable),
            [EventLogVariable] = Environment.GetEnvironmentVariable(EventLogVariable)
        };

        Environment.SetEnvironmentVariable(ConnectionStringVariable, ConnectionString);
        Environment.SetEnvironmentVariable(SigningKeyVariable, TestApp.SigningKey);
        Environment.SetEnvironmentVariable(KeysPathVariable, _keysPath);
        Environment.SetEnvironmentVariable(EventLogVariable, "None");
    }

    /// <summary>
    /// Возвращает базу в пустое состояние перед тестом.
    /// </summary>
    /// <remarks>
    /// Все таблицы <c>public</c>, кроме <c>__EFMigrationsHistory</c>, обрезаются одним
    /// <c>TRUNCATE … CASCADE</c>: порядок внешних ключей не важен, а остатки предыдущего
    /// сценария не влияют на следующий.
    /// </remarks>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        string? tables;

        await using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT string_agg(format('%I.%I', schemaname, tablename), ', ')
                FROM pg_tables
                WHERE schemaname = 'public'
                  AND tablename <> '__EFMigrationsHistory'
                """;

            tables = (string?)await query.ExecuteScalarAsync();
        }

        if (string.IsNullOrEmpty(tables))
        {
            return;
        }

        await using var truncate = connection.CreateCommand();
        truncate.CommandText = $"TRUNCATE TABLE {tables} RESTART IDENTITY CASCADE";
        await truncate.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var (name, value) in _previousEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        if (_keysPath is not null)
        {
            try
            {
                if (Directory.Exists(_keysPath))
                {
                    Directory.Delete(_keysPath, recursive: true);
                }
            }
            catch (IOException)
            {
                // Временный key ring — не предмет проверки.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        if (_databaseName is null)
        {
            return;
        }

        // Пулы Npgsql держат idle-соединения к удаляемой базе; WITH (FORCE) снимает их,
        // а ClearAllPools не даёт открыть новые из уже завершившегося прогона.
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();

        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        await drop.ExecuteNonQueryAsync();
    }

    private static async Task MigrateAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
    }

    private static string BuildAdminConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            builder.Database = "postgres";
        }

        // Служебные CREATE/DROP DATABASE идут отдельным соединением, пул ему не нужен.
        builder.Pooling = false;

        return builder.ConnectionString;
    }

    private static string BuildDatabaseConnectionString(string adminConnectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName,
            Pooling = true
        };

        return builder.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string adminConnectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        await using var create = connection.CreateCommand();
        create.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await create.ExecuteNonQueryAsync();
    }
}
