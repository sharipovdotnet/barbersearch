using Microsoft.Extensions.Logging;

namespace BarberSearch.Infrastructure.UnitTests.Fakes;

/// <summary>
/// Журнал, который запоминает отформатированные сообщения: тесты проверяют, что в лог
/// не попадают токены, ключи и адрес сервисного аккаунта.
/// </summary>
/// <typeparam name="T">Категория журнала, как у настоящего <see cref="ILogger{T}"/>.</typeparam>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];

    /// <summary>
    /// Отформатированные сообщения в порядке записи.
    /// </summary>
    public IReadOnlyList<string> Messages => _messages;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        _messages.Add(formatter(state, exception));
    }
}
